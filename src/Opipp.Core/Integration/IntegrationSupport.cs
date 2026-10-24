namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Processing;

/// <summary>
/// Geometry, CFA phase and provenance a result inherits from its selection. It is a small value
/// record rather than a <see cref="Frame"/> on purpose: the streaming integration must never hold a
/// frame object across a <c>readFrame</c> call, and nothing here is pixel data.
/// </summary>
/// <param name="CanvasPattern">
/// The mosaic pattern the first frame's samples have once its whole-pixel placement is applied, in
/// the first frame's canvas coordinates. <see cref="BayerPattern.None"/> for mono and colour data.
/// </param>
internal readonly record struct IntegrationSelection(
    int Width,
    int Height,
    int Channels,
    int BitDepth,
    BayerPattern CanvasPattern,
    long Index,
    string? SourceName);

/// <summary>
/// Rules shared by every integration route: weighting, registration resolution, CFA phase and the
/// median memory budget. Keeping them here is what makes mean, sum and both median routes agree
/// about input validation — a per-route copy is how the registered raw-Bayer blend (R1) survived.
/// </summary>
internal static class IntegrationSupport
{
    // ------------------------------------------------------------------ options

    /// <summary>Rejects a method <see cref="FrameIntegrator"/> cannot combine.</summary>
    /// <exception cref="NotSupportedException">Median was requested.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The method or coverage policy is undefined.</exception>
    internal static void RequireMeanSumOptions(IntegrationOptions options)
    {
        if (options.Method == IntegrationMethod.Median)
        {
            throw new NotSupportedException(
                "Median integration is not implemented by FrameIntegrator. The exact median paths are " +
                "InMemoryMedianIntegrator, TiledMedianIntegrator and MedianIntegrator, which " +
                "FinalImageIntegrator routes to.");
        }

        if (options.Method is not (IntegrationMethod.Mean or IntegrationMethod.Sum))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Method, "unknown integration method");
        }

        RequireCoverage(options);
    }

    /// <summary>Rejects a coverage policy outside the defined set.</summary>
    internal static void RequireCoverage(IntegrationOptions options)
    {
        if (options.Coverage is not (CoveragePolicy.CropToOverlap or CoveragePolicy.KeepCanvas))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Coverage, "unknown coverage policy");
        }
    }

    /// <summary>Rejects a median configuration that cannot be honoured.</summary>
    internal static void RequireMedianOptions(IntegrationOptions options)
    {
        if (options.Method != IntegrationMethod.Median)
        {
            throw new ArgumentException(
                $"median integration requires {nameof(IntegrationOptions.Method)} == {nameof(IntegrationMethod.Median)}, " +
                $"but it is {options.Method}",
                nameof(options));
        }

        if (!Enum.IsDefined(options.Coverage))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Coverage, "unknown coverage policy");
        }

        if (options.MedianBandRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MedianBandRows,
                "MedianBandRows must be at least 1");
        }

        if (options.MedianScratchBudgetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MedianScratchBudgetBytes,
                "MedianScratchBudgetBytes cannot be negative");
        }

        if (options.MedianInMemoryBudgetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MedianInMemoryBudgetBytes,
                "MedianInMemoryBudgetBytes cannot be negative");
        }

        if (options.MaxRetainedMedianFrames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxRetainedMedianFrames,
                "MaxRetainedMedianFrames cannot be negative");
        }
    }

    // ------------------------------------------------------------------ weights

    /// <summary>
    /// Snapshots and validates per-frame mean/sum weights: each must be finite and non-negative, the
    /// running total must stay finite (so 1e308 + 1e308 is rejected rather than becoming +infinity),
    /// and at least one weight must be positive.
    /// </summary>
    internal static double[]? SnapshotMeanWeights(IReadOnlyList<double>? weights, int frameCount)
    {
        if (weights is null)
        {
            return null;
        }

        var applied = new double[frameCount];
        double total = 0;
        for (int i = 0; i < frameCount; i++)
        {
            double value = weights[i];
            RequireUsableWeight(value, i, "weights");
            applied[i] = value;
            total += value;
        }

        RequireFiniteTotal(total, "weights");
        if (total <= 0)
        {
            throw new ArgumentException("weights must contain at least one positive value", "weights");
        }

        return applied;
    }

    /// <summary>
    /// Median ignores weights, so the only accepted list is absent or uniform 1.0. Anything else is
    /// refused rather than silently dropped: a caller who weighted the frames expects the weighting to
    /// mean something, and a median cannot express it.
    /// </summary>
    internal static void RequireUniformMedianWeights(IReadOnlyList<double>? weights, int frameCount)
    {
        if (weights is null)
        {
            return;
        }

        if (weights.Count != frameCount)
        {
            throw new ArgumentException(
                $"got {weights.Count} weight(s) for {frameCount} frame(s); weights are per frame",
                "weights");
        }

        double total = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            double value = weights[i];
            RequireUsableWeight(value, i, "weights");
            total += value;
            if (Math.Abs(value - 1.0) > 1e-9)
            {
                throw new NotSupportedException(
                    "median integration does not support non-uniform frame weights; " +
                    "duplicate the frames you want to count twice, or use mean/sum.");
            }
        }

        RequireFiniteTotal(total, "weights");
    }

    private static void RequireUsableWeight(double value, int index, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException(
                $"weight {index + 1} is {value}; weights must be finite", parameterName);
        }

        if (value < 0)
        {
            throw new ArgumentException(
                $"weight {index + 1} is negative ({value}); weights must be non-negative", parameterName);
        }
    }

    private static void RequireFiniteTotal(double total, string parameterName)
    {
        if (!double.IsFinite(total))
        {
            throw new ArgumentException(
                $"the weights sum to {total}; the total must be finite. Weights are relative, so divide " +
                "every weight by the same factor — the mean does not change.", parameterName);
        }
    }

    /// <summary>
    /// Rejects a precision buffer any of whose samples is not finite. A finite weight times a 16-bit
    /// sample can still overflow <see cref="double"/> (1e307 x 200 is 2e309), and dividing an infinity
    /// or quantising it would write a plausible-looking wrong pixel. Weights are relative, so scaling
    /// them down is lossless — the caller gets a diagnostic instead of garbage.
    /// </summary>
    internal static void RequireFinitePrecision(double[] precision, IntegrationMethod method)
    {
        for (int i = 0; i < precision.Length; i++)
        {
            double value = precision[i];
            if (!double.IsFinite(value))
            {
                throw new ArgumentException(
                    $"the {method.ToString().ToLowerInvariant()} accumulated to {value} at output sample {i}: the " +
                    "product of the frame weights and samples overflowed double precision. Weights are " +
                    "relative, so divide every weight by the same factor; the result is unchanged.",
                    "weights");
            }
        }
    }

    // ------------------------------------------------------------------ registration

    /// <summary>
    /// Resolves every offset through the shared sub-pixel sampler. The whole-pixel part is applied
    /// exactly; with <see cref="IntegrationOptions.Subpixel"/> on, the fraction is left for the shared
    /// bilinear kernel, and off, the offset is rounded away from zero.
    /// </summary>
    internal static SubpixelSampler[] Placements(
        IReadOnlyList<AlignmentOffset> shifts, IntegrationOptions options, string parameterName)
    {
        var placement = new SubpixelSampler[shifts.Count];
        for (int i = 0; i < shifts.Count; i++)
        {
            AlignmentOffset shift = shifts[i];
            if (!double.IsFinite(shift.Dx) || !double.IsFinite(shift.Dy))
            {
                throw new ArgumentException(
                    $"shift {i + 1} is ({shift.Dx}, {shift.Dy}); offsets must be finite", parameterName);
            }

            placement[i] = SubpixelSampler.FromOffset(shift, options.Subpixel);
        }

        return placement;
    }

    // ------------------------------------------------------------------ CFA phase

    /// <summary>
    /// The mosaic pattern a frame's samples actually have at the first canvas' origin once its
    /// whole-pixel placement is applied. A 2x2 CFA lattice has period two, so an odd shift along
    /// either axis moves the tile to a different one of the four patterns; the input header describes
    /// the frame before registration, not what lands in the output.
    /// </summary>
    internal static BayerPattern AtCanvasOrigin(BayerPattern pattern, SubpixelSampler placement) =>
        GeometricTransforms.TranslatePattern(pattern, placement.Dx, placement.Dy);

    /// <summary>
    /// The pattern of an output frame whose (0, 0) is canvas pixel (<c>rect.X</c>, <c>rect.Y</c>).
    /// Cropping to the overlap can move the phase again: dropping an odd number of columns from the
    /// left of an RGGB mosaic makes the frame's first tile GRBG.
    /// </summary>
    internal static BayerPattern AtRectOrigin(BayerPattern canvasPattern, PixelRect rect) =>
        GeometricTransforms.TranslatePattern(canvasPattern, -rect.X, -rect.Y);

    /// <summary>
    /// Validates one frame's contribution to a mosaic selection and returns its effective pattern.
    /// A fractional placement is refused for a raw mosaic: bilinear resampling needs the four
    /// neighbours around the sample point, which in a CFA image are three different colours plus the
    /// same one again, so the result is not a sample of any colour.
    /// </summary>
    /// <exception cref="BayerPhaseException">A mosaic frame needs a fractional shift.</exception>
    internal static BayerPattern EffectivePattern(int index, BayerPattern pattern, SubpixelSampler placement)
    {
        if (pattern != BayerPattern.None && !placement.IsWholePixel)
        {
            AlignmentOffset applied = placement.Applied;
            throw new BayerPhaseException(
                $"frame {index + 1} is a raw Bayer {pattern} mosaic but its alignment offset " +
                $"({applied.Dx}, {applied.Dy}) has a fractional part. Bilinear resampling blends each " +
                "output sample from four neighbouring pixels, which in a CFA image are different " +
                "colour sites, so the interpolated sample has no single colour. Debayer the frames to " +
                "colour first with -db (or -db=<pattern> to override the detected mosaic pattern) and " +
                "integrate those; in the GUI, enable \"Debayer Monochrome Frames\" before the run. " +
                "Alternatively turn sub-pixel registration off with -stack_subpixel=off so only whole " +
                "pixels move, and only when the rounded offsets are even on both axes, because an odd " +
                "whole-pixel shift moves the CFA phase too.");
        }

        return AtCanvasOrigin(pattern, placement);
    }

    /// <summary>
    /// Requires a frame's effective pattern to equal the selection's. Offsets are compared by the
    /// pattern they produce, not by the frame's header: two frames tagged RGGB and GRBG can share one
    /// phase when the second is registered by an odd column, and two frames both tagged RGGB do not
    /// when one of them is.
    /// </summary>
    /// <exception cref="BayerPhaseException">The effective patterns differ.</exception>
    internal static void RequireSameEffectivePhase(
        int index, BayerPattern pattern, SubpixelSampler placement, BayerPattern expected)
    {
        BayerPattern actual = EffectivePattern(index, pattern, placement);
        if (actual == expected)
        {
            return;
        }

        AlignmentOffset applied = placement.Applied;
        if (pattern == BayerPattern.None || expected == BayerPattern.None)
        {
            string detail = pattern == BayerPattern.None
                ? $"frame {index + 1} is a plain (non-Bayer) frame but frame 1 carries Bayer mosaic pattern {expected}"
                : $"frame {index + 1} carries Bayer mosaic pattern {pattern} but frame 1 is a plain (non-Bayer) frame";

            throw new BayerPhaseException(
                $"{detail}. A CFA plane and a debayered or mono plane cannot be combined; debayer every " +
                "input to colour with -db, or none of them.");
        }

        throw new BayerPhaseException(
            $"frame {index + 1} carries Bayer mosaic pattern {pattern} and its shift ({applied.Dx}, " +
            $"{applied.Dy}) places it at effective pattern {actual}, but frame 1's contribution is at " +
            $"{expected}. Registering a mosaic by an odd number of pixels moves its 2x2 colour tile to " +
            "another phase, so combining these frames would average different colour sites. Debayer the " +
            "frames to colour first with -db (or -db=<pattern> to override the detected mosaic pattern) " +
            "and integrate the colour frames; in the GUI, enable \"Debayer Monochrome Frames\" before the " +
            $"run. Alternatively use frames tagged at the phase their placement produces ({expected}), " +
            "or keep every shift even on both axes (-stack_subpixel=off rounds a fractional shift, but " +
            "an odd whole-pixel offset still moves the phase).");
    }

    /// <summary>
    /// Validates the CFA contract for a whole selection and returns the canvas-origin pattern that all
    /// contributors share (the output tag derives from it, never from the first input header).
    /// </summary>
    /// <exception cref="BayerPhaseException">A frame's effective phase differs, or a mosaic frame needs
    /// a fractional shift.</exception>
    internal static BayerPattern RequireCommonPhase(
        IReadOnlyList<Frame> frames,
        SubpixelSampler[] placement)
    {
        BayerPattern expected = EffectivePattern(0, frames[0].Bayer, placement[0]);
        for (int i = 1; i < frames.Count; i++)
        {
            RequireSameEffectivePhase(i, frames[i].Bayer, placement[i], expected);
        }

        return expected;
    }

    // ------------------------------------------------------------------ median memory

    /// <summary>
    /// The managed-memory budget of the in-memory median route.
    /// </summary>
    /// <remarks>
    /// <see cref="IntegrationOptions.MedianInMemoryBudgetBytes"/> when set; otherwise twice
    /// <see cref="IntegrationOptions.MedianScratchBudgetBytes"/>, saturating at
    /// <see cref="long.MaxValue"/>. The doubling is deliberate and documented: the in-memory route
    /// holds one sample copy per frame and nothing on disk, while the tiled route must additionally
    /// hold the same copy as a scratch file plus the band table. With the defaults (512 MiB scratch)
    /// the in-memory allowance is 1 GiB, which covers <see cref="IntegrationOptions.MaxRetainedMedianFrames"/>
    /// (64) Full-HD RGB frames at 759 MiB — so the frame cap is the binding limit for a typical colour
    /// selection, and the 44-frame Full-HD RGB case that the disk budget refused (522 MiB of scratch)
    /// runs in memory.
    /// </remarks>
    internal static long InMemoryBudget(IntegrationOptions options)
    {
        if (options.MedianInMemoryBudgetBytes > 0)
        {
            return options.MedianInMemoryBudgetBytes;
        }

        return options.MedianScratchBudgetBytes > long.MaxValue / 2
            ? long.MaxValue
            : options.MedianScratchBudgetBytes * 2;
    }

    /// <summary>
    /// Bytes the in-memory route needs: one <see cref="ushort"/> sample copy of every frame, plus the
    /// band table (upper-bounded with the full canvas width, because the output rectangle is narrower
    /// or equal). Saturates instead of throwing so an absurd geometry is routed, not crashed.
    /// </summary>
    internal static long EstimatedInMemoryBytes(
        int frameCount, int width, int height, int channels, IntegrationOptions options)
    {
        long perFrame = Multiply(Multiply(Multiply(width, height), channels), sizeof(ushort));
        long retained = Multiply(perFrame, frameCount);
        int bandRows = Math.Min(Math.Max(options.MedianBandRows, 1), Math.Max(height, 1));
        return Add(retained, BandTableBytes(bandRows, width, channels, frameCount));
    }

    /// <summary>
    /// Bytes of one band table: <c>bandRows x width x channels x frames x 2</c>. It is resident on every
    /// median route, including the streaming one, so it is the term that must always fit a budget.
    /// </summary>
    internal static long BandTableBytes(int bandRows, int width, int channels, int frameCount) =>
        Multiply(Multiply(Multiply(Multiply(bandRows, width), channels), frameCount), sizeof(ushort));

    /// <summary>
    /// Rejects a band configuration whose table alone exceeds the in-memory budget, before any frame is
    /// read or spilled: the alternative is an <see cref="OutOfMemoryException"/> part-way through a run
    /// that has already written scratch.
    /// </summary>
    /// <exception cref="InvalidOperationException">One band does not fit the budget.</exception>
    internal static void RequireBandTableBudget(
        IntegrationSelection selection, int frameCount, IntegrationOptions options)
    {
        int bandRows = Math.Min(Math.Max(options.MedianBandRows, 1), Math.Max(selection.Height, 1));
        long bytes = BandTableBytes(bandRows, selection.Width, selection.Channels, frameCount);
        long budget = InMemoryBudget(options);
        if (bytes > budget)
        {
            throw new InvalidOperationException(
                $"a {bandRows}-row median band over {selection.Width}x{selection.Channels} with " +
                $"{frameCount} frames needs {bytes:N0} bytes, more than the in-memory budget of " +
                $"{budget:N0} ({nameof(IntegrationOptions.EffectiveMedianInMemoryBudgetBytes)}). Lower " +
                $"{nameof(IntegrationOptions.MedianBandRows)} so one band fits, or raise " +
                $"{nameof(IntegrationOptions.MedianInMemoryBudgetBytes)}.");
        }
    }

    /// <summary>
    /// Chooses the median route: in memory when the selection is at or below
    /// <see cref="IntegrationOptions.MaxRetainedMedianFrames"/> <em>and</em> its estimated bytes fit
    /// <see cref="InMemoryBudget"/>; the disk-backed tiled route otherwise.
    /// </summary>
    internal static MedianRoute DecideRoute(
        int frameCount, int width, int height, int channels, IntegrationOptions options, out long estimatedBytes)
    {
        estimatedBytes = EstimatedInMemoryBytes(frameCount, width, height, channels, options);
        if (frameCount > options.MaxRetainedMedianFrames)
        {
            return MedianRoute.Tiled;
        }

        return estimatedBytes <= InMemoryBudget(options) ? MedianRoute.InMemory : MedianRoute.Tiled;
    }

    private static long Multiply(long left, long right)
    {
        if (left < 0 || right < 0)
        {
            return long.MaxValue;
        }

        return left != 0 && right > long.MaxValue / left ? long.MaxValue : left * right;
    }

    private static long Add(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    // ------------------------------------------------------------------ median selection

    /// <summary>
    /// Validates a list-shaped median selection and returns its registration, geometry/provenance and
    /// per-frame origin names. Route selection happens after this: a selection that cannot be
    /// integrated must fail the same way whichever route it would have taken.
    /// </summary>
    internal static void RequireMedianSelection(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        out SubpixelSampler[] placement,
        out IntegrationSelection selection,
        out string?[] sourceNames)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("at least one frame is required", nameof(frames));
        }

        if (shifts.Count != frames.Count)
        {
            throw new ArgumentException(
                $"{shifts.Count} shifts for {frames.Count} frames; the lists must be index-aligned",
                nameof(shifts));
        }

        Frame first = frames[0] ?? throw new ArgumentException("frame 0 is null", nameof(frames));
        int width = first.Width;
        int height = first.Height;
        int channels = first.Channels;
        int bitDepth = first.BitDepth;

        for (int i = 0; i < frames.Count; i++)
        {
            Frame frame = frames[i] ?? throw new ArgumentException($"frame {i} is null", nameof(frames));
            RequireCompatibleFrame(frame, i, width, height, channels, bitDepth, nameof(frames));
        }

        placement = Placements(shifts, options, nameof(shifts));
        BayerPattern canvasPattern = RequireCommonPhase(frames, placement);

        sourceNames = new string?[frames.Count];
        string? sourceName = null;
        for (int i = 0; i < frames.Count; i++)
        {
            sourceNames[i] = frames[i].SourceName;
            if (sourceName is null && !string.IsNullOrWhiteSpace(sourceNames[i]))
            {
                sourceName = sourceNames[i];
            }
        }

        selection = new IntegrationSelection(
            width, height, channels, bitDepth, canvasPattern, first.Index, sourceName);
    }

    /// <summary>
    /// Requires one frame to match the selection's geometry, depth and channel count, and to keep its
    /// Bayer marker off a multi-channel frame.
    /// </summary>
    /// <exception cref="ArgumentException">Any of those differ.</exception>
    internal static void RequireCompatibleFrame(
        Frame frame, int index, int width, int height, int channels, int bitDepth, string parameterName)
    {
        if (frame.Width != width || frame.Height != height)
        {
            throw new ArgumentException(
                $"frame {index + 1} is {frame.Width}x{frame.Height} but frame 1 is {width}x{height}; " +
                "all frames must share one geometry",
                parameterName);
        }

        if (frame.Channels != channels)
        {
            throw new ArgumentException(
                $"frame {index + 1} has {frame.Channels} channel(s) but frame 1 has {channels}; " +
                "all frames must share one channel count",
                parameterName);
        }

        if (frame.BitDepth != bitDepth)
        {
            throw new ArgumentException(
                $"frame {index + 1} is {frame.BitDepth}-bit but frame 1 is {bitDepth}-bit; " +
                "all frames must share one depth",
                parameterName);
        }

        if (frame.Channels != 1 && frame.Bayer != BayerPattern.None)
        {
            throw new ArgumentException(
                $"frame {index + 1} is a colour frame but carries Bayer pattern {frame.Bayer}; " +
                "Bayer markers apply only to single-channel frames",
                parameterName);
        }
    }

    // ------------------------------------------------------------------ frame preparation

    /// <summary>
    /// Writes <paramref name="source"/> into <paramref name="destination"/> in the first frame's canvas
    /// coordinates: an exact copy for a zero shift, a whole-pixel translation, or the shared bilinear
    /// kernel quantised with <see cref="FrameIntegrator.Quantise"/>. Pixels no source sample covers stay
    /// at zero. Used by both median routes so their stores hold identical bytes.
    /// </summary>
    internal static void PrepareFrame(
        Frame source,
        SubpixelSampler placement,
        Frame destination,
        int maxValue,
        CancellationToken cancellationToken)
    {
        if (placement.IsWholePixel)
        {
            if (placement.Dx == 0 && placement.Dy == 0)
            {
                Array.Copy(source.Samples, destination.Samples, source.Samples.Length);
            }
            else
            {
                ShiftInto(source, destination.Samples, placement.Dx, placement.Dy);
            }

            return;
        }

        placement.ResampleInto(source, destination.Samples, maxValue, cancellationToken);
    }

    /// <summary>
    /// Copies <paramref name="frame"/> into <paramref name="destination"/> translated by
    /// (<paramref name="dx"/>, <paramref name="dy"/>), leaving pixels no source sample covers as zero.
    /// </summary>
    internal static void ShiftInto(Frame frame, ushort[] destination, int dx, int dy)
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
}
