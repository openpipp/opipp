using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Xunit;

namespace Opipp.Core.Tests;

/// <summary>
/// Retention tests: what a run keeps on disk must behave exactly like what it keeps in memory, and
/// the scratch store must not outlive the run that opened it — the failed and cancelled runs
/// included. Both tests point the run at a spill directory they own, so the assertions do not race
/// other runs in the shared process temp directory.
/// </summary>
public sealed class RetentionTests
{
    private const string Observer = "Observer Name";
    private const string Telescope = "Telescope Name";
    private const string Instrument = "Instrument Name";

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opipp-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Writes a SER capture whose header carries capture metadata, the way a real recording does.
    /// The pipeline copies those fields onto a SER output by reading the input file named by the
    /// output template frame, which is exactly what a spill used to lose.
    /// </summary>
    private static string WriteCapture(string dir, int width, int height, int frames)
    {
        string path = Path.Combine(dir, "capture.ser");
        SerHeader header = SerHeader.Create(width, height, 1, 8, frames);
        header.Observer = Observer;
        header.Telescope = Telescope;
        header.Instrument = Instrument;

        using var writer = new SerWriter(path, header);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, 8);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    frame.Samples[(y * width) + x] = (ushort)(((x * 3) + (y * 5) + f) % 200);
                }
            }
            writer.Write(frame);
        }
        return path;
    }

    private static string[] SpillFiles(string spillDirectory) =>
        Directory.GetFiles(spillDirectory, "opipp-spill-*.bin");

    [Fact]
    public void SpilledSerOutput_MatchesTheInMemoryRunByteForByte()
    {
        string dir = TempDir();
        string spillDirectory = Path.Combine(dir, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            string input = WriteCapture(dir, 16, 16, 5);
            string generous = Path.Combine(dir, "generous");
            string spilled = Path.Combine(dir, "spilled");

            bool spilledDuringRun = false;
            var observer = new RecordingProgress(_ =>
            {
                if (SpillFiles(spillDirectory).Length > 0) spilledDuringRun = true;
            });

            var inMemory = new Processor(new PipelineSettings
            {
                OutputDirectory = generous,
                OutputExtension = ".ser",
            }).Run([input]);

            var onDisk = new Processor(new PipelineSettings
            {
                OutputDirectory = spilled,
                OutputExtension = ".ser",
                RetentionBudgetBytes = 1, // every accepted frame spills to the temp store
            }, observer)
            {
                SpillDirectory = spillDirectory,
            }.Run([input]);

            Assert.True(spilledDuringRun, "the budget-1 run never created a spill file");
            Assert.Single(inMemory.OutputFiles);
            Assert.Single(onDisk.OutputFiles);

            // The whole file is compared, header included: a spilled capture may not lose the
            // Observer/Telescope/Instrument fields the engine carries from the input SER.
            Assert.Equal(File.ReadAllBytes(inMemory.OutputFiles[0]), File.ReadAllBytes(onDisk.OutputFiles[0]));

            using var reader = new SerReader(onDisk.OutputFiles[0]);
            Assert.Equal(Observer, reader.Header.Observer);
            Assert.Equal(Telescope, reader.Header.Telescope);
            Assert.Equal(Instrument, reader.Header.Instrument);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CancelledRun_ReleasesTheSpillFileImmediately()
    {
        string dir = TempDir();
        string spillDirectory = Path.Combine(dir, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            string input = WriteCapture(dir, 16, 16, 8);
            using var cancellation = new CancellationTokenSource();
            bool spilled = false;

            var observer = new RecordingProgress(progress =>
            {
                if (SpillFiles(spillDirectory).Length == 0) return;
                spilled = true;
                // Cancel from inside the read loop: the next iteration observes the token, so the
                // run unwinds by exception with a live spill store.
                if (progress.FrameIndex >= 3) cancellation.Cancel();
            });

            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".tif",
                RetentionBudgetBytes = 1,
            }, observer)
            {
                SpillDirectory = spillDirectory,
            };

            Assert.ThrowsAny<OperationCanceledException>(() => processor.Run([input], cancellation.Token));

            Assert.True(spilled, "the run never spilled, so disposal was not exercised");
            // No GC.Collect: the directory is empty because the run's finally block disposed the
            // store and deleted its DeleteOnClose scratch file, not because a finalizer ran later.
            Assert.Empty(SpillFiles(spillDirectory));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CancellingDuringContainerPreparation_StopsBeforeWritingTheContainer()
    {
        string dir = TempDir();
        string spillDirectory = Path.Combine(dir, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            string input = WriteCapture(dir, 16, 16, 6);
            string output = Path.Combine(dir, "out");
            using var cancellation = new CancellationTokenSource();
            int prepared = 0;

            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".ser",
                RetentionBudgetBytes = 1, // every queued frame has to be re-read from the store
            })
            {
                SpillDirectory = spillDirectory,
                // This seam fires only while the container pass re-reads the queue, after the input
                // stage is over, so no sleep or read-loop race is involved.
                ContainerFramePrepared = index =>
                {
                    prepared++;
                    if (index == 0) cancellation.Cancel();
                },
            };

            Assert.ThrowsAny<OperationCanceledException>(() => processor.Run([input], cancellation.Token));

            // The check sits inside the pass: one frame was prepared, then the token stopped it.
            // Without it the pass would have re-read and transformed all six before the writer's
            // own check noticed.
            Assert.Equal(1, prepared);
            Assert.Empty(Directory.GetFiles(output, "*.ser", SearchOption.AllDirectories));
            Assert.Empty(SpillFiles(spillDirectory));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FailingOutputPath_ReleasesTheSpillFileImmediately()
    {
        string dir = TempDir();
        string spillDirectory = Path.Combine(dir, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            string input = WriteCapture(dir, 16, 16, 8);
            string output = Path.Combine(dir, "out");
            Directory.CreateDirectory(output);
            // The PIPP layout creates <out>/<capture>/ only after every frame has been accepted and
            // spilled, so a file squatting on that path makes the writer fail mid-run.
            File.WriteAllText(Path.Combine(output, "capture"), "not a directory");

            bool spilled = false;
            var observer = new RecordingProgress(_ =>
            {
                if (SpillFiles(spillDirectory).Length > 0) spilled = true;
            });

            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                RetentionBudgetBytes = 1,
            }, observer)
            {
                SpillDirectory = spillDirectory,
            };

            Assert.Throws<IOException>(() => processor.Run([input]));

            Assert.True(spilled, "the run never spilled, so disposal was not exercised");
            Assert.Empty(SpillFiles(spillDirectory));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Synchronous <see cref="IProgress{T}"/>: <see cref="Progress{T}"/> posts to a synchronization
    /// context and would let the assertions run before the callback that cancels the run.
    /// </summary>
    private sealed class RecordingProgress(Action<PipelineProgress> onReport) : IProgress<PipelineProgress>
    {
        public void Report(PipelineProgress value) => onReport(value);
    }
}
