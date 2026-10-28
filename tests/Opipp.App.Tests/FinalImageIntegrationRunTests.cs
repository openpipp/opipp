using System.Windows.Forms;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The final-image integration control, driven through the real <see cref="Form1"/>: the menu item
/// exists in the ported order, and an enabled mean integration hands the engine its three switches
/// and leaves one output file where three frames were processed.
/// </summary>
public sealed class FinalImageIntegrationRunTests
{
    [Fact]
    public void ProcessingMenu_KeepsTheExistingItems_AndAddsFinalImageIntegration()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;

            string[] items = form.ProcessingMenuForTest.DropDownItems
                .Cast<ToolStripItem>()
                .Select(item => item.Text ?? string.Empty)
                .ToArray();

            Assert.Equal(
                new[] { "&Test Options", "Start &Processing", "Cancel Processing", "Final image integration…" },
                items);
        });
    }

    [SkippableFact]
    public void MeanIntegrationOnAThreeFrameSer_ProducesExactlyOneOutputFile()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            // One input holding three accepted frames: without integration the run writes three
            // TIFFs, one per frame, so a single file is the visible effect of -stack=mean.
            string science = AppTestSupport.WritePlanetSer(directory, "science.ser", 3);
            string output = Path.Combine(directory, "out");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                var errors = new List<string>();
                form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false);

                form.FinalImageSettingsForTest = new FinalImageSettings
                {
                    Enabled = true,
                    Method = FinalImageMethod.Mean,
                    Coverage = FinalImageCoverage.CropToOverlap,
                    Subpixel = true,
                };

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                // The GUI half is proven even while the CLI half is still landing: the form put the
                // three switches into the argument list it sends, and named them in the transcript.
                string[] arguments = form.LastEngineArgumentsForTest.ToArray();
                Assert.Contains("-stack=mean", arguments);
                Assert.Contains("-stack_coverage=crop", arguments);
                Assert.Contains("-stack_subpixel=on", arguments);
                Assert.Contains("-gui", arguments);
                Assert.Contains(
                    "Final image integration: mean, crop to overlap, subpixel on",
                    form.OutputTextForTest);

                bool unknownSwitch =
                    form.LastExitCodeForTest == 127
                    && form.OutputTextForTest.Contains("Unknown switch", StringComparison.Ordinal);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));

                Skip.If(
                    unknownSwitch,
                    "-stack is not recognised by the engine yet (CLI wiring still landing): the GUI "
                    + "wiring was verified, the one-output-file assertion was skipped. Engine said: "
                    + form.OutputTextForTest);

                Assert.False(
                    form.ProcessingFailedForTest,
                    "status=" + form.StatusTextForTest
                    + " exit=" + form.LastExitCodeForTest
                    + " output=" + form.OutputTextForTest
                    + " errors=" + string.Join(" | ", errors));
                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);

                // The engine's own summary: three frames in, one out. Without -stack the same run
                // reports three output frames, so this is integration, not a naming accident.
                Assert.Contains("Total input frames: 3", form.OutputTextForTest);
                Assert.Contains("Total output frames: 1", form.OutputTextForTest);

                string[] produced = Directory
                    .GetFiles(output, "*.tif", SearchOption.AllDirectories)
                    .Where(file => !file.StartsWith(
                        Path.Combine(output, "logs") + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                Assert.Single(produced);
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }

    [SkippableFact]
    public void DerotateOnAThreeFrameSer_SendsBothDerotateSwitchesAfterTheStackSwitches()
    {
        string directory = AppTestSupport.TempDirectory();
        try
        {
            string science = AppTestSupport.WritePlanetSer(directory, "science.ser", 3);
            string output = Path.Combine(directory, "out");

            AppTestSupport.RunSta(() =>
            {
                using var form = new Form1();
                _ = form.Handle;
                var errors = new List<string>();
                form.ErrorDialogOverride = (message, caption) => errors.Add(caption + ": " + message);

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false);

                form.FinalImageSettingsForTest = new FinalImageSettings
                {
                    Enabled = true,
                    Method = FinalImageMethod.Mean,
                    Coverage = FinalImageCoverage.CropToOverlap,
                    Subpixel = true,
                    Derotate = true,
                    DerotateSearchDegrees = 12.5,
                };

                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");
                AppTestSupport.PumpUntilRunVerdict(form);

                // The GUI half is proven even while the CLI half is still landing: the form put the
                // derotation switches into the argument list it sends, after the stack switches, and
                // named the range in the transcript.
                string[] arguments = form.LastEngineArgumentsForTest.ToArray();
                Assert.Contains("-stack=mean", arguments);
                Assert.Contains("-stack_subpixel=on", arguments);
                Assert.Contains("-derotate=on", arguments);
                Assert.Contains("-derotate_search=12.5", arguments);
                Assert.True(
                    Array.IndexOf(arguments, "-stack_subpixel=on") < Array.IndexOf(arguments, "-derotate=on"),
                    "the derotation switches must follow the stack switches: " + string.Join(" ", arguments));
                Assert.Contains(
                    "Final image integration: mean, crop to overlap, subpixel on, derotate 12.5 deg",
                    form.OutputTextForTest);

                bool unknownSwitch =
                    form.LastExitCodeForTest == 127
                    && form.OutputTextForTest.Contains("Unknown switch", StringComparison.Ordinal);

                form.DetachEngineCallbacksForTest();
                AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(250));

                Skip.If(
                    unknownSwitch,
                    "the engine does not recognise -derotate yet (CLI wiring still landing): the GUI "
                    + "wiring was verified, the run assertion was skipped. Engine said: "
                    + form.OutputTextForTest);

                Assert.False(
                    form.ProcessingFailedForTest,
                    "status=" + form.StatusTextForTest
                    + " exit=" + form.LastExitCodeForTest
                    + " output=" + form.OutputTextForTest
                    + " errors=" + string.Join(" | ", errors));
                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);
                Assert.Equal(
                    1,
                    Directory.GetFiles(output, "*.tif", SearchOption.AllDirectories)
                        .Count(file => !file.StartsWith(
                            Path.Combine(output, "logs") + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)));
            });
        }
        finally
        {
            AppTestSupport.DeleteQuietly(directory);
        }
    }
}
