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
/// <para><b>Accumulation and quantisation.</b> Every contribution is accumulated in
/// <see cref="double"/>. The unquantised result is exposed through
/// <see cref="IntegrationResult.PrecisionSamples"/> in the output depth's scale. The quantised frame
/// rounds with half away from zero (<see cref="MidpointRounding.AwayFromZero"/>) and then clamps to
/// the output depth (255 for 8-bit, 65535 for 16-bit). Since samples and weights are non-negative,
/// half away from zero is half-up at every <c>.5</c> boundary: 100.5 becomes 101, and a sum above the
/// depth maximum saturates.</para>
/// <para><b>Coverage.</b> <see cref="IntegrationResult.Coverage"/> has one entry per pixel of the
/// first frame's canvas, row-major, and counts the frames that can supply that pixel. Under
/// <see cref="CoveragePolicy.CropToOverlap"/> the output frame is the intersection of the frames'
/// valid regions (in first-frame canvas coordinates, recorded as
/// <see cref="IntegrationResult.OutputRect"/>); every pixel of it has full coverage. Under
/// <see cref="CoveragePolicy.KeepCanvas"/> the frame keeps the first frame's canvas, pixels no frame
/// can supply are written as <see cref="FillSample"/> and reported with coverage 0, and
/// <see cref="IntegrationResult.OutputRect"/> is the whole canvas.</para>
/// <para><b>Bayer.</b> A colour (three-channel) frame must not carry a Bayer marker, and all frames
/// must share one mosaic pattern; mixing patterns throws <see cref="BayerPhaseException"/> before any
/// sample is combined. The result frame keeps the common pattern (always
/// <see cref="BayerPattern.None"/> for colour).</para>
/// <para>Median is not handled here: the exact, disk-backed median lives in
/// <c>TiledMedianIntegrator</c>, which a facade will route to.</para>
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
    /// carrying a Bayer marker, a non-finite offset, or a non-finite/negative weight. The message
    /// names the reason.
    /// </exception>
    /// <exception cref="BayerPhaseException">The frames carry different Bayer patterns.</exception>
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

        if (options.Method == IntegrationMethod.Median)
        {
            throw new NotSupportedException(
                "Median integration is not implemented by FrameIntegrator. The exact, disk-backed " +
                "median path is TiledMedianIntegrator; a facade will route median requests there.");
        }

        if (options.Method is not (IntegrationMethod.Mean or IntegrationMethod.Sum))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Method, "unknown integration method");
        }

        if (options.Coverage is not (CoveragePolicy.CropToOverlap or CoveragePolicy.KeepCanvas))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Coverage, "unknown coverage policy");
        }

        var first = frames[0]
            ?? throw new ArgumentException("frames[0] is null", nameof(frames));
        int width = first.Width, height = first.Height, channels = first.Channels, depth = first.BitDepth;
        RequireColourHasNoBayer(first, 1, nameof(frames));
        for (int i = 1; i < frames.Count; i++)
        {
            var frame = frames[i]
                ?? throw new ArgumentException($"frames[{i}] is null", nameof(frames));

            if (frame.Width != width || frame.Height != height)
            {
                throw new ArgumentException(
                    $"frame {i + 1} is {frame.Width}x{frame.Height} but frame 1 is {width}x{height}; " +
                    "all frames must share one geometry",
                    nameof(frames));
            }

            if (frame.Channels != channels)
            {
                throw new ArgumentException(
                    $"frame {i + 1} has {frame.Channels} channel(s) but frame 1 has {channels}; " +
                    "all frames must share one channel count",
                    nameof(frames));
            }

            if (frame.BitDepth != depth)
            {
                throw new ArgumentException(
                    $"frame {i + 1} is {frame.BitDepth}-bit but frame 1 is {depth}-bit; " +
                    "all frames must share one depth",
                    nameof(frames));
            }

            RequireColourHasNoBayer(frame, i + 1, nameof(frames));

            if (frame.Bayer != first.Bayer)
            {
                throw new BayerPhaseException(
                    $"frame {i + 1} has Bayer pattern {frame.Bayer} but frame 1 has {first.Bayer}. " +
                    "Combining different mosaic phases would mix colour sites. Debayer the inputs to " +
                    "colour, or use frames with one pattern and alignment shifts of matching parity.");
            }
        }

        // Snapshot and validate the weights so a caller's mutable list cannot change mid-run.
        double[]? appliedWeights = null;
        if (weights is not null)
        {
            appliedWeights = new double[frames.Count];
            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double value = weights[i];
                if (!double.IsFinite(value))
                {
                    throw new ArgumentException(
                        $"weight {i + 1} is {value}; weights must be finite", nameof(weights));
                }

                if (value < 0)
                {
                    throw new ArgumentException(
                        $"weight {i + 1} is negative ({value}); weights must be non-negative", nameof(weights));
                }

                appliedWeights[i] = value;
                total += value;
            }

            if (total <= 0)
            {
                throw new ArgumentException("weights must contain at least one positive value", nameof(weights));
            }
        }

        var placement = new SubpixelSampler[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            AlignmentOffset shift = shifts[i];
            if (!double.IsFinite(shift.Dx) || !double.IsFinite(shift.Dy))
            {
                throw new ArgumentException(
                    $"shift {i + 1} is ({shift.Dx}, {shift.Dy}); offsets must be finite", nameof(shifts));
            }

            placement[i] = SubpixelSampler.FromOffset(shift, options.Subpixel);
        }

        cancellationToken.ThrowIfCancellationRequested();

        PixelRect outputRect = ComputeOutputRect(placement, width, height, options.Coverage, nameof(shifts));

        int pixels = checked(width * height);
        int sampleCount = checked(pixels * channels);
        var coverage = new int[pixels];
        var precision = new double[sampleCount];
        var pixelWeight = new double[pixels];
        int maxValue = depth == 8 ? 255 : 65535;

        for (int i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Accumulate(
                frames[i],
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

        var result = new Frame(outputRect.Width, outputRect.Height, channels, depth)
        {
            Bayer = first.Bayer,
            Index = first.Index,
            SourceName = first.SourceName,
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

        var contributors = new IntegrationContributor[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            contributors[i] = new IntegrationContributor(
                frames[i].SourceName ?? string.Empty,
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

    private static void RequireColourHasNoBayer(Frame frame, int ordinal, string parameterName)
    {
        if (frame.Channels != 1 && frame.Bayer != BayerPattern.None)
        {
            throw new ArgumentException(
                $"frame {ordinal} is a colour frame but carries Bayer pattern {frame.Bayer}; " +
                "Bayer markers apply only to single-channel frames",
                parameterName);
        }
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
