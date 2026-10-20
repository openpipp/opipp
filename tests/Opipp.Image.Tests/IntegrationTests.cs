namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Processing;

/// <summary>
/// Contract tests for <see cref="FrameIntegrator"/>: registration direction, sub-pixel resampling,
/// precision intermediates, quantisation, coverage reporting and input validation. Every assertion
/// reads the returned <see cref="IntegrationResult"/> buffers directly; no file writer is involved.
/// </summary>
public class IntegrationTests
{
    // ---------------------------------------------------------------- helpers

    private static Frame Solid(int width, int height, ushort value, int channels = 1, int bitDepth = 8)
    {
        var frame = new Frame(width, height, channels, bitDepth);
        Array.Fill(frame.Samples, value);
        return frame;
    }

    /// <summary>Single-channel frame whose sample at (x, y) is <c>y * width + x</c>.</summary>
    private static Frame Ramp(int width, int height, int bitDepth = 8)
    {
        var frame = new Frame(width, height, 1, bitDepth);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Samples[(y * width) + x] = (ushort)((y * width) + x);
            }
        }

        return frame;
    }

    private static Frame Samples(int width, int height, int channels, int bitDepth, params ushort[] values) =>
        new(width, height, channels, bitDepth, values);

    private static ushort At(Frame frame, int x, int y, int channel = 0) =>
        frame.Samples[(((y * frame.Width) + x) * frame.Channels) + channel];

    /// <summary>Sample at a first-frame canvas coordinate, mapped through the output rectangle.</summary>
    private static ushort CanvasAt(IntegrationResult result, int canvasX, int canvasY, int channel = 0) =>
        At(result.Frame, canvasX - result.OutputRect.X, canvasY - result.OutputRect.Y, channel);

    /// <summary>Precision sample at a first-frame canvas coordinate; the buffer spans the whole canvas.</summary>
    private static double PrecisionAt(IntegrationResult result, int canvasWidth, int canvasX, int canvasY, int channel = 0) =>
        result.PrecisionSamples![(((canvasY * canvasWidth) + canvasX) * result.Frame.Channels) + channel];

    private static IntegrationResult Run(
        Frame[] frames,
        AlignmentOffset[] shifts,
        double[]? weights = null,
        IntegrationMethod method = IntegrationMethod.Mean,
        CoveragePolicy coverage = CoveragePolicy.CropToOverlap,
        bool subpixel = false)
    {
        return FrameIntegrator.Integrate(
            frames,
            shifts,
            weights,
            new IntegrationOptions { Method = method, Coverage = coverage, Subpixel = subpixel });
    }

    // ---------------------------------------------------------------- integer registration

    [Theory]
    [InlineData(2, -1)]  // content right, up
    [InlineData(-1, 2)]  // content left, down
    public void WholePixelShift_MovesContentExactly(int dx, int dy)
    {
        var ramp = Ramp(6, 4);
        int canvasWidth = ramp.Width, canvasHeight = ramp.Height;

        var result = Run([ramp, ramp.Clone()], [new AlignmentOffset(dx, dy), new AlignmentOffset(dx, dy)]);

        int expectedLeft = Math.Max(0, dx);
        int expectedTop = Math.Max(0, dy);
        int expectedRight = Math.Min(canvasWidth - 1, dx + canvasWidth - 1);
        int expectedBottom = Math.Min(canvasHeight - 1, dy + canvasHeight - 1);

        Assert.Equal(
            new PixelRect(expectedLeft, expectedTop, expectedRight - expectedLeft + 1, expectedBottom - expectedTop + 1),
            result.OutputRect);

        for (int y = 0; y < result.Frame.Height; y++)
        {
            for (int x = 0; x < result.Frame.Width; x++)
            {
                int canvasX = result.OutputRect.X + x;
                int canvasY = result.OutputRect.Y + y;
                ushort expected = (ushort)(((canvasY - dy) * canvasWidth) + (canvasX - dx));

                Assert.Equal(expected, At(result.Frame, x, y));
                Assert.Equal(2, result.Coverage[(canvasY * canvasWidth) + canvasX]);
                Assert.Equal(expected, PrecisionAt(result, canvasWidth, canvasX, canvasY), 9);
            }
        }
    }

    [Fact]
    public void WholePixelShift_RoundsHalfAwayFromZero()
    {
        // 0.5 -> 1 (AwayFromZero), not 0 (banker's rounding). The frame is 8 wide, so the crop starts
        // at column 1 only if the shift was applied as one whole pixel.
        var frame = Solid(8, 4, 10);

        var result = Run([frame], [new AlignmentOffset(0.5, 0.5)]);

        Assert.Equal(new PixelRect(1, 1, 7, 3), result.OutputRect);
        Assert.Equal(10, CanvasAt(result, 1, 1));
    }

    // ---------------------------------------------------------------- sub-pixel registration

    [Fact]
    public void FractionalShift_ConstantFrameStaysConstantOverTheOverlap()
    {
        var frame = Solid(12, 10, 100);
        var shift = new AlignmentOffset(0.37, -0.62);

        var result = Run([frame, frame.Clone()], [shift, shift], subpixel: true);

        Assert.Equal(new PixelRect(1, 0, 11, 9), result.OutputRect);
        for (int y = 0; y < result.Frame.Height; y++)
        {
            for (int x = 0; x < result.Frame.Width; x++)
            {
                Assert.Equal(100.0, PrecisionAt(result, 12, result.OutputRect.X + x, result.OutputRect.Y + y), 9);
                Assert.Equal(100, At(result.Frame, x, y));
            }
        }

        // Every pixel of the crop has both contributors; nothing outside it does.
        for (int canvasY = 0; canvasY < 10; canvasY++)
        {
            for (int canvasX = 0; canvasX < 12; canvasX++)
            {
                bool inside = canvasX >= 1 && canvasX <= 11 && canvasY >= 0 && canvasY <= 8;
                Assert.Equal(inside ? 2 : 0, result.Coverage[(canvasY * 12) + canvasX]);
            }
        }
    }

    [Fact]
    public void FractionalShift_InterpolatesBetweenNeighbours()
    {
        // Source [0, 100] shifted by half a pixel: output column 1 samples 0.5 * 0 + 0.5 * 100 = 50.
        var frame = Samples(2, 1, 1, 8, 0, 100);

        var result = Run([frame, frame.Clone()], [new AlignmentOffset(0.5, 0), new AlignmentOffset(0.5, 0)], subpixel: true);

        Assert.Equal(new PixelRect(1, 0, 1, 1), result.OutputRect);
        Assert.Equal(50.0, PrecisionAt(result, 2, 1, 0), 12);
        Assert.Equal(50, CanvasAt(result, 1, 0));
    }

    [Fact]
    public void FractionalShift_InterpolatesBothAxes()
    {
        // One hot source pixel. A half-pixel offset on both axes puts a quarter of it into each of
        // the four destination samples of the crop, so a kernel that only resampled one axis (or
        // reused a row tap across an offset row) would disagree with these values.
        var frame = new Frame(3, 3, 1, 8);
        frame.Samples[(1 * 3) + 1] = 100;

        var result = Run([frame], [new AlignmentOffset(0.5, 0.5)], subpixel: true);

        Assert.Equal(new PixelRect(1, 1, 2, 2), result.OutputRect);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                Assert.Equal(25.0, PrecisionAt(result, 3, 1 + x, 1 + y), 12);
                Assert.Equal(25, At(result.Frame, x, y));
            }
        }
    }

    [Fact]
    public void FractionalShiftWithoutSubpixel_RoundsInsteadOfResampling()
    {
        var frame = Samples(2, 1, 1, 8, 0, 100);

        var result = Run([frame], [new AlignmentOffset(0.4, 0)], subpixel: false);

        // 0.4 rounds to 0, so nothing is shifted and the crop is the whole frame.
        Assert.Equal(new PixelRect(0, 0, 2, 1), result.OutputRect);
        Assert.Equal(0, At(result.Frame, 0, 0));
        Assert.Equal(100, At(result.Frame, 1, 0));
    }

    // ---------------------------------------------------------------- precision and quantisation

    [Fact]
    public void Mean_KeepsTheHalfSampleInPrecisionAndRoundsUpOnOutput()
    {
        var low = Samples(2, 1, 1, 8, 100, 7);
        var high = Samples(2, 1, 1, 8, 101, 8);

        var result = Run([low, high], [AlignmentOffset.Zero, AlignmentOffset.Zero]);

        Assert.Equal(2, result.PrecisionSamples!.Length);
        Assert.Equal(100.5, result.PrecisionSamples[0]);
        Assert.Equal(7.5, result.PrecisionSamples[1]);

        // Half away from zero: 100.5 -> 101 and 7.5 -> 8, even though 7.5 has an even neighbour.
        Assert.Equal(101, At(result.Frame, 0, 0));
        Assert.Equal(8, At(result.Frame, 1, 0));
    }

    [Theory]
    [InlineData(0, 1, 1)]     // 0.5 -> 1
    [InlineData(1, 2, 2)]     // 1.5 -> 2
    [InlineData(254, 255, 255)] // 254.5 -> 255
    [InlineData(100, 101, 101)]
    public void Mean_HalfSamplesRoundAwayFromZero(int a, int b, int expected)
    {
        var result = Run(
            [Solid(1, 1, (ushort)a), Solid(1, 1, (ushort)b)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero]);

        Assert.Equal((a + b) / 2.0, result.PrecisionSamples![0]);
        Assert.Equal(expected, At(result.Frame, 0, 0));
    }

    [Fact]
    public void Mean_RespectsWeights()
    {
        var black = Solid(1, 1, 0);
        var white = Solid(1, 1, 100);

        var heavyBlack = Run([black, white], [AlignmentOffset.Zero, AlignmentOffset.Zero], [3, 1]);
        Assert.Equal(25.0, heavyBlack.PrecisionSamples![0]);
        Assert.Equal(25, At(heavyBlack.Frame, 0, 0));
        Assert.Equal(3.0, heavyBlack.Contributors[0].Weight);
        Assert.Equal(1.0, heavyBlack.Contributors[1].Weight);

        var heavyWhite = Run([black, white], [AlignmentOffset.Zero, AlignmentOffset.Zero], [1, 3]);
        Assert.Equal(75.0, heavyWhite.PrecisionSamples![0]);
        Assert.Equal(75, At(heavyWhite.Frame, 0, 0));
    }

    [Fact]
    public void Mean_WithoutWeightsTreatsEveryFrameEqually()
    {
        var result = Run(
            [Solid(2, 2, 10), Solid(2, 2, 20), Solid(2, 2, 60)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero, AlignmentOffset.Zero]);

        Assert.Equal(30.0, result.PrecisionSamples![0]);
        Assert.Equal(30, At(result.Frame, 0, 0));
        Assert.All(result.Contributors, contributor => Assert.Equal(1.0, contributor.Weight));
    }

    [Fact]
    public void Sum_ClampsAtTheEightBitMaximum()
    {
        var result = Run(
            [Solid(1, 1, 200), Solid(1, 1, 100)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            method: IntegrationMethod.Sum);

        Assert.Equal(300.0, result.PrecisionSamples![0]);
        Assert.Equal(255, At(result.Frame, 0, 0));
    }

    [Fact]
    public void Sum_ClampsAtTheSixteenBitMaximum()
    {
        var result = Run(
            [Solid(1, 1, 40000, bitDepth: 16), Solid(1, 1, 40000, bitDepth: 16)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            method: IntegrationMethod.Sum);

        Assert.Equal(80000.0, result.PrecisionSamples![0]);
        Assert.Equal(65535, At(result.Frame, 0, 0));
    }

    [Fact]
    public void Sum_DoesNotClampPrecisionBelowTheMaximum()
    {
        var result = Run(
            [Solid(1, 1, 20), Solid(1, 1, 30)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            method: IntegrationMethod.Sum);

        Assert.Equal(50.0, result.PrecisionSamples![0]);
        Assert.Equal(50, At(result.Frame, 0, 0));
    }

    [Fact]
    public void Sum_AppliesWeights()
    {
        var result = Run(
            [Solid(1, 1, 20), Solid(1, 1, 30)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            [2, 1],
            IntegrationMethod.Sum);

        Assert.Equal(70.0, result.PrecisionSamples![0]);
        Assert.Equal(70, At(result.Frame, 0, 0));
    }

    // ---------------------------------------------------------------- coverage

    [Fact]
    public void FullOverlap_EveryPixelHasEveryContributor()
    {
        var result = Run(
            [Solid(4, 4, 10), Solid(4, 4, 20), Solid(4, 4, 30)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero, AlignmentOffset.Zero]);

        Assert.Equal(new PixelRect(0, 0, 4, 4), result.OutputRect);
        Assert.Equal(4, result.Frame.Width);
        Assert.Equal(4, result.Frame.Height);
        Assert.Equal(16, result.Coverage.Length);
        Assert.All(result.Coverage, count => Assert.Equal(3, count));
        Assert.Equal(20, At(result.Frame, 0, 0));
    }

    [Fact]
    public void PartialOverlap_CountsContributorsInsideTheCropAndZeroOutside()
    {
        var result = Run(
            [Solid(4, 4, 10), Solid(4, 4, 20)],
            [AlignmentOffset.Zero, new AlignmentOffset(2, 1)]);

        Assert.Equal(new PixelRect(2, 1, 2, 3), result.OutputRect);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                bool inside = x >= 2 && y >= 1;
                Assert.Equal(inside ? 2 : 0, result.Coverage[(y * 4) + x]);
            }
        }

        Assert.Equal(15, CanvasAt(result, 2, 1));
    }

    [Fact]
    public void CropToOverlap_ShrinksForAFractionalShiftAndKeepsNoEmptyPixel()
    {
        var result = Run(
            [Solid(8, 8, 77), Solid(8, 8, 77)],
            [new AlignmentOffset(0.5, 0.25), new AlignmentOffset(0.5, 0.25)],
            subpixel: true);

        Assert.True(result.OutputRect.Width < 8, "the crop must lose the sampled border column");
        Assert.True(result.OutputRect.Height < 8, "the crop must lose the sampled border row");
        Assert.Equal(new PixelRect(1, 1, 7, 7), result.OutputRect);
        Assert.Equal(7, result.Frame.Width);
        Assert.Equal(7, result.Frame.Height);

        // Precision and coverage stay indexed in the first frame's canvas.
        Assert.Equal(8 * 8, result.PrecisionSamples!.Length);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool inside = x >= 1 && y >= 1;
                Assert.Equal(inside ? 2 : 0, result.Coverage[(y * 8) + x]);
                if (!inside)
                {
                    Assert.Equal(0.0, PrecisionAt(result, 8, x, y));
                }
            }
        }

        for (int y = 0; y < result.Frame.Height; y++)
        {
            for (int x = 0; x < result.Frame.Width; x++)
            {
                Assert.Equal(77, At(result.Frame, x, y));
            }
        }
    }

    [Theory]
    [InlineData(0.5, 1, 7)]   // content moves right and is sampled from the left: column 0 is lost
    [InlineData(-0.5, 0, 7)]  // content moves left and is sampled from the right: the last column is lost
    [InlineData(2.0, 2, 6)]   // a whole-pixel shift loses exactly its own displacement, no extra border
    public void CropToOverlap_DropsTheBorderTheKernelSamplesFrom(double dx, int expectedLeft, int expectedWidth)
    {
        var result = Run([Solid(8, 8, 77)], [new AlignmentOffset(dx, 0)], subpixel: true);

        Assert.Equal(expectedLeft, result.OutputRect.X);
        Assert.Equal(0, result.OutputRect.Y);
        Assert.Equal(expectedWidth, result.OutputRect.Width);
        Assert.Equal(8, result.OutputRect.Height);
        Assert.Equal(expectedWidth, result.Frame.Width);
        for (int y = 0; y < result.Frame.Height; y++)
        {
            for (int x = 0; x < result.Frame.Width; x++)
            {
                Assert.Equal(77, At(result.Frame, x, y));
            }
        }
    }

    [Fact]
    public void CropToOverlap_IntersectsEachFramesValidRegion()
    {
        var frame = Solid(10, 6, 50);

        var result = Run(
            [frame, frame.Clone()],
            [new AlignmentOffset(0.25, 0), new AlignmentOffset(-0.75, 1.5)],
            subpixel: true);

        // Frame 2 loses its right column (Dx = -1, fx = 0.25) and its bottom row (Dy = 1, fy = 0.5);
        // frame 1 loses its left column. The intersection keeps only what both can fill.
        Assert.Equal(new PixelRect(1, 2, 8, 4), result.OutputRect);
        Assert.Equal(8, result.Frame.Width);
        Assert.Equal(4, result.Frame.Height);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(50, At(result.Frame, x, y));
                Assert.Equal(2, result.Coverage[((2 + y) * 10) + 1 + x]);
            }
        }
    }

    [Fact]
    public void KeepCanvas_ReportsZeroCoverageEdgesInsteadOfInventingValues()
    {
        var result = Run(
            [Solid(8, 8, 100), Solid(8, 8, 100)],
            [new AlignmentOffset(0.5, 0), new AlignmentOffset(0.5, 0)],
            coverage: CoveragePolicy.KeepCanvas,
            subpixel: true);

        Assert.Equal(new PixelRect(0, 0, 8, 8), result.OutputRect);
        Assert.Equal(8, result.Frame.Width);
        Assert.Equal(8, result.Frame.Height);

        for (int y = 0; y < 8; y++)
        {
            // Column 0 needs a tap at x = -0.5, so neither frame can supply it.
            Assert.Equal(0, result.Coverage[(y * 8) + 0]);
            Assert.Equal(FrameIntegrator.FillSample, At(result.Frame, 0, y));
            Assert.Equal(0.0, PrecisionAt(result, 8, 0, y));

            for (int x = 1; x < 8; x++)
            {
                Assert.Equal(2, result.Coverage[(y * 8) + x]);
                Assert.Equal(100, At(result.Frame, x, y));
            }
        }
    }

    [Fact]
    public void KeepCanvas_FullOverlapCoversTheWholeCanvas()
    {
        var result = Run(
            [Solid(3, 2, 5), Solid(3, 2, 7)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            coverage: CoveragePolicy.KeepCanvas);

        Assert.Equal(new PixelRect(0, 0, 3, 2), result.OutputRect);
        Assert.All(result.Coverage, count => Assert.Equal(2, count));
        Assert.Equal(6, At(result.Frame, 0, 0));
    }

    // ---------------------------------------------------------------- metadata

    [Fact]
    public void ResultCarriesProvenanceAndCommonBayerPattern()
    {
        var first = Solid(3, 2, 10);
        first.Bayer = BayerPattern.RGGB;
        first.SourceName = "capture-1.ser";
        first.Index = 7;

        var second = Solid(3, 2, 20);
        second.Bayer = BayerPattern.RGGB;
        second.SourceName = "capture-2.ser";

        // An even whole-pixel placement is the only registration a raw mosaic may take: it keeps the
        // 2x2 tile where the RGGB tag says it is (an odd one would retag the output GRBG, and a
        // fractional one is refused outright — see IntegrationCfaPhaseTests).
        var result = Run([first, second], [new AlignmentOffset(2, 0), new AlignmentOffset(2, 0)], subpixel: true);

        Assert.Equal(BayerPattern.RGGB, result.Frame.Bayer);
        Assert.Equal("capture-1.ser", result.Frame.SourceName);
        Assert.Equal(7, result.Frame.Index);
        Assert.Equal(2, result.Contributors.Count);
        Assert.Equal("capture-1.ser", result.Contributors[0].SourceName);
        Assert.Equal("capture-2.ser", result.Contributors[1].SourceName);
        Assert.Equal(2.0, result.Contributors[0].Shift.Dx);
        Assert.Equal(IntegrationMethod.Mean, result.Method);
    }

    [Fact]
    public void IntegerModeReportsTheRoundedShiftAsApplied()
    {
        var frame = Solid(4, 4, 1);

        var result = Run([frame], [new AlignmentOffset(0.5, -1.5)]);

        Assert.Equal(1, result.Contributors[0].Shift.Dx);
        Assert.Equal(-2, result.Contributors[0].Shift.Dy);
    }

    [Fact]
    public void ColourFramesReportNoBayerPattern()
    {
        var first = Solid(2, 2, 30, channels: 3);
        var second = Solid(2, 2, 10, channels: 3);
        first.SourceName = "rgb.png";

        var result = Run([first, second], [AlignmentOffset.Zero, AlignmentOffset.Zero]);

        Assert.Equal(3, result.Frame.Channels);
        Assert.Equal(BayerPattern.None, result.Frame.Bayer);
        Assert.Equal(20, CanvasAt(result, 0, 0, 0));
        Assert.Equal(20, CanvasAt(result, 1, 1, 2));
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void EmptyInputIsRejected()
    {
        var options = new IntegrationOptions();
        var ex = Assert.Throws<ArgumentException>(() =>
            FrameIntegrator.Integrate(Array.Empty<Frame>(), Array.Empty<AlignmentOffset>(), null, options));
        Assert.Contains("at least one frame", ex.Message);
    }

    [Fact]
    public void MismatchedShiftCountIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(2, 2, 1)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero]));
        Assert.Contains("shift", ex.Message);
    }

    [Fact]
    public void MismatchedWeightCountIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(2, 2, 1), Solid(2, 2, 2)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            [1, 1, 1]));
        Assert.Contains("weight", ex.Message);
    }

    [Fact]
    public void MismatchedGeometryIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(4, 4, 1), Solid(4, 5, 1)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero]));
        Assert.Contains("geometry", ex.Message);
    }

    [Fact]
    public void MismatchedDepthIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(4, 4, 1), Solid(4, 4, 1, bitDepth: 16)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero]));
        Assert.Contains("depth", ex.Message);
    }

    [Fact]
    public void MismatchedChannelCountIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(4, 4, 1), Solid(4, 4, 1, channels: 3)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero]));
        Assert.Contains("channel", ex.Message);
    }

    [Fact]
    public void MixedBayerPatternsAreRejected()
    {
        var first = Solid(4, 4, 1);
        first.Bayer = BayerPattern.RGGB;
        var second = Solid(4, 4, 2);
        second.Bayer = BayerPattern.BGGR;

        var ex = Assert.Throws<BayerPhaseException>(() => Run(
            [first, second],
            [AlignmentOffset.Zero, AlignmentOffset.Zero]));
        Assert.Contains("Bayer", ex.Message);
    }

    [Fact]
    public void ColourFrameWithBayerMarkerIsRejected()
    {
        var colour = Solid(4, 4, 1, channels: 3);
        colour.Bayer = BayerPattern.RGGB;

        var ex = Assert.Throws<ArgumentException>(() => Run([colour], [AlignmentOffset.Zero]));
        Assert.Contains("Bayer", ex.Message);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidWeightsAreRejected(double weight)
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(2, 2, 1), Solid(2, 2, 2)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            [weight, 1]));
        Assert.Contains("weight", ex.Message);
    }

    [Fact]
    public void AllZeroWeightsAreRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(2, 2, 1), Solid(2, 2, 2)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            [0, 0]));
        Assert.Contains("positive", ex.Message);
    }

    [Fact]
    public void NonFiniteShiftIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(2, 2, 1)],
            [new AlignmentOffset(double.NaN, 0)]));
        Assert.Contains("finite", ex.Message);
    }

    [Fact]
    public void MedianIsNotSupportedHere()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Run(
            [Solid(2, 2, 1)],
            [AlignmentOffset.Zero],
            method: IntegrationMethod.Median));
        Assert.Contains("TiledMedianIntegrator", ex.Message);
    }

    [Fact]
    public void NonOverlappingShiftsAreRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Run(
            [Solid(4, 4, 1), Solid(4, 4, 2)],
            [AlignmentOffset.Zero, new AlignmentOffset(-100, 0)]));
        Assert.Contains("overlap", ex.Message);
    }

    [Fact]
    public void CancellationThrows()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => FrameIntegrator.Integrate(
            [Solid(4, 4, 1)],
            [AlignmentOffset.Zero],
            null,
            new IntegrationOptions(),
            source.Token));
    }
}
