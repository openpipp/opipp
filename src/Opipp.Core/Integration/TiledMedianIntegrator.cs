namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Processing;

/// <summary>
/// Exact per-pixel median over any number of frames, computed from a disk-backed spill so the whole
/// selection never has to sit in RAM.
/// </summary>
/// <remarks>
/// <para>
/// <b>Band strategy.</b> Every frame is written to a <see cref="FrameStore"/> (a private scratch file)
/// once, already translated by its alignment shift. The output is then produced in horizontal bands of
/// <see cref="IntegrationOptions.MedianBandRows"/> rows: for one band, each frame is re-read from the
/// store and its samples for those rows are gathered into a per-band table laid out so that every
/// output sample owns a contiguous run of contributions. Each run is sorted in place and the middle
/// value taken, so the result is the exact median of the same samples an in-memory median would see
/// — no approximation, no histogram binning, no truncation of the sample set.
/// </para>
/// <para>
/// <b>Memory bound.</b> Peak managed memory is O(one frame) — the staging frame used for shifted
/// writes plus the one store frame materialised per read — and then the band table
/// (<c>bandRows x outWidth x channels x frames x 2</c>), a per-band cursor array
/// (<c>bandRows x outWidth x 4</c>), the output (<c>outWidth x outHeight x channels x 2</c>) and the
/// coverage array (<c>width x height x 4</c>). No term grows with the total number of pixels across
/// frames: 10,000 frames cost the same RAM as 65 with the same geometry and band height. The store
/// itself lives on disk and is deleted on every exit path, including cancellation and failure.
/// </para>
/// <para>
/// <b>Even counts.</b> The middle two samples of an even-length run are averaged half-up,
/// <c>(low + high + 1) / 2</c>, which is exactly <c>FrameStacker</c>'s documented even-median rule
/// (so <c>[0, 1]</c> medians to 1 rather than 0). Odd counts take the middle sample unchanged.
/// </para>
/// <para>
/// <b>Registration.</b> Offsets use the same convention as <see cref="FrameIntegrator"/> and
/// <see cref="FrameAligner.ShiftWithBackground"/>: a positive <c>Dx</c> moves content right, a
/// positive <c>Dy</c> moves content down. The whole-pixel part is always applied exactly. When
/// <see cref="IntegrationOptions.Subpixel"/> is on, a fractional part is applied by bilinear
/// resampling each frame as it is spilled, through the same <see cref="SubpixelSampler"/> kernel
/// mean/sum uses, so the two methods register identically; the interpolated sample is quantised with
/// <see cref="FrameIntegrator.Quantise"/> before it enters the store, which is exactly the rule the
/// mean applies to its output. When sub-pixel is off, or the offset is integral, the offset is
/// rounded away from zero and the frame is copied whole, matching pre-0.2 behaviour. Bilinear
/// resampling needs both taps of the kernel, so a fractional shift loses one border pixel on the side
/// it samples from and the coverage rectangle accounts for that loss instead of clamping the sample
/// at the edge. Each contributor reports its applied shift, so the metadata never claims a
/// registration the pixels did not receive.
/// </para>
/// <para>
/// <b>Coverage.</b> <see cref="IntegrationResult.Coverage"/> has one entry per pixel of the first
/// frame's canvas, row-major, exactly like <see cref="FrameIntegrator"/>'s: under
/// <see cref="CoveragePolicy.CropToOverlap"/> the output frame is the intersection of every frame's
/// valid region and the entries outside that rectangle are zero; under
/// <see cref="CoveragePolicy.KeepCanvas"/> the frame keeps the first canvas, entries count the frames
/// that can supply each pixel, and pixels no frame can supply are written as
/// <see cref="FrameIntegrator.FillSample"/> (a fixed sentinel, never a background estimate) with
/// coverage 0. Frames are never padded, clamped or otherwise sampled outside their own bounds.
/// </para>
/// <para>
/// <b>Scratch budget.</b> <see cref="IntegrationOptions.MedianScratchBudgetBytes"/> caps the pixel
/// bytes the <see cref="FrameStore"/> may hold. The requirement is known before the first frame is
/// spilled, so an over-budget selection fails immediately with
/// <see cref="InvalidOperationException"/> instead of filling the disk and failing later. The band
/// table is bounded by <see cref="IntegrationOptions.MedianBandRows"/>, not by the budget.
/// </para>
/// <para>
/// <b>Validation.</b> All frames must share geometry, depth and CFA pattern; a mismatch is rejected
/// before any frame is spilled or any metadata is built, matching the integration contract that
/// incompatible selections fail with a diagnostic naming the reason.
/// </para>
/// </remarks>
public static class TiledMedianIntegrator
{
    /// <summary>Scratch file prefix; the store deletes the file it owns.</summary>
    private const string ScratchPrefix = "opipp-median-";

