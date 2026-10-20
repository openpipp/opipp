namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Processing;

/// <summary>
/// The one exact per-pixel median: gathers every contributor's samples for a horizontal band,
/// sorts each sample's run and takes the middle value.
/// </summary>
/// <remarks>
/// <para>
/// A route supplies only the reader, so the median arithmetic cannot drift between them:
/// <see cref="TiledMedianIntegrator"/> reads its scratch store, <see cref="InMemoryMedianIntegrator"/>
/// reads retained copies, and the streaming overload of <see cref="FinalImageIntegrator"/> re-reads the
/// caller's source once per band. Every reader must return a frame already translated into the first
/// frame's canvas coordinates (the placement was applied when the frame was prepared), because the
/// band gather indexes the destination pixel directly.
/// </para>
/// <para>
/// <b>Exactness.</b> Each sample's contribution run is contiguous in the band table (sample-major,
/// contribution-minor) and is sorted in place, so the result is the exact median of the samples an
/// in-memory median over the same frames would see: no histogram binning, no truncation, no
/// approximation. Even runs average the middle two half-up, <c>(low + high + 1) / 2</c>, which is
/// <see cref="FrameStacker"/>'s documented rule.
/// </para>
/// <para>
/// <b>Peak memory.</b> One band table (<c>rows x width x channels x frames x 2</c> bytes), a cursor
/// array, the output and the coverage array. No term grows with the number of pixels across all
/// frames beyond the band table's <c>frames</c> factor, which each route bounds differently.
/// </para>
/// </remarks>
internal static class MedianBandPass
{
    /// <summary>
    /// Runs the band pass. <paramref name="readFrame"/> is called once per frame per band, in delivery
    /// order, and the frame it returns is used before the next call — a route may hand back the same
    /// buffer every time.
    /// </summary>
    internal static IntegrationResult Run(
        Func<int, Frame> readFrame,
        int frameCount,
        SubpixelSampler[] placement,
        string?[] sourceNames,
        IntegrationSelection selection,
        IntegrationOptions options,
        MedianRoute route,
        long estimatedBytes,
        CancellationToken cancellationToken)
    {
        int width = selection.Width;
        int height = selection.Height;
        int channels = selection.Channels;

        // Destination-pixel range each frame can supply, in the first frame's canvas coordinates.
        // The sampler has already excluded the border a fractional kernel samples from, so this is
        // exactly the area a resampled or whole-pixel store frame can fill.
        var validX0 = new int[frameCount];
        var validX1 = new int[frameCount];
        var validY0 = new int[frameCount];
        var validY1 = new int[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            validX0[i] = Math.Max(0, placement[i].Left);
            validX1[i] = Math.Min(width, placement[i].Right(width) + 1);
            validY0[i] = Math.Max(0, placement[i].Top);
            validY1[i] = Math.Min(height, placement[i].Bottom(height) + 1);
        }

        PixelRect outputRect;
        if (options.Coverage == CoveragePolicy.CropToOverlap)
        {
            int x0 = 0, y0 = 0, x1 = width, y1 = height;
            for (int i = 0; i < frameCount; i++)
            {
                x0 = Math.Max(x0, validX0[i]);
                x1 = Math.Min(x1, validX1[i]);
                y0 = Math.Max(y0, validY0[i]);
                y1 = Math.Min(y1, validY1[i]);
            }

            if (x1 <= x0 || y1 <= y0)
            {
                throw new ArgumentException(
                    $"no pixel is supplied by all {frameCount} frame(s) after registration; " +
                    "the shifts move the frames too far apart to crop to a common overlap. " +
                    $"Use {nameof(CoveragePolicy.KeepCanvas)} or a smaller alignment offset.",
                    "shifts");
            }

            outputRect = new PixelRect(x0, y0, x1 - x0, y1 - y0);
        }
        else
        {
            outputRect = new PixelRect(0, 0, width, height);
        }

        int outputWidth = outputRect.Width;
        int outputHeight = outputRect.Height;
        int bandRows = Math.Min(options.MedianBandRows, outputHeight);

        long bandSamples = (long)bandRows * outputWidth * channels;
        long tableLength = bandSamples * frameCount;
        if (tableLength > Array.MaxLength)
        {
            throw new InvalidOperationException(
                $"a {bandRows}-row median band over {outputWidth}x{channels} with {frameCount} frames needs " +
                $"{tableLength:N0} samples, above the maximum single-array length. Lower " +
                $"{nameof(IntegrationOptions.MedianBandRows)}.");
        }

        var table = new ushort[tableLength];
        var cursors = new int[(long)bandRows * outputWidth];
        var output = new ushort[(long)outputWidth * outputHeight * channels];
        // Coverage is one entry per pixel of the first frame's canvas in both policies (the same
        // shape FrameIntegrator produces); pixels outside the output rectangle stay zero.
        var coverage = new int[(long)width * height];

        for (int bandY = 0; bandY < outputHeight; bandY += bandRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int rows = Math.Min(bandRows, outputHeight - bandY);
            int bandPixels = rows * outputWidth;
            // bandY is relative to the output rect; the canvas rows it covers start at OutputRect.Y.
            int bandStart = outputRect.Y + bandY;
            int bandEnd = bandStart + rows;
            Array.Clear(cursors, 0, bandPixels);

            for (int i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Frames are re-read one band at a time: peak resident pixel data is one frame, never
                // the whole selection.
                Frame stored = readFrame(i)
                    ?? throw new InvalidOperationException(
                        $"readFrame({i}) returned null on band {bandY / bandRows}; the source must supply " +
                        $"{frameCount} frame(s) for every band");

                if (stored.Width != width || stored.Height != height || stored.Channels != channels)
                {
                    throw new InvalidOperationException(
                        $"readFrame({i}) returned {stored.Width}x{stored.Height}x{stored.Channels} on band " +
                        $"{bandY / bandRows} but the selection is {width}x{height}x{channels}; a frame source " +
                        "must keep one geometry across re-reads");
                }

                int x0 = Math.Max(outputRect.X, validX0[i]);
                int x1 = Math.Min(outputRect.X + outputWidth, validX1[i]);
                int y0 = Math.Max(outputRect.Y, validY0[i]);
                int y1 = Math.Min(outputRect.Y + outputHeight, validY1[i]);
                if (x1 <= x0 || y1 <= y0) continue;

                int rowStart = Math.Max(y0, bandStart);
                int rowEnd = Math.Min(y1, bandEnd);
                if (rowEnd <= rowStart) continue;

                ushort[] samples = stored.Samples;
                int columnCount = x1 - x0;

                for (int y = rowStart; y < rowEnd; y++)
                {
                    // The frame is already in output-canvas coordinates: the shift was applied when it
                    // was prepared, so band gathering reads the destination pixel directly. Applying
                    // the shift again here would double it.
                    int sourceRow = (y * width + x0) * channels;
                    int pixelBase = (y - bandStart) * outputWidth + (x0 - outputRect.X);

                    for (int column = 0; column < columnCount; column++)
                    {
                        int pixel = pixelBase + column;
                        int slot = pixel * channels * frameCount + cursors[pixel];
                        int source = sourceRow + column * channels;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            table[slot + channel * frameCount] = samples[source + channel];
                        }

                        cursors[pixel]++;
                    }
                }
            }

            // Median of each sample's contribution run. The run is contiguous (sample-major table,
            // contribution-minor), so it sorts in place without a scratch copy. Coverage is indexed
            // in the first frame's canvas, like FrameIntegrator's, so entries outside the output
            // rectangle stay zero.
            for (int row = 0; row < rows; row++)
            {
                int pixelRow = row * outputWidth;
                int coverageRow = (bandStart + row) * width + outputRect.X;

                for (int column = 0; column < outputWidth; column++)
                {
                    int pixel = pixelRow + column;
                    int contributions = cursors[pixel];
                    coverage[coverageRow + column] = contributions;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        // `sample` indexes the band table (band-relative); `destination` indexes the
                        // output frame (band offset applied).
                        int sample = pixel * channels + channel;
                        int destination = (bandY * outputWidth + pixel) * channels + channel;
                        if (contributions == 0)
                        {
                            // KeepCanvas only: no frame supplies this pixel. A defined fill, not invented data.
                            output[destination] = FrameIntegrator.FillSample;
                            continue;
                        }

                        if ((sample & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();

                        Span<ushort> run = table.AsSpan(sample * frameCount, contributions);
                        run.Sort();
                        output[destination] = MedianOfSorted(run);
                    }
                }
            }
        }

        var contributors = new IntegrationContributor[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            contributors[i] = new IntegrationContributor(sourceNames[i] ?? string.Empty, 1.0, placement[i].Applied);
        }

        var frame = new Frame(outputWidth, outputHeight, channels, selection.BitDepth, output)
        {
            // The tag describes the samples actually written: the canvas phase moved by the placement
            // and then by the crop origin. It is never copied from the first input's header.
            Bayer = IntegrationSupport.AtRectOrigin(selection.CanvasPattern, outputRect),
            Index = selection.Index,
            SourceName = selection.SourceName,
        };

        return new IntegrationResult
        {
            Frame = frame,
            Coverage = coverage,
            Contributors = contributors,
            Method = IntegrationMethod.Median,
            OutputRect = outputRect,
            PrecisionSamples = null,
            MedianRoute = route,
            MedianEstimatedBytes = estimatedBytes,
        };
    }

    /// <summary>
    /// Middle value of an already sorted run: the middle sample for odd lengths, half-up on the two
    /// middle samples for even lengths (<see cref="FrameStacker"/>'s documented rule).
    /// </summary>
    private static ushort MedianOfSorted(ReadOnlySpan<ushort> sorted)
    {
        int n = sorted.Length;
        return (n & 1) == 1
            ? sorted[n / 2]
            : (ushort)(((int)sorted[n / 2 - 1] + sorted[n / 2] + 1) / 2);
    }
}
