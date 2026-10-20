namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Processing;
using Xunit;

/// <summary>
/// The streaming overload's contract: mean/sum read every index exactly once, median re-reads one band
/// at a time, and neither holds a frame across a <c>readFrame</c> call — so a source that returns the
/// same buffer every time must still integrate correctly.
/// </summary>
public sealed class IntegrationStreamingTests
{
    private const int Width = 9;
    private const int Height = 7;
    private const int FrameCount = 5;

    private static ushort Value(int frame, int sample) => (ushort)(((frame * 37) + (sample * 11)) % 251);

    private static Frame Fresh(int index, int channels = 1)
    {
        var frame = new Frame(Width, Height, channels, 16) { SourceName = $"src-{index}.ser" };
        for (int p = 0; p < Width * Height; p++)
        {
            for (int c = 0; c < channels; c++)
            {
                frame.Samples[(p * channels) + c] = Value(index, p + (c * 3));
            }
        }

        return frame;
    }

    private static List<Frame> List() => Enumerable.Range(0, FrameCount).Select(i => Fresh(i)).ToList();

    private static AlignmentOffset[] Shifts()
    {
        var shifts = new AlignmentOffset[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            shifts[i] = new AlignmentOffset((i % 3) - 1, ((i / 2) % 3) - 1);
        }

        return shifts;
    }

    /// <summary>Counts reads per index and records the order they were requested in.</summary>
    private sealed class CountingSource
    {
        private readonly Func<int, Frame> _make;

        public CountingSource(Func<int, Frame> make, int count)
        {
            _make = make;
            Reads = new int[count];
        }

        public int[] Reads { get; }

        public List<int> Order { get; } = new();

        public Frame Read(int index)
        {
            Reads[index]++;
            Order.Add(index);
            return _make(index);
        }
    }

    private static IntegrationOptions Options(IntegrationMethod method, int bandRows = 3) => new()
    {
        Method = method,
        Coverage = CoveragePolicy.KeepCanvas,
        Subpixel = true,
        MedianBandRows = bandRows,
    };

    // ------------------------------------------------------------ read counts and order

    [Theory]
    [InlineData(IntegrationMethod.Mean)]
    [InlineData(IntegrationMethod.Sum)]
    public void MeanAndSum_ReadEveryIndexExactlyOnceInOrder(IntegrationMethod method)
    {
        var source = new CountingSource(index => Fresh(index), FrameCount);

        IntegrationResult result = FinalImageIntegrator.Integrate(
            source.Read, FrameCount, Shifts(), null, Options(method));

        Assert.All(source.Reads, reads => Assert.Equal(1, reads));
        Assert.Equal(Enumerable.Range(0, FrameCount), source.Order);
        Assert.Equal(method, result.Method);
    }

    [Fact]
    public void Median_ReadsEachIndexOnceForValidationAndOncePerBand()
    {
        var source = new CountingSource(index => Fresh(index), FrameCount);
        IntegrationOptions options = Options(IntegrationMethod.Median, bandRows: 3);

        IntegrationResult result = FinalImageIntegrator.Integrate(
            source.Read, FrameCount, Shifts(), null, options);

        int bands = (result.OutputRect.Height + options.MedianBandRows - 1) / options.MedianBandRows;
        Assert.True(bands > 1, "the test needs more than one band to distinguish the passes");
        Assert.All(source.Reads, reads => Assert.Equal(1 + bands, reads));

        // Probe/validate pass, then one ascending pass per band. The probe reads index 0 and validation
        // reads 1..n-1, so the first frameCount entries are exactly one ascending sweep.
        Assert.Equal(Enumerable.Range(0, FrameCount), source.Order.Take(FrameCount));
        for (int band = 0; band < bands; band++)
        {
            Assert.Equal(
                Enumerable.Range(0, FrameCount),
                source.Order.Skip(FrameCount + (band * FrameCount)).Take(FrameCount));
        }

        Assert.Equal(MedianRoute.Streamed, result.MedianRoute);
    }

