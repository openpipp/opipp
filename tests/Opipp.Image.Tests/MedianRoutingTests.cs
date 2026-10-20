namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Xunit;

/// <summary>
/// R5: median routing. A selection at or below <see cref="IntegrationOptions.MaxRetainedMedianFrames"/>
/// whose estimated bytes fit the declared in-memory budget must not need scratch at all; anything above
/// either limit must take the exact disk-backed route. Both routes must produce identical pixels, so the
/// only difference the tests are allowed to see is <see cref="IntegrationResult.MedianRoute"/>.
/// </summary>
public sealed class MedianRoutingTests
{
    private const int Width = 8;
    private const int Height = 8;

    /// <summary>Deterministic per-sample noise so a route cannot coincide with the reference by luck.</summary>
    private static ushort Sample(int sample, int frame)
    {
        uint h = unchecked((uint)sample * 2654435761u) ^ unchecked((uint)(frame + 1) * 2246822519u);
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return (ushort)(h % 65536);
    }

    private static Frame MakeFrame(int width, int height, int channels, int seed)
    {
        var frame = new Frame(width, height, channels, 16);
        for (int p = 0; p < frame.Samples.Length; p++)
        {
            frame.Samples[p] = Sample(p, seed);
        }

        return frame;
    }

    private static List<Frame> MakeFrames(int count, int width = Width, int height = Height, int channels = 1)
    {
        var frames = new List<Frame>(count);
        for (int i = 0; i < count; i++)
        {
            frames.Add(MakeFrame(width, height, channels, i));
        }

        return frames;
    }

    private static AlignmentOffset[] Shifts(int count)
    {
        var shifts = new AlignmentOffset[count];
        for (int i = 0; i < count; i++)
        {
            shifts[i] = new AlignmentOffset((i % 3) - 1, ((i / 3) % 3) - 1);
        }

        return shifts;
    }

    private static IntegrationOptions Options(int bandRows = 4)
    {
        return new IntegrationOptions
        {
            Method = IntegrationMethod.Median,
            Coverage = CoveragePolicy.CropToOverlap,
            MedianBandRows = bandRows,
        };
    }

