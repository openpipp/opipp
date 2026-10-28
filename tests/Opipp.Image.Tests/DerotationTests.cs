namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Quality;

/// <summary>
/// Field-rotation correction (<see cref="Derotator"/>) contract tests.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are analytic: <see cref="CrateredDisc"/> evaluates a cratered disc at coordinates that
/// are rotated by an exact angle, so a sequence built from it has an exact ground truth and carries no
/// resampling error of its own. Only the estimator and <see cref="Derotator.Rotate"/> interpolate, which
/// is what the 0.15 degree accuracy claim is about.
/// </para>
/// <para>
/// Signs follow the contract: <see cref="Derotator.EstimateAngles"/> reports the clockwise correction
/// that undoes a frame's rotation, so a frame whose content is rotated by +t reports -t.
/// </para>
/// </remarks>
public class DerotationTests
{
    private const int Background = 18;
    private const int Peak = 235;

    /// <summary>Craters as (u, v, radius, depth) in units of the disc radius.</summary>
    private static readonly (double U, double V, double Radius, double Depth)[] Craters =
    {
        (0.55, -0.38, 0.26, 0.55),
        (-0.62, 0.42, 0.20, 0.48),
        (0.12, 0.72, 0.16, 0.42),
        (-0.28, -0.68, 0.13, 0.38),
        (0.80, 0.46, 0.11, 0.34),
        (-0.84, -0.12, 0.09, 0.30),
        (-0.10, 0.18, 0.07, 0.26),
    };

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// The scene in its own coordinates: a soft-edged disc, seven craters and one bright speck. Nothing
    /// about it is rotationally symmetric, so every rotation has a unique correlation peak.
    /// </summary>
    private static double Scene(double dx, double dy, int size)
    {
        double radius = size * 0.40;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double coverage = Math.Clamp(radius + 0.5 - distance, 0, 1);
        if (coverage <= 0) return Background;

        double value = Background + ((Peak - Background) * coverage);
        foreach (var crater in Craters)
        {
            double craterX = crater.U * radius, craterY = crater.V * radius;
            double craterRadius = crater.Radius * radius;
            double d = Math.Sqrt(((dx - craterX) * (dx - craterX)) + ((dy - craterY) * (dy - craterY)));
            double cover = Math.Clamp(craterRadius + 0.5 - d, 0, 1);
            if (cover > 0) value -= crater.Depth * (Peak - Background) * cover;
        }

        double speckX = 0.72 * radius, speckY = -0.80 * radius;
        double speckRadius = Math.Max(1.2, 0.035 * radius);
        double speckDistance = Math.Sqrt(((dx - speckX) * (dx - speckX)) + ((dy - speckY) * (dy - speckY)));
        value += 0.30 * (Peak - Background) * Math.Clamp(speckRadius + 0.5 - speckDistance, 0, 1);

        return Math.Clamp(value, 0, 255);
    }

