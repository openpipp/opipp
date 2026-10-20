namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Processing;

/// <summary>
/// Combines selected science frames into one final image by weighted mean or weighted sum, applying
/// per-frame alignment offsets and reporting per-pixel coverage.
/// </summary>
/// <remarks>
/// <para><b>Shift direction.</b> Offsets use the same convention as
/// <see cref="FrameAligner.ShiftWithBackground"/>: the offset is the displacement applied to the
/// frame's <em>content</em>, so a positive <c>Dx</c> moves content to the right and a positive
/// <c>Dy</c> moves it down. Output pixel <c>(x, y)</c> therefore samples source coordinate
/// <c>(x - Dx, y - Dy)</c>.</para>
/// <para><b>Registration.</b> The integer part of every offset is applied exactly. With
/// <see cref="IntegrationOptions.Subpixel"/> the fractional part is applied by bilinear resampling of
/// the source through <see cref="SubpixelSampler"/>, the same kernel
/// <see cref="TiledMedianIntegrator"/> uses for median, so the two methods register identically;
/// without it the offset is rounded to whole pixels with
/// <see cref="MidpointRounding.AwayFromZero"/>, exactly like <see cref="FrameAligner.ShiftWithBackground"/>.
/// Bilinear resampling needs both taps of the kernel, so a fractional shift loses one border pixel on
/// the side it samples from; the coverage rectangle accounts for that loss instead of clamping the
/// sample at the edge (which would invent data).</para>
/// <para><b>Raw mosaics are registered by whole pixels only, and the phase follows the placement.</b>
/// A CFA image is a 2x2 colour lattice, so a raw frame's pattern is part of its geometry: an odd shift
/// along either axis moves the tile to a different one of the four patterns, and bilinear resampling
/// would blend different colour sites into one sample. This integrator therefore (a) rejects a
/// fractional offset on a mosaic frame with <see cref="BayerPhaseException"/> — debayer to colour, or
/// turn sub-pixel off — and (b) compares the <em>effective</em> pattern each frame's placement produces
/// rather than the input headers, so two frames tagged RGGB and GRBG can share one phase when the
/// second is registered by an odd column, while two frames both tagged RGGB cannot when one of them is.
/// The result is tagged with the pattern the output samples actually have at the output origin, after
/// the placement and after any crop; no stale input tag is copied forward.</para>
/// <para><b>Accumulation and quantisation.</b> Every contribution is accumulated in
/// <see cref="double"/>. The unquantised result is exposed through
/// <see cref="IntegrationResult.PrecisionSamples"/> in the output depth's scale. The quantised frame
/// rounds with half away from zero (<see cref="MidpointRounding.AwayFromZero"/>) and then clamps to
/// the output depth (255 for 8-bit, 65535 for 16-bit). Since samples and weights are non-negative,
/// half away from zero is half-up at every <c>.5</c> boundary: 100.5 becomes 101, and a sum above the
/// depth maximum saturates. A finite weight can still overflow the accumulator (1e307 x 200 is
/// 2e309), so a non-finite precision sample is rejected with a diagnostic instead of being written as
/// a plausible-looking pixel.</para>
/// <para><b>Coverage.</b> <see cref="IntegrationResult.Coverage"/> has one entry per pixel of the
/// first frame's canvas, row-major, and counts the frames that can supply that pixel. Under
/// <see cref="CoveragePolicy.CropToOverlap"/> the output frame is the intersection of the frames'
/// valid regions (in first-frame canvas coordinates, recorded as
/// <see cref="IntegrationResult.OutputRect"/>); every pixel of it has full coverage. Under
/// <see cref="CoveragePolicy.KeepCanvas"/> the frame keeps the first frame's canvas, pixels no frame
/// can supply are written as <see cref="FillSample"/> and reported with coverage 0, and
/// <see cref="IntegrationResult.OutputRect"/> is the whole canvas.</para>
/// <para><b>Bayer.</b> A colour (three-channel) frame must not carry a Bayer marker. For a mosaic
/// selection the output tag is the effective pattern at the output origin; a plain mono frame mixed
/// with a mosaic one is rejected, because a CFA plane and a non-CFA plane cannot be combined.</para>
/// <para>Median is not handled here: the exact median lives in <see cref="MedianIntegrator"/>, which
/// <see cref="FinalImageIntegrator"/> routes to.</para>
/// </remarks>
public static class FrameIntegrator
{
    /// <summary>
    /// Sample written into output pixels that no contributor can supply. It is deliberately a fixed
    /// sentinel rather than a background estimate: a zero-coverage pixel must never look measured.
    /// </summary>
    public const ushort FillSample = 0;