    // ------------------------------------------------------------ buffer reuse

    [Theory]
    [InlineData(IntegrationMethod.Mean)]
    [InlineData(IntegrationMethod.Sum)]
    [InlineData(IntegrationMethod.Median)]
    public void ReusedSourceBuffer_MatchesTheListBasedIntegration(IntegrationMethod method)
    {
        // One Frame instance whose samples are rewritten on every read: anything that kept a reference
        // to a previously returned frame would integrate the last frame's pixels over and over.
        var buffer = new Frame(Width, Height, 1, 16) { SourceName = "reused.ser" };

        Frame Read(int index)
        {
            for (int p = 0; p < Width * Height; p++)
            {
                buffer.Samples[p] = Value(index, p);
            }

            buffer.Index = index;
            return buffer;
        }

        IntegrationOptions options = Options(method);
        IntegrationResult streamed = FinalImageIntegrator.Integrate(Read, FrameCount, Shifts(), null, options);
        IntegrationResult listed = FinalImageIntegrator.Integrate(List(), Shifts(), null, options);

        Assert.Equal(listed.Frame.Samples, streamed.Frame.Samples);
        Assert.Equal(listed.Coverage, streamed.Coverage);
        Assert.Equal(listed.OutputRect, streamed.OutputRect);
        Assert.Equal(listed.Frame.Width, streamed.Frame.Width);
        Assert.Equal(listed.Frame.Height, streamed.Frame.Height);
        Assert.Equal(listed.PrecisionSamples, streamed.PrecisionSamples);

        // Provenance comes from the frames as they were read, not from the buffer's final state alone.
        Assert.Equal("reused.ser", streamed.Frame.SourceName);
        Assert.All(streamed.Contributors, contributor => Assert.Equal("reused.ser", contributor.SourceName));
    }

    [Fact]
    public void StreamingMedian_IgnoresTheRetentionCapBecauseItRetainsNothing()
    {
        var source = new CountingSource(index => Fresh(index), FrameCount);
        IntegrationOptions options = Options(IntegrationMethod.Median);
        options.MaxRetainedMedianFrames = 0; // would force the tiled route on the list overload

        IntegrationResult result = FinalImageIntegrator.Integrate(
            source.Read, FrameCount, Shifts(), null, options);

        Assert.Equal(MedianRoute.Streamed, result.MedianRoute);
        Assert.Equal(0, result.MedianEstimatedBytes);
    }

    [Fact]
    public void StreamingMedian_MatchesTheListBasedMedian()
    {
        IntegrationOptions options = Options(IntegrationMethod.Median);
        var source = new CountingSource(index => Fresh(index), FrameCount);

        IntegrationResult streamed = FinalImageIntegrator.Integrate(source.Read, FrameCount, Shifts(), null, options);
        IntegrationResult listed = FinalImageIntegrator.Integrate(List(), Shifts(), null, options);

        Assert.Equal(listed.Frame.Samples, streamed.Frame.Samples);
        Assert.Equal(listed.Coverage, streamed.Coverage);
    }

    // ------------------------------------------------------------ weights

    [Fact]
    public void StreamingMean_AppliesWeights()
    {
        double[] weights = [3, 1, 2, 1, 4];
        IntegrationOptions options = Options(IntegrationMethod.Mean);

        IntegrationResult streamed = FinalImageIntegrator.Integrate(
            index => Fresh(index), FrameCount, Shifts(), weights, options);
        IntegrationResult listed = FinalImageIntegrator.Integrate(List(), Shifts(), weights, options);

        Assert.Equal(listed.Frame.Samples, streamed.Frame.Samples);
        Assert.Equal(listed.PrecisionSamples, streamed.PrecisionSamples);
        Assert.Equal(weights, streamed.Contributors.Select(c => c.Weight));
    }

