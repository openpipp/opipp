namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Stacking;
using Xunit;

/// <summary>
/// Pipeline-level proof that median stacking works on frames the retention layer has spilled to disk.
/// The stacker itself retains copies, but a pipeline run can hand it frames re-read from
/// <c>FrameStore</c>; those must carry the same samples as the in-memory run, or a spilled capture
/// would silently stack different pixels. The run is forced to spill every accepted frame with a
/// one-byte budget, and the expectations are the independently computed median of the submitted
/// values, not the stacker's own output.
/// </summary>
public sealed class MedianPipelineRetentionTests
{
    private const int Width = 8;
    private const int Height = 4;
    private const int FrameCount = 5;

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opipp-median-spill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Deterministic 8-bit pattern with a different value per pixel and frame.</summary>
    private static ushort Value(int pixel, int frame) => (ushort)(((pixel * 37) + (frame * 23)) % 251);

    private static string WriteCapture(string dir)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, Width, Height, 1, 8);
        for (int f = 0; f < FrameCount; f++)
        {
            var frame = new Frame(Width, Height, 1, 8);
            for (int p = 0; p < Width * Height; p++) frame.Samples[p] = Value(p, f);
            writer.Write(frame);
        }
        return path;
    }

    /// <summary>The median of the five submitted values for one pixel, computed independently.</summary>
    private static ushort MedianOf(int pixel)
    {
        var values = new ushort[FrameCount];
        for (int f = 0; f < FrameCount; f++) values[f] = Value(pixel, f);
        Array.Sort(values);
        return values[FrameCount / 2];
    }

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {path}");
    }

    [Fact]
    public void MedianStack_WithEveryFrameSpilled_MatchesTheInMemoryRunAndTheExpectedMedian()
    {
        string dir = TempDir();
        string spillDirectory = Path.Combine(dir, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            string input = WriteCapture(dir);
            string memoryOut = Path.Combine(dir, "memory");
            string spilledOut = Path.Combine(dir, "spilled");

            var inMemoryRun = new Processor(new PipelineSettings
            {
                OutputDirectory = memoryOut,
                OutputExtension = ".tif",
                Stack = StackMethod.Median,
            }).Run([input]);

            bool spilledDuringRun = false;
            var observer = new RecordingProgress(_ =>
            {
                if (Directory.GetFiles(spillDirectory, "opipp-spill-*.bin").Length > 0)
                    spilledDuringRun = true;
            });

            var spilledRun = new Processor(new PipelineSettings
            {
                OutputDirectory = spilledOut,
                OutputExtension = ".tif",
                Stack = StackMethod.Median,
                RetentionBudgetBytes = 1, // every accepted frame spills to the temp store
            }, observer)
            {
                SpillDirectory = spillDirectory,
            }.Run([input]);

            Assert.True(spilledDuringRun, "the budget-1 run never created a spill file");
            Assert.Single(inMemoryRun.OutputFiles);
            Assert.Single(spilledRun.OutputFiles);

            var inMemory = ReadTiff(inMemoryRun.OutputFiles[0]);
            var fromDisk = ReadTiff(spilledRun.OutputFiles[0]);

            Assert.Equal(Width, fromDisk.Width);
            Assert.Equal(Height, fromDisk.Height);
            Assert.Equal(1, fromDisk.Channels);
            Assert.Equal(8, fromDisk.BitDepth);

            var expected = new ushort[Width * Height];
            for (int p = 0; p < expected.Length; p++) expected[p] = MedianOf(p);

            // Absolute values: a spilled frame that reads back shifted, truncated or reordered fails here.
            Assert.Equal(expected, fromDisk.Samples);

            // And the retention contract: disk and memory produce the same master.
            Assert.Equal(inMemory.Samples, fromDisk.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class RecordingProgress(Action<PipelineProgress> onReport) : IProgress<PipelineProgress>
    {
        public void Report(PipelineProgress value) => onReport(value);
    }
}
