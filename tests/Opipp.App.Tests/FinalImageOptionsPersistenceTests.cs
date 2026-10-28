using System.Windows.Forms;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The final-image integration choice as part of the saved option set: Save Options writes it, Load
/// Options restores it (in the same form and in a fresh one), Reset Options turns it off, Undo Reset
/// brings it back, and an options file written before those keys existed loads with explicit
/// defaults.
/// </summary>
/// <remarks>
/// The setting used to be assigned only at start-up and when the dialog was accepted, so a saved
/// profile re-opened with integration off and Reset Options left <c>-stack=...</c> in the next run's
/// argument list.
/// </remarks>
public sealed class FinalImageOptionsPersistenceTests
{
    private static FinalImageSettings SumKeepCanvasNoSubpixel => new()
    {
        Enabled = true,
        Method = FinalImageMethod.Sum,
        Coverage = FinalImageCoverage.KeepCanvas,
        Subpixel = false,
    };

    private static FinalImageSettings DerotateOn => new()
    {
        Enabled = true,
        Method = FinalImageMethod.Sum,
        Coverage = FinalImageCoverage.KeepCanvas,
        Subpixel = false,
        Derotate = true,
        DerotateSearchDegrees = 12.5,
    };

    private static void AssertDerotateOn(FinalImageSettings settings, string because)
    {
        Assert.True(settings.Derotate, "derotation should be on: " + because);
        Assert.Equal(12.5, settings.DerotateSearchDegrees);
    }

    private static void AssertSumKeepCanvasNoSubpixel(FinalImageSettings settings, string because)
    {
        Assert.True(settings.Enabled, "integration should be enabled: " + because);
        Assert.Equal(FinalImageMethod.Sum, settings.Method);
        Assert.Equal(FinalImageCoverage.KeepCanvas, settings.Coverage);
        Assert.False(settings.Subpixel, "subpixel should be off: " + because);
    }

    private static void AssertDefaults(FinalImageSettings settings, string because)
    {
        Assert.False(settings.Enabled, "integration should be off: " + because);
        Assert.Equal(FinalImageMethod.Mean, settings.Method);
        Assert.Equal(FinalImageCoverage.CropToOverlap, settings.Coverage);
        Assert.True(settings.Subpixel, "subpixel should be on: " + because);
        Assert.False(settings.Derotate, "derotation should be off: " + because);
        Assert.Equal(5.0, settings.DerotateSearchDegrees);
    }

