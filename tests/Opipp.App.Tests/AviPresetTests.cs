using Opipp.Core.Formats;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The AVI presets, driven through the real form and the real engine.
/// </summary>
/// <remarks>
/// "Planetary Animation (AVI)" asked for FMP4 (MPEG-4) and "AVI Archive" for ULRG (Ut Video). The
/// managed writer can only produce uncompressed DIB/Y800, so both presets failed at run time. Every
/// enabled preset must produce a configuration the engine implements, and the storage cost of
/// uncompressed output must be visible in the UI.
/// </remarks>
public sealed class AviPresetTests
{
    /// <summary>Codecs the managed engine can write. AVI_FILE: only DIB/Y800 is implementable.</summary>
    private static readonly string[] SupportedFourCC = ["BI_RGB", "DIB ", "Y800"];

    private static void AssertPresetIsSupported(Form1 form, string preset)
    {
        Assert.True(form.AviOutputSelectedForTest, preset + " did not select AVI output");
        Assert.Equal(0, form.AviCodecIndexForTest);
        Assert.Contains("Uncompressed", form.AviStorageTextForTest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryCodecChoiceCanBeWrittenByTheEngine()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;

            Assert.True(form.AviCodecChoiceCountForTest >= 1);
            for (int i = 0; i < form.AviCodecChoiceCountForTest; i++)
            {
                string text = form.AviCodecChoiceTextForTest(i);
                Assert.Contains("DIB", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ULRG", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("FMP4", text, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Fact]
    public void PlanetaryAnimationPreset_SelectsUncompressedOutput()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;

            form.SelectPlanetaryAnimationAviPresetForTest();

            AssertPresetIsSupported(form, "Planetary Animation (AVI)");
        });
    }

    [Fact]
    public void AviArchivePreset_SelectsUncompressedOutput()
    {
        AppTestSupport.RunSta(() =>
        {
            using var form = new Form1();
            _ = form.Handle;

            form.SelectAviArchivePresetForTest();

            AssertPresetIsSupported(form, "AVI Archive");
            Assert.Equal("_dib", form.OutputSuffixForTest);
        });
    }

    [Fact]
    public void AviArchivePreset_WritesAReadableUncompressedAvi()
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
                string science = AppTestSupport.WritePlanetSer(directory, "capture.ser");
                string output = Path.Combine(directory, "out");

                form.SelectAviArchivePresetForTest();
                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false,
                    aviOutput: true);

                Assert.Equal(0, form.AviCodecIndexForTest);
                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the AVI archive run to finish");

                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);
                Assert.False(form.ProcessingFailedForTest, string.Join(" | ", errors));

                string[] avi = Directory.GetFiles(output, "*.avi", SearchOption.AllDirectories);
                Assert.Single(avi);
                AviInfo info = AviProbe.GetInfo(avi[0]);
                Assert.True(info.FrameCount > 0, "the AVI has no frames");
                Assert.Contains(info.FourCC, SupportedFourCC);

                using var reader = new AviReader(avi[0]);
                Assert.True(reader.FrameCount > 0);
                Assert.NotNull(reader.ReadFrame());

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
    public void PlanetaryAnimationPreset_WritesAReadableUncompressedAvi()
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
                string science = AppTestSupport.WritePlanetSer(directory, "capture.ser");
                string output = Path.Combine(directory, "out");

                form.SelectPlanetaryAnimationAviPresetForTest();
                // The preset enables planet detection; the synthetic frames hold a 10x10 object.
                form.SetMinimumPlanetSizeForTest(5m);
                form.PrepareRunForTest(
                    [science],
                    outputDirectory: output,
                    flatCalibration: false,
                    flatDarkSubtraction: false,
                    darkSubtraction: false,
                    aviOutput: true);

                Assert.Equal(0, form.AviCodecIndexForTest);
                form.StartProcessingForTest();
                Assert.True(form.ProcessingRunningForTest, "the run did not start: " + form.OutputTextForTest);
                AppTestSupport.PumpUntil(() => !form.ProcessingRunningForTest, "the animation run to finish");

                Assert.Equal(0, form.LastExitCodeForTest);
                Assert.Equal("COMPLETE", form.StatusTextForTest);
                Assert.False(form.ProcessingFailedForTest, string.Join(" | ", errors));

                string[] avi = Directory.GetFiles(output, "*.avi", SearchOption.AllDirectories);
                Assert.Single(avi);
                AviInfo info = AviProbe.GetInfo(avi[0]);
                Assert.True(info.FrameCount > 0, "the animated AVI has no frames");
                Assert.Contains(info.FourCC, SupportedFourCC);

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