    /// <summary>
    /// A frame whose content is rotated clockwise by <paramref name="contentDegrees"/>: the scene is
    /// sampled at the inverse rotation, the same convention as <see cref="Derotator.Rotate"/>.
    /// </summary>
    private static Frame CrateredDisc(
        int size,
        double contentDegrees = 0,
        double offsetX = 0,
        double offsetY = 0,
        bool craters = true)
    {
        double theta = contentDegrees * Math.PI / 180.0;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double centre = (size - 1) / 2.0;
        var frame = new Frame(size, size, 1, 8);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - centre - offsetX;
                double dy = y - centre - offsetY;
                double u = (dx * cos) + (dy * sin);
                double v = (-dx * sin) + (dy * cos);
                frame.Samples[(y * size) + x] = (ushort)Math.Round(craters ? Scene(u, v, size) : SmoothDisc(u, v, size));
            }
        }

        return frame;
    }

    /// <summary>The disc without craters: high contrast, but no angular information at all.</summary>
    private static double SmoothDisc(double dx, double dy, int size)
    {
        double radius = size * 0.40;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double coverage = Math.Clamp(radius + 0.5 - distance, 0, 1);
        return Background + ((Peak - Background) * coverage);
    }

    /// <summary>
    /// A textured surface with no limb: broadband aperiodic relief over the whole frame, like the
    /// surface of a planet at high resolution. Nothing here is invariant under a rotation about the
    /// centre, so every part of it records the field's rotation.
    /// </summary>
    private static Frame TexturedSurface(int size, double contentDegrees = 0)
    {
        double theta = contentDegrees * Math.PI / 180.0;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double centre = (size - 1) / 2.0;
        var frame = new Frame(size, size, 1, 8);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - centre, dy = y - centre;
                double u = (dx * cos) + (dy * sin);
                double v = (-dx * sin) + (dy * cos);

                // Four octaves, roughly 1/f: fine detail drives the sharpness metric, coarse detail
                // is what survives the matcher's downsampling.
                double value = 40
                    + (18 * Noise(u, v, 1.8)) + (26 * Noise(u + 13, v - 7, 3.5))
                    + (34 * Noise(u - 11, v + 3, 7.0)) + (42 * Noise(u + 7, v - 19, 14.0));

                frame.Samples[(y * size) + x] = (ushort)Math.Clamp(Math.Round(value), 0, 255);
            }
        }

        return frame;
    }

    /// <summary>Deterministic value noise in 0..1 on a lattice of <paramref name="cell"/> pixels.</summary>
    private static double Noise(double x, double y, double cell)
    {
        double fx = x / cell, fy = y / cell;
        int i0 = (int)Math.Floor(fx), j0 = (int)Math.Floor(fy);
        double tx = fx - i0, ty = fy - j0;
        double sx = tx * tx * (3 - (2 * tx)), sy = ty * ty * (3 - (2 * ty));

        double v00 = Hash(i0, j0), v10 = Hash(i0 + 1, j0);
        double v01 = Hash(i0, j0 + 1), v11 = Hash(i0 + 1, j0 + 1);
        double top = v00 + ((v10 - v00) * sx);
        double bottom = v01 + ((v11 - v01) * sx);
        return top + ((bottom - top) * sy);
    }

    /// <summary>Integer hash, so the fixture is identical on every platform and every run.</summary>
    private static double Hash(int i, int j)
    {
        uint h = (uint)((i * 374761393) + (j * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h % 100003u) / 100003.0;
    }

    /// <summary>A flat frame of one value.</summary>
    private static Frame Flat(int size, int value)
    {
        var frame = new Frame(size, size, 1, 8);
        Array.Fill(frame.Samples, (ushort)value);
        return frame;
    }

    /// <summary>Mean of a stack, rounded back to the frames' sample range.</summary>
    private static Frame MeanStack(IReadOnlyList<Frame> frames)
    {
        var first = frames[0];
        var stack = new Frame(first.Width, first.Height, first.Channels, first.BitDepth);
        int maximum = first.BitDepth == 8 ? 255 : 65535;

        for (int i = 0; i < stack.Samples.Length; i++)
        {
            double sum = 0;
            foreach (var frame in frames) sum += frame.Samples[i];
            stack.Samples[i] = (ushort)Math.Clamp((int)Math.Round(sum / frames.Count), 0, maximum);
        }

        return stack;
    }

    /// <summary>
    /// Variance of the 4-neighbour Laplacian over the annulus between <paramref name="innerFraction"/>
    /// and <paramref name="outerFraction"/> of the half-width: the edge-sharpness metric of the
    /// field-rotation review, which reported 41 against 1539 on a real 1.45 degree Moon sequence.
    /// </summary>
    /// <remarks>
    /// The annulus matters. Field rotation turns the frame about its centre, so detail within a few tens
    /// of pixels of the centre is displaced by well under a pixel at any realistic angle; including it
    /// would flatter translation-only stacking with a region where rotation cannot cost anything.
    /// </remarks>
    private static double LaplacianVariance(Frame frame, double innerFraction, double outerFraction)
    {
        var luma = QualityMetrics.ToLuma(frame);
        int width = frame.Width, height = frame.Height;
        double centreX = (width - 1) / 2.0, centreY = (height - 1) / 2.0;
        double inner = innerFraction * width / 2.0, outer = outerFraction * width / 2.0;

        double sum = 0, sumSquares = 0;
        int count = 0;
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                double radius = Math.Sqrt(((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY)));
                if (radius < inner || radius > outer) continue;

                double laplacian = (4.0 * luma[(y * width) + x])
                    - luma[((y - 1) * width) + x]
                    - luma[((y + 1) * width) + x]
                    - luma[(y * width) + x - 1]
                    - luma[(y * width) + x + 1];
                sum += laplacian;
                sumSquares += laplacian * laplacian;
                count++;
            }
        }

        double mean = sum / count;
        return (sumSquares / count) - (mean * mean);
    }

    // ---------------------------------------------------------------- common crop

    [Fact]
    public void CommonCrop_ZeroDegreesIsTheWholeFrame()
    {
        Assert.Equal(new PixelRect(0, 0, 640, 480), Derotator.CommonCrop(640, 480, 0));

        // 180 degrees is the same rotated rectangle, so it must not shrink the crop either.
        Assert.Equal(new PixelRect(0, 0, 640, 480), Derotator.CommonCrop(640, 480, 180));
        Assert.Equal(new PixelRect(0, 0, 192, 192), Derotator.CommonCrop(192, 192, 360));
    }

    [Theory]
    [InlineData(640, 480, 1.0, 7, 5, 625, 469)]
    [InlineData(640, 480, 2.0, 14, 11, 611, 458)]
    [InlineData(640, 480, 5.0, 32, 24, 575, 431)]
    [InlineData(192, 192, 1.45, 2, 2, 187, 187)]
    [InlineData(256, 256, 2.0, 4, 4, 247, 247)]
    public void CommonCrop_MatchesTheHandComputedRectangle(
        int width, int height, double angle, int x, int y, int cropWidth, int cropHeight)
    {
        Assert.Equal(new PixelRect(x, y, cropWidth, cropHeight), Derotator.CommonCrop(width, height, angle));
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(192, 192)]
    [InlineData(1024, 768)]
    public void CommonCrop_StaysInsideTheRotatedFrame(int width, int height)
    {
        double centreX = width / 2.0, centreY = height / 2.0;

        for (double angle = 0; angle <= 6.0; angle += 0.25)
        {
            var crop = Derotator.CommonCrop(width, height, angle);
            Assert.InRange(crop.Width, 1, width);
            Assert.InRange(crop.Height, 1, height);

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                double theta = sign * angle * Math.PI / 180.0;
                double cos = Math.Cos(theta), sin = Math.Sin(theta);

                foreach (var corner in new[]
                {
                    (X: (double)crop.X, Y: (double)crop.Y),
                    (X: (double)crop.X + crop.Width, Y: (double)crop.Y),
                    (X: (double)crop.X, Y: (double)crop.Y + crop.Height),
                    (X: (double)crop.X + crop.Width, Y: (double)crop.Y + crop.Height),
                })
                {
                    double dx = corner.X - centreX, dy = corner.Y - centreY;
                    double rotatedX = centreX + (dx * cos) - (dy * sin);
                    double rotatedY = centreY + (dx * sin) + (dy * cos);

                    // 0.75 px covers the whole-pixel flooring and the half-pixel centring offset.
                    Assert.InRange(rotatedX, -0.75, width + 0.75);
                    Assert.InRange(rotatedY, -0.75, height + 0.75);
                }
            }
        }
    }

    [Fact]
    public void CommonCrop_ShrinksAsTheAngleGrows()
    {
        int previousWidth = int.MaxValue, previousHeight = int.MaxValue;
        for (double angle = 0; angle <= 10.0; angle += 0.25)
        {
            var crop = Derotator.CommonCrop(640, 480, angle);
            Assert.True(crop.Width <= previousWidth, $"width grew at {angle} degrees");
            Assert.True(crop.Height <= previousHeight, $"height grew at {angle} degrees");
            previousWidth = crop.Width;
            previousHeight = crop.Height;
        }
    }

    [Fact]
    public void CommonCrop_ClampsToAtLeastOnePixel()
    {
        // A very wide strip rotated by 45 degrees: the height limit is under a pixel, so it clamps.
        var crop = Derotator.CommonCrop(512, 8, 45);
        Assert.Equal(1, crop.Height);
        Assert.InRange(crop.Width, 1, 512);
        Assert.InRange(crop.X, 0, 511);

        var narrow = Derotator.CommonCrop(8, 512, 45);
        Assert.Equal(1, narrow.Width);
    }

    // ---------------------------------------------------------------- rotate

    [Fact]
    public void Rotate_ZeroDegreesCopiesTheFrameExactly()
    {
        var frame = CrateredDisc(64);
        frame.Index = 7;
        frame.SourceName = "capture.ser";

        var rotated = Derotator.Rotate(frame, 0);

        Assert.NotSame(frame, rotated);
        Assert.Equal(frame.Samples, rotated.Samples);
        Assert.Equal(64, rotated.Width);
        Assert.Equal(64, rotated.Height);
        Assert.Equal(1, rotated.Channels);
        Assert.Equal(8, rotated.BitDepth);
        Assert.Equal(BayerPattern.None, rotated.Bayer);
        Assert.Equal(7, rotated.Index);
        Assert.Equal("capture.ser", rotated.SourceName);
    }

    [Fact]
    public void Rotate_KeepsTheInteriorOfAConstantFrameConstant()
    {
        const int width = 129, height = 97;
        var frame = new Frame(width, height, 1, 8);
        Array.Fill(frame.Samples, (ushort)128);

        var rotated = Derotator.Rotate(frame, 17.0);

        int marginX = width / 5, marginY = height / 5;
        for (int y = marginY; y < height - marginY; y++)
        {
            for (int x = marginX; x < width - marginX; x++)
            {
                Assert.Equal((ushort)128, rotated.Samples[(y * width) + x]);
            }
        }

        // Bilinear taps outside the frame take the zero background, so the corners vacated by the
        // rotation are black rather than smeared edge values.
        Assert.Equal((ushort)0, rotated.Samples[0]);
        Assert.Equal((ushort)0, rotated.Samples[width - 1]);
    }

    [Fact]
    public void Rotate_QuarterTurnPermutesEveryColourPlaneExactly()
    {
        const int size = 32;
        var frame = new Frame(size, size, 3, 8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = ((y * size) + x) * 3;
                frame.Samples[i] = (ushort)(x * 3);
                frame.Samples[i + 1] = (ushort)(y * 5);
                frame.Samples[i + 2] = (ushort)(x + y);
            }
        }

        var rotated = Derotator.Rotate(frame, 90);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // A clockwise quarter turn puts the source of (x,y) at (y, size-1-x).
                int source = (((size - 1 - x) * size) + y) * 3;
                int destination = ((y * size) + x) * 3;
                Assert.Equal(frame.Samples[source], rotated.Samples[destination]);
                Assert.Equal(frame.Samples[source + 1], rotated.Samples[destination + 1]);
                Assert.Equal(frame.Samples[source + 2], rotated.Samples[destination + 2]);
            }
        }
    }

    [Fact]
    public void Rotate_KeepsGeometryDepthAndChannels()
    {
        var frame = new Frame(80, 48, 3, 16);
        var rotated = Derotator.Rotate(frame, -1.25);

        Assert.Equal(80, rotated.Width);
        Assert.Equal(48, rotated.Height);
        Assert.Equal(3, rotated.Channels);
        Assert.Equal(16, rotated.BitDepth);
        Assert.Equal(frame.SampleCount, rotated.SampleCount);
    }

    [Fact]
    public void Rotate_RejectsABayerMosaic()
    {
        var frame = CrateredDisc(48);
        frame.Bayer = BayerPattern.GBRG;

        var error = Assert.Throws<ArgumentException>(() => Derotator.Rotate(frame, 0.5));

        Assert.Contains("Bayer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GBRG", error.Message);
    }

    // ---------------------------------------------------------------- centroid

    [Fact]
    public void Centroid_IsNullWhenNothingPassesTheThreshold()
    {
        Assert.Null(Derotator.Centroid(Flat(32, 10), 100));
        Assert.Null(Derotator.Centroid(new Frame(32, 32, 1, 8), 1));
    }

    [Fact]
    public void Centroid_OfGreyColourEqualsMono()
    {
        const int size = 96;
        var mono = CrateredDisc(size);
        var colour = new Frame(size, size, 3, 8);
        for (int i = 0; i < mono.Samples.Length; i++)
        {
            ushort value = mono.Samples[i];
            colour.Samples[i * 3] = value;
            colour.Samples[(i * 3) + 1] = value;
            colour.Samples[(i * 3) + 2] = value;
        }

        var monoCentroid = Derotator.Centroid(mono, 100);
        var colourCentroid = Derotator.Centroid(colour, 100);

        Assert.NotNull(monoCentroid);
        Assert.NotNull(colourCentroid);
        Assert.Equal(monoCentroid.Value.X, colourCentroid.Value.X, 6);
        Assert.Equal(monoCentroid.Value.Y, colourCentroid.Value.Y, 6);
    }

    // ---------------------------------------------------------------- translation by correlation

    /// <summary>An integer shift has to come back exactly, in the offset convention the shift uses.</summary>
    [Theory]
    [InlineData(7, -5)]
    [InlineData(-9, 4)]
    [InlineData(0, 11)]
    [InlineData(3, 3)]
    public void MatchOffset_RecoversAWholePixelTranslation(int dx, int dy)
    {
        const int size = 128;
        var reference = TexturedSurface(size);
        var moved = FrameAligner.Shift(reference, new AlignmentOffset(dx, dy));

        var offset = Derotator.MatchOffset(reference, moved, 16);

        // The offset undoes the shift that produced `moved`, so it is the negative of it.
        Assert.Equal(-dx, offset.Dx, 0);
        Assert.Equal(-dy, offset.Dy, 0);
    }

    /// <summary>A fraction of a pixel is measured by the parabola, not rounded to the nearest whole one.</summary>
    [Theory]
    [InlineData(3.4, -2.25)]
    [InlineData(-1.75, 4.6)]
    [InlineData(0.5, 0.5)]
    public void MatchOffset_ResolvesSubPixelTranslations(double dx, double dy)
    {
        const int size = 160;
        var reference = TexturedSurface(size);
        var moved = FrameAligner.Shift(reference, new AlignmentOffset(dx, dy));

        var offset = Derotator.MatchOffset(reference, moved, 12);

        Assert.Equal(-dx, offset.Dx, 1);
        Assert.Equal(-dy, offset.Dy, 1);
    }

    /// <summary>
    /// Correlation beats the centroid on a limb-lit disc, which is why the pipeline uses it: the
    /// centroid of the synthetic disc is not the disc's centre, so a frame shifted by a whole pixel
    /// still has a centroid difference of a fraction of one.
    /// </summary>
    [Fact]
    public void MatchOffset_AgreesWithTheAppliedShiftWhereTheCentroidDoesNot()
    {
        const int size = 160;
        var reference = CrateredDisc(size);
        var moved = FrameAligner.Shift(reference, new AlignmentOffset(6, -4));
        int threshold = FrameAligner.OtsuThreshold(reference);

        var byCorrelation = Derotator.MatchOffset(reference, moved, 12);
        var referenceCentroid = Derotator.Centroid(reference, threshold);
        var movedCentroid = Derotator.Centroid(moved, threshold);
        Assert.NotNull(referenceCentroid);
        Assert.NotNull(movedCentroid);

        double centroidDx = referenceCentroid.Value.X - movedCentroid.Value.X;

        Assert.Equal(-6, byCorrelation.Dx, 0);
        Assert.Equal(4, byCorrelation.Dy, 0);
        Assert.True(
            Math.Abs(centroidDx - 6) > 0.25,
            $"the fixture no longer shows the centroid's bias (dx {centroidDx:F2})");
    }

    // ---------------------------------------------------------------- angle estimation

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.2)]
    [InlineData(-0.2)]
    [InlineData(0.5)]
    [InlineData(-0.5)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(1.45)]
    [InlineData(2.0)]
    [InlineData(-2.0)]
    public void EstimateAngles_RecoversAKnownRotation(double contentDegrees)
    {
        var frames = new[] { CrateredDisc(192), CrateredDisc(192, contentDegrees) };
        var options = new DerotationOptions { Keyframes = 2, SampleSide = 192 };

        var angles = Derotator.EstimateAngles(frames, options);

        Assert.Equal(2, angles.Length);
        Assert.Equal(0.0, angles[0]);
        Assert.True(
            Math.Abs(angles[1] + contentDegrees) <= 0.15,
            $"content rotated {contentDegrees:F2} deg; expected correction {-contentDegrees:F2} deg, got {angles[1]:F3} deg");
    }

    [Fact]
    public void EstimateAngles_InterpolatesTheCurveBetweenKeyframes()
    {
        const int count = 9;
        var frames = new Frame[count];
        var content = new double[count];
        for (int i = 0; i < count; i++)
        {
            content[i] = -0.8 + (1.6 * i / (count - 1));
            frames[i] = CrateredDisc(160, content[i]);
        }

        var angles = Derotator.EstimateAngles(frames, new DerotationOptions { Keyframes = 3, SampleSide = 160 });

        Assert.Equal(count, angles.Length);
        for (int i = 0; i < count; i++)
        {
            // Angles are measured against frame 0, whose own content is -0.8 degrees.
            double expected = content[0] - content[i];
            Assert.True(
                Math.Abs(angles[i] - expected) <= 0.2,
                $"frame {i}: content {content[i]:F2} deg, expected {expected:F2}, got {angles[i]:F3}");
        }
    }

    [Fact]
    public void EstimateAngles_ReturnsOneAnglePerFrameAndIsDeterministic()
    {
        var frames = new Frame[5];
        for (int i = 0; i < frames.Length; i++) frames[i] = CrateredDisc(160, -0.6 + (0.3 * i));
        var options = new DerotationOptions { Keyframes = 3, SampleSide = 160 };

        var first = Derotator.EstimateAngles(frames, options);
        var second = Derotator.EstimateAngles(frames, options);

        Assert.Equal(frames.Length, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(0.0, first[0]);
        Assert.All(first, angle => Assert.True(Math.Abs(angle) <= 1.5));
    }

    [Fact]
    public void EstimateAngles_FeaturelessFramesYieldZero()
    {
        var flat = Flat(160, 128);
        var flatAngles = Derotator.EstimateAngles(
            new[] { flat, flat.Clone() }, new DerotationOptions { Keyframes = 2, SampleSide = 160 });
        Assert.All(flatAngles, angle => Assert.Equal(0.0, angle));

        // One sample level of contrast: there is a shape, but nothing to correlate a rotation against.
        var quiet = Flat(160, 128);
        var quietFaint = CrateredDisc(160);
        for (int i = 0; i < quiet.Samples.Length; i++)
        {
            quietFaint.Samples[i] = (ushort)(quietFaint.Samples[i] > 30 ? 129 : 128);
        }

        var quietAngles = Derotator.EstimateAngles(
            new[] { quietFaint, quiet.Clone() }, new DerotationOptions { Keyframes = 2, SampleSide = 160 });
        Assert.All(quietAngles, angle => Assert.Equal(0.0, angle));
    }

    [Fact]
    public void EstimateAngles_KeyframeWithNoObjectYieldsZero()
    {
        // Nothing in the second frame clears the threshold, so there is no centroid to anchor on and
        // the correction must be "none" rather than the first angle the sweep happens to try.
        var frames = new[] { CrateredDisc(160), new Frame(160, 160, 1, 8) };
        var angles = Derotator.EstimateAngles(frames, new DerotationOptions { Keyframes = 2, SampleSide = 160 });

        Assert.Equal(0.0, angles[1]);
    }

    [Fact]
    public void EstimateAngles_RotationallySymmetricObjectYieldsZero()
    {
        var frames = new[] { CrateredDisc(160, craters: false), CrateredDisc(160, 1.5, craters: false) };
        var angles = Derotator.EstimateAngles(frames, new DerotationOptions { Keyframes = 2, SampleSide = 160 });

        Assert.Equal(0.0, angles[1]);
    }

    [Fact]
    public void EstimateAngles_HonoursCancellation()
    {
        var frames = new Frame[4];
        for (int i = 0; i < frames.Length; i++) frames[i] = CrateredDisc(128, 0.1 * i);
        var options = new DerotationOptions { Keyframes = 4, SampleSide = 128 };

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => Derotator.EstimateAngles(frames, options, cancellation.Token));
    }

    [Fact]
    public void EstimateAngles_SingleFrameIsZero()
    {
        var angles = Derotator.EstimateAngles(new[] { CrateredDisc(64) }, new DerotationOptions());
        Assert.Equal(new[] { 0.0 }, angles);
    }

    // ---------------------------------------------------------------- prepare

    [Fact]
    public void Prepare_AppliesTheAngleTheCropAndTheCentringTranslation()
    {
        const int size = 160;
        double centre = (size - 1) / 2.0;
        var frame = CrateredDisc(size, 0, offsetX: 11, offsetY: -8);
        var crop = Derotator.CommonCrop(size, size, 0);
        var reference = (size / 2.0, size / 2.0);

        var prepared = Derotator.Prepare(frame, 0, crop, reference, null);

        Assert.Equal(crop.Width, prepared.Frame.Width);
        Assert.Equal(crop.Height, prepared.Frame.Height);
        Assert.Equal(0.0, prepared.AngleDegrees);
        Assert.Equal(frame.Channels, prepared.Frame.Channels);
        Assert.Equal(frame.BitDepth, prepared.Frame.BitDepth);

        var centroid = Derotator.Centroid(prepared.Frame, FrameAligner.OtsuThreshold(prepared.Frame));
        Assert.NotNull(centroid);

        // Applying the reported offset has to put the object on the reference point.
        Assert.Equal(reference.Item1, centroid.Value.X + prepared.Offset.Dx, 6);
        Assert.Equal(reference.Item2, centroid.Value.Y + prepared.Offset.Dy, 6);

        // And that offset is (up to the scene's own asymmetry) the displacement of the object.
        Assert.True(Math.Abs(prepared.Offset.Dx + 11) < 2.0, $"dx {prepared.Offset.Dx:F2}");
        Assert.True(Math.Abs(prepared.Offset.Dy - 8) < 2.0, $"dy {prepared.Offset.Dy:F2}");
        Assert.True(centroid.Value.X > centre, "the object should still sit right of centre before the offset");
    }

    [Fact]
    public void Prepare_ReportsTheObjectPositionInCroppedCoordinates()
    {
        const int size = 160;
        var crop = Derotator.CommonCrop(size, size, 1.0);
        var frame = CrateredDisc(size, 0, offsetX: -6, offsetY: 5);

        var prepared = Derotator.Prepare(frame, 0.7, crop, (size / 2.0, size / 2.0), 60);

        Assert.Equal(0.7, prepared.AngleDegrees);
        Assert.Equal(crop.Width, prepared.Frame.Width);
        Assert.Equal(crop.Height, prepared.Frame.Height);

        var centroid = Derotator.Centroid(prepared.Frame, 60);
        Assert.NotNull(centroid);
        Assert.Equal(size / 2.0, centroid.Value.X + prepared.Offset.Dx, 6);
        Assert.Equal(size / 2.0, centroid.Value.Y + prepared.Offset.Dy, 6);
    }

    [Fact]
    public void Prepare_RejectsACropOutsideTheFrame()
    {
        var frame = CrateredDisc(64);
        Assert.Throws<ArgumentException>(
            () => Derotator.Prepare(frame, 0, new PixelRect(100, 100, 10, 10), (0, 0), null));
    }

    /// <summary>
    /// A capture that turns fast is followed: four degrees a frame over eight frames is 28 degrees
    /// end to end, six times the default search range, and every frame still has to land on its angle.
    /// </summary>
    [Fact]
    public void EstimateAngles_FollowsAFastFieldRotation()
    {
        const int size = 192, count = 8;
        const double perFrame = 4.0;

        var frames = new Frame[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = TexturedSurface(size, i * perFrame);
        }

        var angles = Derotator.EstimateAngles(frames, new DerotationOptions { Keyframes = 12 });

        Assert.Equal(count, angles.Length);
        Assert.Equal(0.0, angles[0]);
        for (int i = 1; i < count; i++)
        {
            Assert.True(
                Math.Abs(angles[i] + (i * perFrame)) <= 0.35,
                $"frame {i}: expected {-i * perFrame:F2} deg, got {angles[i]:F3}");
        }
    }

    /// <summary>
    /// The rotation kernel has to be bicubic, not bilinear: bilinear resampling is a low-pass, and on
    /// a stack of thirty rotated frames its blur is the difference between keeping half the single
    /// frame's edge energy and almost none of it.
    /// </summary>
    [Fact]
    public void Rotate_BicubicKeepsMoreEdgeSharpnessThanBilinear()
    {
        const int size = 256;
        var surface = TexturedSurface(size);

        var bicubic = Derotator.Rotate(surface, 1.7);
        var bilinear = RotateBilinear(surface, 1.7);

        double bicubicVariance = LaplacianVariance(bicubic, 0.35, 0.48);
        double bilinearVariance = LaplacianVariance(bilinear, 0.35, 0.48);

        // Measured on this fixture: 84 against 45, a factor of 1.9. The margin is asserted at 1.5 so
        // the test is about the kernel's character, not about one fixture's exact numbers.
        Assert.True(
            bicubicVariance > 1.5 * bilinearVariance,
            $"bicubic kept {bicubicVariance:F0} of the edge variance, bilinear {bilinearVariance:F0}");
    }

    /// <summary>A bilinear rotation about the image centre, for comparison with <see cref="Derotator.Rotate"/>.</summary>
    private static Frame RotateBilinear(Frame frame, double angleDegrees)
    {
        int size = frame.Width;
        double theta = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double centre = (size - 1) / 2.0;
        var result = new Frame(size, size, 1, 8);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - centre, dy = y - centre;
                double sx = centre + (dx * cos) + (dy * sin);
                double sy = centre - (dx * sin) + (dy * cos);
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                double fx = sx - x0, fy = sy - y0;
                double value = 0;
                for (int j = 0; j < 2; j++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        int px = x0 + i, py = y0 + j;
                        if ((uint)px >= (uint)size || (uint)py >= (uint)size) continue;
                        double weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                        value += frame.Samples[(py * size) + px] * weight;
                    }
                }

                result.Samples[(y * size) + x] = (ushort)Math.Clamp((int)Math.Round(value), 0, 255);
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- end to end

    /// <summary>
    /// The review's case: a sequence whose field rotates cannot be stacked by translation alone, because
    /// every frame is smeared by a different rotation. Derotating first has to keep the edge sharpness
    /// that translation-only stacking loses.
    /// </summary>
    /// <remarks>
    /// Twenty-four frames over four degrees, which is what a long alt-az capture looks like. The
    /// survey measures the annulus outside 35% of the half-width, where a rotation about the frame
    /// centre actually moves detail by several pixels.
    /// </remarks>
    [Fact]
    public void Derotation_KeepsEdgeSharpnessThatTranslationOnlyStackingLoses()
    {
        const int size = 768, count = 24;
        const double span = 4.0; // +/- 2 degrees across the sequence

        var scene = TexturedSurface(size);
        var frames = new Frame[count];
        var content = new double[count];
        for (int i = 0; i < count; i++)
        {
            content[i] = (-span / 2) + (span * i / (count - 1));
            frames[i] = Derotator.Rotate(scene, content[i]);
        }

        int threshold = FrameAligner.OtsuThreshold(frames[0]);
        var referenceCentroid = Derotator.Centroid(frames[0], threshold);
        Assert.NotNull(referenceCentroid);

        // What PIPP does today: integer centring, no rotation correction.
        var translated = new Frame[count];
        for (int i = 0; i < count; i++)
        {
            var centroid = Derotator.Centroid(frames[i], threshold);
            Assert.NotNull(centroid);
            translated[i] = FrameAligner.Shift(frames[i], new AlignmentOffset(
                Math.Round(referenceCentroid.Value.X - centroid.Value.X),
                Math.Round(referenceCentroid.Value.Y - centroid.Value.Y)));
        }

        var translationStack = MeanStack(translated);

        // Rotation correction: estimate the curve, derotate into one common crop, centre, average.
        var options = new DerotationOptions { Keyframes = 8, SampleSide = 256 };
        var angles = Derotator.EstimateAngles(frames, options);
        for (int i = 0; i < count; i++)
        {
            // Angles are relative to frame 0, which carries the sequence's first rotation.
            double expected = content[0] - content[i];
            Assert.True(
                Math.Abs(angles[i] - expected) <= 0.15,
                $"frame {i}: content {content[i]:F2} deg, expected {expected:F2}, got {angles[i]:F3}");
        }

        var crop = Derotator.CommonCrop(size, size, span / 2);

        // Prepare with a zero reference reports minus the centroid as the offset, which is how the
        // reference object's position in cropped coordinates is read back.
        var referencePrepared = Derotator.Prepare(frames[0], angles[0], crop, (0, 0), threshold);
        var cropCentroid = (-referencePrepared.Offset.Dx, -referencePrepared.Offset.Dy);

        var derotated = new Frame[count];
        for (int i = 0; i < count; i++)
        {
            var prepared = Derotator.Prepare(frames[i], angles[i], crop, cropCentroid, threshold);
            derotated[i] = FrameAligner.Shift(prepared.Frame, new AlignmentOffset(
                Math.Round(prepared.Offset.Dx), Math.Round(prepared.Offset.Dy)));
        }

        var derotatedStack = MeanStack(derotated);

        double single = LaplacianVariance(frames[0], 0.5, 0.95);
        double translation = LaplacianVariance(translationStack, 0.5, 0.95);
        double corrected = LaplacianVariance(derotatedStack, 0.5, 0.95);

        Assert.True(single > 0);
        Assert.True(
            corrected > 5 * translation,
            $"Laplacian variance {corrected:F1} after derotation vs {translation:F1} with translation only " +
            $"({corrected / translation:F2}x, expected more than 5x; single frame {single:F1})");
        Assert.True(corrected > 0.5 * single, $"derotation kept only {100 * corrected / single:F0}% of the sharpness");
        Assert.True(translation < 0.25 * single, $"translation-only stacking kept {100 * translation / single:F0}%");
    }
}
