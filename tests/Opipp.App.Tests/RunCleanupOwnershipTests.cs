using System.Collections.Generic;
using System.Threading;
using Opipp.Core.Pipeline;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// Ownership of the temporary files a run may delete.
/// </summary>
/// <remarks>
/// Cancelling used to enumerate every <c>*.tmp</c> in the output directory and delete it. The
/// output directory is often an existing folder the operator owns — with the default options it is
/// the source folder itself — so an unrelated <c>keeper.tmp</c> was destroyed by pressing Cancel.
/// Only files the current run registered may be removed.
/// </remarks>
public sealed class RunCleanupOwnershipTests
{
    /// <summary>A master build that parks until the form cancels it.</summary>
    private sealed class BlockingBuild : IMasterFrameBuild
    {
        private readonly ManualResetEventSlim _started;

        public BlockingBuild(ManualResetEventSlim started) => _started = started;

        public MasterBuildResult Build(
            IReadOnlyList<string> files,
            string? subtractFile,
            string? masterPath,
            string? pgmPath,
            int frameLimit,
            Action<int>? progress,
            CancellationToken cancellationToken)
        {
            _started.Set();
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(60));
            cancellationToken.ThrowIfCancellationRequested();
            return MasterBuildResult.Failed("the blocking test build was not cancelled");
        }
    }

    /// <summary>Writes the sentinel pair the run must never touch, and returns their paths.</summary>
    private static (string Keeper, string Sentinel) WriteSentinels(string directory)
    {
        string keeper = Path.Combine(directory, "keeper.tmp");
        string sentinel = Path.Combine(directory, "sentinel.dat");
        File.WriteAllText(keeper, "this file does not belong to any run");
        File.WriteAllText(sentinel, "neither does this one");
        return (keeper, sentinel);
    }

    private static void AssertSentinelsSurvived(string keeper, string sentinel, string because)
    {
        Assert.True(File.Exists(keeper), "keeper.tmp was deleted: " + because);
        Assert.True(File.Exists(sentinel), "sentinel.dat was deleted: " + because);
        Assert.Equal("this file does not belong to any run", File.ReadAllText(keeper));
    }

    [Fact]
    public void SuccessfulRun_LeavesPreExistingTmpAndSentinelFilesAlone()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            form.ErrorDialogOverride = (_, _) => { };

            string directory = AppTestSupport.TempDirectory();
            try
            {
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125);
                string dark = AppTestSupport.WriteUniformSer(directory, "dark.ser", 10);
                string output = Path.Combine(directory, "out");
                Directory.CreateDirectory(output);
                (string keeper, string sentinel) = WriteSentinels(output);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    darkFiles: [dark],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");

                Assert.Equal("COMPLETE", form.StatusTextForTest);
                AssertSentinelsSurvived(keeper, sentinel, "status=" + form.StatusTextForTest);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(150));
            }
            finally
            {
                // Detach before disposal: the engine's async readers may still deliver a
                // trailing line, and marshalling it to a disposed form kills the test host.
                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void FailedRun_LeavesPreExistingTmpAndSentinelFilesAlone()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            form.ErrorDialogOverride = (_, _) => { };

            string directory = AppTestSupport.TempDirectory();
            try
            {
                // A 16x16 capture with an 8x8 master: the engine rejects the calibration and exits 1.
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125, 16);
                string dark = AppTestSupport.WriteUniformSer(directory, "dark.ser", 10, 8);
                string output = Path.Combine(directory, "out");
                Directory.CreateDirectory(output);
                (string keeper, string sentinel) = WriteSentinels(output);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    darkFiles: [dark],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the failed run to finish");

                Assert.NotEqual(0, form.LastExitCodeForTest);
                Assert.Equal("FAILED WITH ERRORS", form.StatusTextForTest);
                AssertSentinelsSurvived(keeper, sentinel, "status=" + form.StatusTextForTest);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(150));
            }
            finally
            {
                // Detach before disposal: the engine's async readers may still deliver a
                // trailing line, and marshalling it to a disposed form kills the test host.
                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void CancelledRun_LeavesPreExistingTmpAndSentinelFilesAlone()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            form.ErrorDialogOverride = (_, _) => { };

            string directory = AppTestSupport.TempDirectory();
            try
            {
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125);
                string dark = AppTestSupport.WriteUniformSer(directory, "dark.ser", 10);
                string output = Path.Combine(directory, "out");
                Directory.CreateDirectory(output);
                (string keeper, string sentinel) = WriteSentinels(output);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    darkFiles: [dark],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                using var buildStarted = new ManualResetEventSlim();
                form.MasterBuildOverride = new BlockingBuild(buildStarted);
                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => buildStarted.IsSet, "the master build to start");

                form.CancelProcessingForTest();
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the cancelled build to finish");

                Assert.Equal("CANCELLED", form.StatusTextForTest);
                AssertSentinelsSurvived(keeper, sentinel, "status=" + form.StatusTextForTest);
            }
            finally
            {
                // Detach before disposal: the engine's async readers may still deliver a
                // trailing line, and marshalling it to a disposed form kills the test host.
                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void CancelledRunInTheSourceDirectory_LeavesUnrelatedTmpFilesAlone()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            form.ErrorDialogOverride = (_, _) => { };

            string directory = AppTestSupport.TempDirectory();
            try
            {
                // No explicit output directory and no output subdirectory: the run writes into the
                // source folder itself, which is exactly where the blanket sweep did the damage.
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125);
                string dark = AppTestSupport.WriteUniformSer(directory, "dark.ser", 10);
                (string keeper, string sentinel) = WriteSentinels(directory);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: null,
                    darkFiles: [dark],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                using var buildStarted = new ManualResetEventSlim();
                form.MasterBuildOverride = new BlockingBuild(buildStarted);
                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => buildStarted.IsSet, "the master build to start");

                form.CancelProcessingForTest();
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the cancelled build to finish");

                Assert.Equal("CANCELLED", form.StatusTextForTest);
                AssertSentinelsSurvived(keeper, sentinel, "status=" + form.StatusTextForTest);
            }
            finally
            {
                // Detach before disposal: the engine's async readers may still deliver a
                // trailing line, and marshalling it to a disposed form kills the test host.
                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void Registry_DeletesOnlyFilesUnderItsOwnOutputRoot()
    {
        string directory = AppTestSupport.TempDirectory();
        string outside = AppTestSupport.TempDirectory();
        try
        {
            string insideTmp = Path.Combine(directory, "owned.tmp");
            string insideKeeper = Path.Combine(directory, "keeper.tmp");
            string outsideFile = Path.Combine(outside, "elsewhere.tmp");
            File.WriteAllText(insideTmp, "x");
            File.WriteAllText(insideKeeper, "y");
            File.WriteAllText(outsideFile, "z");

            var registry = new RunTempArtifacts(directory);
            Assert.True(registry.Register(insideTmp));
            Assert.False(registry.Register(outsideFile), "a path outside the output root must be refused");
            Assert.False(registry.Register(insideKeeper + ".missing"));
            Assert.Equal(1, registry.Count);

            Assert.Equal(1, registry.DeleteAll());
            Assert.False(File.Exists(insideTmp));
            Assert.True(File.Exists(insideKeeper), "an unregistered file in the output root was deleted");
            Assert.True(File.Exists(outsideFile));
            Assert.Equal(0, registry.Count);
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
            AppTestSupport.DeleteQuietly(outside);
        }
    }
}
