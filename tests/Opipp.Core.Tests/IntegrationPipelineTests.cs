using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Pipeline;

namespace Opipp.Core.Tests;

/// <summary>
/// Pipeline-level tests for final-image integration: the selection is streamed from the retention
/// store instead of materialised, each run stages inside its own directory so it can never touch an
/// unrelated file, a killed run's orphaned directory is swept by the next run, and the requested
/// area of interest is applied after registration so it keeps its full geometry.
/// </summary>
/// <remarks>
/// <para>
/// Every test drives the real <see cref="Processor"/> over a synthetic SER capture, so the frames take
/// the same retention path a long capture does. The streaming tests point the run at a spill
/// directory they own and set a one-byte retention budget, which forces every accepted frame into the
/// store; that is the case where an integration that materialises the selection shows up as a large
/// live-memory jump.
/// </para>
/// <para>
/// The memory assertion needs a quiet heap, so this class runs in a collection whose tests do not run
/// in parallel with other collections: a concurrent test's allocations must not be attributed to the
/// integration.
/// </para>
/// </remarks>
[Collection("integration-memory")]
public sealed class IntegrationPipelineTests
{
    // The tracking fixture: a 160x120 capture whose asymmetric 24x24 feature moves between frames.
    // A whole-pixel translation is measured as a whole-pixel offset, so the registered frames can be
    // compared sample for sample.
    private const int Width = 160;
    private const int Height = 120;
    private const int FeatureX = 48;
    private const int FeatureY = 40;
    private const int FeatureSize = 24;

    // ---------------------------------------------------------------------------------------------
    // Streaming: the selection is read on demand, never materialised
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A mean over a spilled selection pulls one frame at a time, in delivery order, and never holds
    /// the selection. Before the fix the pipeline built a <c>List&lt;Frame&gt;</c> of every retained
    /// frame before calling the integrator, so a 300-frame selection at 256x128 (~19.2 MB of pixels)
    /// was live before the first sample was combined.
    /// </summary>
    [Fact]
    public void Mean_StreamsEveryFrameOnce_AndNeverHoldsTheSelection()
    {
        const int frameCount = 300;
        string directory = NewDirectory();
        string spillDirectory = Path.Combine(directory, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", frameCount, 256, 128, f => (ushort)(f % 60));
            string output = Path.Combine(directory, "out");

            var reads = new List<int>();
            bool spilledDuringIntegration = false;
            long baseline = GC.GetTotalMemory(forceFullCollection: true);
            long peak = 0;

            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                RetentionBudgetBytes = 1, // every accepted frame spills: nothing is inline
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            })
            {
                SpillDirectory = spillDirectory,
                IntegrationFrameRead = index =>
                {
                    reads.Add(index);
                    spilledDuringIntegration |= Directory.GetFiles(spillDirectory, "opipp-spill-*.bin").Length > 0;
                    long live = GC.GetTotalMemory(forceFullCollection: true) - baseline;
                    if (live > peak) peak = live;
                },
            };

            ProcessingReport report = processor.Run([input]);

            Assert.Single(report.OutputFiles);
            Assert.True(File.Exists(report.OutputFiles[0]));
            Assert.True(spilledDuringIntegration, "the frames must come from the retention store on demand");

            // Sequential, each frame exactly once: the integrator never asks for a frame twice and
            // never goes back for one it already consumed.
            Assert.Equal(Enumerable.Range(0, frameCount), reads);

            // One frame plus the store's fixed buffers, not the whole selection. The threshold is far
            // below the 19.2 MB a materialised selection costs and far above the ~1 MB the store uses.
            Assert.True(
                peak < 6L * 1024 * 1024,
                $"integration held {peak / 1024.0 / 1024.0:F1} MB live for {frameCount} spilled frames; " +
                "the selection must be streamed, not materialised");

            // The streamed samples really are the ones combined: 300 frames of 0..59 mean 29.5, which
            // half-away-from-zero quantisation writes as 30.
            Frame integrated = ReadTiff(report.OutputFiles[0]);
            Assert.Equal(30, integrated.Samples[(20 * 256) + 20]);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// Median re-reads the selection band by band — the same bound the disk-backed median documents —
    /// so the read sequence is a series of complete, ascending passes over the whole selection and
    /// never a single pass that holds every frame.
    /// </summary>
    [Fact]
    public void Median_ReadsCompleteSequentialPasses_OneBandAtATime()
    {
        const int frameCount = 24;
        string directory = NewDirectory();
        string spillDirectory = Path.Combine(directory, "spill");
        Directory.CreateDirectory(spillDirectory);
        try
        {
            // A constant per-frame value makes the median checkable: 24 frames put 11 and 12 in the
            // middle, and the even-count rule rounds their half-up mean to 12.
            string input = WriteCapture(directory, "capture.ser", frameCount, 64, 48, f => (ushort)f);
            string output = Path.Combine(directory, "out");

            var reads = new List<int>();
            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                RetentionBudgetBytes = 1,
                Integration = new IntegrationOptions
                {
                    Method = IntegrationMethod.Median,
                    MedianBandRows = 8,
                    MedianScratchBudgetBytes = 64L * 1024 * 1024,
                },
            })
            {
                SpillDirectory = spillDirectory,
                IntegrationFrameRead = reads.Add,
            };

