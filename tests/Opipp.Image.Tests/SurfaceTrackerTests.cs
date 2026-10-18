namespace Opipp.Image.Tests;

using Opipp.Core.Frames;
using Opipp.Core.Alignment;

/// <summary>
/// Surface-feature tracking (<c>-en_surface_feature_tracking</c>) contract tests.
/// </summary>
/// <remarks>
/// The tracker is the engine's: a 5×5-blurred, percentile-stretched template matched by a LUT-SSD
/// score over an asymmetric window of <c>[−box/16, +box/16 − 1]</c>. That window is a property of the
/// algorithm, so fixtures use a box large enough for the displacement under test (a 48-pixel box
/// searches −3…+2, exactly as the original does).
/// </remarks>
public class SurfaceTrackerTests
{
    private const int Background = 20;
    private const int Peak = 235;

    // ---------------------------------------------------------------- fixtures

    /// <summary>Soft-edged disc: a smooth, unambiguous feature on a flat background.</summary>
    private static Frame Disc(int width, int height, double cx, double cy, double radius,
                              int background = Background, int peak = Peak)
    {
        var frame = new Frame(width, height, 1, 8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - cx, dy = y - cy;
                double coverage = Math.Clamp(radius + 0.5 - Math.Sqrt((dx * dx) + (dy * dy)), 0, 1);
                frame.Samples[(y * width) + x] =
                    (ushort)Math.Round(background + ((peak - background) * coverage));
            }
        }