    // ------------------------------------------------------------ validation

    [Fact]
    public void ZeroFrameCountIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinalImageIntegrator.Integrate(
            _ => throw new InvalidOperationException("must not be called"),
            0,
            Array.Empty<AlignmentOffset>(),
            null,
            new IntegrationOptions()));
    }

    [Fact]
    public void ShiftCountMustMatchTheFrameCount()
    {
        Assert.Throws<ArgumentException>(() => FinalImageIntegrator.Integrate(
            index => Fresh(index), 2, new[] { AlignmentOffset.Zero }, null, new IntegrationOptions()));
    }

    [Fact]
    public void NullFromTheSourceIsReportedWithItsIndex()
    {
        var error = Assert.Throws<InvalidOperationException>(() => FinalImageIntegrator.Integrate(
            index => index == 1 ? null! : Fresh(index),
            FrameCount,
            Shifts(),
            null,
            new IntegrationOptions()));

        Assert.Contains("readFrame(1)", error.Message);
    }

    [Fact]
    public void AMismatchedGeometryIsRejectedMidStream()
    {
        var error = Assert.Throws<ArgumentException>(() => FinalImageIntegrator.Integrate(
            index => index == 2 ? new Frame(Width + 1, Height, 1, 16) : Fresh(index),
            FrameCount,
            Shifts(),
            null,
            new IntegrationOptions()));

        Assert.Contains("geometry", error.Message);
    }

    [Fact]
    public void CancellationIsCheckedBeforeTheFirstRead()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        bool read = false;

        Assert.Throws<OperationCanceledException>(() => FinalImageIntegrator.Integrate(
            index =>
            {
                read = true;
                return Fresh(index);
            },
            FrameCount,
            Shifts(),
            null,
            new IntegrationOptions(),
            source.Token));

        Assert.False(read);
    }

    [Fact]
    public void NonUniformMedianWeightsAreRejectedBeforeReading()
    {
        bool read = false;

        Assert.Throws<NotSupportedException>(() => FinalImageIntegrator.Integrate(
            index =>
            {
                read = true;
                return Fresh(index);
            },
            FrameCount,
            Shifts(),
            [1, 2, 1, 1, 1],
            new IntegrationOptions { Method = IntegrationMethod.Median }));

        Assert.False(read);
    }

    [Fact]
    public void StreamingFractionalMosaicShiftIsRejected()
    {
        Frame Mosaic(int index)
        {
            Frame frame = Fresh(index);
            frame.Bayer = BayerPattern.RGGB;
            return frame;
        }

        var shifts = new AlignmentOffset[FrameCount];
        for (int i = 0; i < shifts.Length; i++)
        {
            shifts[i] = new AlignmentOffset(0.5, 0);
        }

        Assert.Throws<BayerPhaseException>(() => FinalImageIntegrator.Integrate(
            Mosaic, FrameCount, shifts, null, new IntegrationOptions { Subpixel = true }));

        Assert.Throws<BayerPhaseException>(() => FinalImageIntegrator.Integrate(
            Mosaic, FrameCount, shifts, null,
            new IntegrationOptions { Method = IntegrationMethod.Median, Subpixel = true }));
    }

    [Fact]
    public void StreamingOddMosaicShiftBetweenFramesIsRejected()
    {
        Frame Mosaic(int index)
        {
            Frame frame = Fresh(index);
            frame.Bayer = BayerPattern.RGGB;
            return frame;
        }

        var shifts = new AlignmentOffset[FrameCount];
        for (int i = 0; i < shifts.Length; i++)
        {
            shifts[i] = new AlignmentOffset(i == 0 ? 0 : 1, 0);
        }

        Assert.Throws<BayerPhaseException>(() => FinalImageIntegrator.Integrate(
            Mosaic, FrameCount, shifts, null, new IntegrationOptions { Subpixel = true }));
    }
}