    /// <summary>Combines <paramref name="frames"/> into one final image.</summary>
    /// <param name="frames">Frames in delivery order. All must share geometry, channel count and depth.</param>
    /// <param name="shifts">One alignment offset per frame, in <see cref="AlignmentOffset"/>'s convention.</param>
    /// <param name="weights">
    /// Optional non-negative per-frame weight; null means 1.0 for every frame. With zero weights the
    /// mean is undefined, so the list must contain at least one positive weight.
    /// </param>
    /// <param name="options">Method, coverage policy and sub-pixel switch.</param>
    /// <param name="cancellationToken">Checked before work and periodically while accumulating.</param>
    /// <exception cref="ArgumentException">
    /// No frames, mismatched shift/weight counts, mismatched geometry/depth/channels, a colour frame
    /// carrying a Bayer marker, a non-finite offset, a non-finite/negative weight, a non-finite weight
    /// total, or a precision sample that overflowed <see cref="double"/>. The message names the reason.
    /// </exception>
    /// <exception cref="BayerPhaseException">
    /// The frames' effective mosaic phases differ, a raw mosaic frame needs a fractional shift, or a
    /// mosaic frame is mixed with a plain mono one.
    /// </exception>
    /// <exception cref="NotSupportedException">The median method was requested.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IReadOnlyList<double>? weights,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        if (frames.Count == 0)
        {
            throw new ArgumentException("at least one frame is required", nameof(frames));
        }

        if (shifts.Count != frames.Count)
        {
            throw new ArgumentException(
                $"got {shifts.Count} shift(s) for {frames.Count} frame(s); every frame needs one offset",
                nameof(shifts));
        }

        if (weights is not null && weights.Count != frames.Count)
        {
            throw new ArgumentException(
                $"got {weights.Count} weight(s) for {frames.Count} frame(s); weights are per frame",
                nameof(weights));
        }

        IntegrationSupport.RequireMeanSumOptions(options);