        return frame;
    }

    /// <summary>A disc with fine structure, so the match is unique and not rotationally ambiguous.</summary>
    private static Frame Textured(int width, int height)
    {
        var frame = Disc(width, height, width / 2.0, height / 2.0, 18, 60, 200);
        uint state = 12345u;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                state = (state * 1664525u) + 1013904223u;
                int n = (int)((state >> 16) % 61) - 30;
                int i = (y * width) + x;
                frame.Samples[i] = (ushort)Math.Clamp(frame.Samples[i] + n, 0, 255);
            }
        }
        return frame;
    }

    /// <summary>Deterministic uniform noise in [-amplitude, +amplitude].</summary>
    private static Frame AddNoise(Frame source, uint seed, int amplitude)
    {
        var result = source.Clone();
        uint state = seed;
        uint span = (uint)((2 * amplitude) + 1);
        for (int i = 0; i < result.Samples.Length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            int noise = (int)((state >> 16) % span) - amplitude;
            result.Samples[i] = (ushort)Math.Clamp(result.Samples[i] + noise, 0, 255);
        }

        return result;
    }

    /// <summary>Re-encodes a mono frame as R=G=B, which Rec.601 luma maps back to the identical value.</summary>
    private static Frame ToRgb(Frame luma)
    {
        var rgb = new Frame(luma.Width, luma.Height, 3, 8);
        for (int i = 0; i < luma.Width * luma.Height; i++)
        {
            rgb.Samples[i * 3] = luma.Samples[i];
            rgb.Samples[(i * 3) + 1] = luma.Samples[i];
            rgb.Samples[(i * 3) + 2] = luma.Samples[i];
        }

        return rgb;
    }

    /// <summary>Scales 8-bit samples into the 16-bit range (v * 257 maps 255 to 65535 exactly).</summary>
    private static Frame To16Bit(Frame source) =>
        new(source.Width, source.Height, 1, 16, source.Samples.Select(v => (ushort)(v * 257)).ToArray());

    /// <summary>Applies gain then a constant offset, clamping to the 8-bit range.</summary>
    private static Frame GainOffset(Frame source, double gain, int offset)
    {
        var result = source.Clone();
        for (int i = 0; i < result.Samples.Length; i++)
        {
            result.Samples[i] = (ushort)Math.Clamp((int)Math.Round((source.Samples[i] * gain) + offset), 0, 255);
        }

        return result;
    }

    /// <summary>Square box of side <c>2*half + 1</c> centred on a pixel.</summary>
    private static PixelRect Box(int cx, int cy, int half) =>
        new(cx - half, cy - half, (2 * half) + 1, (2 * half) + 1);

    private static AlignmentOffset Measured(Frame reference, Frame frame, PixelRect box) =>
        SurfaceTracker.Measure(reference, frame, 0, box)
        ?? throw new InvalidOperationException("expected the tracker to find the pattern");

    // ------------------------------------------------------------------ motion

    [Fact]
    public void Measure_IdenticalFrames_ReportsZero()
    {
        var frame = Textured(160, 160);
        var box = Box(80, 80, 24); // 48 px → search window −3…+2

        var measured = Measured(frame, frame, box);

        Assert.Equal(0, measured.Dx);
        Assert.Equal(0, measured.Dy);
    }

    [Theory]
    [InlineData(2, -1)]
    [InlineData(-3, 2)]
    [InlineData(0, -2)]
    public void Measure_KnownTranslation_ReturnsTheDisplacement(int dx, int dy)
    {
        var reference = Textured(160, 160);
        var frame = new Frame(160, 160, 1, 8);
        // Translate the content by (dx, dy) with wraparound so the frame stays fully populated.
        for (int y = 0; y < 160; y++)
            for (int x = 0; x < 160; x++)
            {
                int sx = (((x - dx) % 160) + 160) % 160;
                int sy = (((y - dy) % 160) + 160) % 160;
                frame.Samples[(y * 160) + x] = reference.Samples[(sy * 160) + sx];
            }

        var measured = Measured(reference, frame, Box(80, 80, 24));

        Assert.Equal(dx, measured.Dx);
        Assert.Equal(dy, measured.Dy);
    }

    [Fact]
    public void Measure_GainAndOffset_DoNotMoveTheMeasurement()
    {
        var reference = Textured(160, 160);
        var frame = GainOffset(reference, 1.15, 8);

        var measured = Measured(reference, frame, Box(80, 80, 24));

        Assert.Equal(0, measured.Dx);
        Assert.Equal(0, measured.Dy);
    }

    [Fact]
    public void Measure_NoisyFrame_StillReportsZero()
    {
        var reference = Textured(160, 160);
        var frame = AddNoise(reference, 7u, 4);

        var measured = Measured(reference, frame, Box(80, 80, 24));

        Assert.Equal(0, measured.Dx);
        Assert.Equal(0, measured.Dy);
    }

    [Fact]
    public void Measure_ColourAndMonoFramesAgree()
    {
        var mono = Textured(160, 160);
        var colour = ToRgb(mono);

        var measured = Measured(colour, colour, Box(80, 80, 24));

        Assert.Equal(0, measured.Dx);
        Assert.Equal(0, measured.Dy);
    }

    [Fact]
    public void Measure_SixteenBitFramesMatchEightBitFrames()
    {
        var mono = Textured(160, 160);
        var wide = To16Bit(mono);

        var measured = Measured(wide, wide, Box(80, 80, 24));

        Assert.Equal(0, measured.Dx);
        Assert.Equal(0, measured.Dy);
    }

    // ------------------------------------------------------------- rejections

    [Fact]
    public void Measure_FlatBox_ReturnsNull()
    {
        var flat = new Frame(160, 160, 1, 8);
        for (int i = 0; i < flat.Samples.Length; i++) flat.Samples[i] = 100;

        Assert.Null(SurfaceTracker.Measure(flat, flat, 0, Box(80, 80, 24)));
    }

    [Fact]
    public void Measure_BoxOutsideTheFrame_ReturnsNull()
    {
        var frame = Textured(160, 160);
        Assert.Null(SurfaceTracker.Measure(frame, frame, 0, new PixelRect(200, 200, 32, 32)));
        Assert.Null(SurfaceTracker.Measure(frame, frame, 0, new PixelRect(10, 10, 0, 5)));
        Assert.Null(SurfaceTracker.Measure(frame, frame, 0, new PixelRect(48, 48, 10, -3)));
    }

    // ------------------------------------------------------------- validation

    [Fact]
    public void Measure_DifferentGeometry_Throws()
    {
        var reference = Textured(160, 160);
        var smaller = Textured(120, 120);

        Assert.Throws<ArgumentException>(() =>
            SurfaceTracker.Measure(reference, smaller, 0, Box(60, 60, 24)));
    }

    [Fact]
    public void Measure_NegativeRadius_Throws()
    {
        var frame = Textured(160, 160);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SurfaceTracker.Measure(frame, frame, -1, Box(80, 80, 24)));
    }

    [Fact]
    public void Measure_NullArguments_Throw()
    {
        var frame = Textured(160, 160);
        Assert.Throws<ArgumentNullException>(() =>
            SurfaceTracker.Measure(null!, frame, 0, Box(80, 80, 24)));
        Assert.Throws<ArgumentNullException>(() =>
            SurfaceTracker.Measure(frame, null!, 0, Box(80, 80, 24)));
    }
}
