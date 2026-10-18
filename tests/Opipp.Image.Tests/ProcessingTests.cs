namespace Opipp.Image.Tests;

using Opipp.Core.Frames;
using Opipp.Core.Alignment;
using Opipp.Core.Processing;

/// <summary>
/// Hand-computed expectations for PIPP's deterministic processing primitives. Every geometric
/// expectation is derived from the documented destination-to-source mapping; resampling, stretch,
/// gain and gamma values were computed on paper from the formulas in the XML docs.
/// </summary>
public class ProcessingTests
{
    private static Frame Mono(int width, int height, params int[] samples) =>
        new(width, height, 1, 8, samples.Select(v => (ushort)v).ToArray());

    private static Frame Mono16(int width, int height, params int[] samples) =>
        new(width, height, 1, 16, samples.Select(v => (ushort)v).ToArray());

    private static Frame Rgb(int width, int height, params int[] samples) =>
        new(width, height, 3, 8, samples.Select(v => (ushort)v).ToArray());

    private static int[] Values(Frame frame) => Array.ConvertAll(frame.Samples, s => (int)s);

    private static void AssertValues(Frame frame, params int[] expected) =>
        Assert.Equal(expected, Values(frame));

    // ---------------------------------------------------------------- geometry

    [Fact]
    public void FlipHorizontal_3x2_MirrorsColumns()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.FlipHorizontal(source);

