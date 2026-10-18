namespace Pipp.Image.Tests;

using Pipp.Core.Frames;
using Pipp.Core.Alignment;

/// <summary>
/// Surface-feature tracking (<c>-en_surface_feature_tracking</c>) contract tests. Every fixture is a
/// synthetic frame whose content is known exactly: a soft-edged disc on a flat background, optionally
/// displaced by <see cref="FrameAligner.Shift"/> (the same resampler the pipeline uses), noised or
/// re-encoded. The expected displacement is therefore the value the fixture was built with, not a
/// number measured from the implementation.
/// </summary>
public class SurfaceTrackerTests
{
    private const int Background = 20;
    private const int Peak = 235;

    // ---------------------------------------------------------------- fixtures

    /// <summary>8-bit mono frame holding a soft-edged disc; coverage is anti-aliased so the profile
    /// across the peak is smooth enough for the sub-pixel fit to have a well-defined answer.</summary>
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

    /// <summary>Element-wise maximum, used to place two independent objects in one frame.</summary>
    private static Frame Merge(Frame a, Frame b)
    {
        var result = a.Clone();
        for (int i = 0; i < result.Samples.Length; i++)
        {
            result.Samples[i] = Math.Max(a.Samples[i], b.Samples[i]);
        }

        return result;
    }

    /// <summary>Deterministic uniform noise in [-amplitude, +amplitude]; an LCG rather than
    /// <see cref="Random"/> so the fixture is identical on every runtime.</summary>
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

    /// <summary>Runs the tracker and fails the test with a readable message when it rejects.</summary>
    private static AlignmentOffset Measured(Frame reference, Frame frame, int searchRadius, PixelRect box) =>
        SurfaceTracker.Measure(reference, frame, searchRadius, box)
        ?? throw new InvalidOperationException("expected the tracker to find the pattern");

    // ------------------------------------------------------------------ motion

    [Fact]
    public void Measure_IdenticalFrames_ReportsZeroOnBothAxes()
    {
        var reference = Disc(96, 96, 48, 48, 12);
        var copy = reference.Clone();

        var sameInstance = Measured(reference, reference, 4, Box(48, 48, 16));
        var sameContent = Measured(reference, copy, 4, Box(48, 48, 16));

        Assert.InRange(sameInstance.Dx, -0.02, 0.02);
        Assert.InRange(sameInstance.Dy, -0.02, 0.02);
        Assert.InRange(sameContent.Dx, -0.02, 0.02);
        Assert.InRange(sameContent.Dy, -0.02, 0.02);
    }

    [Fact]
    public void Measure_KnownTranslation_ReturnsTheDisplacement()
    {
        // The convention matches FrameAligner.EstimateTranslation: positive X means the tracked
        // content sits further right in `frame`. Shifting the reference by (7, -5) therefore has to
        // come back as (7, -5), not the negated "shift to apply" form.
        var reference = Disc(96, 96, 48, 48, 12);
        var frame = FrameAligner.Shift(reference, new AlignmentOffset(7, -5));

        var offset = Measured(reference, frame, 12, Box(48, 48, 16));

        Assert.Equal((7, -5), offset.Round());
        Assert.InRange(offset.Dx, 6.7, 7.3);
        Assert.InRange(offset.Dy, -5.3, -4.7);
    }

    [Fact]
    public void Measure_HalfPixelTranslation_RefinesBelowOnePixel()
    {
        // A bilinearly shifted disc puts the correlation peak at 2.5 / -1.5; the integer grid alone
        // could only offer 2 or 3, so a result this close to the half pixel can only come from the
        // parabola fit on the peak and its neighbours.
        var reference = Disc(96, 96, 48, 48, 12);
        var frame = FrameAligner.Shift(reference, new AlignmentOffset(2.5, -1.5));

        var offset = Measured(reference, frame, 8, Box(48, 48, 16));

        Assert.InRange(offset.Dx, 2.2, 2.8);
        Assert.InRange(offset.Dy, -1.8, -1.2);
    }