    /// <summary>Independent median: gather, sort, middle (half-up for even counts).</summary>
    private static ushort[] Reference(IReadOnlyList<Frame> frames, AlignmentOffset[] shifts, PixelRect rect)
    {
        int channels = frames[0].Channels;
        int canvasWidth = frames[0].Width;
        var samples = new ushort[rect.Width * rect.Height * channels];
        var values = new List<ushort>(frames.Count);

        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    values.Clear();
                    int canvasX = rect.X + x;
                    int canvasY = rect.Y + y;
                    for (int f = 0; f < frames.Count; f++)
                    {
                        int sourceX = canvasX - (int)shifts[f].Dx;
                        int sourceY = canvasY - (int)shifts[f].Dy;
                        if (sourceX < 0 || sourceX >= frames[f].Width || sourceY < 0 || sourceY >= frames[f].Height)
                        {
                            continue;
                        }

                        values.Add(frames[f].Samples[((sourceY * frames[f].Width) + sourceX) * channels + c]);
                    }

                    values.Sort();
                    int n = values.Count;
                    samples[(((y * rect.Width) + x) * channels) + c] = (n & 1) == 1
                        ? values[n / 2]
                        : (ushort)(((int)values[(n / 2) - 1] + values[n / 2] + 1) / 2);
                }
            }
        }

        return samples;
    }

    private static IntegrationResult Run(List<Frame> frames, IntegrationOptions options) =>
        FinalImageIntegrator.Integrate(frames, Shifts(frames.Count), null, options);

    // ------------------------------------------------------------ the frame cap

    [Fact]
    public void SixtyFourFrames_RunInMemoryAndStayExact()
    {
        List<Frame> frames = MakeFrames(64);

        IntegrationResult result = Run(frames, Options());

        Assert.Equal(MedianRoute.InMemory, result.MedianRoute);
        Assert.Equal(Reference(frames, Shifts(frames.Count), result.OutputRect), result.Frame.Samples);
    }

    [Fact]
    public void SixtyFiveFrames_SpillAndStayExact()
    {
        List<Frame> frames = MakeFrames(65);

        IntegrationResult result = Run(frames, Options());

        Assert.Equal(MedianRoute.Tiled, result.MedianRoute);
        Assert.Equal(Reference(frames, Shifts(frames.Count), result.OutputRect), result.Frame.Samples);
    }

    [Theory]
    [InlineData(10, 10, MedianRoute.InMemory)] // exactly at the cap: in memory
    [InlineData(10, 9, MedianRoute.Tiled)]     // one above it: spilled
    public void FrameCapBoundaryIsInclusive(int frameCount, int cap, MedianRoute expected)
    {
        List<Frame> frames = MakeFrames(frameCount);
        IntegrationOptions options = Options();
        options.MaxRetainedMedianFrames = cap;

        IntegrationResult result = Run(frames, options);

        Assert.Equal(expected, result.MedianRoute);
        Assert.Equal(Reference(frames, Shifts(frameCount), result.OutputRect), result.Frame.Samples);
    }

    [Fact]
    public void BothRoutesProduceTheSamePixelsAndMetadata()
    {
        List<Frame> frames = MakeFrames(7);

        IntegrationOptions inMemory = Options();
        IntegrationOptions spilled = Options();
        spilled.MaxRetainedMedianFrames = 0;

        IntegrationResult memory = Run(frames, inMemory);
        IntegrationResult disk = Run(frames, spilled);

        Assert.Equal(MedianRoute.InMemory, memory.MedianRoute);
        Assert.Equal(MedianRoute.Tiled, disk.MedianRoute);
        Assert.Equal(disk.Frame.Samples, memory.Frame.Samples);
        Assert.Equal(disk.Coverage, memory.Coverage);
        Assert.Equal(disk.OutputRect, memory.OutputRect);
        Assert.Equal(disk.Frame.Bayer, memory.Frame.Bayer);
    }

    // ------------------------------------------------------------ the byte budget

    [Theory]
    [InlineData(0, MedianRoute.InMemory)]  // exactly the documented estimate: fits
    [InlineData(-1, MedianRoute.Tiled)]    // one byte short: spilled
    public void ByteBudgetBoundaryFollowsTheDocumentedEstimate(int slack, MedianRoute expected)
    {
        List<Frame> frames = MakeFrames(6);
        IntegrationOptions options = Options(bandRows: 4);

        // Documented rule: retained samples + one band table, upper-bounded with the canvas width.
        long estimated = (6L * Width * Height * 1 * sizeof(ushort))
                         + (4L * Width * 1 * 6 * sizeof(ushort));
        options.MedianInMemoryBudgetBytes = estimated + slack;

        IntegrationResult result = Run(frames, options);

        Assert.Equal(expected, result.MedianRoute);
        Assert.Equal(estimated, result.MedianEstimatedBytes);
        Assert.Equal(Reference(frames, Shifts(frames.Count), result.OutputRect), result.Frame.Samples);
    }

    [Fact]
    public void InMemoryBudgetAboveTheCapRoutesToTheTiledPath_WhichStillSucceeds()
    {
        // The frame cap binds even though the byte budget is generous, and the tiled route has scratch
        // to spare, so the run is routed, not refused.
        List<Frame> frames = MakeFrames(12);
        IntegrationOptions options = Options();
        options.MaxRetainedMedianFrames = 4;
        options.MedianInMemoryBudgetBytes = long.MaxValue;

        IntegrationResult result = Run(frames, options);

        Assert.Equal(MedianRoute.Tiled, result.MedianRoute);
        Assert.Equal(Reference(frames, Shifts(frames.Count), result.OutputRect), result.Frame.Samples);
    }

    [Fact]
    public void FortyFourFullHdRgbFrames_FitTheInMemoryBudget()
    {
        // The reported case: 44-frame Full-HD RGB needs 522 MiB of scratch, so the 512 MiB disk budget
        // refused it even though 44 < 64. The routing decision is what changed, so it is asserted
        // directly (allocating 44 Full-HD RGB frames would cost 522 MiB in the test host).
        var options = new IntegrationOptions { Method = IntegrationMethod.Median };

        MedianRoute route = IntegrationSupport.DecideRoute(
            44, 1920, 1080, 3, options, out long estimated);

        Assert.Equal(MedianRoute.InMemory, route);
        Assert.Equal(
            (44L * 1920 * 1080 * 3 * sizeof(ushort)) + (32L * 1920 * 3 * 44 * sizeof(ushort)),
            estimated);

        // And the disk route would still have refused it: that is the failure this change removes.
        long scratchForFileRoute = 44L * 1920 * 1080 * 3 * sizeof(ushort);
        Assert.True(scratchForFileRoute > options.MedianScratchBudgetBytes);
        Assert.True(estimated < options.EffectiveMedianInMemoryBudgetBytes);
    }

    [Theory]
    [InlineData(64, 1920, 1080, 3, MedianRoute.InMemory)] // 759 MiB + 23 MiB band table: inside 1 GiB
    [InlineData(65, 1920, 1080, 3, MedianRoute.Tiled)]    // the cap decides
    [InlineData(64, 4096, 4096, 3, MedianRoute.Tiled)]    // 6.4 GB: the budget decides, below the cap
    [InlineData(64, 1920, 1080, 1, MedianRoute.InMemory)]
    public void LargeSelectionsRouteByTheDocumentedLimits(int frameCount, int width, int height, int channels, MedianRoute expected)
    {
        MedianRoute route = IntegrationSupport.DecideRoute(
            frameCount, width, height, channels, new IntegrationOptions { Method = IntegrationMethod.Median }, out _);

        Assert.Equal(expected, route);
    }

    [Fact]
    public void InMemoryBudgetDerivesFromTheScratchBudget()
    {
        var options = new IntegrationOptions();
        Assert.Equal(1024L * 1024 * 1024, options.EffectiveMedianInMemoryBudgetBytes);
        Assert.Equal(2 * options.MedianScratchBudgetBytes, options.EffectiveMedianInMemoryBudgetBytes);

        options.MedianInMemoryBudgetBytes = 1234;
        Assert.Equal(1234, options.EffectiveMedianInMemoryBudgetBytes);

        options.MedianInMemoryBudgetBytes = 0;
        options.MedianScratchBudgetBytes = long.MaxValue;
        Assert.Equal(long.MaxValue, options.EffectiveMedianInMemoryBudgetBytes);
    }

    // ------------------------------------------------------------ early failure

    [Fact]
    public void ScratchBudgetBelowOneFrame_FailsBeforeAnyScratchFileExists()
    {
        // The cap forces the tiled route; the disk budget cannot hold a single frame, so the run must
        // fail immediately and say so, rather than creating a store and dying half-way.
        List<Frame> frames = MakeFrames(6);
        string directory = Path.Combine(Path.GetTempPath(), "opipp-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            IntegrationOptions options = Options();
            options.MaxRetainedMedianFrames = 0;
            options.MedianInMemoryBudgetBytes = 1; // in memory is not an option either
            options.MedianScratchBudgetBytes = (Width * Height * sizeof(ushort)) - 1;

            bool observed = false;
            var error = Assert.Throws<InvalidOperationException>(() => TiledMedianIntegrator.Integrate(
                frames,
                Shifts(frames.Count),
                options,
                CancellationToken.None,
                directory,
                _ => observed = true));

            Assert.Contains("even one band", error.Message);
            Assert.Contains("MedianScratchBudgetBytes", error.Message);
            Assert.False(observed, "the store was created even though the budget could not hold one frame");
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BandTableThatCannotFitTheBudget_IsRefusedBeforeReadingOrSpilling()
    {
        List<Frame> frames = MakeFrames(5);
        IntegrationOptions options = Options(bandRows: Height);

        // 8 rows x 8 columns x 1 channel x 5 frames x 2 bytes = 640 bytes; the budget is below that, so
        // not even one band can be resident.
        options.MedianInMemoryBudgetBytes = 639;
        options.MaxRetainedMedianFrames = 0;

        var error = Assert.Throws<InvalidOperationException>(
            () => TiledMedianIntegrator.Integrate(frames, Shifts(frames.Count), options));

        Assert.Contains("median band", error.Message);
        Assert.Contains(nameof(IntegrationOptions.MedianBandRows), error.Message);
    }

    // ------------------------------------------------------------ direct in-memory entry point

    [Fact]
    public void DirectInMemoryCall_EnforcesTheFrameCap()
    {
        List<Frame> frames = MakeFrames(65);

        var error = Assert.Throws<InvalidOperationException>(
            () => InMemoryMedianIntegrator.Integrate(frames, Shifts(frames.Count), Options()));

        Assert.Contains(nameof(IntegrationOptions.MaxRetainedMedianFrames), error.Message);
    }

    [Fact]
    public void DirectInMemoryCall_EnforcesTheByteBudget()
    {
        List<Frame> frames = MakeFrames(4);
        IntegrationOptions options = Options();
        options.MedianInMemoryBudgetBytes = 100;

        var error = Assert.Throws<InvalidOperationException>(
            () => InMemoryMedianIntegrator.Integrate(frames, Shifts(frames.Count), options));

        Assert.Contains("in-memory median needs", error.Message);
        Assert.Contains(nameof(IntegrationOptions.EffectiveMedianInMemoryBudgetBytes), error.Message);
    }

    [Fact]
    public void MeanAndSumDoNotReportAMedianRoute()
    {
        foreach (IntegrationMethod method in new[] { IntegrationMethod.Mean, IntegrationMethod.Sum })
        {
            IntegrationResult result = FinalImageIntegrator.Integrate(
                MakeFrames(3), Shifts(3), null, new IntegrationOptions { Method = method });

            Assert.Equal(MedianRoute.NotApplicable, result.MedianRoute);
            Assert.Equal(0, result.MedianEstimatedBytes);
        }
    }

    [Fact]
    public void MedianWeightsMustBeAbsentOrUniform()
    {
        List<Frame> frames = MakeFrames(3);
        AlignmentOffset[] shifts = Shifts(3);

        IntegrationResult uniform = FinalImageIntegrator.Integrate(
            frames, shifts, [1.0, 1.0, 1.0], Options());
        Assert.Equal(MedianRoute.InMemory, uniform.MedianRoute);

        Assert.Throws<NotSupportedException>(() => FinalImageIntegrator.Integrate(
            frames, shifts, [1.0, 2.0, 1.0], Options()));
    }
}
