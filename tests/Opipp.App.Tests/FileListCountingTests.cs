using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The GUI's calibration master counting, driven through the real file list and the real form.
/// </summary>
/// <remarks>
/// A FITS file is a container: its primary image and every image extension are separate frames.
/// The file list used to add one frame per file, and the handoff plan then used that figure as an
/// enforced build limit — a master built from two two-image files stopped after the first file's
/// two frames and the science was silently calibrated with the wrong master.
/// </remarks>
public sealed class FileListCountingTests
{
    /// <summary>Runs the prepared form to its end state and returns when it is idle again.</summary>
    private static void RunToCompletion(Form1 form)
    {
        Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
        AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the run to finish");
        form.DetachEngineCallbacksForTest();
        AppTestSupport.PumpFor(TimeSpan.FromMilliseconds(150));
    }

    [Theory]
    [InlineData(".fits")]
    [InlineData(".fit")]
    [InlineData(".fts")]
    public void MultiImageFitsDark_IsCountedPerImage_AndBothFilesReachTheMaster(string extension)
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
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 100);
                // Two files of two images each: values 0,0 and 40,40. The four-frame master is 20,
                // so the science must come out at 80. Counting one frame per file capped the build
                // at two frames taken from the first file, which produced a 0 master and 100.
                string darkA = AppTestSupport.WriteSplitFits(
                    directory, "dark_a" + extension, (0, 0), (0, 0));
                string darkB = AppTestSupport.WriteSplitFits(
                    directory, "dark_b" + extension, (40, 40), (40, 40));
                string output = Path.Combine(directory, "out");

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    darkFiles: [darkA, darkB],
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: true);

                Assert.Equal(4, form.DarkListFramesForTest);

                form.StartProcessingForTest();
                RunToCompletion(form);

                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);
                Assert.False(form.ProcessingFailedForTest, string.Join(" | ", errors));

                var calibrated = AppTestSupport.ReadSingleScienceTiff(output);
                Assert.Equal(80, calibrated[0, 0]);
                Assert.Equal(80, calibrated[7, 0]);
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
    public void MultiImageFitsFlat_BothFilesReachTheFlatMaster()
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
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 60);
                // Four flat images: left halves 40, right halves 40,40,120,120. The master is
                // left=40, right=80 with mean 60, so a 60 science comes out at 90 / 45.
                // Using the first file only (40 everywhere, mean 40) would leave 60 / 60.
                string flatA = AppTestSupport.WriteSplitFits(
                    directory, "flat_a.fits", (40, 40), (40, 40));
                string flatB = AppTestSupport.WriteSplitFits(
                    directory, "flat_b.fits", (40, 120), (40, 120));
                string output = Path.Combine(directory, "out");

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatFiles: [flatA, flatB],
                    flatCalibration: true,
                    flatDarkSubtraction: false,
                    darkSubtraction: false);

                Assert.Equal(4, form.FlatListFramesForTest);

                form.StartProcessingForTest();
                RunToCompletion(form);

                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.False(form.ProcessingFailedForTest, string.Join(" | ", errors));

                var calibrated = AppTestSupport.ReadSingleScienceTiff(output);
                Assert.Equal(90, calibrated[0, 0]);
                Assert.Equal(45, calibrated[7, 0]);
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
    public void MultiImageFitsFlatDark_BothFilesReachTheMaster_AndTheSavedMasterProvesIt()
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
                string science = AppTestSupport.WriteUniformSer(directory, "science.ser", 90);
                string flat = AppTestSupport.WriteUniformSer(directory, "flat.ser", 100);
                // Flat darks: left 0 everywhere, right 0,0,40,40 -> master left 0, right 20.
                string flatDarkA = AppTestSupport.WriteSplitFits(
                    directory, "flat_dark_a.fits", (0, 0), (0, 0));
                string flatDarkB = AppTestSupport.WriteSplitFits(
                    directory, "flat_dark_b.fits", (0, 40), (0, 40));
                string output = Path.Combine(directory, "out");

                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatFiles: [flat],
                    flatDarkFiles: [flatDarkA, flatDarkB],
                    flatCalibration: true,
                    flatDarkSubtraction: true,
                    darkSubtraction: false,
                    saveFlatDark: true);

                Assert.Equal(4, form.FlatDarkListFramesForTest);

                form.StartProcessingForTest();
                RunToCompletion(form);

                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.False(form.ProcessingFailedForTest, string.Join(" | ", errors));

                // The saved master is the average of all four images; the first file alone is 0.
                string saved = Path.Combine(output, "logs", "pipp_master_flat_dark.tif");
                Assert.True(File.Exists(saved), "the master flat dark was not saved");
                using (var reader = new Opipp.Core.Formats.TiffReader(saved))
                {
                    var master = reader.ReadFrame() ?? throw new InvalidOperationException("the saved master has no frame");
                    Assert.Equal(0, master[0, 0]);
                    Assert.Equal(20, master[7, 0]);
                }

                // Corrected flat: left 100 (mean 90) -> 90*90/100 = 81; right 80 -> 90*90/80 = 101.
                var calibrated = AppTestSupport.ReadSingleScienceTiff(output);
                Assert.Equal(81, calibrated[0, 0]);
                Assert.Equal(101, calibrated[7, 0]);
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
    public void FullBuild_HasNoFrameLimit_WhileATestRunKeepsItsPreviewCap()
    {
        var request = new MasterHandoffRequest
        {
            TestRun = false,
            Dark = new MasterStageRequest(MasterKind.Dark)
            {
                Enabled = true,
                Files = ["dark.fits"],
                FrameCount = 2,
                MasterPath = "pipp_master_dark.tmp",
            },
        };

        MasterHandoffPlan full = MasterHandoffPlanner.Create(request);
        Assert.Equal(0, full.Dark.FrameLimit);
        Assert.Equal(2, full.Dark.ExpectedFrames);

        request.TestRun = true;
        MasterHandoffPlan preview = MasterHandoffPlanner.Create(request);
        Assert.Equal(2, preview.Dark.FrameLimit);
    }
}
