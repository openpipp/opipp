namespace Pipp.Image.Tests;

using Pipp.Core.Frames;
using Pipp.Core.Stacking;

/// <summary>
/// Contract tests for <see cref="FrameStacker"/>, the managed replacement for
/// <c>pipp_dll!stack_frames</c>. The expected values come from the native stacker's arithmetic
/// (REA evidence <c>ev_c981d57d…</c>: a 32-bit per-pixel accumulator followed by unsigned integer
/// division), not from the implementation under test.
/// </summary>
public class StackingTests
{
    private static Frame Constant(int width, int height, ushort value, int channels = 1, int bitDepth = 8)
    {
        var frame = new Frame(width, height, channels, bitDepth);
        for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = value;
        return frame;
    }

    private static Frame Samples(int width, int height, int channels, int bitDepth, params ushort[] samples) =>
        new(width, height, channels, bitDepth, samples);

    // ---------------------------------------------------------------- mean

    [Fact]
    public void MeanOfTwoFrames_TruncatesTheHalf()
    {
        // The engine divides 21 by 2 with integer division: 10.5 -> 10.
        var stacker = new FrameStacker(2, 1, 1, 8, StackMethod.Mean);
        stacker.Add(Samples(2, 1, 1, 8, 10, 20));
        stacker.Add(Samples(2, 1, 1, 8, 11, 22));

        var result = stacker.Result();

        Assert.Equal(new ushort[] { 10, 21 }, result.Samples);
    }

    [Fact]
    public void MeanWithoutFraction_IsExact()
    {
        var stacker = new FrameStacker(2, 1, 1, 8, StackMethod.Mean);
        stacker.Add(Samples(2, 1, 1, 8, 10, 20));
        stacker.Add(Samples(2, 1, 1, 8, 12, 22));

        Assert.Equal(new ushort[] { 11, 21 }, stacker.Result().Samples);
    }

    [Theory]
    [InlineData(10, 11, 10)] // 10.5 -> 10
    [InlineData(1, 2, 1)]    // 1.5 -> 1
    [InlineData(0, 1, 0)]    // 0.5 -> 0
    [InlineData(254, 255, 254)]
    public void MeanHalfBoundary_RoundsDown(int a, int b, int expected)
    {
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Mean);
        stacker.Add(Constant(1, 1, (ushort)a));
        stacker.Add(Constant(1, 1, (ushort)b));

        Assert.Equal(expected, stacker.Result().Samples[0]);
    }

    [Fact]
    public void MeanOfFourFrames_TruncatesFraction()
    {
        // (1 + 1 + 1 + 2) / 4 = 1.25 -> 1.
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Mean);
        foreach (ushort v in new ushort[] { 1, 1, 1, 2 }) stacker.Add(Constant(1, 1, v));

        Assert.Equal(1, stacker.Result().Samples[0]);
    }

    [Fact]
    public void MeanOfSixteenBitFrames_KeepsFullDepthAndTruncates()
    {
        var stacker = new FrameStacker(2, 1, 1, 16, StackMethod.Mean);
        stacker.Add(Samples(2, 1, 1, 16, 1000, 65535));
        stacker.Add(Samples(2, 1, 1, 16, 1001, 65534));

        var result = stacker.Result();

        Assert.Equal(16, result.BitDepth);
        Assert.Equal(new ushort[] { 1000, 65534 }, result.Samples);
    }

    [Fact]
    public void MeanOfSingleFrame_ReturnsThatFrame()
    {
        var only = Samples(3, 1, 1, 8, 0, 128, 255);
        var stacker = new FrameStacker(3, 1, 1, 8, StackMethod.Mean);
        stacker.Add(only);

        var result = stacker.Result();

        Assert.Equal(only.Samples, result.Samples);
        Assert.Equal(1, stacker.FrameCount);
    }

    [Fact]
    public void MeanAveragesEachChannelIndependently()
    {
        // Two RGB pixels; the middle sample of each pixel is averaged separately.
        var stacker = new FrameStacker(2, 1, 3, 8, StackMethod.Mean);
        stacker.Add(Samples(2, 1, 3, 8, 10, 20, 30, 40, 50, 60));
        stacker.Add(Samples(2, 1, 3, 8, 12, 22, 32, 42, 52, 62));

        Assert.Equal(new ushort[] { 11, 21, 31, 41, 51, 61 }, stacker.Result().Samples);
    }

    // ---------------------------------------------------------------- rejection

    [Fact]
    public void AddRejectsDifferentWidth()
    {
        var stacker = new FrameStacker(4, 4, 1, 8);
        Assert.Throws<ArgumentException>(() => stacker.Add(Constant(5, 4, 1)));
    }

    [Fact]
    public void AddRejectsDifferentHeight()
    {
        var stacker = new FrameStacker(4, 4, 1, 8);
        Assert.Throws<ArgumentException>(() => stacker.Add(Constant(4, 3, 1)));
    }

    [Fact]
    public void AddRejectsDifferentChannelCount()
    {
        var stacker = new FrameStacker(4, 4, 1, 8);
        Assert.Throws<ArgumentException>(() => stacker.Add(Constant(4, 4, 1, channels: 3)));
    }

    [Fact]
    public void AddRejectsDifferentBitDepth()
    {
        var stacker = new FrameStacker(4, 4, 1, 8);
        Assert.Throws<ArgumentException>(() => stacker.Add(Constant(4, 4, 1, bitDepth: 16)));
    }

    [Fact]
    public void AddRejectsMismatchedSubtractFrame()
    {
        var stacker = new FrameStacker(4, 4, 1, 8);
        Assert.Throws<ArgumentException>(() => stacker.Add(Constant(4, 4, 10), subtract: Constant(3, 4, 1)));
    }

    [Fact]
    public void ResultWithoutFrames_Throws()
    {
        var stacker = new FrameStacker(2, 2, 1, 8);
        Assert.Throws<InvalidOperationException>(() => stacker.Result());
    }
}
