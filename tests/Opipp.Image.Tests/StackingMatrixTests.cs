namespace Opipp.Image.Tests;

using Opipp.Core.Frames;
using Opipp.Core.Stacking;

/// <summary>
/// The numerical matrix for <see cref="FrameStacker"/>: every combination of mean/sum/median with
/// mono8, RGB8, mono16 and RGB16, asserted at exact values rather than ranges. The expectations are
/// computed by hand from the documented contracts:
/// <list type="bullet">
/// <item>mean = unsigned integer division of the 64-bit accumulator by the frame count (truncates);</item>
/// <item>sum = accumulator clamped at 255 (8-bit) or 65535 (16-bit);</item>
/// <item>median = middle value for an odd count, and <c>(a + b + 1) / 2</c> for an even count, so halves round up.</item>
/// </list>
/// The half-boundary contract is deliberately asymmetric: a two-frame stack of 0 and 1 has mean 0 but
/// median 1.
/// </summary>
public class StackingMatrixTests
{
    private static Frame Make(int width, int height, int channels, int bitDepth, params ushort[] samples) =>
        new(width, height, channels, bitDepth, samples);

    private static ushort StackMono(StackMethod method, int bitDepth, params ushort[] values)
    {
        var stacker = new FrameStacker(1, 1, 1, bitDepth, method);
        foreach (ushort value in values) stacker.Add(Make(1, 1, 1, bitDepth, value));
        return stacker.Result().Samples[0];
    }

    private static ushort[] StackRgb(StackMethod method, int bitDepth, params ushort[][] frames)
    {
        int samples = frames[0].Length;
        var stacker = new FrameStacker(samples / 3, 1, 3, bitDepth, method);
        foreach (ushort[] frame in frames) stacker.Add(Make(samples / 3, 1, 3, bitDepth, frame));
        return stacker.Result().Samples;
    }

    // ---------------------------------------------------------------- mono 8-bit