        var first = frames[0]
            ?? throw new ArgumentException("frames[0] is null", nameof(frames));
        int width = first.Width, height = first.Height, channels = first.Channels, depth = first.BitDepth;
        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i]
                ?? throw new ArgumentException($"frames[{i}] is null", nameof(frames));

            IntegrationSupport.RequireCompatibleFrame(frame, i, width, height, channels, depth, nameof(frames));
        }

        // Snapshot and validate the weights so a caller's mutable list cannot change mid-run.
        double[]? appliedWeights = IntegrationSupport.SnapshotMeanWeights(weights, frames.Count);

        var placement = IntegrationSupport.Placements(shifts, options, nameof(shifts));

        // Validate the whole selection before a sample is touched, so a mixed-phase or
        // fractional-mosaic request fails without allocating output buffers.
        BayerPattern canvasPattern = IntegrationSupport.RequireCommonPhase(frames, placement);

        cancellationToken.ThrowIfCancellationRequested();

        var selection = new IntegrationSelection(
            width, height, channels, depth, canvasPattern, first.Index, first.SourceName);

        return IntegrateReader(
            i => frames[i],
            frames.Count,
            placement,
            appliedWeights,
            options,
            selection,
            firstFrame: null,
            validate: null,
            cancellationToken);
    }

    /// <summary>
    /// Streaming overload: combines frames pulled one at a time from <paramref name="readFrame"/>
    /// instead of from a materialised list, so a caller holding thousands of frames — or re-reading
    /// them from a container — never has to keep them all resident.
    /// </summary>
    /// <remarks>
    /// <para><b>Read contract.</b> Indices are requested in ascending order, <c>0 .. frameCount - 1</c>,
    /// exactly once each, and the frame returned for an index is accumulated before the next request. A
    /// source may therefore hand back the same <see cref="Frame"/> instance or sample buffer for every
    /// index: nothing the source returns is held across a <c>readFrame</c> call. The frame in the
    /// result is a new one owned by this method.</para>
    /// <para><b>Validation.</b> Frame 0 fixes the geometry, depth, channel count and provenance. Every
    /// later frame is checked as it arrives (geometry, depth, channels, Bayer marker, effective CFA
    /// phase) and rejected before it contributes, so a bad frame fails the run but never corrupts the
    /// accumulated result: nothing is returned on a throw.</para>
    /// </remarks>
    /// <param name="readFrame">Supplies frame <c>i</c>; must not return null.</param>
    /// <param name="frameCount">Number of frames to pull.</param>
    /// <param name="shifts">One alignment offset per frame, index-aligned with the reads.</param>
    /// <param name="weights">Optional per-frame weights; see the list overload.</param>
    /// <param name="options">Method, coverage policy and sub-pixel switch.</param>
    /// <param name="cancellationToken">Checked before work and periodically while accumulating.</param>
    /// <exception cref="ArgumentException">Same as the list overload.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="readFrame"/> returned null.</exception>
    /// <exception cref="BayerPhaseException">
    /// The effective mosaic phases differ, or a raw mosaic frame needs a fractional shift.
    /// </exception>
    /// <exception cref="NotSupportedException">The median method was requested.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        Func<int, Frame> readFrame,
        int frameCount,
        IReadOnlyList<AlignmentOffset> shifts,
        IReadOnlyList<double>? weights,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readFrame);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        if (frameCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "at least one frame is required");
        }

        if (shifts.Count != frameCount)
        {
            throw new ArgumentException(
                $"got {shifts.Count} shift(s) for {frameCount} frame(s); every frame needs one offset",
                nameof(shifts));
        }

        if (weights is not null && weights.Count != frameCount)
        {
            throw new ArgumentException(
                $"got {weights.Count} weight(s) for {frameCount} frame(s); weights are per frame",
                nameof(weights));
        }

        IntegrationSupport.RequireMeanSumOptions(options);

        cancellationToken.ThrowIfCancellationRequested();

        Frame first = ReadFrame(readFrame, 0, frameCount);
        int width = first.Width, height = first.Height, channels = first.Channels, depth = first.BitDepth;
        IntegrationSupport.RequireCompatibleFrame(first, 0, width, height, channels, depth, nameof(readFrame));

        double[]? appliedWeights = IntegrationSupport.SnapshotMeanWeights(weights, frameCount);
        var placement = IntegrationSupport.Placements(shifts, options, nameof(shifts));

        // Frame 0's effective phase is the reference every later frame must match. Its samples are read
        // once, during the accumulation of index 0, so a reused source buffer stays safe.
        BayerPattern canvasPattern = IntegrationSupport.EffectivePattern(0, first.Bayer, placement[0]);
        var selection = new IntegrationSelection(
            width, height, channels, depth, canvasPattern, first.Index, first.SourceName);

        return IntegrateReader(
            readFrame,
            frameCount,
            placement,
            appliedWeights,
            options,
            selection,
            firstFrame: first,
            validate: (frame, index) =>
            {
                IntegrationSupport.RequireCompatibleFrame(
                    frame, index, width, height, channels, depth, nameof(readFrame));

                if (index > 0)
                {
                    IntegrationSupport.RequireSameEffectivePhase(index, frame.Bayer, placement[index], canvasPattern);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Quantises a precision sample: half away from zero, then clamped to the depth maximum.
    /// Samples reaching this point are non-negative, so every exact <c>.5</c> rounds up.
    /// </summary>
    public static ushort Quantise(double value, int maxValue)
    {
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            return 0;
        }

        return rounded >= maxValue ? (ushort)maxValue : (ushort)rounded;
    }

    /// <summary>
    /// The shared mean/sum engine. Both public overloads funnel through here, so the arithmetic,
    /// coverage bookkeeping and quantisation cannot diverge between the list and streaming paths; they
    /// differ only in where a frame comes from and whether it still needs validating.
    /// </summary>
    private static IntegrationResult IntegrateReader(
        Func<int, Frame> readFrame,
        int frameCount,
        SubpixelSampler[] placement,
        double[]? appliedWeights,
        IntegrationOptions options,
        IntegrationSelection selection,
        Frame? firstFrame,
        Action<Frame, int>? validate,
        CancellationToken cancellationToken)
    {
        int width = selection.Width, height = selection.Height, channels = selection.Channels, depth = selection.BitDepth;

        PixelRect outputRect = ComputeOutputRect(placement, width, height, options.Coverage, "shifts");

        int pixels = checked(width * height);
        int sampleCount = checked(pixels * channels);
        var coverage = new int[pixels];
        var precision = new double[sampleCount];
        var pixelWeight = new double[pixels];
        int maxValue = depth == 8 ? 255 : 65535;
        var sourceNames = new string?[frameCount];

        // A frame supplied for index 0 by the streaming overload is used for that slot only: it is
        // accumulated before the next readFrame call, and the reference is dropped so nothing can reach
        // into a source buffer the next read may have overwritten.
        Frame? pending = firstFrame;

        for (int i = 0; i < frameCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Frame frame = pending ?? ReadFrame(readFrame, i, frameCount);
            pending = null;
            validate?.Invoke(frame, i);
            sourceNames[i] = frame.SourceName;

            Accumulate(
                frame,
                placement[i],
                appliedWeights?[i] ?? 1.0,
                outputRect,
                coverage,
                pixelWeight,
                precision,
                width,
                channels,
                cancellationToken);
        }

        // A pixel whose total contributing weight is zero carries no measurement (only reachable with
        // zero weights, which are legal). Report it as uncovered rather than dividing by zero.
        for (int p = 0; p < pixels; p++)
        {
            if (coverage[p] > 0 && pixelWeight[p] <= 0)
            {
                coverage[p] = 0;
            }
        }

        if (options.Method == IntegrationMethod.Mean)
        {
            for (int p = 0; p < pixels; p++)
            {
                if (coverage[p] == 0)
                {
                    continue;
                }

                double weight = pixelWeight[p];
                int index = p * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    precision[index + ch] /= weight;
                }
            }
        }

        // Extreme but finite weights can overflow the double accumulator; refuse to quantise an
        // infinity into a wrong-looking pixel.
        IntegrationSupport.RequireFinitePrecision(precision, options.Method);

        var result = new Frame(outputRect.Width, outputRect.Height, channels, depth)
        {
            // The tag describes the output samples: the selection's canvas phase moved to the output
            // origin by the crop (an odd crop origin swaps the 2x2 tile).
            Bayer = IntegrationSupport.AtRectOrigin(selection.CanvasPattern, outputRect),
            Index = selection.Index,
            SourceName = selection.SourceName,
        };

        for (int y = 0; y < outputRect.Height; y++)
        {
            int canvasRow = (outputRect.Y + y) * width;
            int outRow = y * outputRect.Width;
            for (int x = 0; x < outputRect.Width; x++)
            {
                int sourcePixel = canvasRow + outputRect.X + x;
                int sourceIndex = sourcePixel * channels;
                int targetIndex = (outRow + x) * channels;
                if (coverage[sourcePixel] == 0)
                {
                    // Unreached only in KeepCanvas; written as the fill sentinel, never as data.
                    for (int ch = 0; ch < channels; ch++)
                    {
                        result.Samples[targetIndex + ch] = FillSample;
                        precision[sourceIndex + ch] = 0;
                    }

                    continue;
                }

                for (int ch = 0; ch < channels; ch++)
                {
                    result.Samples[targetIndex + ch] = Quantise(precision[sourceIndex + ch], maxValue);
                }
            }
        }

        var contributors = new IntegrationContributor[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            contributors[i] = new IntegrationContributor(
                sourceNames[i] ?? string.Empty,
                appliedWeights?[i] ?? 1.0,
                placement[i].Applied);
        }

        return new IntegrationResult
        {
            Frame = result,
            Coverage = coverage,
            Contributors = contributors,
            Method = options.Method,
            OutputRect = outputRect,
            PrecisionSamples = precision,
        };
    }

    private static Frame ReadFrame(Func<int, Frame> readFrame, int index, int frameCount)
    {
        return readFrame(index)
            ?? throw new InvalidOperationException(
                $"readFrame({index}) returned null; the source must supply {frameCount} frame(s)");
    }

    /// <summary>
    /// The region of the first frame's canvas every frame can fill. Under
    /// <see cref="CoveragePolicy.KeepCanvas"/> that is the whole canvas.
    /// </summary>
    private static PixelRect ComputeOutputRect(
        SubpixelSampler[] placement,
        int width,
        int height,
        CoveragePolicy policy,
        string parameterName)
    {
        if (policy == CoveragePolicy.KeepCanvas)
        {
            return new PixelRect(0, 0, width, height);
        }

        int left = int.MinValue, top = int.MinValue, right = int.MaxValue, bottom = int.MaxValue;
        foreach (var p in placement)
        {
            left = Math.Max(left, p.Left);
            right = Math.Min(right, p.Right(width));
            top = Math.Max(top, p.Top);
            bottom = Math.Min(bottom, p.Bottom(height));
        }

        left = Math.Max(left, 0);
        top = Math.Max(top, 0);
        right = Math.Min(right, width - 1);
        bottom = Math.Min(bottom, height - 1);

        if (left > right || top > bottom)
        {
            throw new ArgumentException(
                "the frames' valid regions do not overlap inside the first frame's canvas after the " +
                "requested shifts; nothing could be integrated",
                parameterName);
        }

        return new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    private static void Accumulate(
        Frame frame,
        SubpixelSampler placement,
        double weight,
        PixelRect rect,
        int[] coverage,
        double[] pixelWeight,
        double[] precision,
        int width,
        int channels,
        CancellationToken cancellationToken)
    {
        int xStart = Math.Max(rect.X, placement.Left);
        int xEnd = Math.Min(rect.X + rect.Width - 1, placement.Right(frame.Width));
        int yStart = Math.Max(rect.Y, placement.Top);
        int yEnd = Math.Min(rect.Y + rect.Height - 1, placement.Bottom(frame.Height));
        if (xStart > xEnd || yStart > yEnd)
        {
            return;
        }

        if (placement.IsWholePixel)
        {
            AccumulateWholePixels(
                frame, placement, weight, xStart, xEnd, yStart, yEnd,
                coverage, pixelWeight, precision, width, channels, cancellationToken);
            return;
        }

        int span = xEnd - xStart + 1;

        // Column taps are the same for every row, so resolve them once through the shared sampler:
        // each entry is the two source columns and the fractional weight of the second. Both taps are
        // inside the source because the placement rectangle already excludes the border a bilinear
        // kernel cannot reach.
        var columns = new SubpixelSampler.AxisTap[span];
        for (int k = 0; k < span; k++)
        {
            columns[k] = placement.HorizontalTap(xStart + k);
        }

        Span<double> values = stackalloc double[3];
        for (int y = yStart; y <= yEnd; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = placement.VerticalTap(y);
            int rowIndex = y * width;

            for (int k = 0; k < span; k++)
            {
                int x = xStart + k;
                int pixel = rowIndex + x;
                int target = pixel * channels;
                placement.SamplePixel(frame, x, y, columns[k], row, values);
                for (int ch = 0; ch < channels; ch++)
                {
                    precision[target + ch] += weight * values[ch];
                }

                coverage[pixel]++;
                pixelWeight[pixel] += weight;
            }
        }
    }

    private static void AccumulateWholePixels(
        Frame frame,
        SubpixelSampler placement,
        double weight,
        int xStart,
        int xEnd,
        int yStart,
        int yEnd,
        int[] coverage,
        double[] pixelWeight,
        double[] precision,
        int width,
        int channels,
        CancellationToken cancellationToken)
    {
        var samples = frame.Samples;
        for (int y = yStart; y <= yEnd; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int sourceRow = (y - placement.Dy) * width;
            int row = y * width;
            for (int x = xStart; x <= xEnd; x++)
            {
                int source = (sourceRow + x - placement.Dx) * channels;
                int pixel = row + x;
                int target = pixel * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    precision[target + ch] += weight * samples[source + ch];
                }

                coverage[pixel]++;
                pixelWeight[pixel] += weight;
            }
        }
    }
}
