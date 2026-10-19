namespace Opipp.Image.Tests;

using Opipp.Core.Frames;
using Opipp.Core.Stacking;

/// <summary>
/// Contract tests for <see cref="FrameStacker"/> that are not about a particular numerical path:
/// sample ownership, the median frame limit, constructor validation, cancellation, and the current
/// Bayer behaviour. These encode promises callers may rely on, so a future change that breaks one of
/// them is a deliberate edit to this file, not an accident.
/// </summary>
public class StackingContractTests
{
    private static Frame Make(int width, int height, int channels, int bitDepth, params ushort[] samples) =>
        new(width, height, channels, bitDepth, samples);

    // ---------------------------------------------------------------- ownership

    [Fact]
    public void Median_OneReusedBuffer_YieldsTheMedianOfTheSubmittedValues()
    {
        // The caller reuses a single buffer, as a streaming reader does. Each Add must snapshot the
        // samples: retaining the reference would make all three reads see the last value (100).
        var buffer = new ushort[] { 10 };
        var frame = new Frame(1, 1, 1, 8, buffer);
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Median);

        stacker.Add(frame);
        buffer[0] = 30;
        stacker.Add(frame);
        buffer[0] = 100;
        stacker.Add(frame);

        Assert.Equal(3, stacker.FrameCount);
        Assert.Equal(30, stacker.Result().Samples[0]);
        Assert.Equal(100, buffer[0]); // Add never writes through the caller's buffer
    }

    [Fact]
    public void Median_MutatingCallerArraysAfterAdd_LeavesTheStackUnchanged()
    {
        var first = Make(3, 1, 1, 8, 10, 20, 30);
        var second = Make(3, 1, 1, 8, 12, 22, 32);
        var third = Make(3, 1, 1, 8, 11, 21, 31);
        var stacker = new FrameStacker(3, 1, 1, 8, StackMethod.Median);
        stacker.Add(first);
        stacker.Add(second);
        stacker.Add(third);

        first.Clear();
        Array.Fill(second.Samples, (ushort)255);
        third.Samples[0] = 0;

        Assert.Equal(new ushort[] { 11, 21, 31 }, stacker.Result().Samples);
    }

    [Fact]
    public void Mean_ConsumesSamplesOnAdd_SoLaterMutationIsHarmless()
    {
        var first = Make(1, 1, 1, 8, 10);
        var second = Make(1, 1, 1, 8, 30);
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Mean);
        stacker.Add(first);
        stacker.Add(second);

        first.Samples[0] = 100;
        second.Samples[0] = 100;

        Assert.Equal(20, stacker.Result().Samples[0]);
    }

    [Fact]
    public void Add_DoesNotModifyTheCallersFrameOrTheReference()
    {
        var frameSamples = new ushort[] { 0, 100, 255 };
        var referenceSamples = new ushort[] { 50, 50, 50 };
        var frame = Make(3, 1, 1, 8, frameSamples);
        var reference = Make(3, 1, 1, 8, referenceSamples);
        var stacker = new FrameStacker(3, 1, 1, 8, StackMethod.Mean);

        stacker.Add(frame, reference);

        Assert.Equal(new ushort[] { 0, 100, 255 }, frameSamples);
        Assert.Equal(new ushort[] { 50, 50, 50 }, referenceSamples);
        Assert.Equal(new ushort[] { 0, 50, 205 }, stacker.Result().Samples);
    }

    // ---------------------------------------------------------------- median limit

    [Fact]
    public void Median_AcceptsTheConfiguredMaximumOf64Frames()
    {
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Median);
        Assert.Equal(64, stacker.MaxMedianFrames);

        for (int i = 0; i < 64; i++) stacker.Add(Make(1, 1, 1, 8, (ushort)i));

        Assert.Equal(64, stacker.FrameCount);
        // Even count over 0..63: (31 + 32 + 1) / 2 = 32.
        Assert.Equal(32, stacker.Result().Samples[0]);
    }

    [Fact]
    public void Median_RejectsThe65thFrame_WithAnActionableError()
    {
        var stacker = new FrameStacker(2, 1, 1, 8, StackMethod.Median);
        var frame = Make(2, 1, 1, 8, 1, 2);
        for (int i = 0; i < 64; i++) stacker.Add(frame);

        var error = Assert.Throws<MedianFrameLimitException>(() => stacker.Add(frame));

        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(64, error.FrameLimit);
        Assert.Equal(2, error.Width);
        Assert.Equal(1, error.Height);
        Assert.Equal(1, error.Channels);
        Assert.Equal(4, error.BytesPerFrame); // 2 x 1 x 1 x 2 bytes
        Assert.Contains("at most 64 frames", error.Message);
        Assert.Contains("frame 65", error.Message);
        Assert.Contains("Disk-backed median stacking is not implemented", error.Message);
        Assert.Equal(64, stacker.FrameCount); // a rejected frame is not counted
    }

    [Fact]
    public void Median_HonoursACustomLimit()
    {
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Median, maxMedianFrames: 3);
        for (int i = 0; i < 3; i++) stacker.Add(Make(1, 1, 1, 8, (ushort)i));

        var error = Assert.Throws<MedianFrameLimitException>(() => stacker.Add(Make(1, 1, 1, 8, 9)));
        Assert.Equal(3, error.FrameLimit);
        Assert.Equal(3, stacker.FrameCount);
    }

    [Fact]
    public void MeanAndSum_AreUnbounded_SoTheMedianCapDoesNotApply()
    {
        var stacker = new FrameStacker(1, 1, 1, 8, StackMethod.Sum);
        var frame = Make(1, 1, 1, 8, 1);
        for (int i = 0; i < 100; i++) stacker.Add(frame);

        Assert.Equal(100, stacker.FrameCount);
        Assert.Equal(100, stacker.Result().Samples[0]);
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Constructor_RejectsAnUnknownStackMethod(int raw)
    {
        var method = (StackMethod)raw;
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameStacker(1, 1, 1, 8, method));
        Assert.Equal("method", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsAnInvalidMedianFrameLimit(int limit)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameStacker(1, 1, 1, 8, StackMethod.Median, limit));
        Assert.Equal("maxMedianFrames", error.ParamName);
    }

    [Theory]
    [InlineData(0, 1, 1, 8, "width")]
    [InlineData(-4, 1, 1, 8, "width")]
    [InlineData(1, 0, 1, 8, "height")]
    [InlineData(1, 1, 2, 8, "channels")]
    [InlineData(1, 1, 4, 8, "channels")]
    [InlineData(1, 1, 1, 12, "bitDepth")]
    [InlineData(1, 1, 1, 0, "bitDepth")]
    public void Constructor_RejectsInvalidGeometry(int width, int height, int channels, int bitDepth, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameStacker(width, height, channels, bitDepth));
        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void Median_RejectsMismatchedGeometryChannelCountAndDepth()
    {
        var stacker = new FrameStacker(4, 4, 1, 8, StackMethod.Median);

        Assert.Throws<ArgumentException>(() => stacker.Add(Make(5, 4, 1, 8, new ushort[20])));
        Assert.Throws<ArgumentException>(() => stacker.Add(Make(4, 3, 1, 8, new ushort[12])));
        Assert.Throws<ArgumentException>(() => stacker.Add(Make(4, 4, 3, 8, new ushort[48])));
        Assert.Throws<ArgumentException>(() => stacker.Add(Make(4, 4, 1, 16, new ushort[16])));
        Assert.Equal(0, stacker.FrameCount);
    }

    [Fact]
    public void Result_BeforeAnyFrame_Throws()
    {
        var stacker = new FrameStacker(2, 2, 1, 8, StackMethod.Median);
        Assert.Throws<InvalidOperationException>(() => stacker.Result());
    }

    // ---------------------------------------------------------------- cancellation

    [Theory]
    [InlineData(StackMethod.Mean)]
    [InlineData(StackMethod.Sum)]
    [InlineData(StackMethod.Median)]
    public void Result_ObservesACancelledToken(StackMethod method)
    {
        var stacker = new FrameStacker(2, 1, 1, 8, method);
        stacker.Add(Make(2, 1, 1, 8, 10, 20));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => stacker.Result(cancelled.Token));
        // Without a cancelled token the same stacker still produces a result.
        Assert.Equal(2, stacker.Result().Samples.Length);
    }

    // ---------------------------------------------------------------- Bayer policy (current behaviour)

    [Fact]
    public void BayerFrame_IsAcceptedAndTheResultIsMarkedNone()
    {
        // Current policy, documented not endorsed: the stacker ignores Frame.Bayer on input - it does
        // not reject a mosaic and does not carry the pattern to the master. Result() always sets
        // Bayer = None, so a stacked Bayer selection comes out as a plain mono master and a later
        // debayer can no longer recognise it. Changing this is a policy change, not a bug fix in the
        // stacker alone.
        var bayer = Make(2, 1, 1, 8, 10, 20);
        bayer.Bayer = BayerPattern.RGGB;
        var stacker = new FrameStacker(2, 1, 1, 8, StackMethod.Mean);

        stacker.Add(bayer);
        var master = stacker.Result();

        Assert.Equal(new ushort[] { 10, 20 }, master.Samples);
        Assert.Equal(BayerPattern.None, master.Bayer);
    }

    [Fact]
    public void NonBayerFrame_IsUnaffectedByTheBayerPolicy()
    {
        var frame = Make(2, 1, 1, 8, 10, 20);
        Assert.Equal(BayerPattern.None, frame.Bayer);

        var stacker = new FrameStacker(2, 1, 1, 8, StackMethod.Median);
        stacker.Add(frame);

        Assert.Equal(BayerPattern.None, stacker.Result().Bayer);
    }
}