    [Fact]
    public void SaveOptions_WritesTheSixFinalImageKeys()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string file = Path.Combine(directory, "profile.pof");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                form.FinalImageSettingsForTest = DerotateOn;
                form.SaveOptionsForTest(file);
            });

            string[] lines = File.ReadAllLines(file);
            Assert.Equal("[PIPP OPTIONS FILE]", lines[0]);
            Assert.Contains("FINAL_IMAGE_ENABLED True", lines);
            Assert.Contains("FINAL_IMAGE_METHOD sum", lines);
            Assert.Contains("FINAL_IMAGE_COVERAGE canvas", lines);
            Assert.Contains("FINAL_IMAGE_SUBPIXEL off", lines);
            Assert.Contains("FINAL_IMAGE_DEROTATE True", lines);
            Assert.Contains("FINAL_IMAGE_DEROTATE_SEARCH 12.5", lines);
            Assert.Equal(
                6,
                lines.Count(line => line.StartsWith("FINAL_IMAGE_", StringComparison.Ordinal)));
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void SaveOptions_WritesDerotationOffAtTheDefaultRange()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string file = Path.Combine(directory, "profile.pof");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                form.SaveOptionsForTest(file);
            });

            string[] lines = File.ReadAllLines(file);
            Assert.Contains("FINAL_IMAGE_DEROTATE False", lines);
            Assert.Contains("FINAL_IMAGE_DEROTATE_SEARCH 5.0", lines);
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [SkippableFact]
    public void SaveThenLoad_RestoresTheChoice_AndTheRunSendsTheSwitches()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string science = AppTestSupport.WritePlanetSer(directory, "capture.ser", 3);
            string output = Path.Combine(directory, "out");
            string file = Path.Combine(directory, "profile.pof");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                var errors = new List<string>();
                form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

                form.FinalImageSettingsForTest = SumKeepCanvasNoSubpixel;
                form.SaveOptionsForTest(file);

                // What a fresh profile looks like: everything back at the defaults.
                form.FinalImageSettingsForTest = new FinalImageSettings();
                AssertDefaults(form.FinalImageSettingsForTest, "before loading the saved file");

                form.LoadOptionsForTest(file);
                AssertSumKeepCanvasNoSubpixel(form.FinalImageSettingsForTest, "after loading the saved file");

                // Load Options resets every other option too, so the run is prepared afterwards.
                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                string[] arguments = form.LastEngineArgumentsForTest.ToArray();
                Assert.Contains("-stack=sum", arguments);
                Assert.Contains("-stack_coverage=canvas", arguments);
                Assert.Contains("-stack_subpixel=off", arguments);
                Assert.Contains(
                    "Final image integration: sum, keep first canvas, subpixel off",
                    form.OutputTextForTest);

                bool unknownSwitch =
                    form.LastExitCodeForTest == 127
                    && form.OutputTextForTest.Contains("Unknown switch", StringComparison.Ordinal);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));

                Skip.If(unknownSwitch, "the engine does not recognise -stack: " + form.OutputTextForTest);

                Assert.False(
                    form.ProcessingFailedForTest,
                    "status=" + form.StatusTextForTest
                    + " exit=" + form.LastExitCodeForTest
                    + " output=" + form.OutputTextForTest
                    + " errors=" + string.Join(" | ", errors));
                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void AFreshForm_LoadingTheSavedFile_RestoresTheChoice()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string file = Path.Combine(directory, "profile.pof");

            AppTestSupport.RunSta(() =>
            {
                using (var writer = new Form1())
                {
                    _ = writer.Handle;
                    writer.FinalImageSettingsForTest = SumKeepCanvasNoSubpixel;
                    writer.SaveOptionsForTest(file);
                }

                // A second form never saw the dialog: everything it knows comes from the file.
                using var reader = new Form1();
                _ = reader.Handle;
                AssertDefaults(reader.FinalImageSettingsForTest, "before the fresh form loads the file");

                reader.LoadOptionsForTest(file);
                AssertSumKeepCanvasNoSubpixel(reader.FinalImageSettingsForTest, "in a fresh form loading the file");
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void ResetOptions_DisablesIntegration_AndUndoReset_RestoresIt()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string snapshot = Path.Combine(directory, "reset_options.pof");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                form.ResetOptionsPathOverride = snapshot;
                form.FinalImageSettingsForTest = SumKeepCanvasNoSubpixel;

                form.ResetOptionsForTest();

                AssertDefaults(form.FinalImageSettingsForTest, "after Reset Options");
                Assert.True(
                    File.Exists(snapshot),
                    "Reset Options must park a snapshot for Undo Reset at " + snapshot);

                form.UndoResetOptionsForTest();
                AssertSumKeepCanvasNoSubpixel(form.FinalImageSettingsForTest, "after Undo Reset Options");
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [SkippableFact]
    public void AResetForm_SendsNoIntegrationSwitches()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string science = AppTestSupport.WritePlanetSer(directory, "capture.ser", 3);
            string output = Path.Combine(directory, "out");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                form.ResetOptionsPathOverride = Path.Combine(directory, "reset_options.pof");
                form.ErrorDialogOverride = (_, _) => { };
                form.FinalImageSettingsForTest = SumKeepCanvasNoSubpixel;

                form.ResetOptionsForTest();

                // Reset Options clears the file lists and the output settings, so the run is
                // prepared again; the only thing under test is that no -stack switch goes out.
                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false);

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                string[] arguments = form.LastEngineArgumentsForTest.ToArray();
                Assert.DoesNotContain(
                    arguments,
                    argument => argument.StartsWith("-stack", StringComparison.Ordinal));
                Assert.DoesNotContain("Final image integration:", form.OutputTextForTest);

                bool unknownSwitch =
                    form.LastExitCodeForTest == 127
                    && form.OutputTextForTest.Contains("Unknown switch", StringComparison.Ordinal);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));

                Skip.If(unknownSwitch, "the engine does not recognise -stack: " + form.OutputTextForTest);

                Assert.Equal("COMPLETE", form.StatusTextForTest);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void AnOptionsFileWithoutTheFinalImageKeys_LoadsWithIntegrationOff()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            // An options file written before the final-image keys existed: no FINAL_IMAGE_* line at
            // all, and nothing else in it is exotic enough to raise the load-error report.
            string file = Path.Combine(directory, "old.pof");
            File.WriteAllLines(
                file,
                new[]
                {
                    "[PIPP OPTIONS FILE]",
                    "BATCH_MODE True",
                    "FRAME_LIMIT False",
                    "FRAME_RANGE_TYPE 0",
                    "OUTPUT_TIF True",
                });

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                var errors = new List<string>();
                form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

                // A form that had integration enabled must not keep it just because the file is old.
                form.FinalImageSettingsForTest = SumKeepCanvasNoSubpixel;

                form.LoadOptionsForTest(file);

                AssertDefaults(form.FinalImageSettingsForTest, "after loading a file without the new keys");
                Assert.Empty(errors);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void AFreshForm_LoadingAProfileWithDerotation_RestoresTheToggleAndTheRange()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string file = Path.Combine(directory, "profile.pof");

            AppTestSupport.RunSta(() =>
            {
                using (var writer = new Form1())
                {
                    _ = writer.Handle;
                    writer.FinalImageSettingsForTest = DerotateOn;
                    writer.SaveOptionsForTest(file);
                }

                using var reader = new Form1();
                _ = reader.Handle;
                var errors = new List<string>();
                reader.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);
                Assert.False(reader.FinalImageSettingsForTest.Derotate, "a fresh form starts with derotation off");

                reader.LoadOptionsForTest(file);

                AssertDerotateOn(reader.FinalImageSettingsForTest, "in a fresh form loading the saved profile");
                AssertSumKeepCanvasNoSubpixel(reader.FinalImageSettingsForTest, "alongside the rest of the choice");
                Assert.Empty(errors);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void ResetOptions_TurnsDerotationOff_AndUndoReset_RestoresToggleAndRange()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string snapshot = Path.Combine(directory, "reset_options.pof");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                form.ResetOptionsPathOverride = snapshot;
                form.FinalImageSettingsForTest = DerotateOn;

                form.ResetOptionsForTest();

                AssertDefaults(form.FinalImageSettingsForTest, "after Reset Options");
                Assert.True(File.Exists(snapshot), "Reset Options must park a snapshot for Undo Reset");

                form.UndoResetOptionsForTest();

                AssertDerotateOn(form.FinalImageSettingsForTest, "after Undo Reset Options");
                AssertSumKeepCanvasNoSubpixel(form.FinalImageSettingsForTest, "after Undo Reset Options");
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void AnOptionsFileWithOnlyTheOriginalFourFinalImageKeys_LoadsWithDerotationOff()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            // A profile from before derotation existed: the four integration keys are there, the two
            // FINAL_IMAGE_DEROTATE* keys are not. It must load as integration on and derotation off.
            string file = Path.Combine(directory, "old-with-integration.pof");
            File.WriteAllLines(
                file,
                new[]
                {
                    "[PIPP OPTIONS FILE]",
                    "BATCH_MODE True",
                    "OUTPUT_TIF True",
                    "FINAL_IMAGE_ENABLED True",
                    "FINAL_IMAGE_METHOD sum",
                    "FINAL_IMAGE_COVERAGE canvas",
                    "FINAL_IMAGE_SUBPIXEL off",
                });

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                var errors = new List<string>();
                form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

                // A form that had derotation on must not keep it just because the file predates the keys.
                form.FinalImageSettingsForTest = DerotateOn;

                form.LoadOptionsForTest(file);

                AssertSumKeepCanvasNoSubpixel(
                    form.FinalImageSettingsForTest,
                    "after loading a profile without the derotation keys");
                Assert.False(form.FinalImageSettingsForTest.Derotate, "old profiles must default derotation off");
                Assert.Equal(5.0, form.FinalImageSettingsForTest.DerotateSearchDegrees);
                Assert.Empty(errors);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [Fact]
    public void AnUnreadableDerotateValue_LeavesTheExplicitDefaults()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string file = Path.Combine(directory, "bad-derotate.pof");
            File.WriteAllLines(
                file,
                new[]
                {
                    "[PIPP OPTIONS FILE]",
                    "BATCH_MODE True",
                    "FINAL_IMAGE_ENABLED True",
                    "FINAL_IMAGE_DEROTATE maybe",
                    "FINAL_IMAGE_DEROTATE_SEARCH 90",
                });

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;

                // Neither value is usable (the toggle is not a boolean, the range is outside 0.1..45),
                // so both keep the reset defaults rather than being silently turned into a request the
                // file did not make. The report is the shared "Errors Found In Options File" message
                // box, which this test lets time out; the rejection itself is asserted directly in
                // FinalImageSettingsTests.
                form.FinalImageSettingsForTest = DerotateOn;

                form.LoadOptionsForTest(file);

                Assert.True(form.FinalImageSettingsForTest.Enabled, "the readable keys still apply");
                Assert.False(form.FinalImageSettingsForTest.Derotate);
                Assert.Equal(5.0, form.FinalImageSettingsForTest.DerotateSearchDegrees);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }
}