    [Fact]
    public void Mono8_Mean_TruncatesEachPixelIndependently()
    {
        var stacker = new FrameStacker(4, 1, 1, 8, StackMethod.Mean);
        stacker.Add(Make(4, 1, 1, 8, 10, 200, 0, 255));
        stacker.Add(Make(4, 1, 1, 8, 11, 201, 0, 0));
        stacker.Add(Make(4, 1, 1, 8, 12, 202, 1, 0));

        // 33/3 = 11, 603/3 = 201, 1/3 = 0, 255/3 = 85.
        Assert.Equal(new ushort[] { 11, 201, 0, 85 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mono8_Sum_ClampsAt255()
    {
        var stacker = new FrameStacker(4, 1, 1, 8, StackMethod.Sum);
        stacker.Add(Make(4, 1, 1, 8, 200, 10, 0, 255));
        stacker.Add(Make(4, 1, 1, 8, 100, 20, 255, 255));

        Assert.Equal(new ushort[] { 255, 30, 255, 255 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mono8_Median_OddCount_KeepsDuplicatesAndRejectsOutliers()
    {
        var stacker = new FrameStacker(4, 1, 1, 8, StackMethod.Median);
        stacker.Add(Make(4, 1, 1, 8, 5, 0, 7, 10));
        stacker.Add(Make(4, 1, 1, 8, 9, 100, 7, 12));
        stacker.Add(Make(4, 1, 1, 8, 5, 255, 8, 11));

        // p0 keeps the duplicated 5; p1 drops both outliers (0 and 255); p2 has 7 twice.
        Assert.Equal(new ushort[] { 5, 100, 7, 11 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mono8_Median_EvenCount_RoundsHalvesUp()
    {
        var stacker = new FrameStacker(4, 1, 1, 8, StackMethod.Median);
        stacker.Add(Make(4, 1, 1, 8, 0, 254, 10, 1));
        stacker.Add(Make(4, 1, 1, 8, 1, 255, 10, 2));

        // (0+1+1)/2 = 1, (254+255+1)/2 = 255, (10+10+1)/2 = 10, (1+2+1)/2 = 2.
        Assert.Equal(new ushort[] { 1, 255, 10, 2 }, stacker.Result().Samples);
    }

    // ---------------------------------------------------------------- RGB 8-bit

    [Fact]
    public void Rgb8_Mean_AveragesEachChannelIndependently()
    {
        var stacker = new FrameStacker(2, 1, 3, 8, StackMethod.Mean);
        stacker.Add(Make(2, 1, 3, 8, 10, 20, 30, 40, 50, 60));
        stacker.Add(Make(2, 1, 3, 8, 12, 22, 32, 42, 52, 62));

        Assert.Equal(new ushort[] { 11, 21, 31, 41, 51, 61 }, stacker.Result().Samples);
    }

    [Fact]
    public void Rgb8_Sum_ClampsPerChannel()
    {
        var stacker = new FrameStacker(2, 1, 3, 8, StackMethod.Sum);
        stacker.Add(Make(2, 1, 3, 8, 200, 50, 0, 10, 20, 30));
        stacker.Add(Make(2, 1, 3, 8, 100, 250, 10, 1, 2, 3));

        // R and G of pixel 0 saturate at 300 -> 255; the rest stay exact.
        Assert.Equal(new ushort[] { 255, 255, 10, 11, 22, 33 }, stacker.Result().Samples);
    }

    [Fact]
    public void Rgb8_Median_OddCount_IsPerChannelPerPixel()
    {
        var stacker = new FrameStacker(2, 1, 3, 8, StackMethod.Median);
        stacker.Add(Make(2, 1, 3, 8, 10, 200, 30, 5, 5, 5));
        stacker.Add(Make(2, 1, 3, 8, 12, 100, 60, 7, 3, 9));
        stacker.Add(Make(2, 1, 3, 8, 11, 150, 40, 6, 4, 7));

        // Pixel 0: (11, 150, 40). Pixel 1 channels are 5/6/7, 3/4/5, 5/7/9.
        Assert.Equal(new ushort[] { 11, 150, 40, 6, 4, 7 }, stacker.Result().Samples);
    }

    [Fact]
    public void Rgb8_Median_EvenCount_RoundsHalvesUpPerChannel()
    {
        var even = new FrameStacker(1, 1, 3, 8, StackMethod.Median);
        even.Add(Make(1, 1, 3, 8, 0, 1, 100));
        even.Add(Make(1, 1, 3, 8, 0, 0, 101));
        Assert.Equal(new ushort[] { 0, 1, 101 }, even.Result().Samples);

        var mean = new FrameStacker(1, 1, 3, 8, StackMethod.Mean);
        mean.Add(Make(1, 1, 3, 8, 0, 1, 100));
        mean.Add(Make(1, 1, 3, 8, 0, 0, 101));
        Assert.Equal(new ushort[] { 0, 0, 100 }, mean.Result().Samples);
    }

    // ---------------------------------------------------------------- mono 16-bit

    [Fact]
    public void Mono16_Mean_KeepsFullDepthAndTruncates()
    {
        var stacker = new FrameStacker(3, 1, 1, 16, StackMethod.Mean);
        stacker.Add(Make(3, 1, 1, 16, 1000, 65535, 0));
        stacker.Add(Make(3, 1, 1, 16, 1001, 65534, 0));

        Assert.Equal(new ushort[] { 1000, 65534, 0 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mono16_Sum_ClampsAt65535()
    {
        var stacker = new FrameStacker(3, 1, 1, 16, StackMethod.Sum);
        stacker.Add(Make(3, 1, 1, 16, 40000, 30000, 0));
        stacker.Add(Make(3, 1, 1, 16, 40000, 30000, 1));

        Assert.Equal(new ushort[] { 65535, 60000, 1 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mono16_Median_OddCount_HandlesFullScaleDuplicatesAndOutliers()
    {
        var stacker = new FrameStacker(4, 1, 1, 16, StackMethod.Median);
        stacker.Add(Make(4, 1, 1, 16, 65535, 0, 300, 1000));
        stacker.Add(Make(4, 1, 1, 16, 0, 65534, 200, 1002));
        stacker.Add(Make(4, 1, 1, 16, 65534, 65535, 100, 1001));

        Assert.Equal(new ushort[] { 65534, 65534, 200, 1001 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mono16_Median_EvenCount_RoundsHalvesUp()
    {
        var stacker = new FrameStacker(3, 1, 1, 16, StackMethod.Median);
        stacker.Add(Make(3, 1, 1, 16, 65534, 0, 1000));
        stacker.Add(Make(3, 1, 1, 16, 65535, 1, 1001));

        Assert.Equal(new ushort[] { 65535, 1, 1001 }, stacker.Result().Samples);
    }

    // ---------------------------------------------------------------- RGB 16-bit

    [Fact]
    public void Rgb16_Mean_TruncatesEachChannel()
    {
        var stacker = new FrameStacker(1, 1, 3, 16, StackMethod.Mean);
        stacker.Add(Make(1, 1, 3, 16, 1000, 2000, 3000));
        stacker.Add(Make(1, 1, 3, 16, 1001, 2001, 3001));

        Assert.Equal(new ushort[] { 1000, 2000, 3000 }, stacker.Result().Samples);
    }

    [Fact]
    public void Rgb16_Sum_ClampsEachChannelAt65535()
    {
        var stacker = new FrameStacker(1, 1, 3, 16, StackMethod.Sum);
        stacker.Add(Make(1, 1, 3, 16, 30000, 65535, 40000));
        stacker.Add(Make(1, 1, 3, 16, 30001, 1, 20000));

        // 60001 exact, 65536 -> 65535, 60000 exact.
        Assert.Equal(new ushort[] { 60001, 65535, 60000 }, stacker.Result().Samples);
    }

    [Fact]
    public void Rgb16_Median_OddCount_IsPerChannelPerPixel()
    {
        var stacker = new FrameStacker(2, 1, 3, 16, StackMethod.Median);
        stacker.Add(Make(2, 1, 3, 16, 100, 60000, 30000, 65535, 0, 5));
        stacker.Add(Make(2, 1, 3, 16, 30000, 40000, 65535, 0, 65535, 6));
        stacker.Add(Make(2, 1, 3, 16, 20000, 50000, 0, 1, 1, 7));

        Assert.Equal(new ushort[] { 20000, 50000, 30000, 1, 1, 6 }, stacker.Result().Samples);
    }

    [Fact]
    public void Rgb16_Median_EvenCount_RoundsHalvesUpPerChannel()
    {
        var stacker = new FrameStacker(1, 1, 3, 16, StackMethod.Median);
        stacker.Add(Make(1, 1, 3, 16, 65534, 0, 1000));
        stacker.Add(Make(1, 1, 3, 16, 65535, 1, 1001));

        Assert.Equal(new ushort[] { 65535, 1, 1001 }, stacker.Result().Samples);
    }

    // ---------------------------------------------------------------- contracts

    [Fact]
    public void TwoFrameHalfBoundary_MeanTruncatesButMedianRoundsUp()
    {
        // The documented asymmetry: same inputs, different rounding rules.
        Assert.Equal(0, StackMono(StackMethod.Mean, 8, 0, 1));
        Assert.Equal(1, StackMono(StackMethod.Median, 8, 0, 1));

        Assert.Equal(65534, StackMono(StackMethod.Mean, 16, 65534, 65535));
        Assert.Equal(65535, StackMono(StackMethod.Median, 16, 65534, 65535));
    }

    [Theory]
    [InlineData(10, 11, 10)]
    [InlineData(1, 2, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(254, 255, 254)]
    public void Mean_HalfBoundaries_Truncate(int a, int b, int expected)
    {
        Assert.Equal(expected, StackMono(StackMethod.Mean, 8, (ushort)a, (ushort)b));
    }

    [Fact]
    public void Sum_BelowFullScale_IsExact()
    {
        Assert.Equal(30, StackMono(StackMethod.Sum, 8, 10, 20));
        Assert.Equal(60000, StackMono(StackMethod.Sum, 16, 30000, 30000));
    }

    [Fact]
    public void ReferenceSubtraction_ClampsThenAverages()
    {
        var reference = Make(2, 1, 1, 8, 50, 200);
        var stacker = new FrameStacker(2, 1, 1, 8, StackMethod.Mean);
        stacker.Add(Make(2, 1, 1, 8, 0, 255), reference);
        stacker.Add(Make(2, 1, 1, 8, 100, 250), reference);

        // The requested contract case: inputs 0 and 100 with reference 50 average to 25.
        // Pixel 1: 255-200=55 and 250-200=50 -> 105/2 = 52.
        Assert.Equal(new ushort[] { 25, 52 }, stacker.Result().Samples);
    }

    [Fact]
    public void ReferenceSubtraction_AppliesBeforeTheMedian()
    {
        var reference = Make(1, 1, 1, 8, 50);
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Median);
        stacker.Add(Make(1, 1, 1, 8, 0), reference);
        stacker.Add(Make(1, 1, 1, 8, 100), reference);
        stacker.Add(Make(1, 1, 1, 8, 100), reference);

        // 0-50 clamps to 0; 100-50 = 50 twice; median of 0, 50, 50 is 50.
        Assert.Equal(50, stacker.Result().Samples[0]);
    }

    [Theory]
    [InlineData(StackMethod.Mean)]
    [InlineData(StackMethod.Sum)]
    public void SixteenBitAccumulatorGuard_65538SaturatedFrames_StayAt65535(StackMethod method)
    {
        // 65538 x 65535 = 4,295,032,830, above uint.MaxValue (4,294,967,295): a 32-bit accumulator
        // would wrap. The 64-bit accumulator must keep the mean exact and the sum clamped.
        var stacker = new FrameStacker(1, 1, 1, 16, method);
        var frame = Make(1, 1, 1, 16, 65535);
        for (int i = 0; i < 65_538; i++) stacker.Add(frame);

        Assert.Equal(65535, stacker.Result().Samples[0]);
    }

    [Fact]
    public void Median_OutlierAtBothEnds_IsStillTheMiddleValue()
    {
        // Five frames: one black, one white, three around 100. The median ignores both outliers.
        Assert.Equal(100, StackMono(StackMethod.Median, 8, 0, 99, 100, 101, 255));
        Assert.Equal(100, StackMono(StackMethod.Median, 16, 0, 99, 100, 101, 65535));
    }
}
