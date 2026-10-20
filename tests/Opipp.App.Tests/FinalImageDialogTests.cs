using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The modal editor is constructed for real on an STA thread: it must show the stored choice,
/// expose the same choice back, and never create <see cref="Form1"/> or its designer resources.
/// </summary>
public sealed class FinalImageDialogTests
{
    [Fact]
    public void Defaults_ShowMeanCropSubpixelOn_AndCaptureDisabled()
    {
        AppTestSupport.RunSta(() =>
        {
            using var dialog = new FinalImageDialog();
            _ = dialog.Handle;

            Assert.False(dialog.EnableCheckBoxForTest.Checked);
            Assert.Equal("Mean", dialog.MethodComboBoxForTest.SelectedItem);
            Assert.Equal("Crop to overlap", dialog.CoverageComboBoxForTest.SelectedItem);
            Assert.True(dialog.SubpixelCheckBoxForTest.Checked);

            Assert.Equal(
                new object[] { "Mean", "Sum", "Median" },
                dialog.MethodComboBoxForTest.Items.Cast<object>());
            Assert.Equal(
                new object[] { "Crop to overlap", "Keep first canvas" },
                dialog.CoverageComboBoxForTest.Items.Cast<object>());

            // Disabled means nothing to configure, so the dependent controls are not editable.
            Assert.False(dialog.MethodComboBoxForTest.Enabled);
            Assert.False(dialog.CoverageComboBoxForTest.Enabled);
            Assert.False(dialog.SubpixelCheckBoxForTest.Enabled);

            FinalImageSettings captured = dialog.Settings;
            Assert.False(captured.Enabled);
            Assert.Equal(FinalImageMethod.Mean, captured.Method);
            Assert.Equal(FinalImageCoverage.CropToOverlap, captured.Coverage);
            Assert.True(captured.Subpixel);
            Assert.Empty(captured.ToSwitches());

            Assert.Contains("double precision", dialog.NoteTextForTest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("one output file", dialog.NoteTextForTest, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void RoundTripsAStoredChoice_AndReturnsWhatTheOperatorSelected()
    {
        AppTestSupport.RunSta(() =>
        {
            var stored = new FinalImageSettings
            {
                Enabled = true,
                Method = FinalImageMethod.Median,
                Coverage = FinalImageCoverage.KeepCanvas,
                Subpixel = false,
            };

            using var dialog = new FinalImageDialog(stored);
            _ = dialog.Handle;

            Assert.True(dialog.EnableCheckBoxForTest.Checked);
            Assert.Equal("Median", dialog.MethodComboBoxForTest.SelectedItem);
            Assert.Equal("Keep first canvas", dialog.CoverageComboBoxForTest.SelectedItem);
            Assert.False(dialog.SubpixelCheckBoxForTest.Checked);
            Assert.True(dialog.MethodComboBoxForTest.Enabled);
            Assert.True(dialog.CoverageComboBoxForTest.Enabled);
            Assert.True(dialog.SubpixelCheckBoxForTest.Enabled);

            FinalImageSettings captured = dialog.Settings;
            Assert.Equal(stored.Enabled, captured.Enabled);
            Assert.Equal(stored.Method, captured.Method);
            Assert.Equal(stored.Coverage, captured.Coverage);
            Assert.Equal(stored.Subpixel, captured.Subpixel);
            Assert.Equal(stored.ToSwitches(), captured.ToSwitches());

            // A different choice through the controls produces that choice, not the stored one.
            dialog.ChooseForTest(enabled: true, FinalImageMethod.Sum, FinalImageCoverage.CropToOverlap, subpixel: true);

            FinalImageSettings changed = dialog.Settings;
            Assert.Equal(FinalImageMethod.Sum, changed.Method);
            Assert.Equal(FinalImageCoverage.CropToOverlap, changed.Coverage);
            Assert.True(changed.Subpixel);
            Assert.Equal(
                new[] { "-stack=sum", "-stack_coverage=crop", "-stack_subpixel=on" },
                changed.ToSwitches());
        });
    }

    [Fact]
    public void UncheckingEnable_DisablesTheDependentControlsAgain()
    {
        AppTestSupport.RunSta(() =>
        {
            var stored = new FinalImageSettings { Enabled = true };
            using var dialog = new FinalImageDialog(stored);
            _ = dialog.Handle;

            dialog.EnableCheckBoxForTest.Checked = false;

            Assert.False(dialog.MethodComboBoxForTest.Enabled);
            Assert.False(dialog.CoverageComboBoxForTest.Enabled);
            Assert.False(dialog.SubpixelCheckBoxForTest.Enabled);
            Assert.Empty(dialog.Settings.ToSwitches());
        });
    }
}
