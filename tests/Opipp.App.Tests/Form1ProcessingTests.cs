using System.Collections.Generic;
using System.Threading;
using Opipp.Core.Pipeline;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The form-state defects, driven through the real <see cref="Form1"/> on an STA thread: a
/// cancelled master build must leave the form usable, a non-zero engine exit must read as failed,
/// and declining Quit must not delete the cached masters.
/// </summary>
public sealed class Form1ProcessingTests
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

    [Fact]
    public void CancelDuringMasterBuild_RestoresTheControls_AndTheSameFormStartsAgain()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            var errors = new List<string>();
            form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

            string directory = AppTestSupport.TempDirectory();
            try
            {
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125, 16);
                string flat = AppTestSupport.WriteSplitSer(directory, "flat.ser", 200, 100, 16);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: Path.Combine(directory, "out1"),
                    flatFiles: [flat],
                    flatCalibration: true,
                    flatDarkSubtraction: false);

                using var firstBuildStarted = new ManualResetEventSlim();
                form.MasterBuildOverride = new BlockingBuild(firstBuildStarted);

                Assert.False(form.ProcessingRunningForTest, "PrepareRunForTest started a run on its own");
                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => firstBuildStarted.IsSet, "the master build to start");
                Assert.False(form.RunEnabledForTest);

                // Cancel during the in-process build: there is no engine process, so this is the
                // path that used to show CANCELLED while leaving Start Processing disabled.
                form.CancelProcessingForTest();
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the cancelled build to finish");

                Assert.Equal("CANCELLED", form.StatusTextForTest);
                Assert.True(form.RunEnabledForTest, "Start Processing stayed disabled after a cancelled master build");
                Assert.False(form.ProcessingFailedForTest, "status=" + form.StatusTextForTest + " output=" + form.OutputTextForTest + " errors=" + string.Join(" | ", errors));

                // The second run on the same instance must actually start; the restored controls are
                // what allow it (and runBTN.Enabled is the visible state the operator clicks).
                using var secondBuildStarted = new ManualResetEventSlim();
                form.MasterBuildOverride = new BlockingBuild(secondBuildStarted);
                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the second run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => secondBuildStarted.IsSet, "the second run's master build to start");
                Assert.False(form.RunEnabledForTest);

                form.CancelProcessingForTest();
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the second cancelled build to finish");
                Assert.Equal("CANCELLED", form.StatusTextForTest);
                Assert.True(form.RunEnabledForTest);

                // Let any late engine/output callback be delivered before the form is disposed.
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(300));
            }
            finally
            {
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void CancelWithALockedTempFile_StillRestoresTheControls()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            form.ErrorDialogOverride = (_, _) => { };

            string directory = AppTestSupport.TempDirectory();
            FileStream? hold = null;
            try
            {
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125, 16);
                string flat = AppTestSupport.WriteSplitSer(directory, "flat.ser", 200, 100, 16);
                string output = Path.Combine(directory, "out");
                Directory.CreateDirectory(output);

                // show_cancelled_state clears *.tmp from the output directory. A file someone still
                // holds open makes that cleanup throw; the controls must be restored regardless.
                string locked = Path.Combine(output, "locked.tmp");
                File.WriteAllBytes(locked, [1]);
                hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatFiles: [flat],
                    flatCalibration: true,
                    flatDarkSubtraction: false);

                using var buildStarted = new ManualResetEventSlim();
                form.MasterBuildOverride = new BlockingBuild(buildStarted);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => buildStarted.IsSet, "the master build to start");

                form.CancelProcessingForTest();
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the cancelled build to finish");

                Assert.Equal("CANCELLED", form.StatusTextForTest);
                Assert.True(form.RunEnabledForTest, "a cleanup failure left Start Processing disabled");
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(200));
            }
            finally
            {
                hold?.Dispose();
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void IncompatibleMasterGeometry_EndsFailed_NotComplete()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            form.ErrorDialogOverride = (_, _) => { };

            string directory = AppTestSupport.TempDirectory();
            try
            {
                // A 16x16 capture with an 8x8 master dark: the engine rejects the calibration and
                // exits 1 (its runtime/calibration failure code), which the GUI used to report as
                // COMPLETE because it only treated negative codes as failure.
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125, 16);
                string dark = AppTestSupport.WriteUniformSer(directory, "dark.ser", 10, 8);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: Path.Combine(directory, "out"),
                    darkFiles: [dark],
                    flatCalibration: false,
                    darkSubtraction: true);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the failed run to finish");

                Assert.Equal(1, form.LastExitCodeForTest);
                Assert.Equal("FAILED WITH ERRORS", form.StatusTextForTest);
                Assert.True(form.ProcessingFailedForTest);
                Assert.True(form.RunEnabledForTest, "a failed run must leave the form usable");

                // Stop the engine's stream readers from marshalling into the form, then drain any
                // notification that was already on its way, before disposal.
                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));
            }
            finally
            {
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void SaveMasterFlatDark_SavesTheBuiltMaster_AndTheScienceStaysCorrected()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;
            var errors = new List<string>();
            form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

            string directory = AppTestSupport.TempDirectory();
            try
            {
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 125);
                string flat = AppTestSupport.WriteSplitSer(directory, "flat.ser", 200, 100);
                string flatDark = AppTestSupport.WriteSplitSer(directory, "flat_dark.ser", 50, 0);
                string output = Path.Combine(directory, "out");

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatFiles: [flat],
                    flatDarkFiles: [flatDark],
                    flatCalibration: true,
                    flatDarkSubtraction: true,
                    saveFlatDark: true);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");

                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);
                Assert.False(form.ProcessingFailedForTest, string.Join(" | ", errors));

                // The engine never received -flat_dark_tmp (the flat is corrected in the GUI), so
                // the GUI itself wrote the saved master.
                string saved = Path.Combine(output, "logs", "pipp_master_flat_dark.tif");
                Assert.True(File.Exists(saved), "the master flat dark was not saved");
                using (var reader = new Opipp.Core.Formats.TiffReader(saved))
                {
                    var frame = reader.ReadFrame() ?? throw new InvalidOperationException("the saved TIFF has no frame");
                    Assert.Equal(50, frame[0, 0]);
                    Assert.Equal(0, frame[7, 0]);
                }

                var calibrated = AppTestSupport.ReadSingleScienceTiff(output);
                Assert.Equal(104, calibrated[0, 0]);
                Assert.Equal(156, calibrated[7, 0]);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));
            }
            finally
            {
                form.SetProcessingRunningForTest(false);
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void DecliningQuit_KeepsTheTempDirectoryAndItsCachedMasters()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            bool asked = false;
            form.QuitConfirmationOverride = () =>
            {
                asked = true;
                return false;
            };

            string cachedMaster = Path.Combine(form.TempDirectoryForTest, "pipp_master_flat.tmp");
            File.WriteAllBytes(cachedMaster, [1, 2, 3]);

            // The quit path only prompts (and only deletes) while a run owns the form.
            form.SetProcessingRunningForTest(true);
            try
            {
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
                form.RaiseFormClosingForTest(closing);

                Assert.True(asked, "the quit confirmation was not consulted");
                Assert.True(closing.Cancel, "declining the quit must cancel the close");
                Assert.True(Directory.Exists(form.TempDirectoryForTest), "the declined close deleted the session temp directory");
                Assert.True(File.Exists(cachedMaster), "the declined close deleted a cached master");
            }
            finally
            {
                form.SetProcessingRunningForTest(false);
            }
        });
    }
}
