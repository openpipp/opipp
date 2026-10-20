namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Processing;
using Opipp.Core.Stacking;
using Xunit;

/// <summary>
/// Contract tests for <see cref="TiledMedianIntegrator"/>: the disk-backed band median must be exact
/// (equal to an independent per-pixel reference, not merely close), must match the stacker's even
/// rule, must respect the declared scratch budget, must spill even for tiny selections, and must
/// leave nothing behind when cancelled.
/// </summary>
/// <remarks>
/// The reference below is deliberately naive and independent: it gathers each pixel's contributions
/// into a list, sorts it and picks the middle. It shares no code with the integrator, so an indexing,
/// banding or spill bug shows up as a mismatch rather than as a self-consistent result.
/// </remarks>
public sealed class TiledMedianTests
{
    private const int Width = 32;
    private const int Height = 32;

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opipp-tiled-median-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Deterministic per-sample noise in the full sample range.</summary>
    private static ushort Sample(int sample, int frame, int bitDepth)
    {
        uint h = unchecked((uint)sample * 2654435761u) ^ unchecked((uint)(frame + 1) * 2246822519u);
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return bitDepth == 8 ? (ushort)(h % 256) : (ushort)(h % 65536);
    }

    private static Frame MakeFrame(int width, int height, int channels, int bitDepth, int seed, string? sourceName = null)
    {
        var frame = new Frame(width, height, channels, bitDepth) { SourceName = sourceName };
        for (int p = 0; p < frame.Samples.Length; p++)
            frame.Samples[p] = Sample(p, seed, bitDepth);
        return frame;
    }

    private static List<Frame> MakeFrames(int count, int width, int height, int channels = 1, int bitDepth = 16,
        bool named = false)
    {
        var frames = new List<Frame>(count);
        for (int i = 0; i < count; i++)
            frames.Add(MakeFrame(width, height, channels, bitDepth, i, named ? $"src-{i}.ser" : null));
        return frames;
    }

    /// <summary>Small, varied integer shifts so every test exercises real registration.</summary>
    private static (int X, int Y)[] SmallShifts(int count)
    {
        var shifts = new (int X, int Y)[count];
        for (int i = 0; i < count; i++) shifts[i] = ((i % 3) - 1, ((i / 3) % 3) - 1);
        return shifts;
    }

    private static AlignmentOffset[] AsOffsets((int X, int Y)[] shifts) =>
        shifts.Select(s => new AlignmentOffset(s.X, s.Y)).ToArray();

    private static IntegrationOptions CropOptions(int bandRows = 32) => new()
    {
        Method = IntegrationMethod.Median,
        Coverage = CoveragePolicy.CropToOverlap,
        MedianBandRows = bandRows,
    };

    private static IntegrationOptions CanvasOptions(int bandRows = 32) => new()
    {
        Method = IntegrationMethod.Median,
        Coverage = CoveragePolicy.KeepCanvas,
        MedianBandRows = bandRows,
    };

