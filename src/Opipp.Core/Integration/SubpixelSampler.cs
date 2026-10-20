namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;

/// <summary>
/// The one sub-pixel registration kernel: an alignment offset split into a whole-pixel translation
/// and the fractional remainder applied by bilinear resampling.
/// </summary>
/// <remarks>
/// <para><b>Direction.</b> Destination pixel <c>x</c> samples source coordinate <c>x - Dx - Fx</c>,
/// so a positive <c>Dx</c> moves content right and a positive <c>Dy</c> moves it down, the same
/// convention as <see cref="FrameAligner.ShiftWithBackground"/>.</para>
/// <para><b>Whole pixels.</b> With sub-pixel off the offset is rounded to whole pixels with
/// <see cref="MidpointRounding.AwayFromZero"/> and the fraction is zero, matching the pre-0.2
/// registration exactly. An integral offset with sub-pixel on also leaves no fraction, so it takes
/// the same whole-pixel path.</para>
/// <para><b>Borders.</b> Bilinear resampling needs both taps of the kernel, so a fractional shift
/// loses one border pixel on the side it samples from: <see cref="Left"/> and <see cref="Top"/> move
/// inward by one when the corresponding fraction is non-zero. <see cref="Right"/> and
/// <see cref="Bottom"/> are the last destination pixel the frame can fill. Callers crop to the
/// intersection of those ranges instead of clamping the sample at the edge, which would invent
/// data.</para>
/// <para><b>One kernel for both methods.</b> <see cref="FrameIntegrator"/> (mean/sum) and
/// <see cref="TiledMedianIntegrator"/> (median) both resolve their placement through
/// <see cref="FromOffset"/> and both take per-sample values from <see cref="SamplePixel"/>, so the
/// two methods cannot disagree about where a frame's content lands or how it is interpolated. The
/// mean keeps the interpolated <see cref="double"/>; the median quantises it with
/// <see cref="FrameIntegrator.Quantise"/> before spilling, which is the same rule the mean applies
/// on output.</para>
/// </remarks>
internal readonly struct SubpixelSampler
{
    private SubpixelSampler(int dx, int dy, double fx, double fy)
    {
        Dx = dx;
        Dy = dy;
        Fx = fx;
        Fy = fy;
    }

    /// <summary>Whole-pixel part of the horizontal displacement.</summary>
    public int Dx { get; }

    /// <summary>Whole-pixel part of the vertical displacement.</summary>
    public int Dy { get; }

    /// <summary>Fractional horizontal displacement left to the bilinear kernel, in <c>[0, 1)</c>.</summary>
    public double Fx { get; }

    /// <summary>Fractional vertical displacement left to the bilinear kernel, in <c>[0, 1)</c>.</summary>
    public double Fy { get; }

    /// <summary>True when no resampling is needed and an exact integer copy applies.</summary>
    public bool IsWholePixel => Fx == 0 && Fy == 0;

    /// <summary>
    /// Splits <paramref name="offset"/> into a whole-pixel part and a fraction. With
    /// <paramref name="subpixel"/> false the offset is rounded away from zero and the fraction is
    /// zero; with it true the whole part is the floor and the fraction is the remainder.
    /// </summary>
    public static SubpixelSampler FromOffset(AlignmentOffset offset, bool subpixel)
    {
        if (!subpixel)
        {
            return new SubpixelSampler(
                (int)Math.Round(offset.Dx, MidpointRounding.AwayFromZero),
                (int)Math.Round(offset.Dy, MidpointRounding.AwayFromZero),
                0,
                0);
        }

        int dx = (int)Math.Floor(offset.Dx);
        int dy = (int)Math.Floor(offset.Dy);
        return new SubpixelSampler(dx, dy, offset.Dx - dx, offset.Dy - dy);
    }

    /// <summary>
    /// First destination column whose two bilinear taps exist. With a fraction the floor tap sits one
    /// pixel lower, so the low side loses the border pixel the kernel samples from.
    /// </summary>
    public int Left => Dx + (Fx > 0 ? 1 : 0);

    /// <summary>First destination row whose two bilinear taps exist.</summary>
    public int Top => Dy + (Fy > 0 ? 1 : 0);

    /// <summary>Last destination column the frame can fill.</summary>
    public int Right(int width) => Dx + width - 1;

    /// <summary>Last destination row the frame can fill.</summary>
    public int Bottom(int height) => Dy + height - 1;

    /// <summary>The displacement actually applied, for provenance.</summary>
    public AlignmentOffset Applied => new(Dx + Fx, Dy + Fy);

    /// <summary>Resolves the two source columns and the weight of the second for destination column
    /// <paramref name="x"/>.</summary>
    public AxisTap HorizontalTap(int x) => ResolveTap(x - Dx - Fx);

    /// <summary>Resolves the two source rows and the weight of the second for destination row
    /// <paramref name="y"/>.</summary>
    public AxisTap VerticalTap(int y) => ResolveTap(y - Dy - Fy);

    /// <summary>
    /// Samples <paramref name="frame"/> at destination pixel (<paramref name="x"/>,
    /// <paramref name="y"/>) for every channel, writing one value per channel into
    /// <paramref name="values"/>.
    /// </summary>
    public void SamplePixel(Frame frame, int x, int y, Span<double> values) =>
        SamplePixel(frame, x, y, HorizontalTap(x), VerticalTap(y), values);

    /// <summary>
    /// Samples with pre-resolved taps. Callers that walk a row resolve the row once and the columns
    /// once, then reuse both here; the arithmetic is identical to the per-pixel overload.
    /// </summary>
    public void SamplePixel(
        Frame frame,
        int x,
        int y,
        in AxisTap column,
        in AxisTap row,
        Span<double> values)
    {
        int channels = frame.Channels;
        ushort[] samples = frame.Samples;
        int width = frame.Width;
        int a = ((row.Low * width) + column.Low) * channels;

        if (IsWholePixel)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                values[ch] = samples[a + ch];
            }

            return;
        }

        int b = ((row.Low * width) + column.High) * channels;
        int c = ((row.High * width) + column.Low) * channels;
        int d = ((row.High * width) + column.High) * channels;

        double ax = column.Fraction;
        double ay = row.Fraction;
        double w00 = (1 - ax) * (1 - ay);
        double w01 = ax * (1 - ay);
        double w10 = (1 - ax) * ay;
        double w11 = ax * ay;

        for (int ch = 0; ch < channels; ch++)
        {
            values[ch] = (samples[a + ch] * w00) + (samples[b + ch] * w01) +
                         (samples[c + ch] * w10) + (samples[d + ch] * w11);
        }
    }

    /// <summary>
    /// Resamples <paramref name="source"/> into <paramref name="destination"/> in destination-canvas
    /// coordinates (same geometry), quantising each interpolated sample with
    /// <see cref="FrameIntegrator.Quantise"/> at <paramref name="maxValue"/> for the frame's depth.
    /// Pixels outside the sampler's valid rectangle are cleared to zero, exactly the uncovered border
    /// the integer shift leaves, so the median's spill and mean's accumulation agree about which
    /// destination pixels a frame can supply.
    /// </summary>
    /// <returns>The destination rectangle that received data; zero-sized when the frame falls
    /// entirely outside its own canvas.</returns>
    public PixelRect ResampleInto(
        Frame source,
        ushort[] destination,
        int maxValue,
        CancellationToken cancellationToken = default)
    {
        int width = source.Width;
        int height = source.Height;
        int channels = source.Channels;
        int required = checked(width * height * channels);
        if (destination.Length != required)
        {
            throw new ArgumentException(
                $"destination has {destination.Length} sample(s) but {width}x{height}x{channels} needs {required}",
                nameof(destination));
        }

        Array.Clear(destination);

        int xStart = Math.Max(0, Left);
        int xEnd = Math.Min(width - 1, Right(width));
        int yStart = Math.Max(0, Top);
        int yEnd = Math.Min(height - 1, Bottom(height));
        if (xStart > xEnd || yStart > yEnd)
        {
            return new PixelRect(0, 0, 0, 0);
        }

        Span<double> values = stackalloc double[3];
        for (int y = yStart; y <= yEnd; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = VerticalTap(y);
            for (int x = xStart; x <= xEnd; x++)
            {
                SamplePixel(source, x, y, HorizontalTap(x), row, values);
                int target = ((y * width) + x) * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    destination[target + ch] = FrameIntegrator.Quantise(values[ch], maxValue);
                }
            }
        }

        return new PixelRect(xStart, yStart, xEnd - xStart + 1, yEnd - yStart + 1);
    }

    private static AxisTap ResolveTap(double source)
    {
        int low = (int)Math.Floor(source);
        double fraction = source - low;
        return new AxisTap(low, fraction == 0 ? low : low + 1, fraction);
    }

    /// <summary>
    /// One axis of the bilinear kernel: the two source indices and the fraction of the second tap.
    /// </summary>
    public readonly struct AxisTap
    {
        internal AxisTap(int low, int high, double fraction)
        {
            Low = low;
            High = high;
            Fraction = fraction;
        }

        /// <summary>Source index of the first tap.</summary>
        public int Low { get; }

        /// <summary>Source index of the second tap; equal to <see cref="Low"/> when the fraction is
        /// zero, so a whole-pixel axis costs no extra sample.</summary>
        public int High { get; }

        /// <summary>Weight of <see cref="High"/>; <see cref="Low"/> takes <c>1 - Fraction</c>.</summary>
        public double Fraction { get; }
    }
}