            ProcessingReport report = processor.Run([input]);

            Assert.Single(report.OutputFiles);
            Frame integrated = ReadTiff(report.OutputFiles[0]);
            Assert.Equal(64, integrated.Width);
            Assert.Equal(48, integrated.Height);
            Assert.Equal(12, integrated.Samples[(24 * 64) + 32]);

            // Every read sequence is a complete pass 0..N-1: the route never interleaves frames from
            // different passes, and every frame is supplied to each pass.
            Assert.NotEmpty(reads);
            Assert.Equal(0, reads.Count % frameCount);
            for (int pass = 0; pass < reads.Count / frameCount; pass++)
            {
                Assert.Equal(
                    Enumerable.Range(0, frameCount),
                    reads.Skip(pass * frameCount).Take(frameCount));
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// Cancelling while the integrator is pulling frames aborts the run with no image left behind:
    /// the stream is abandoned mid-pass rather than completed into a materialised list.
    /// </summary>
    [Fact]
    public void CancelledDuringIntegration_WritesNoImage()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", 12, Width, Height, f => (ushort)(40 + f));
            string output = Path.Combine(directory, "out");

            using var cancellation = new CancellationTokenSource();
            int reads = 0;
            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                RetentionBudgetBytes = 1,
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            })
            {
                IntegrationFrameRead = _ =>
                {
                    if (++reads == 3) cancellation.Cancel();
                },
            };

            Assert.Throws<OperationCanceledException>(() => processor.Run([input], cancellation.Token));

            Assert.Equal(3, reads);
            Assert.Empty(Directory.GetFiles(output, "*.tif"));
            Assert.Empty(Directory.GetFiles(output, "*.integrating*"));
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Staging: each run owns a directory, and only its own files are ever written or deleted
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A successful run stages inside its own <c>.opipp-staging/&lt;run-guid&gt;/</c> directory and
    /// removes it along with the finished image: the output root ends up holding the image and the
    /// operator's files, and no engine bookkeeping at all. The file already sitting at the old
    /// predictable staging name is neither truncated nor moved away.
    /// </summary>
    [Fact]
    public void Staging_SuccessLeavesNoStagingDirectory_AndAPreexistingStagingNamedFileUntouched()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", 3, Width, Height, f => 100);
            string output = Path.Combine(directory, "out");
            Directory.CreateDirectory(output);
            string sentinel = Path.Combine(output, "capture.integrating.tif");
            byte[] sentinelBytes = "operator data, not staging"u8.ToArray();
            File.WriteAllBytes(sentinel, sentinelBytes);

            ProcessingReport report = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            }).Run([input]);

            string integrated = Path.Combine(output, "capture.tif");
            Assert.Equal(integrated, Assert.Single(report.OutputFiles));

            // The staged file really landed at the destination with the integrated samples.
            Frame image = ReadTiff(integrated);
            Assert.Equal(Width, image.Width);
            Assert.Equal(100, image.Samples[(20 * Width) + 20]);

            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
            AssertNoStaging(output);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// A failure after the staging file exists removes this run's whole staging directory: the writer
    /// rejects a 16-bit frame for AVI, so the directory creation, the failed write and the cleanup all
    /// run, and the sentinel at the old staging name survives.
    /// </summary>
    [Fact]
    public void Staging_IsRemovedAfterAWriterFailure_AndTheSentinelSurvives()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", 3, 64, 48, f => (ushort)(1000 + f), bitDepth: 16);
            string output = Path.Combine(directory, "out");
            Directory.CreateDirectory(output);
            string sentinel = Path.Combine(output, "capture.integrating.avi");
            byte[] sentinelBytes = "operator data, not staging"u8.ToArray();
            File.WriteAllBytes(sentinel, sentinelBytes);

            Assert.Throws<NotSupportedException>(() => new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".avi",
                WriteLogFile = false,
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            }).Run([input]));

            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
            Assert.False(File.Exists(Path.Combine(output, "capture.avi")));
            AssertNoStaging(output);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// A cancelled integration leaves no image and no staging directory, and the unrelated sentinel at
    /// the old predictable name is untouched: this run only ever owned the directory it created.
    /// </summary>
    [Fact]
    public void Staging_IsRemovedAfterCancellation_AndTheSentinelSurvives()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", 3, 64, 48, f => 120);
            string output = Path.Combine(directory, "out");
            Directory.CreateDirectory(output);
            string sentinel = Path.Combine(output, "capture.integrating.tif");
            byte[] sentinelBytes = "operator data, not staging"u8.ToArray();
            File.WriteAllBytes(sentinel, sentinelBytes);

            using var cancellation = new CancellationTokenSource();
            var processor = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            })
            {
                // The integration has read a frame, so the failure lands inside the branch that owns
                // the staging directory rather than in the input stage.
                IntegrationFrameRead = _ => cancellation.Cancel(),
            };

            Assert.Throws<OperationCanceledException>(() => processor.Run([input], cancellation.Token));

            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
            Assert.False(File.Exists(Path.Combine(output, "capture.tif")));
            AssertNoStaging(output);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// A GUI kill cannot run the engine's cleanup, so the run's staging directory outlives it. The
    /// next run sweeps that orphan, but only that one: a recent directory may belong to a run that is
    /// still writing, a directory this engine did not name is not its to delete, and files outside
    /// <c>.opipp-staging</c> are never enumerated at all.
    /// </summary>
    [Fact]
    public void Staging_StaleRunDirectoryFromAKilledRun_IsRemovedByTheNextRun()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", 3, 64, 48, f => 130);
            string output = Path.Combine(directory, "out");
            string stagingRoot = Path.Combine(output, ".opipp-staging");
            Directory.CreateDirectory(stagingRoot);

            // The kill: the staged file and its run's directory were left behind, timestamped long ago.
            string staleRun = Path.Combine(stagingRoot, new string('a', 32));
            Directory.CreateDirectory(staleRun);
            File.WriteAllBytes(Path.Combine(staleRun, "capture.integrating.tif"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(staleRun, "leftover.bin"), "partial output");
            Directory.SetLastWriteTimeUtc(staleRun, DateTime.UtcNow - TimeSpan.FromHours(2));

            // A run still staging right now: fresh, so the sweep must leave it alone.
            string liveRun = Path.Combine(stagingRoot, new string('b', 32));
            Directory.CreateDirectory(liveRun);
            File.WriteAllText(Path.Combine(liveRun, "capture.integrating.tif"), "in flight");
            Directory.SetLastWriteTimeUtc(liveRun, DateTime.UtcNow);

            // Anything under the staging root that this engine did not name belongs to the operator.
            string operatorDirectory = Path.Combine(stagingRoot, "operator-notes");
            Directory.CreateDirectory(operatorDirectory);
            File.WriteAllText(Path.Combine(operatorDirectory, "note.txt"), "not staging");
            Directory.SetLastWriteTimeUtc(operatorDirectory, DateTime.UtcNow - TimeSpan.FromDays(30));

            // Unrelated files outside the staging root, including one at the old staging name.
            string sentinel = Path.Combine(output, "capture.integrating.tif");
            byte[] sentinelBytes = "operator data, not staging"u8.ToArray();
            File.WriteAllBytes(sentinel, sentinelBytes);
            string note = Path.Combine(output, "notes.txt");
            File.WriteAllText(note, "keep me");

            ProcessingReport report = new Processor(new PipelineSettings
            {
                OutputDirectory = output,
                OutputExtension = ".tif",
                WriteLogFile = false,
                Integration = new IntegrationOptions { Method = IntegrationMethod.Mean },
            }).Run([input]);

            string integrated = Path.Combine(output, "capture.tif");
            Assert.Equal(integrated, Assert.Single(report.OutputFiles));

            // The next run's own image is correct and its own directory is gone with it.
            Frame image = ReadTiff(integrated);
            Assert.Equal(64, image.Width);
            Assert.Equal(48, image.Height);
            Assert.Equal(130, image.Samples[(10 * 64) + 10]);

            Assert.False(Directory.Exists(staleRun), "the killed run's directory must be swept");
            Assert.False(File.Exists(Path.Combine(staleRun, "leftover.bin")));
            Assert.True(Directory.Exists(liveRun), "a recent staging directory may belong to a live run");
            Assert.Equal("in flight", File.ReadAllText(Path.Combine(liveRun, "capture.integrating.tif")));
            Assert.True(Directory.Exists(operatorDirectory), "only GUID-named run directories are swept");
            Assert.Equal("not staging", File.ReadAllText(Path.Combine(operatorDirectory, "note.txt")));
            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
            Assert.Equal("keep me", File.ReadAllText(note));

            // Nothing was added to the staging root by the sweep, and the surviving operator entries
            // are the only things left in it besides the live run.
            string[] remaining = Directory.GetDirectories(stagingRoot);
            Array.Sort(remaining, StringComparer.OrdinalIgnoreCase);
            string[] expected = [liveRun, operatorDirectory];
            Array.Sort(expected, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(expected, remaining);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Area of interest: applied after registration, in output coordinates
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Three frames whose feature moves right by three, not at all and left by one pixel. The request
    /// is the 24x24 box around the feature; the aligned image must be that whole rectangle, not the
    /// smaller overlap a pre-registration crop would leave. Both offset signs are covered.
    /// </summary>
    [Fact]
    public void AreaOfInterest_Crop_KeepsTheRequestedRectangleUnderBothOffsetSigns()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(
                directory, "capture.ser", Feature(0), Feature(3), Feature(-1));
            string output = Path.Combine(directory, "out");

            ProcessingReport report = new Processor(TrackedSettings(output, CoveragePolicy.CropToOverlap))
                .Run([input]);

            Frame integrated = ReadTiff(Assert.Single(report.OutputFiles));
            Assert.Equal(FeatureSize, integrated.Width);
            Assert.Equal(FeatureSize, integrated.Height);

            // Every frame carries the same feature under an exact whole-pixel translation, so the
            // aligned mean is the first frame's box sample for sample.
            Frame expected = Feature(0);
            for (int y = 0; y < FeatureSize; y++)
            {
                for (int x = 0; x < FeatureSize; x++)
                {
                    Assert.Equal(
                        expected.Samples[((FeatureY + y) * Width) + FeatureX + x],
                        integrated.Samples[(y * FeatureSize) + x]);
                }
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// Canvas coverage keeps the first frame's geometry; the area of interest is still the requested
    /// rectangle with the same pixels, because the aligned overlap covers it.
    /// </summary>
    [Fact]
    public void AreaOfInterest_Canvas_KeepsTheRequestedRectangle()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(
                directory, "capture.ser", Feature(0), Feature(3), Feature(-1));
            string output = Path.Combine(directory, "out");

            ProcessingReport report = new Processor(TrackedSettings(output, CoveragePolicy.KeepCanvas))
                .Run([input]);

            Frame integrated = ReadTiff(Assert.Single(report.OutputFiles));
            Assert.Equal(FeatureSize, integrated.Width);
            Assert.Equal(FeatureSize, integrated.Height);

            Frame expected = Feature(0);
            for (int y = 0; y < FeatureSize; y++)
            {
                for (int x = 0; x < FeatureSize; x++)
                {
                    Assert.Equal(
                        expected.Samples[((FeatureY + y) * Width) + FeatureX + x],
                        integrated.Samples[(y * FeatureSize) + x]);
                }
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// Integration of a registered pair must land on exactly the pixels the ordinary (per-frame)
    /// path produces for the reference frame: same geometry, same samples. That is the invariant the
    /// crop-after-registration order restores.
    /// </summary>
    [Fact]
    public void AreaOfInterest_OneFrame_MatchesTheOrdinaryRegisteredPath()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Feature(0), Feature(3));

            var integrationSettings = TrackedSettings(Path.Combine(directory, "integrated"), CoveragePolicy.CropToOverlap);
            integrationSettings.Integration = new IntegrationOptions
            {
                Method = IntegrationMethod.Mean,
                Subpixel = false,
            };

            var ordinarySettings = TrackedSettings(Path.Combine(directory, "ordinary"), CoveragePolicy.CropToOverlap);
            ordinarySettings.Integration = null;

            ProcessingReport integratedReport = new Processor(integrationSettings).Run([input]);
            ProcessingReport ordinaryReport = new Processor(ordinarySettings).Run([input]);

            Frame integrated = ReadTiff(Assert.Single(integratedReport.OutputFiles));
            Assert.Equal(2, ordinaryReport.OutputFiles.Count);
            Frame reference = ReadTiff(ordinaryReport.OutputFiles[0]);

            Assert.Equal(reference.Width, integrated.Width);
            Assert.Equal(reference.Height, integrated.Height);
            Assert.Equal(reference.Samples, integrated.Samples);
        }
        finally
        {
            Delete(directory);
        }
    }

    /// <summary>
    /// A request that misses the integrated region entirely is a configuration mistake, reported
    /// instead of silently writing pixels outside the box.
    /// </summary>
    [Fact]
    public void AreaOfInterest_OutsideTheIntegratedRegion_IsReported()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = WriteCapture(directory, "capture.ser", Feature(0), Feature(3));
            string output = Path.Combine(directory, "out");
            PipelineSettings settings = TrackedSettings(output, CoveragePolicy.CropToOverlap);
            settings.AreaOfInterest = new PixelRect(Width - 2, Height - 2, 2, 2);

            var error = Assert.Throws<InvalidOperationException>(() => new Processor(settings).Run([input]));

            Assert.Contains("area of interest", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(output, "*.tif"));
        }
        finally
        {
            Delete(directory);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Settings that measure a whole-pixel registration with the surface tracker and then crop to the
    /// feature box, exactly like an operator would with <c>-en_surface_feature_tracking</c>,
    /// <c>-afb_*</c>, <c>-aoi_crop</c> and <c>-stack_subpixel=off</c>.
    /// </summary>
    private static PipelineSettings TrackedSettings(string outputDirectory, CoveragePolicy coverage) => new()
    {
        OutputDirectory = outputDirectory,
        OutputExtension = ".tif",
        WriteLogFile = false,
        SurfaceTracking = true,
        EnableStabilisation = true,
        AlignmentBox = new PixelRect(FeatureX, FeatureY, FeatureSize, FeatureSize),
        AreaOfInterest = new PixelRect(FeatureX, FeatureY, FeatureSize, FeatureSize),
        AreaOfInterestCrop = true,
        Integration = new IntegrationOptions
        {
            Method = IntegrationMethod.Mean,
            Coverage = coverage,
            Subpixel = false,
        },
    };

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "opipp-integration-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Writes a mono capture whose frames all carry <c>value(frameIndex)</c>.</summary>
    private static string WriteCapture(
        string directory,
        string name,
        int frameCount,
        int width,
        int height,
        Func<int, ushort> value,
        int bitDepth = 8)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, width, height, 1, bitDepth, frameCount: 0);
        for (int f = 0; f < frameCount; f++)
        {
            var frame = new Frame(width, height, 1, bitDepth);
            Array.Fill(frame.Samples, value(f));
            writer.Write(frame);
        }

        return path;
    }

    private static string WriteCapture(string directory, string name, params Frame[] frames)
    {
        string path = Path.Combine(directory, name);
        using var writer = new SerWriter(path, Width, Height, 1, 8, frameCount: 0);
        foreach (Frame frame in frames)
        {
            writer.Write(frame);
        }

        return path;
    }

    /// <summary>
    /// One frame of a capture whose asymmetric 24x24 feature sits at <c>x = 48 + shiftX</c>,
    /// <c>y = 40</c>: an exact whole-pixel translation, so the measured offset is whole.
    /// </summary>
    private static Frame Feature(int shiftX)
    {
        var frame = new Frame(Width, Height, 1, 8);
        Array.Fill(frame.Samples, (ushort)40);
        for (int ly = 0; ly < FeatureSize; ly++)
        {
            for (int lx = 0; lx < FeatureSize; lx++)
            {
                int x = FeatureX + shiftX + lx, y = FeatureY + ly;
                if (x < 0 || x >= Width)
                {
                    continue;
                }

                ushort value = 60;
                if (lx is >= 3 and < 12 && ly is >= 3 and < 12) value = 220;
                if (ly is >= 18 and < 20) value = 25;
                if (lx is >= 17 and < 20 && ly < 8) value = 150;
                frame.Samples[(y * Width) + x] = value;
            }
        }

        return frame;
    }

    /// <summary>
    /// Asserts the output root carries no staging bookkeeping: no <c>.opipp-staging</c> container
    /// anywhere beneath it (a finished or cleaned-up run leaves none) and no file at the old
    /// <c>&lt;name&gt;.integrating-&lt;guid&gt;&lt;ext&gt;</c> shape, which is what a killed run used
    /// to leave in the output root itself.
    /// </summary>
    private static void AssertNoStaging(string outputRoot)
    {
        Assert.False(
            Directory.Exists(Path.Combine(outputRoot, Processor.StagingDirectoryName)),
            "the run's staging directory must not survive the run");
        Assert.Empty(Directory.GetFiles(outputRoot, "*.integrating-*", SearchOption.AllDirectories));
    }

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidDataException($"'{path}' holds no frame");
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Collection whose tests run alone. The streaming test measures the live managed heap, so a test in
/// another collection allocating at the same time would be attributed to the integration.
/// </summary>
[CollectionDefinition("integration-memory", DisableParallelization = true)]
public sealed class IntegrationMemoryCollection;