    /// <summary>
    /// Independent reference: gather, sort, take the middle (half-up for even counts), zero where no
    /// frame supplies the pixel.
    /// </summary>
    private static (ushort[] Samples, int[] Coverage) Reference(
        IReadOnlyList<Frame> frames, (int X, int Y)[] shifts, PixelRect rect)
    {
        int channels = frames[0].Channels;
        int canvasWidth = frames[0].Width;
        int canvasHeight = frames[0].Height;
        var samples = new ushort[rect.Width * rect.Height * channels];
        // Coverage is per pixel of the first frame's canvas, zero outside the output rect.
        var coverage = new int[canvasWidth * canvasHeight];
        var values = new List<ushort>(frames.Count);

        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                int destinationPixel = y * rect.Width + x;
                int canvasPixel = (rect.Y + y) * canvasWidth + rect.X + x;

                for (int c = 0; c < channels; c++)
                {
                    values.Clear();
                    int canvasX = rect.X + x;
                    int canvasY = rect.Y + y;
                    for (int f = 0; f < frames.Count; f++)
                    {
                        Frame frame = frames[f];
                        int sourceX = canvasX - shifts[f].X;
                        int sourceY = canvasY - shifts[f].Y;
                        if (sourceX < 0 || sourceX >= frame.Width || sourceY < 0 || sourceY >= frame.Height) continue;
                        values.Add(frame.Samples[(sourceY * frame.Width + sourceX) * channels + c]);
                    }

                    values.Sort();
                    int n = values.Count;
                    samples[destinationPixel * channels + c] = n == 0
                        ? (ushort)0
                        : (n & 1) == 1
                            ? values[n / 2]
                            : (ushort)(((int)values[n / 2 - 1] + values[n / 2] + 1) / 2);

                    if (c == 0) coverage[canvasPixel] = n;
                }
            }
        }

        return (samples, coverage);
    }

    private static void AssertMatchesReference(
        IReadOnlyList<Frame> frames, (int X, int Y)[] shifts, IntegrationOptions options)
    {
        IntegrationResult result = TiledMedianIntegrator.Integrate(frames, AsOffsets(shifts), options);
        (ushort[] expectedSamples, int[] expectedCoverage) = Reference(frames, shifts, result.OutputRect);

        Assert.Equal(IntegrationMethod.Median, result.Method);
        Assert.Null(result.PrecisionSamples);
        Assert.Equal(frames[0].Width * frames[0].Height, result.Coverage.Length);
        Assert.Equal(result.OutputRect.Width, result.Frame.Width);
        Assert.Equal(result.OutputRect.Height, result.Frame.Height);
        Assert.Equal(expectedSamples, result.Frame.Samples);
        Assert.Equal(expectedCoverage, result.Coverage);
    }

    [Theory]
    [InlineData(3, 32)]
    [InlineData(64, 5)]
    [InlineData(65, 7)]
    [InlineData(1000, 4)]
    public void Median_MatchesIndependentReference(int frameCount, int bandRows)
    {
        List<Frame> frames = MakeFrames(frameCount, Width, Height);
        (int X, int Y)[] shifts = SmallShifts(frameCount);

        AssertMatchesReference(frames, shifts, CropOptions(bandRows));
    }

    [Fact]
    public void Median_MatchesIndependentReference_ForColourFrames()
    {
        List<Frame> frames = MakeFrames(7, 16, 8, channels: 3, bitDepth: 8, named: true);
        (int X, int Y)[] shifts = [(0, 0), (1, 0), (-1, 1), (0, -1), (2, 2), (-2, 0), (1, -1)];

        AssertMatchesReference(frames, shifts, CropOptions(bandRows: 3));
    }

    [Fact]
    public void EvenCount_UsesTheStackersHalfUpRule()
    {
        // Ordered so the two middle samples are 10+p and 11+p: half-up medians to 11+p, truncation
        // would give 10+p. This is the rule FrameStacker documents and implements.
        var frames = new List<Frame>();
        foreach (int offset in new[] { 5, 10, 11, 30 })
        {
            var frame = new Frame(8, 4, 1, 16);
            for (int p = 0; p < frame.Samples.Length; p++) frame.Samples[p] = (ushort)(offset + (p % 20));
            frames.Add(frame);
        }

        IntegrationResult result = TiledMedianIntegrator.Integrate(frames, AsOffsets([(0, 0), (0, 0), (0, 0), (0, 0)]), CropOptions());

        var stacker = new FrameStacker(8, 4, 1, 16, StackMethod.Median);
        foreach (Frame frame in frames) stacker.Add(frame);
        Frame stacked = stacker.Result();

        var expected = new ushort[32];
        for (int p = 0; p < expected.Length; p++) expected[p] = (ushort)(11 + (p % 20));

        Assert.Equal(expected, result.Frame.Samples);
        Assert.Equal(stacked.Samples, result.Frame.Samples);
        Assert.Equal(new PixelRect(0, 0, 8, 4), result.OutputRect);
    }

    [Fact]
    public void TwoFrames_MedianRoundsHalfUp_LikeTheStacker()
    {
        // Contract example: [0, 1] means 0 but medians to 1.
        var low = new Frame(4, 2, 1, 16);
        var high = new Frame(4, 2, 1, 16);
        Array.Fill(high.Samples, (ushort)1);

        IntegrationResult result = TiledMedianIntegrator.Integrate(
            [low, high], AsOffsets([(0, 0), (0, 0)]), CropOptions());

        Assert.All(result.Frame.Samples, v => Assert.Equal(1, v));
    }

    [Fact]
    public void SmallScratchBudget_StillExact_AndProvesEveryFrameWasSpilled()
    {
        List<Frame> frames = MakeFrames(8, 16, 8);
        (int X, int Y)[] shifts = SmallShifts(frames.Count);
        string dir = TempDir();
        try
        {
            long bytesPerFrame = 16L * 8 * 2;
            var options = CropOptions();
            options.MedianScratchBudgetBytes = bytesPerFrame * frames.Count; // exactly enough, no slack

            FrameStore? observed = null;
            IntegrationResult result = TiledMedianIntegrator.Integrate(
                frames, AsOffsets(shifts), options, CancellationToken.None, dir, store => observed = store);

            Assert.NotNull(observed);
            Assert.Equal(frames.Count, observed!.Count);
            Assert.Equal(bytesPerFrame * frames.Count, observed.StoredBytes);
            Assert.StartsWith(Path.Combine(dir, "opipp-median-"), observed.Path);
            // The store opens with FileOptions.DeleteOnClose, so Windows hides the name from
            // File.Exists while the handle is open; Count/StoredBytes are the live proof that every
            // frame went through the scratch file rather than staying in memory.
            string spilledPath = observed.Path;

            (ushort[] expectedSamples, int[] expectedCoverage) = Reference(frames, shifts, result.OutputRect);
            Assert.Equal(expectedSamples, result.Frame.Samples);
            Assert.Equal(expectedCoverage, result.Coverage);

            // The scratch file is gone once the call returns.
            Assert.False(File.Exists(spilledPath));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ScratchBudgetTooSmall_FailsBeforeSpillingAnything()
    {
        List<Frame> frames = MakeFrames(8, 16, 8);
        string dir = TempDir();
        try
        {
            long bytesPerFrame = 16L * 8 * 2;
            var options = CropOptions();
            options.MedianScratchBudgetBytes = bytesPerFrame * frames.Count - 1;

            var error = Assert.Throws<InvalidOperationException>(() => TiledMedianIntegrator.Integrate(
                frames, AsOffsets(SmallShifts(frames.Count)), options, CancellationToken.None, dir, null));

            Assert.Contains("MedianScratchBudgetBytes", error.Message);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Cancellation_Throws_AndLeavesNoScratchOrResult()
    {
        List<Frame> frames = MakeFrames(12, 24, 16, channels: 1);
        string dir = TempDir();
        try
        {
            using var cts = new CancellationTokenSource();
            FrameStore? observed = null;

            // Cancel once the frames are on disk: the next band check is where the run stops, so this
            // covers the cancellation point after spilling and inside the band pass.
            Assert.Throws<OperationCanceledException>(() => TiledMedianIntegrator.Integrate(
                frames,
                AsOffsets(SmallShifts(frames.Count)),
                CropOptions(bandRows: 4),
                cts.Token,
                dir,
                store =>
                {
                    observed = store;
                    cts.Cancel();
                }));

            Assert.NotNull(observed);
            Assert.Equal(frames.Count, observed!.Count);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AlreadyCancelledToken_ThrowsBeforeCreatingScratch()
    {
        string dir = TempDir();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => TiledMedianIntegrator.Integrate(
                MakeFrames(3, 8, 8), AsOffsets(SmallShifts(3)), CropOptions(), cts.Token, dir, null));

            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeepCanvas_ReportsCoveragePerPixel_AndFillsUncoveredPixelsWithZero()
    {
        List<Frame> frames = MakeFrames(3, 16, 16);
        (int X, int Y)[] shifts = [(1, 1), (2, 0), (0, 2)];

        IntegrationResult result = TiledMedianIntegrator.Integrate(frames, AsOffsets(shifts), CanvasOptions());

        Assert.Equal(new PixelRect(0, 0, 16, 16), result.OutputRect);
        Assert.Equal(16 * 16, result.Coverage.Length);

        // Top-left corner: no frame can supply it (frame 1 needs x>=2, frame 2 needs y>=2).
        Assert.Equal(0, result.Coverage[0]);
        Assert.Equal(0, result.Frame.Samples[0]);

        // Bottom-right corner: every frame supplies it.
        Assert.Equal(3, result.Coverage[^1]);

        (ushort[] expectedSamples, int[] expectedCoverage) = Reference(frames, shifts, result.OutputRect);
        Assert.Equal(expectedSamples, result.Frame.Samples);
        Assert.Equal(expectedCoverage, result.Coverage);
        Assert.Contains(result.Coverage, c => c == 0);
        Assert.All(result.Coverage, c => Assert.InRange(c, 0, frames.Count));
    }

    [Fact]
    public void CropToOverlap_IntersectsTheShiftedRects()
    {
        List<Frame> frames = MakeFrames(3, 16, 16);
        // Valid x: [0,16), [2,18) -> [2,16), [0,13) -> intersection [2,13).
        // Valid y: [0,16), [0,15), [1,17) -> intersection [1,15).
        (int X, int Y)[] shifts = [(0, 0), (2, -1), (-3, 1)];

        IntegrationResult result = TiledMedianIntegrator.Integrate(frames, AsOffsets(shifts), CropOptions());

        Assert.Equal(new PixelRect(2, 1, 11, 14), result.OutputRect);
        Assert.Equal(11, result.Frame.Width);
        Assert.Equal(14, result.Frame.Height);
        Assert.Equal(11 * 14, result.Frame.Samples.Length);

        // Coverage spans the first canvas; entries outside the cropped rect stay zero.
        Assert.Equal(16 * 16, result.Coverage.Length);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                bool inside = x >= 2 && x < 13 && y >= 1 && y < 15;
                Assert.Equal(inside ? 3 : 0, result.Coverage[y * 16 + x]);
            }
        }

        (ushort[] expectedSamples, int[] expectedCoverage) = Reference(frames, shifts, result.OutputRect);
        Assert.Equal(expectedSamples, result.Frame.Samples);
        Assert.Equal(expectedCoverage, result.Coverage);
    }

    [Fact]
    public void Contributors_ListEverySourceInDeliveryOrder_WithWeightAndAppliedShift()
    {
        List<Frame> frames = MakeFrames(4, 8, 8, named: true);
        (int X, int Y)[] shifts = [(0, 0), (1, -2), (-1, 3), (2, 1)];

        IntegrationResult result = TiledMedianIntegrator.Integrate(frames, AsOffsets(shifts), CropOptions());

        Assert.Equal(frames.Count, result.Contributors.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            IntegrationContributor contributor = result.Contributors[i];
            Assert.Equal($"src-{i}.ser", contributor.SourceName);
            Assert.Equal(1.0, contributor.Weight);
            Assert.Equal(new AlignmentOffset(shifts[i].X, shifts[i].Y), contributor.Shift);
        }
    }

    [Fact]
    public void IntegerShiftMovesContentExactly()
    {
        // A ramp makes the direction unambiguous: with KeepCanvas the vacated columns stay at the fill
        // value and every other sample is the ramp read two columns to the left, so a positive Dx
        // moves content right (the same direction as FrameAligner.ShiftWithBackground).
        var frame = new Frame(8, 4, 1, 16);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
                frame.Samples[y * 8 + x] = (ushort)(x * 100 + y);

        IntegrationResult result = TiledMedianIntegrator.Integrate(
            [frame], AsOffsets([(2, 0)]), CanvasOptions());

        Assert.Equal(new PixelRect(0, 0, 8, 4), result.OutputRect);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                ushort expected = x < 2 ? (ushort)0 : (ushort)((x - 2) * 100 + y);
                Assert.Equal(expected, result.Frame.Samples[y * 8 + x]);
                Assert.Equal(x < 2 ? 0 : 1, result.Coverage[y * 8 + x]);
            }
        }
    }

    [Fact]
    public void NonMedianMethod_IsRejected()
    {
        List<Frame> frames = MakeFrames(2, 8, 8);
        var options = new IntegrationOptions { Method = IntegrationMethod.Mean };

        var error = Assert.Throws<ArgumentException>(() => TiledMedianIntegrator.Integrate(
            frames, AsOffsets([(0, 0), (0, 0)]), options));

        Assert.Contains("Median", error.Message);
    }

    [Fact]
    public void MismatchedGeometryOrCfa_IsRejectedBeforeSpilling()
    {
        List<Frame> frames = MakeFrames(2, 8, 8);
        frames[1] = new Frame(8, 7, 1, 16);

        Assert.Throws<ArgumentException>(() => TiledMedianIntegrator.Integrate(
            frames, AsOffsets([(0, 0), (0, 0)]), CropOptions()));

        var mono = new Frame(4, 4, 1, 16) { Bayer = BayerPattern.RGGB };
        var grbg = new Frame(4, 4, 1, 16) { Bayer = BayerPattern.GRBG };
        Assert.Throws<BayerPhaseException>(() => TiledMedianIntegrator.Integrate(
            [mono, grbg], AsOffsets([(0, 0), (0, 0)]), CropOptions()));
    }

    [Fact]
    public void ShiftCountMustMatchFrameCount()
    {
        List<Frame> frames = MakeFrames(3, 8, 8);

        Assert.Throws<ArgumentException>(() => TiledMedianIntegrator.Integrate(
            frames, AsOffsets([(0, 0)]), CropOptions()));
    }

    // ---------------------------------------------------------------- sub-pixel registration

    private static Frame Solid(int width, int height, ushort value, int channels = 1, int bitDepth = 8)
    {
        var frame = new Frame(width, height, channels, bitDepth);
        Array.Fill(frame.Samples, value);
        return frame;
    }

    private static IntegrationResult MeanOfSingleFrame(Frame frame, AlignmentOffset shift, CoveragePolicy coverage) =>
        FrameIntegrator.Integrate(
            [frame],
            [shift],
            null,
            new IntegrationOptions { Method = IntegrationMethod.Mean, Coverage = coverage, Subpixel = true });

    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.37, -0.62)]
    [InlineData(-0.75, 1.5)]
    [InlineData(2.25, -3.5)]
    public void Subpixel_MedianOfIdenticalFramesMatchesTheSingleFrameMean(double dx, double dy)
    {
        // The shared kernel must place a fractional shift identically for both methods. With N
        // identical frames the median of N identical quantised samples is that sample, so the median
        // frame must equal the mean of one frame sample for sample, including the output rectangle.
        Frame frame = MakeFrame(Width, Height, 1, 16, seed: 0);
        AlignmentOffset shift = new(dx, dy);

        IntegrationResult median = TiledMedianIntegrator.Integrate(
            [frame, frame.Clone(), frame.Clone()],
            [shift, shift, shift],
            new IntegrationOptions
            {
                Method = IntegrationMethod.Median,
                Coverage = CoveragePolicy.CropToOverlap,
                Subpixel = true,
                MedianBandRows = 5,
            });

        IntegrationResult mean = MeanOfSingleFrame(frame, shift, CoveragePolicy.CropToOverlap);

        Assert.Equal(mean.OutputRect, median.OutputRect);
        Assert.Equal(mean.Frame.Width, median.Frame.Width);
        Assert.Equal(mean.Frame.Height, median.Frame.Height);
        Assert.Equal(mean.Frame.Samples, median.Frame.Samples);
        Assert.Equal(shift, median.Contributors[0].Shift);

        for (int y = 0; y < median.Frame.Height; y++)
        {
            for (int x = 0; x < median.Frame.Width; x++)
            {
                Assert.Equal(3, median.Coverage[((median.OutputRect.Y + y) * Width) + median.OutputRect.X + x]);
            }
        }
    }

    [Theory]
    [InlineData(0.37, -0.62)]
    [InlineData(-0.25, 0.5)]
    public void Subpixel_ConstantFrameStaysConstantOverTheOverlap(double dx, double dy)
    {
        Frame frame = Solid(12, 10, 100);
        AlignmentOffset shift = new(dx, dy);

        IntegrationResult median = TiledMedianIntegrator.Integrate(
            [frame, frame.Clone(), frame.Clone()],
            [shift, shift, shift],
            new IntegrationOptions
            {
                Method = IntegrationMethod.Median,
                Coverage = CoveragePolicy.CropToOverlap,
                Subpixel = true,
                MedianBandRows = 4,
            });

        // Bilinear weights sum to one, so a constant surface survives resampling and quantisation.
        Assert.All(median.Frame.Samples, sample => Assert.Equal(100, sample));
        Assert.Equal(
            MeanOfSingleFrame(frame, shift, CoveragePolicy.CropToOverlap).OutputRect,
            median.OutputRect);
    }

    [Fact]
    public void Subpixel_HalfPixelStepEdgeInterpolatesTheSameUnderMedianAndMean()
    {
        var frame = new Frame(4, 1, 1, 8, new ushort[] { 0, 0, 100, 100 });
        AlignmentOffset shift = new(0.5, 0);

        IntegrationResult median = TiledMedianIntegrator.Integrate(
            [frame, frame.Clone()],
            [shift, shift],
            new IntegrationOptions
            {
                Method = IntegrationMethod.Median,
                Coverage = CoveragePolicy.CropToOverlap,
                Subpixel = true,
            });

        IntegrationResult mean = MeanOfSingleFrame(frame, shift, CoveragePolicy.CropToOverlap);

        Assert.Equal(new PixelRect(1, 0, 3, 1), median.OutputRect);
        Assert.Equal(new ushort[] { 0, 50, 100 }, median.Frame.Samples);
        Assert.Equal(mean.Frame.Samples, median.Frame.Samples);
    }

    [Fact]
    public void Subpixel_KeepCanvasMatchesMeanIncludingTheUncoveredBorder()
    {
        Frame frame = MakeFrame(10, 8, 1, 8, seed: 2);
        AlignmentOffset shift = new(0.5, 0.25);

        IntegrationResult median = TiledMedianIntegrator.Integrate(
            [frame, frame.Clone()],
            [shift, shift],
            new IntegrationOptions
            {
                Method = IntegrationMethod.Median,
                Coverage = CoveragePolicy.KeepCanvas,
                Subpixel = true,
                MedianBandRows = 3,
            });

        IntegrationResult mean = MeanOfSingleFrame(frame, shift, CoveragePolicy.KeepCanvas);

        Assert.Equal(new PixelRect(0, 0, 10, 8), median.OutputRect);
        Assert.Equal(mean.Frame.Samples, median.Frame.Samples);

        // Column 0 and row 0 need a tap outside the source, so no frame supplies them; both methods
        // write the fill sentinel instead of inventing an edge sample.
        for (int x = 0; x < 10; x++) Assert.Equal(0, median.Coverage[x]);
        for (int y = 0; y < 8; y++) Assert.Equal(0, median.Coverage[y * 10]);
    }

    [Theory]
    [InlineData(0.4, 0.4)]
    [InlineData(-0.6, 0.5)]
    [InlineData(2.49, -2.51)]
    public void SubpixelOff_RoundsFractionalOffsetsExactlyLikeMean(double dx, double dy)
    {
        Frame frame = MakeFrame(16, 8, 1, 16, seed: 3);
        AlignmentOffset shift = new(dx, dy);

        IntegrationResult median = TiledMedianIntegrator.Integrate(
            [frame],
            [shift],
            new IntegrationOptions
            {
                Method = IntegrationMethod.Median,
                Coverage = CoveragePolicy.KeepCanvas,
                Subpixel = false,
                MedianBandRows = 3,
            });

        IntegrationResult mean = FrameIntegrator.Integrate(
            [frame],
            [shift],
            null,
            new IntegrationOptions { Method = IntegrationMethod.Mean, Coverage = CoveragePolicy.KeepCanvas, Subpixel = false });

        Assert.Equal(mean.OutputRect, median.OutputRect);
        Assert.Equal(mean.Frame.Samples, median.Frame.Samples);
        Assert.Equal(
            new AlignmentOffset(
                Math.Round(dx, MidpointRounding.AwayFromZero),
                Math.Round(dy, MidpointRounding.AwayFromZero)),
            median.Contributors[0].Shift);
    }
}
