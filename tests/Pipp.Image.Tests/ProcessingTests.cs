namespace Pipp.Image.Tests;

using Pipp.Core.Frames;
using Pipp.Core.Alignment;
using Pipp.Core.Processing;

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
    [InlineData(MonoMode.All, 23)]        // 71 / 3 truncates
    [InlineData(MonoMode.RedGreen, 15)]   // 30 / 2
    [InlineData(MonoMode.RedBlue, 25)]    // 51 / 2 truncates
    [InlineData(MonoMode.GreenBlue, 30)]  // 61 / 2 truncates
    public void MonoConvert_EveryMode_UsesDocumentedChannelMapping(MonoMode mode, int expected)
    {
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

        AssertValues(result, 20, 101);
    }

    // ------------------------------------------------------- histogram stretch

    [Fact]
    public void StretchToWhitePoint_204_UsesSinglePrecisionGain()
    {
        // The engine's gain is a float: 50*2.04f = 101.99999 -> 101, 75 -> 152, 100 -> 203.
        // Verified against the original binary; integer arithmetic would give 102/153/204.
        var source = Mono(3, 1, 50, 75, 100);

        var result = HistogramStretch.StretchToWhitePoint(source, 204);

        AssertValues(result, 101, 152, 203);
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
    public void StretchToWhitePoint_204_AppliesTwoPointZeroFourGain()
    {
        // Values match the original engine measured on the same ramp: 38->77, 51->104, 102->208,
        // 123->250 at -hs=204, i.e. trunc(v * 204 / 100) with clipping.
        var source = Mono(6, 1, 0, 51, 102, 153, 204, 255);

        var result = HistogramStretch.StretchToWhitePoint(source, 204);

        AssertValues(result, 0, 104, 208, 255, 255, 255);
    }

    [Fact]
    public void StretchToWhitePoint_PreserveBlackPoint_MapsMinimumToZero()
    {
        var source = Mono(3, 1, 10, 102, 204);

        var result = HistogramStretch.StretchToWhitePoint(source, 204, preserveBlackPoint: true);

        // black=10, then truncate((v-10)*204/100): 0, 187, clipped 255
        AssertValues(result, 0, 187, 255);
    }

    [Fact]
    public void StretchToWhitePoint_WithoutBlackPoint_PreservesLowValues()
    {
        var source = Mono(3, 1, 10, 102, 204);

        var result = HistogramStretch.StretchToWhitePoint(source, 204, preserveBlackPoint: false);

        AssertValues(result, 20, 208, 255); // trunc(10*2.04)=20, trunc(102*2.04)=208
    }

    [Fact]
    public void StretchToPercent_80_AppliesGainOfOnePointTwoFive()
    {
        // the percentage and integer spellings are different things in the engine:
        // 80% means a white point at 80% of full scale (a 1.25x gain), not the same as -hs=204
        var source = Mono(3, 1, 0, 102, 204);

        var result = HistogramStretch.StretchToPercent(source, 80);

        AssertValues(result, 0, 127, 254);
    }

    [Fact]
    public void StretchToPercent_40_AppliesGainOfTwoAndAHalf()
    {
        var source = Mono(3, 1, 0, 51, 102);

        var result = HistogramStretch.StretchToPercent(source, 40);

        // percentage spelling means "white at 40% of full scale" (gain 100/40) with the engine's
        // one-unit bias: trunc((v*100 - 1)/40)
        AssertValues(result, 0, 127, 254);
    }

    [Fact]
    public void StretchToWhitePoint_16Bit_UsesSameGainPercentage()
    {
        var source = Mono16(3, 1, 0, 26214, 52428);

        var result = HistogramStretch.StretchToWhitePoint(source, 204);

        // the gain applies to 16-bit samples too: trunc(26214*2.04)=53476, 52428*2.04 clips
        AssertValues(result, 0, 53476, 65535);
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
        var source = Rgb(2, 1,
            10, 20, 40,
            50, 100, 200);

        var equalised = HistogramStretch.EqualiseRgb(source);
        var result = HistogramStretch.StretchToWhitePoint(equalised, 204);

        // equalised to (40,40,40) and (200,200,200), then trunc(v*204/100)
        AssertValues(result,
            81, 81, 81,
            255, 255, 255);
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