        Assert.Equal(3, result.Width);
        Assert.Equal(2, result.Height);
        AssertValues(result, 3, 2, 1, 6, 5, 4);
    }

    [Fact]
    public void FlipVertical_3x2_MirrorsRows()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.FlipVertical(source);

        AssertValues(result, 4, 5, 6, 1, 2, 3);
    }

    [Fact]
    public void Rotate90Clockwise_3x2_Becomes2x3()
    {
        // 1 2 3     4 1
        // 4 5 6  -> 5 2
        //           6 3
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.Rotate90Clockwise(source);

        Assert.Equal(2, result.Width);
        Assert.Equal(3, result.Height);
        AssertValues(result, 4, 1, 5, 2, 6, 3);
    }

    [Fact]
    public void Rotate180_3x2_ReversesAllSamples()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.Rotate180(source);

        AssertValues(result, 6, 5, 4, 3, 2, 1);
    }

    [Fact]
    public void Rotate270_3x2_Becomes2x3()
    {
        // 1 2 3     3 6
        // 4 5 6  -> 2 5
        //           1 4
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.Rotate270(source);

        Assert.Equal(2, result.Width);
        Assert.Equal(3, result.Height);
        AssertValues(result, 3, 6, 2, 5, 1, 4);
    }

    [Fact]
    public void Rotate90Clockwise_Then270_RestoresOriginal()
    {
        var source = Rgb(2, 2,
            1, 2, 3, 4, 5, 6,
            7, 8, 9, 10, 11, 12);

        var result = GeometricTransforms.Rotate270(
            GeometricTransforms.Rotate90Clockwise(source));

        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
        Assert.Equal(source.Samples, result.Samples);
    }

    [Fact]
    public void Rotate90Clockwise_Colour_PreservesChannelInterleaving()
    {
        var source = Rgb(2, 2,
            1, 2, 3, 4, 5, 6,
            7, 8, 9, 10, 11, 12);

        var result = GeometricTransforms.Rotate90Clockwise(source);

        Assert.Equal(3, result.Channels);
        AssertValues(result,
            7, 8, 9, 1, 2, 3,
            10, 11, 12, 4, 5, 6);
    }

    [Fact]
    public void Crop_ExtractsRequestedRegion()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.Crop(source, 1, 0, 2, 2);

        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        AssertValues(result, 2, 3, 5, 6);
    }

    [Fact]
    public void Crop_FullFrame_ReturnsIndependentCopy()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = GeometricTransforms.Crop(source, 0, 0, 3, 2);

        Assert.NotSame(source, result);
        Assert.Equal(source.Samples, result.Samples);
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]   // negative origin
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)]    // empty rectangle
    [InlineData(0, 0, 1, 0)]
    [InlineData(2, 0, 2, 1)]    // right edge overflow
    [InlineData(0, 1, 1, 2)]    // bottom edge overflow
    public void Crop_InvalidRectangle_Throws(int x, int y, int width, int height)
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);
        Assert.ThrowsAny<ArgumentException>(() => GeometricTransforms.Crop(source, x, y, width, height));
    }

    // ------------------------------------------------------------------ resize

    [Fact]
    public void Bilinear_2x2_To_4x4_MatchesEngineCornerAlignedGrid()
    {
        // Engine mapping: source coordinate = i * src/dst, so weights are 0, 0.5, 1 and then
        // clamped to the last sample (pipp_core FUN_14005ca60, the "Stretching the image..." routine).
        var source = Mono(2, 2,
            0, 60,
            120, 180);

        var result = Resize.Bilinear(source, 4, 4);

        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        AssertValues(result,
            0, 30, 60, 60,
            60, 90, 120, 120,
            120, 150, 180, 180,
            120, 150, 180, 180);
    }

    [Fact]
    public void Bilinear_2x2_To_1x1_SamplesTopLeftCorner()
    {
        // The engine's sampler starts at source coordinate 0 and does not area-average, so a
        // degenerate downscale to 1x1 returns the first pixel rather than the mean of the block.
        var source = Mono(2, 2,
            0, 60,
            120, 180);

        var result = Resize.Bilinear(source, 1, 1);

        AssertValues(result, 0);
    }

    [Fact]
    public void Bilinear_Colour_ResizesEachChannel()
    {
        var source = Rgb(2, 2,
            0, 10, 20, 30, 40, 50,
            60, 70, 80, 90, 100, 110);

        var result = Resize.Bilinear(source, 1, 1);

        Assert.Equal(3, result.Channels);
        // same corner-aligned rule as the mono case: first source pixel, all three channels
        AssertValues(result, 0, 10, 20);
    }

    [Fact]
    public void ResizeKeepAspect_DerivesHeightFromWidth()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = Resize.ResizeKeepAspect(source, 6, -1);

        Assert.Equal(6, result.Width);
        Assert.Equal(4, result.Height); // round(6 * 2 / 3) = 4
    }

    [Fact]
    public void ResizeKeepAspect_DerivesWidthFromHeight_RoundsAwayFromZero()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = Resize.ResizeKeepAspect(source, -1, 3);

        Assert.Equal(5, result.Width); // round(3 * 3 / 2) = round(4.5) = 5
        Assert.Equal(3, result.Height);
    }

    [Fact]
    public void ResizeKeepAspect_TwoMinusOnes_Throws()
    {
        var source = Mono(2, 2, 1, 2, 3, 4);
        Assert.Throws<ArgumentException>(() => Resize.ResizeKeepAspect(source, -1, -1));
    }

    [Fact]
    public void Bilinear_SameSize_ReturnsIndependentCopy()
    {
        var source = Mono(2, 2, 1, 2, 3, 4);

        var result = Resize.Bilinear(source, 2, 2);

        Assert.NotSame(source, result);
        Assert.Equal(source.Samples, result.Samples);
    }

    // ------------------------------------------------------------------ median

    [Fact]
    public void Median_RemovesSaltAndPepper_AndReplicatesEdges()
    {
        // Only the centre is corrupted. Clamped addressing means the corner windows repeat
        // the border samples, so the filter is stable everywhere except the hot pixel.
        var source = Mono(3, 3,
            10, 10, 20,
            10, 255, 20,
            20, 20, 30);

        var result = MedianFilter3x3.Apply(source);

        AssertValues(result,
            10, 10, 20,
            10, 20, 20,
            20, 20, 30);
    }

    [Fact]
    public void Median_Colour_FiltersEachChannelIndependently()
    {
        var source = new Frame(3, 3, 3, 8);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                source[x, y, 0] = (ushort)(x == 1 && y == 1 ? 255 : 10);
                source[x, y, 1] = 100;
                source[x, y, 2] = 200;
            }
        }

        var result = MedianFilter3x3.Apply(source);

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.Equal(10, (int)result[x, y, 0]);
                Assert.Equal(100, (int)result[x, y, 1]);
                Assert.Equal(200, (int)result[x, y, 2]);
            }
        }
    }

    // ----------------------------------------------------------------- binning

    [Fact]
    public void Binning_Average_4x4_To_2x2_RoundsHalfUp()
    {
        var source = Mono(4, 4,
             1, 2, 3, 4,
             5, 6, 7, 8,
             9, 10, 11, 12,
            13, 14, 15, 16);

        var result = Binning.Apply(source, 2, BinningMode.Average);

        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        // 14/4=3.5 -> 4, 22/4=5.5 -> 6, 46/4=11.5 -> 12, 54/4=13.5 -> 14
        AssertValues(result,
            4, 6,
            12, 14);
    }

    [Fact]
    public void Binning_Sum_4x4_To_2x2_AddsBlocks()
    {
        var source = Mono(4, 4,
             1, 2, 3, 4,
             5, 6, 7, 8,
             9, 10, 11, 12,
            13, 14, 15, 16);

        var result = Binning.Apply(source, 2, BinningMode.Sum);

        AssertValues(result,
            14, 22,
            46, 54);
    }

    [Fact]
    public void Binning_FactorOne_ReturnsClone()
    {
        var source = Mono(3, 2, 1, 2, 3, 4, 5, 6);

        var result = Binning.Apply(source, 1, BinningMode.Average);

        Assert.NotSame(source, result);
        Assert.Equal(source.Samples, result.Samples);
    }

    [Fact]
    public void Binning_Sum_ClampsAtEightBitMaximum()
    {
        var source = Mono(2, 2, 200, 200, 200, 200);

        var result = Binning.Apply(source, 2, BinningMode.Sum);

        AssertValues(result, 255);
    }

    [Fact]
    public void Binning_Colour_2x2_To_1x1_AveragesEachChannel()
    {
        var source = Rgb(2, 2,
            2, 4, 6, 4, 6, 8,
            6, 8, 10, 8, 10, 12);

        var result = Binning.Apply(source, 2, BinningMode.Average);

        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(3, result.Channels);
        AssertValues(result, 5, 7, 9);
    }

    [Fact]
    public void Binning_IgnoresTrailingPartialBlocks()
    {
        var source = Mono(5, 3,
             1, 2, 3, 4, 5,
             6, 7, 8, 9, 10,
            11, 12, 13, 14, 15);

        var result = Binning.Apply(source, 2, BinningMode.Average);

        Assert.Equal(2, result.Width);  // last column ignored
        Assert.Equal(1, result.Height); // last row ignored
        AssertValues(result, 4, 6);     // (1+2+6+7)/4, (3+4+8+9)/4
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public void Binning_FactorOutsideOneToEight_Throws(int factor)
    {
        var source = Mono(4, 4, new int[16]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Binning.Apply(source, factor, BinningMode.Average));
    }

    [Fact]
    public void Binning_FactorLargerThanFrame_Throws()
    {
        var source = Mono(4, 4, new int[16]);
        Assert.Throws<ArgumentException>(() => Binning.Apply(source, 8, BinningMode.Sum));
    }

    // ------------------------------------------------------------ mono convert

    [Theory]
    [InlineData(MonoMode.Blue, 41)]
    [InlineData(MonoMode.Green, 20)]
    [InlineData(MonoMode.Red, 10)]
    [InlineData(MonoMode.All, 19)]        // trunc(0.299*10 + 0.587*20 + 0.114*41) = trunc(19.404)
    [InlineData(MonoMode.RedGreen, 16)]   // trunc(14.73 / 0.886) = trunc(16.625)
    [InlineData(MonoMode.RedBlue, 18)]    // trunc(7.664 / 0.413) = trunc(18.56)
    [InlineData(MonoMode.GreenBlue, 23)]  // trunc(16.414 / 0.701) = trunc(23.415)
    public void MonoConvert_EveryMode_UsesDocumentedChannelMapping(MonoMode mode, int expected)
    {
        // Weights bisected against pipp_core.exe with a 1,056-combination RGB grid: modes 3-6 are
        // Rec.601 luma (and luma-weighted two-channel pairs), not plain averages.
        var source = Rgb(1, 1, 10, 20, 41);

        var result = MonoConvert.Apply(source, mode);

        Assert.Equal(1, result.Channels);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        AssertValues(result, expected);
    }

    [Fact]
    public void MonoConvert_MonoInput_ReturnsCloneRegardlessOfMode()
    {
        var source = Mono(2, 1, 7, 9);

        var result = MonoConvert.Apply(source, MonoMode.Blue);

        Assert.NotSame(source, result);
        Assert.Equal(source.Channels, result.Channels);
        Assert.Equal(source.Samples, result.Samples);
    }

    [Fact]
    public void MonoConvert_AveragesEachPixelSeparately()
    {
        var source = Rgb(2, 1,
            10, 20, 30, 100, 101, 102);

        var result = MonoConvert.Apply(source, MonoMode.All);

        // Rec.601 luma per pixel: trunc(18.15) and trunc(100.815); the engine uses the same weights.
        AssertValues(result, 18, 100);
    }

    // ------------------------------------------------------- histogram stretch
    //
    // The engine's -hs is not a constant gain. It measures a white point from the frame - the mean of
    // its 25 brightest samples - and normalises to it (FUN_14009b4a0 in the original core). Fixtures
    // below therefore pin the bright tail explicitly: a tail that averages 255 gives exactly the old
    // "gain = whitePoint/100" behaviour, and a tail of 225 reproduces the mapping measured on a real
    // 640x640 Saturn capture.

    /// <summary>8-bit frame with <see cref="HistogramStretch.TailSampleCount"/> samples of
    /// <paramref name="tail"/> followed by the probe values.</summary>
    private static Frame WithTail8(int tail, params int[] probes)
    {
        var samples = new int[HistogramStretch.TailSampleCount + probes.Length];
        Array.Fill(samples, tail, 0, HistogramStretch.TailSampleCount);
        probes.CopyTo(samples, HistogramStretch.TailSampleCount);
        return Mono(samples.Length, 1, samples);
    }

    /// <summary>16-bit frame with <see cref="HistogramStretch.TailSampleCount"/> samples of
    /// <paramref name="tail"/> followed by the probe values.</summary>
    private static Frame WithTail16(int tail, params int[] probes)
    {
        var samples = new int[HistogramStretch.TailSampleCount + probes.Length];
        Array.Fill(samples, tail, 0, HistogramStretch.TailSampleCount);
        probes.CopyTo(samples, HistogramStretch.TailSampleCount);
        return Mono16(samples.Length, 1, samples);
    }

    [Fact]
    public void MeasureWhiteSamples_AveragesTheTwentyFiveBrightestSamples()
    {
        // 24 samples at 255 and one at 200: the bin that crosses the 25-sample budget contributes only
        // the samples still needed, so the white point is (24*255 + 200)/25 = 252.8 -> 252, not 255.
        var samples = new int[40];
        Array.Fill(samples, 10);
        for (int i = 0; i < 24; i++) samples[i] = 255;
        samples[24] = 200;
        var frame = Mono(samples.Length, 1, samples);

        Assert.Equal(252, HistogramStretch.MeasureWhiteSamples(frame)[0]);
    }

    [Fact]
    public void MeasureBlackSamples_AveragesTheTwentyFiveDarkestSamples()
    {
        // Mirror of the white point: 24 samples at 0 plus one at 55 gives (0*24 + 55)/25 = 2.2 -> 2.
        var samples = new int[40];
        Array.Fill(samples, 200);
        for (int i = 0; i < 24; i++) samples[i] = 0;
        samples[24] = 55;
        var frame = Mono(samples.Length, 1, samples);

        Assert.Equal(2, HistogramStretch.MeasureBlackSamples(frame)[0]);
    }

    [Fact]
    public void StretchToWhitePoint_SaturatedTail_ReproducesTheMeasuredOracleMapping()
    {
        // A saturated bright tail (mean 255) makes the gain exactly whitePoint/100 = 2.04, which is the
        // mapping measured against the original binary on its synthetic fixtures: 38 -> 77, 50 -> 101,
        // 75 -> 152, 100 -> 203, 123 -> 250. The one-unit "bias" is the truncation of a product that
        // lands just under the integer (204*2.55/255 is slightly below 2.04 in double precision).
        var frame = WithTail8(255, 0, 38, 50, 75, 100, 123);

        var result = HistogramStretch.StretchToWhitePoint(frame, 204);

        Assert.Equal(0, result.Samples[25]);
        Assert.Equal(77, result.Samples[26]);
        Assert.Equal(101, result.Samples[27]);
        Assert.Equal(152, result.Samples[28]);
        Assert.Equal(203, result.Samples[29]);
        Assert.Equal(250, result.Samples[30]);
        Assert.All(result.Samples.Take(HistogramStretch.TailSampleCount), v => Assert.Equal(255, v));
    }

    [Fact]
    public void CentreOffset_MovesObjectToEngineTarget()
    {
        // A disc centred at (48,48) in a 96x96 frame. The original engine lands it at (48,47),
        // so the x target is width/2 and the y target is height-1-height/2.
        var frame = new Frame(96, 96, 1, 8);
        for (int y = 38; y <= 58; y++)
        {
            for (int x = 38; x <= 58; x++)
            {
                frame.Samples[(y * 96) + x] = 255;
            }
        }

        var offset = FrameAligner.CentreOffset(frame, 128);

        Assert.Equal(0, offset.Dx);
        Assert.Equal(-1, offset.Dy);
    }

    [Fact]
    public void StretchToWhitePoint_RealFrameTail_MatchesTheOracleMapping()
    {
        // A bright tail averaging 225 gives -hs=204 a gain of 204*2.55/225 = 2.312, which is exactly
        // what the original does on the real Saturn capture (measured there: 38 -> 87, 50 -> 115,
        // 75 -> 173, 100 -> 231, 123 -> clipped). The old constant-gain model produced 77/101/152/203.
        var frame = WithTail8(225, 0, 38, 50, 75, 100, 123);

        var result = HistogramStretch.StretchToWhitePoint(frame, 204);

        Assert.Equal(0, result.Samples[25]);
        Assert.Equal(87, result.Samples[26]);
        Assert.Equal(115, result.Samples[27]);
        Assert.Equal(173, result.Samples[28]);
        Assert.Equal(231, result.Samples[29]);
        Assert.Equal(255, result.Samples[30]);
    }

    [Fact]
    public void StretchToWhitePoint_PreserveBlackPoint_SubtractsTheMeasuredBlackPoint()
    {
        // -hs_bp: 25 samples at 20 (the frame's 25 darkest) become the black point, so the gain is
        // 204*2.55/(255-20) = 2.2136 and values below 20 map to 0.
        var samples = new List<int>();
        samples.AddRange(Enumerable.Repeat(20, HistogramStretch.TailSampleCount));
        samples.AddRange([30, 120, 220]);
        samples.AddRange(Enumerable.Repeat(255, HistogramStretch.TailSampleCount));
        var source = Mono(samples.Count, 1, [.. samples]);

        var result = HistogramStretch.StretchToWhitePoint(source, 204, preserveBlackPoint: true);

        Assert.Equal(0, result.Samples[0]);
        Assert.Equal(22, result.Samples[25]);   // (30-20) * 2.2136 = 22.1
        Assert.Equal(221, result.Samples[26]);  // (120-20) * 2.2136 = 221.4
        Assert.Equal(255, result.Samples[27]);  // clipped
    }

    [Fact]
    public void StretchToWhitePoint_WithoutBlackPoint_StretchesFromZero()
    {
        var samples = new List<int>();
        samples.AddRange(Enumerable.Repeat(20, HistogramStretch.TailSampleCount));
        samples.AddRange([30, 120, 220]);
        samples.AddRange(Enumerable.Repeat(255, HistogramStretch.TailSampleCount));
        var source = Mono(samples.Count, 1, [.. samples]);

        var result = HistogramStretch.StretchToWhitePoint(source, 204, preserveBlackPoint: false);

        // black point 0, white 255: the plain 2.04x gain
        Assert.Equal(40, result.Samples[0]);    // 20 * 2.04 = 40.8
        Assert.Equal(61, result.Samples[25]);   // 30 * 2.04 = 61.2
        Assert.Equal(244, result.Samples[26]);  // 120 * 2.04 = 244.8
        Assert.Equal(255, result.Samples[27]);  // 220 * 2.04 clips
    }

    [Fact]
    public void StretchToPercent_80_IsTheSameSettingAsWhitePoint204()
    {
        // The engine's parser stores percent*255/100 in the same field as -hs=<n>, so 80% and 204 are
        // one setting (verified in FUN_1400ab9a0, the switch parser).
        var frame = WithTail8(255, 0, 102, 204);

        var byPercent = HistogramStretch.StretchToPercent(frame, 80);
        var byWhitePoint = HistogramStretch.StretchToWhitePoint(frame, 204);

        Assert.Equal(byWhitePoint.Samples, byPercent.Samples);
        Assert.Equal(208, byPercent.Samples[26]);  // 102 * 2.04 = 208.08
        Assert.Equal(255, byPercent.Samples[27]);  // 204 * 2.04 clips
    }

    [Fact]
    public void StretchToPercent_40_EqualsWhitePoint102()
    {
        // 40% -> 40*255/100 = 102, i.e. a 1.02x gain on a saturated tail.
        var frame = WithTail8(255, 0, 51, 102, 204);

        var result = HistogramStretch.StretchToPercent(frame, 40);

        Assert.Equal(52, result.Samples[26]);   // 51 * 1.02 = 52.02
        Assert.Equal(104, result.Samples[27]);  // 102 * 1.02 = 104.04
        Assert.Equal(208, result.Samples[28]);  // 204 * 1.02 = 208.08
    }

    [Fact]
    public void StretchToWhitePoint_16Bit_MeasuresTheTailInSixteenBitUnits()
    {
        // A saturated 16-bit tail gives the same 2.04x gain; the constant is 655.35 = 65535/100 rather
        // than 2.55 = 255/100.
        var source = WithTail16(65535, 0, 26214, 52428);

        var result = HistogramStretch.StretchToWhitePoint(source, 204);

        Assert.Equal(0, result.Samples[25]);
        Assert.Equal(53476, result.Samples[26]);  // 26214 * 2.04 = 53476.6
        Assert.Equal(65535, result.Samples[27]);  // 52428 * 2.04 clips
    }

    [Fact]
    public void StretchToWhitePoint_Colour_UsesTheSmallestChannelGainUnlessRgbIsRequested()
    {
        // R and G tails average 255, B averages 200. Without -hsrgb the engine applies the smallest
        // gain (2.04) to every channel; with it each channel keeps its own (B would get 2.601).
        var samples = new List<int>();
        for (int i = 0; i < HistogramStretch.TailSampleCount; i++) samples.AddRange([255, 255, 200]);
        samples.AddRange([100, 100, 100]);
        var source = Rgb(samples.Count / 3, 1, [.. samples]);
        int probe = HistogramStretch.TailSampleCount * 3;

        var shared = HistogramStretch.StretchToWhitePoint(source, 204);
        var perChannel = HistogramStretch.StretchToWhitePoint(source, 204, perChannelGains: true);

        Assert.Equal(203, shared.Samples[probe]);      // 100 * 2.04 = 203.99..., truncated (the
        Assert.Equal(203, shared.Samples[probe + 1]);  // engine's one-unit artefact: 100 -> 203)
        Assert.Equal(203, shared.Samples[probe + 2]);
        Assert.Equal(203, perChannel.Samples[probe]);
        Assert.Equal(203, perChannel.Samples[probe + 1]);
        Assert.Equal(255, perChannel.Samples[probe + 2]); // 100 * 2.601 = 260, clipped
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void StretchToWhitePoint_InvalidWhitePoint_Throws(int whitePoint)
    {
        var source = Mono(1, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HistogramStretch.StretchToWhitePoint(source, whitePoint));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void StretchToPercent_InvalidPercent_Throws(int percent)
    {
        var source = Mono(1, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HistogramStretch.StretchToPercent(source, percent));
    }

    [Fact]
    public void EqualiseRgb_ScalesEachChannelToTheLargestMaximum()
    {
        var source = Rgb(2, 1,
            10, 20, 40,
            50, 100, 200);

        var result = HistogramStretch.EqualiseRgb(source);

        // target = 200: R gains 4x, G 2x, B 1x
        AssertValues(result,
            40, 40, 40,
            200, 200, 200);
    }

    [Fact]
    public void EqualiseRgb_ThenStretch_ProducesNeutralGrey()
    {
        // 25 pixels of each tone so the stretch has a measurable bright tail: equalising a (10,20,40)
        // and a (50,100,200) pixel gives (40,40,40) and (200,200,200), then the 200 tail gives a
        // 204*2.55/200 = 2.601x gain and the result stays neutral.
        var samples = new List<int>();
        for (int i = 0; i < HistogramStretch.TailSampleCount; i++) samples.AddRange([10, 20, 40]);
        for (int i = 0; i < HistogramStretch.TailSampleCount; i++) samples.AddRange([50, 100, 200]);
        var source = Rgb(samples.Count / 3, 1, [.. samples]);

        var equalised = HistogramStretch.EqualiseRgb(source);
        var result = HistogramStretch.StretchToWhitePoint(equalised, 204);

        int tail = HistogramStretch.TailSampleCount * 3;
        Assert.Equal([104, 104, 104], Values(result)[..3]);               // 40 * 2.601 = 104.0
        Assert.Equal([255, 255, 255], Values(result)[tail..(tail + 3)]);  // 200 * 2.601 clips
    }

    [Fact]
    public void EqualiseRgb_Mono_ReturnsClone()
    {
        var source = Mono(2, 1, 3, 9);

        var result = HistogramStretch.EqualiseRgb(source);

        Assert.NotSame(source, result);
        Assert.Equal(source.Channels, result.Channels);
        Assert.Equal(source.Samples, result.Samples);
    }

    // --------------------------------------------------------------- tone curve

    [Fact]
    public void Gain_MultipliesAndClamps()
    {
        var source = Mono(4, 1, 0, 10, 100, 200);

        var result = ToneCurve.ApplyGain(source, 2.0);

        AssertValues(result, 0, 20, 200, 255);
    }

    [Fact]
    public void Gain_LessThanOne_Darkens()
    {
        var source = Mono(4, 1, 0, 10, 100, 200);

        var result = ToneCurve.ApplyGain(source, 0.5);

        AssertValues(result, 0, 5, 50, 100);
    }

    [Fact]
    public void Gain_Zero_ProducesBlack()
    {
        var source = Mono(3, 1, 0, 10, 255);

        var result = ToneCurve.ApplyGain(source, 0.0);

        AssertValues(result, 0, 0, 0);
    }

    [Fact]
    public void Gain_16Bit_ClampsAt65535()
    {
        var source = Mono16(2, 1, 10000, 40000);

        var result = ToneCurve.ApplyGain(source, 2.0);

        AssertValues(result, 20000, 65535);
    }

    [Fact]
    public void Gain_HugeFiniteMultiplier_ClampsWithoutOverflow()
    {
        var source = Mono(3, 1, 0, 1, 255);

        var result = ToneCurve.ApplyGain(source, double.MaxValue);

        AssertValues(result, 0, 255, 255);
    }

    [Fact]
    public void Gamma_Two_BrightensMidtones()
    {
        var source = Mono(5, 1, 0, 64, 128, 192, 255);

        var result = ToneCurve.ApplyGamma(source, 2.0);

        // 255*sqrt(v/255) = 127.75, 180.67, 221.27 -> 128, 181, 221
        AssertValues(result, 0, 128, 181, 221, 255);
    }

    [Fact]
    public void Gamma_One_IsIdentityOverFullRange()
    {
        var source = Mono16(3, 1, 0, 12345, 65535);

        var result = ToneCurve.ApplyGamma(source, 1.0);

        AssertValues(result, 0, 12345, 65535);
    }

    [Fact]
    public void Gamma_IsMonotonic_AndKeepsEndpoints()
    {
        var values = Enumerable.Range(0, 256).ToArray();
        var source = Mono(256, 1, values);

        var gamma = ToneCurve.ApplyGamma(source, 2.2);
        var gain = ToneCurve.ApplyGain(source, 1.5);

        for (int i = 1; i < 256; i++)
        {
            Assert.True(gamma.Samples[i] >= gamma.Samples[i - 1], $"gamma not monotonic at {i}");
            Assert.True(gain.Samples[i] >= gain.Samples[i - 1], $"gain not monotonic at {i}");
        }

        Assert.Equal(0, (int)gamma.Samples[0]);
        Assert.Equal(255, (int)gamma.Samples[255]);
        Assert.Equal(0, (int)gain.Samples[0]);
        Assert.Equal(255, (int)gain.Samples[255]); // 255*1.5 clamped
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Gain_Invalid_Throws(double gain)
    {
        var source = Mono(1, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => ToneCurve.ApplyGain(source, gain));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Gamma_Invalid_Throws(double gamma)
    {
        var source = Mono(1, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => ToneCurve.ApplyGamma(source, gamma));
    }

    // -------------------------------------------------------------- statistics

    [Fact]
    public void CountOverexposed_Mono_CountsInclusiveSamples()
    {
        var source = Mono(4, 1, 254, 255, 255, 10);

        Assert.Equal(new[] { 2 }, Statistics.CountOverexposed(source, 255));
        Assert.Equal(new[] { 3 }, Statistics.CountOverexposed(source, 254));
    }

    [Fact]
    public void CountOverexposed_Colour_CountsPerChannel()
    {
        var source = Rgb(2, 1,
            255, 100, 255,
            0, 255, 200);

        Assert.Equal(new[] { 1, 1, 1 }, Statistics.CountOverexposed(source, 255));
        Assert.Equal(new[] { 1, 1, 2 }, Statistics.CountOverexposed(source, 200));
    }

    [Fact]
    public void MaxPixel_ReturnsChannelMaxima()
    {
        Assert.Equal(new[] { 9 }, Statistics.MaxPixel(Mono(3, 1, 3, 9, 2)));

        var colour = Rgb(2, 1,
            10, 200, 30,
            40, 50, 60);
        Assert.Equal(new[] { 40, 200, 60 }, Statistics.MaxPixel(colour));
    }

    [Fact]
    public void MeanPixel_ReturnsChannelMeans()
    {
        var mono = Mono(4, 1, 1, 2, 3, 4);
        var monoMeans = Statistics.MeanPixel(mono);
        Assert.Equal(2.5, monoMeans[0], 10);

        var colour = Rgb(2, 1,
            10, 20, 30,
            40, 60, 80);
        var means = Statistics.MeanPixel(colour);
        Assert.Equal(25.0, means[0], 10);
        Assert.Equal(40.0, means[1], 10);
        Assert.Equal(55.0, means[2], 10);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void CountOverexposed_ThresholdOutsideEightBitRange_Throws(int threshold)
    {
        var source = Mono(1, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Statistics.CountOverexposed(source, threshold));
    }

    // ---------------------------------------------------------------- ownership

    [Fact]
    public void EveryStage_DoesNotMutateItsInput()
    {
        var source = Rgb(2, 2,
            1, 2, 3, 4, 5, 6,
            7, 8, 9, 10, 11, 12);
        var snapshot = (ushort[])source.Samples.Clone();

        _ = GeometricTransforms.FlipHorizontal(source);
        _ = GeometricTransforms.FlipVertical(source);
        _ = GeometricTransforms.Rotate90Clockwise(source);
        _ = GeometricTransforms.Rotate180(source);
        _ = GeometricTransforms.Rotate270(source);
        _ = GeometricTransforms.Crop(source, 0, 0, 1, 1);
        _ = Resize.Bilinear(source, 5, 3);
        _ = Resize.ResizeKeepAspect(source, 4, -1);
        _ = MedianFilter3x3.Apply(source);
        _ = Binning.Apply(source, 2, BinningMode.Average);
        _ = Binning.Apply(source, 2, BinningMode.Sum);
        _ = MonoConvert.Apply(source, MonoMode.All);
        _ = HistogramStretch.StretchToWhitePoint(source, 204);
        _ = HistogramStretch.StretchToPercent(source, 80);
        _ = HistogramStretch.EqualiseRgb(source);
        _ = ToneCurve.ApplyGain(source, 1.5);
        _ = ToneCurve.ApplyGamma(source, 2.2);
        _ = Statistics.CountOverexposed(source, 8);
        _ = Statistics.MaxPixel(source);
        _ = Statistics.MeanPixel(source);

        Assert.Equal(snapshot, source.Samples);
    }

    [Fact]
    public void Stages_PreserveBitDepthAndProvenance()
    {
        var source = Mono(2, 2, 1, 2, 3, 4);
        source.Bayer = BayerPattern.RGGB;
        source.Index = 42;
        source.SourceName = "capture.ser";

        var rotated = GeometricTransforms.Rotate90Clockwise(source);
        var binned = Binning.Apply(source, 2, BinningMode.Average);
        var stretched = HistogramStretch.StretchToWhitePoint(source, 204);

        foreach (var result in new[] { rotated, binned, stretched })
        {
            Assert.Equal(8, result.BitDepth);
            Assert.Equal(BayerPattern.RGGB, result.Bayer);
            Assert.Equal(42, result.Index);
            Assert.Equal("capture.ser", result.SourceName);
        }
    }
}
