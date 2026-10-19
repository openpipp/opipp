using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// What the processing transcript says when a run does not complete.
/// </summary>
/// <remarks>
/// The engine's stdout ends normally even when the run stopped early, so the old end-of-stream
/// trailer printed "PROCESSING COMPLETE" for a run that had written some frames and then failed on
/// a later input while the status label said FAILED WITH ERRORS. The verdict now waits for the exit
/// code, and the failure form says the output is incomplete and repeats the engine's diagnostics.
/// </remarks>
public sealed class PartialFailureDisplayTests
{
    [Fact]
    public void PartialRun_WithALaterInputMissing_EndsVisiblyIncomplete()
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
                string good = AppTestSupport.WriteUniformSer(directory, "good.ser", 100);
                string missing = AppTestSupport.WriteUniformSer(directory, "missing.ser", 100);
                string output = Path.Combine(directory, "out");

                // Both inputs are accepted by the file list (they exist); the second is gone by the
                // time the engine opens it, so the first input's frames are written and the run
                // then fails on the second input.
                form.PrepareRunForTest(
                    [good, missing],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false);
                File.Delete(missing);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the partial run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                Assert.NotEqual(0, form.LastExitCodeForTest);
                Assert.Equal("FAILED WITH ERRORS", form.StatusTextForTest);
                Assert.True(form.ProcessingFailedForTest);
                Assert.True(form.RunVerdictWrittenForTest, "the transcript verdict was never written");

                string transcript = form.OutputTextForTest;
                Assert.DoesNotContain("PROCESSING COMPLETE", transcript);
                Assert.Contains("PROCESSING INCOMPLETE", transcript);
                Assert.Contains("incomplete", transcript, StringComparison.OrdinalIgnoreCase);

                // The engine's diagnostics are surfaced in the transcript, not only in the modal
                // error dialog: the missing input is named there.
                Assert.Contains("Error", transcript);
                Assert.Contains("missing.ser", transcript);

                // Frames from the first input really were written: "incomplete", not "nothing".
                Assert.Single(Directory.GetFiles(output, "*.tif", SearchOption.AllDirectories));

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
    public void FailedRunBeforeAnyOutput_SaysIncomplete_AndRepeatsTheEngineDiagnostic()
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

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: Path.Combine(directory, "out"),
                    darkFiles: [dark],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the failed run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                string transcript = form.OutputTextForTest;
                Assert.DoesNotContain("PROCESSING COMPLETE", transcript);
                Assert.Contains("PROCESSING INCOMPLETE", transcript);
                Assert.Contains("exited with code 1", transcript);
                // The run-level reason the engine wrote to stderr is in the transcript too.
                Assert.Contains("Error", transcript);

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
    public void CompletedRun_StillSaysComplete()
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

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: Path.Combine(directory, "out"),
                    darkFiles: [dark],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                Assert.Equal(0, form.LastExitCodeForTest);
                string transcript = form.OutputTextForTest;
                Assert.Contains("PROCESSING COMPLETE", transcript);
                Assert.DoesNotContain("PROCESSING INCOMPLETE", transcript);

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
}