    /// <summary>Scratch file extension.</summary>
    private const string ScratchExtension = ".bin";

    /// <summary>
    /// Integrates <paramref name="frames"/> with an exact per-pixel median.
    /// </summary>
    /// <param name="frames">Frames in delivery order. Every frame must share geometry, depth and CFA pattern.</param>
    /// <param name="shifts">
    /// Per-frame alignment offset, index-aligned with <paramref name="frames"/>. The whole-pixel part
    /// is always applied; the fractional part is bilinear-resampled when
    /// <see cref="IntegrationOptions.Subpixel"/> is on and rounded away from zero when it is off.
    /// </param>
    /// <param name="options">
    /// Integration options. <see cref="IntegrationOptions.Method"/> must be
    /// <see cref="IntegrationMethod.Median"/>.
    /// </param>
    /// <param name="cancellationToken">Checked per band, per frame read and periodically per sample.</param>
    /// <returns>
    /// The median frame, per-pixel coverage, contributors in delivery order, the covered rectangle and
    /// <see cref="IntegrationResult.PrecisionSamples"/> = null (a median of integers is an integer, so
    /// there is no high-precision intermediate to expose).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// No frames, mismatched list lengths, a frame's geometry/depth differs, an offset is not finite,
    /// or no pixel is covered by every frame under <see cref="CoveragePolicy.CropToOverlap"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A band height or budget is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scratch budget cannot hold the selection, or the band table would exceed the maximum array
    /// length.
    /// </exception>
    /// <exception cref="BayerPhaseException">The frames carry different CFA patterns.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        return Integrate(frames, shifts, options, cancellationToken, scratchDirectory: null, storeObserver: null);
    }

    /// <summary>
    /// Test seam: the same integration with a caller-chosen scratch directory and a hook that sees the
    /// populated store before band processing starts.
    /// </summary>
    /// <remarks>
    /// Both extra arguments exist so tests can prove the frames really were spilled (the store reports
    /// its frame count, byte count and path) and can assert the scratch file is gone afterwards.
    /// Production callers use the four-argument overload.
    /// </remarks>
    internal static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        CancellationToken cancellationToken,
        string? scratchDirectory,
        Action<FrameStore>? storeObserver)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Method != IntegrationMethod.Median)
        {
            throw new ArgumentException(
                $"tiled median integration requires {nameof(IntegrationOptions.Method)} == {nameof(IntegrationMethod.Median)}, " +
                $"but it is {options.Method}",
                nameof(options));
        }

        if (!Enum.IsDefined(options.Coverage))
            throw new ArgumentOutOfRangeException(nameof(options), options.Coverage, "unknown coverage policy");
        if (options.MedianBandRows < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MedianBandRows,
                "MedianBandRows must be at least 1");
        if (options.MedianScratchBudgetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MedianScratchBudgetBytes,
                "MedianScratchBudgetBytes cannot be negative");
        if (frames.Count == 0)
            throw new ArgumentException("at least one frame is required", nameof(frames));
        if (shifts.Count != frames.Count)
            throw new ArgumentException(
                $"{shifts.Count} shifts for {frames.Count} frames; the lists must be index-aligned",
                nameof(shifts));
        if (scratchDirectory is not null && string.IsNullOrWhiteSpace(scratchDirectory))
            throw new ArgumentException("scratch directory cannot be blank", nameof(scratchDirectory));

        cancellationToken.ThrowIfCancellationRequested();

        // Validate the whole selection before a single sample is written: a mixed-geometry or mixed-CFA
        // run must fail without leaving scratch behind or changing any provenance.
        Frame first = frames[0] ?? throw new ArgumentException("frame 0 is null", nameof(frames));
        int width = first.Width;
        int height = first.Height;
        int channels = first.Channels;
        int bitDepth = first.BitDepth;

        for (int i = 0; i < frames.Count; i++)
        {
            Frame frame = frames[i] ?? throw new ArgumentException($"frame {i} is null", nameof(frames));
            if (frame.Width != width || frame.Height != height || frame.Channels != channels)
            {
                throw new ArgumentException(
                    $"frame {i} is {frame.Width}x{frame.Height}x{frame.Channels}, expected {width}x{height}x{channels}",
                    nameof(frames));
            }

            if (frame.BitDepth != bitDepth)
                throw new ArgumentException($"frame {i} is {frame.BitDepth}-bit, expected {bitDepth}-bit", nameof(frames));
            if (frame.Channels != 1 && frame.Bayer != BayerPattern.None)
                throw new ArgumentException($"frame {i} is a colour frame but carries Bayer pattern {frame.Bayer}",
                    nameof(frames));
            if (frame.Bayer != first.Bayer)
            {
                throw new BayerPhaseException(
                    $"frame {i + 1} has Bayer pattern {frame.Bayer} but the selection has {first.Bayer}. " +
                    "Combining different mosaic phases would mix colour sites; use matching patterns and alignment " +
                    "shifts with matching parity.");
            }
        }

        // Resolve every offset through the shared sub-pixel sampler: the whole-pixel part is applied
        // exactly and, with Subpixel on, the fraction is left to the same bilinear kernel mean/sum
        // uses. A non-finite offset is a caller bug, not something to round.
        var placement = new SubpixelSampler[frames.Count];
        for (int i = 0; i < shifts.Count; i++)
        {
            AlignmentOffset offset = shifts[i];
            if (!double.IsFinite(offset.Dx) || !double.IsFinite(offset.Dy))
                throw new ArgumentException($"shift {i} is not a finite offset ({offset.Dx}, {offset.Dy})", nameof(shifts));
            placement[i] = SubpixelSampler.FromOffset(offset, options.Subpixel);
        }

        int frameCount = frames.Count;
        long bytesPerFrame = (long)width * height * channels * sizeof(ushort);
        long requiredScratch = bytesPerFrame * frameCount;
        if (requiredScratch > options.MedianScratchBudgetBytes)
        {
            throw new InvalidOperationException(
                $"disk-backed median needs {requiredScratch:N0} bytes of scratch for {frameCount} frame(s) of " +
                $"{width}x{height}x{channels}, but MedianScratchBudgetBytes is {options.MedianScratchBudgetBytes:N0}. " +
                "Reduce the selection, raise the budget, or use mean/sum.");
        }

        // --- spill -------------------------------------------------------------------------------
        // One staging frame is reused for every shifted write; FrameStore copies the samples into the
        // scratch file during Append, so nothing here outlives the iteration.
        string directory = scratchDirectory ?? Path.GetTempPath();
        string scratchPath = Path.Combine(directory, ScratchPrefix + Guid.NewGuid().ToString("n") + ScratchExtension);

        var staging = new Frame(width, height, channels, bitDepth) { Bayer = first.Bayer };

        // The store is the only scratch this method creates and it owns deletion: the using scope
        // runs on every path, including cancellation and a failed band pass. Nothing partial is
        // returned either — the result is built in local arrays and handed back only after the last
        // band, so a throw discards it.
        using var store = new FrameStore(scratchPath);

        int maxValue = bitDepth == 8 ? 255 : 65535;
        for (int i = 0; i < frameCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Frame frame = frames[i];
            SubpixelSampler sampler = placement[i];
            if (sampler.IsWholePixel)
            {
                if (sampler.Dx == 0 && sampler.Dy == 0)
                {
                    store.Append(frame);
                }
                else
                {
                    ShiftInto(frame, staging.Samples, sampler.Dx, sampler.Dy);
                    store.Append(staging);
                }
            }
            else
            {
                // One frame is resampled at a time into the same staging buffer the integer shift
                // uses: the store copies the samples during Append, so the peak stays one frame.
                sampler.ResampleInto(frame, staging.Samples, maxValue, cancellationToken);
                store.Append(staging);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        storeObserver?.Invoke(store);

        return IntegrateBands(frames, placement, options, store, first, cancellationToken);
    }

    /// <summary>
    /// Copies <paramref name="frame"/> into <paramref name="destination"/> translated by
    /// (<paramref name="dx"/>, <paramref name="dy"/>), leaving pixels no source sample covers as zero.
    /// </summary>
    private static void ShiftInto(Frame frame, ushort[] destination, int dx, int dy)
    {
        Array.Clear(destination);

        int width = frame.Width;
        int height = frame.Height;
        int channels = frame.Channels;

        int xStart = Math.Max(0, dx);
        int xEnd = Math.Min(width, width + dx);
        int yStart = Math.Max(0, dy);
        int yEnd = Math.Min(height, height + dy);
        if (xEnd <= xStart || yEnd <= yStart) return;

        int sourceX = xStart - dx;
        int runSamples = (xEnd - xStart) * channels;
        ushort[] source = frame.Samples;

        for (int y = yStart; y < yEnd; y++)
        {
            int sourceRow = ((y - dy) * width + sourceX) * channels;
            int destinationRow = (y * width + xStart) * channels;
            Array.Copy(source, sourceRow, destination, destinationRow, runSamples);
        }
    }

    /// <summary>
    /// Second pass: derive the output rectangle, then walk the image in bands gathering, sorting and
    /// emitting the median.
    /// </summary>
    private static IntegrationResult IntegrateBands(
        IReadOnlyList<Frame> frames,
        SubpixelSampler[] placement,
        IntegrationOptions options,
        FrameStore store,
        Frame first,
        CancellationToken cancellationToken)
    {
        int width = first.Width;
        int height = first.Height;
        int channels = first.Channels;
        int frameCount = frames.Count;

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
                Frame stored = store.Read(i);

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
                    // The store already holds this frame in output-canvas coordinates: the shift was
                    // applied when it was spilled, so band gathering reads the destination pixel
                    // directly. Applying the shift again here would double it.
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
        string? sourceName = null;
        for (int i = 0; i < frameCount; i++)
        {
            string? name = frames[i].SourceName;
            contributors[i] = new IntegrationContributor(name ?? string.Empty, 1.0, placement[i].Applied);
            if (sourceName is null && !string.IsNullOrWhiteSpace(name)) sourceName = name;
        }

        var frame = new Frame(outputWidth, outputHeight, channels, first.BitDepth, output)
        {
            Bayer = first.Bayer,
            Index = first.Index,
            SourceName = sourceName,
        };

        return new IntegrationResult
        {
            Frame = frame,
            Coverage = coverage,
            Contributors = contributors,
            Method = IntegrationMethod.Median,
            OutputRect = outputRect,
            PrecisionSamples = null,
        };
    }

    /// <summary>
    /// Middle value of an already sorted run: the middle sample for odd lengths, half-up on the two
    /// middle samples for even lengths (FrameStacker's documented rule).
    /// </summary>
    private static ushort MedianOfSorted(ReadOnlySpan<ushort> sorted)
    {
        int n = sorted.Length;
        return (n & 1) == 1
            ? sorted[n / 2]
            : (ushort)(((int)sorted[n / 2 - 1] + sorted[n / 2] + 1) / 2);
    }
}