    [Fact]
    public void Measure_SearchRadiusZero_ReportsNoMovement()
    {
        var reference = Disc(64, 64, 32, 32, 9);
        var frame = FrameAligner.Shift(reference, new AlignmentOffset(3, 2));

        var offset = Measured(reference, frame, 0, Box(32, 32, 12));

        Assert.Equal(0.0, offset.Dx, 9);
        Assert.Equal(0.0, offset.Dy, 9);
    }

    [Fact]
    public void Measure_NoisyFrame_RecoversTheTranslation()
    {
        // Independent uniform noise on both frames survives the correlation as a roughly 2% energy
        // term, so the peak still sits on the true offset but the fixture cannot be solved by
        // comparing pixels exactly.
        var reference = AddNoise(Disc(96, 96, 48, 48, 12), seed: 12345u, amplitude: 25);
        var frame = AddNoise(FrameAligner.Shift(reference, new AlignmentOffset(5, -4)), seed: 999u, amplitude: 25);

        var offset = Measured(reference, frame, 12, Box(48, 48, 16));

        Assert.Equal((5, -4), offset.Round());
    }

    [Fact]
    public void Measure_PatternOutsideTheSearchRadius_ReturnsNull()
    {
        // The disc sits 30 px away and the search only reaches 6, so every candidate window is pure
        // background: no variance, no correlation, no measurement.
        var reference = Disc(96, 96, 48, 48, 10);
        var frame = Disc(96, 96, 78, 48, 10);

        Assert.Null(SurfaceTracker.Measure(reference, frame, 6, Box(48, 48, 10)));
    }

    [Fact]
    public void Measure_BoxTracksOnlyItsOwnObject()
    {
        // Two discs: the one inside the box moves +4 px, the brighter one outside moves -9 px. The
        // measurement must follow the box, not the brightest thing in the frame.
        var reference = Merge(Disc(96, 96, 40, 48, 8, peak: 200), Disc(96, 96, 80, 48, 8, peak: 255));
        var frame = Merge(Disc(96, 96, 44, 48, 8, peak: 200), Disc(96, 96, 71, 48, 8, peak: 255));

        var offset = Measured(reference, frame, 8, Box(40, 48, 10));

        Assert.Equal((4, 0), offset.Round());
    }

    // ---------------------------------------------------------------- formats

    [Fact]
    public void Measure_ColourAndMonoFramesAgree()
    {
        // R=G=B projects to the same luma sample, so the colour and mono paths must measure the same
        // sub-pixel displacement - including the mixed case of a colour reference against a mono frame.
        var reference = Disc(96, 96, 48, 48, 12);
        var frame = FrameAligner.Shift(reference, new AlignmentOffset(-6, 4));
        var box = Box(48, 48, 16);

        var mono = Measured(reference, frame, 10, box);
        var colour = Measured(ToRgb(reference), ToRgb(frame), 10, box);
        var mixed = Measured(ToRgb(reference), frame, 10, box);

        Assert.Equal(mono.Dx, colour.Dx, 9);
        Assert.Equal(mono.Dy, colour.Dy, 9);
        Assert.Equal(mono.Dx, mixed.Dx, 9);
        Assert.Equal(mono.Dy, mixed.Dy, 9);
        Assert.Equal((-6, 4), mono.Round());
    }

    [Fact]
    public void Measure_SixteenBitFramesMatchEightBitFrames()
    {
        // Correlation is scale-free: multiplying every sample by 257 changes neither the peak nor the
        // parabola fit, so a 16-bit re-encode has to report the same offset as the 8-bit original.
        var reference = Disc(96, 96, 48, 48, 12);
        var frame = FrameAligner.Shift(reference, new AlignmentOffset(4, 6));
        var box = Box(48, 48, 16);

        var eightBit = Measured(reference, frame, 10, box);
        var sixteenBit = Measured(To16Bit(reference), To16Bit(frame), 10, box);

        Assert.Equal(8, reference.BitDepth);
        Assert.Equal(16, To16Bit(reference).BitDepth);
        Assert.Equal(eightBit.Dx, sixteenBit.Dx, 9);
        Assert.Equal(eightBit.Dy, sixteenBit.Dy, 9);
        Assert.Equal((4, 6), sixteenBit.Round());
    }

    [Fact]
    public void Measure_GainAndBrightnessChange_DoNotMoveTheMeasurement()
    {
        // Zero-mean normalisation is what makes the tracker usable across a run: a captured frame that
        // is both 4% brighter and sitting on a +8 pedestal has the same pattern, and it must report the
        // same displacement as the clean reference. (A raw SAD or cosine score would drift with the
        // pedestal.) 235 * 1.04 + 8 = 252.4 stays inside the 8-bit range, so nothing clips.
        var reference = Disc(96, 96, 48, 48, 12);
        var frame = GainOffset(FrameAligner.Shift(reference, new AlignmentOffset(6, 3)), gain: 1.04, offset: 8);
        var box = Box(48, 48, 16);

        var clean = Measured(reference, FrameAligner.Shift(reference, new AlignmentOffset(6, 3)), 10, box);
        var altered = Measured(reference, frame, 10, box);

        Assert.Equal((6, 3), altered.Round());
        Assert.Equal(clean.Dx, altered.Dx, 6);
        Assert.Equal(clean.Dy, altered.Dy, 6);
    }

    // -------------------------------------------------------------- rejection

    [Fact]
    public void Measure_FeaturelessBox_ReturnsNull()
    {
        // A flat box has zero variance: there is nothing to correlate, and the score is undefined.
        var reference = new Frame(64, 64, 1, 8);
        for (int i = 0; i < reference.Samples.Length; i++) reference.Samples[i] = 100;
        var frame = new Frame(64, 64, 1, 8);
        for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = 160;

        Assert.Null(SurfaceTracker.Measure(reference, frame, 4, new PixelRect(16, 16, 32, 32)));
    }

    [Fact]
    public void Measure_FlatFrameAgainstDetailedReference_ReturnsNull()
    {
        // The reference box has structure but the frame is a blank field (clouded out / defocused), so
        // every candidate window is featureless and no offset can reach the acceptance threshold.
        var reference = Disc(64, 64, 32, 32, 9);
        var frame = new Frame(64, 64, 1, 8);
        for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = Background;

        Assert.Null(SurfaceTracker.Measure(reference, frame, 6, Box(32, 32, 12)));
    }

    [Fact]
    public void Measure_BoxOutsideTheFrame_ReturnsNull()
    {
        var reference = Disc(96, 96, 48, 48, 12);

        Assert.Null(SurfaceTracker.Measure(reference, reference, 4, new PixelRect(200, 200, 20, 20)));
        Assert.Null(SurfaceTracker.Measure(reference, reference, 4, new PixelRect(10, 10, 0, 5)));
        Assert.Null(SurfaceTracker.Measure(reference, reference, 4, new PixelRect(48, 48, 10, -3)));
    }

    // ------------------------------------------------------------ guard rails

    [Fact]
    public void Measure_MismatchedGeometry_Throws()
    {
        var reference = Disc(96, 96, 48, 48, 12);
        var smaller = Disc(64, 64, 32, 32, 8);

        Assert.Throws<ArgumentException>(() =>
            SurfaceTracker.Measure(reference, smaller, 4, Box(48, 48, 8)));
    }

    [Fact]
    public void Measure_NegativeSearchRadius_Throws()
    {
        var reference = Disc(96, 96, 48, 48, 12);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SurfaceTracker.Measure(reference, reference, -1, Box(48, 48, 8)));
    }

    [Fact]
    public void Measure_NullFrames_Throw()
    {
        var reference = Disc(96, 96, 48, 48, 12);

        Assert.Throws<ArgumentNullException>(() =>
            SurfaceTracker.Measure(null!, reference, 4, Box(48, 48, 8)));
        Assert.Throws<ArgumentNullException>(() =>
            SurfaceTracker.Measure(reference, null!, 4, Box(48, 48, 8)));
    }

    [Fact]
    public void Measure_DoesNotMutateItsInputs()
    {
        var reference = Disc(96, 96, 48, 48, 12);
        var frame = FrameAligner.Shift(reference, new AlignmentOffset(3, 3));
        var referenceSnapshot = (ushort[])reference.Samples.Clone();
        var frameSnapshot = (ushort[])frame.Samples.Clone();

        _ = SurfaceTracker.Measure(reference, frame, 6, Box(48, 48, 16));

        Assert.Equal(referenceSnapshot, reference.Samples);
        Assert.Equal(frameSnapshot, frame.Samples);
    }
}
