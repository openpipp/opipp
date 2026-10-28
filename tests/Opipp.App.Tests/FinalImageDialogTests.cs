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
            Assert.False(dialog.DerotateCheckBoxForTest.Enabled);
            Assert.False(dialog.DerotateSearchNumericForTest.Enabled);

            // Derotation is off and its range sits at the engine default, one decimal, 0.1..45.
            Assert.False(dialog.DerotateCheckBoxForTest.Checked);
            Assert.Equal(5.0m, dialog.DerotateSearchNumericForTest.Value);
            Assert.Equal(1, dialog.DerotateSearchNumericForTest.DecimalPlaces);
            Assert.Equal(0.1m, dialog.DerotateSearchNumericForTest.Minimum);
            Assert.Equal(45m, dialog.DerotateSearchNumericForTest.Maximum);

            FinalImageSettings captured = dialog.Settings;
            Assert.False(captured.Enabled);
            Assert.Equal(FinalImageMethod.Mean, captured.Method);
            Assert.Equal(FinalImageCoverage.CropToOverlap, captured.Coverage);
            Assert.True(captured.Subpixel);
            Assert.False(captured.Derotate);
            Assert.Equal(5.0, captured.DerotateSearchDegrees);
            Assert.Empty(captured.ToSwitches());

            Assert.Contains("double precision", dialog.NoteTextForTest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("one output file", dialog.NoteTextForTest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Correct field rotation (derotate)", dialog.DerotateCheckBoxForTest.Text);
        });
    }

    [Fact]
    public void TheNote_ExplainsWhatDerotationDoes_AndThatItNeedsIntegration()
    {
        AppTestSupport.RunSta(() =>
        {
            using var dialog = new FinalImageDialog();
            _ = dialog.Handle;

            string note = dialog.NoteTextForTest;

            Assert.Contains("per frame", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("image centre", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rectangle", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rotations fill", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("requires final-image integration", note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ordinary conversion is unaffected", note, StringComparison.OrdinalIgnoreCase);
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
            Assert.False(dialog.DerotateCheckBoxForTest.Enabled);
            Assert.False(dialog.DerotateSearchNumericForTest.Enabled);
            Assert.Empty(dialog.Settings.ToSwitches());
        });
    }

    [Fact]
    public void Derotation_RoundTripsThroughTheControls_AndDrivesTheSearchRangeEnablement()
    {
        AppTestSupport.RunSta(() =>
        {
            var stored = new FinalImageSettings
            {
                Enabled = true,
                Method = FinalImageMethod.Mean,
                Coverage = FinalImageCoverage.CropToOverlap,
                Subpixel = true,
                Derotate = true,
                DerotateSearchDegrees = 12.5,
            };

            using var dialog = new FinalImageDialog(stored);
            _ = dialog.Handle;

            Assert.True(dialog.DerotateCheckBoxForTest.Checked);
            Assert.Equal(12.5m, dialog.DerotateSearchNumericForTest.Value);
            Assert.True(dialog.DerotateCheckBoxForTest.Enabled);
            Assert.True(dialog.DerotateSearchNumericForTest.Enabled);

            FinalImageSettings captured = dialog.Settings;
            Assert.True(captured.Derotate);
            Assert.Equal(12.5, captured.DerotateSearchDegrees);
            Assert.Equal(stored.ToSwitches(), captured.ToSwitches());
            Assert.Contains("-derotate=on", captured.ToSwitches());
            Assert.Contains("-derotate_search=12.5", captured.ToSwitches());

            // The range spinner only means something while derotation is ticked.
            dialog.DerotateCheckBoxForTest.Checked = false;

            Assert.False(dialog.DerotateSearchNumericForTest.Enabled);
            Assert.False(dialog.Settings.Derotate);
            Assert.DoesNotContain("-derotate=on", dialog.Settings.ToSwitches());

            dialog.DerotateCheckBoxForTest.Checked = true;
            Assert.True(dialog.DerotateSearchNumericForTest.Enabled);

            // A different range through the control produces that range, not the stored one.
            dialog.ChooseForTest(
                enabled: true,
                FinalImageMethod.Mean,
                FinalImageCoverage.CropToOverlap,
                subpixel: true,
                derotate: true,
                derotateSearchDegrees: 2.5);

            FinalImageSettings changed = dialog.Settings;
            Assert.True(changed.Derotate);
            Assert.Equal(2.5, changed.DerotateSearchDegrees);
            Assert.Equal(
                new[]
                {
                    "-stack=mean", "-stack_coverage=crop", "-stack_subpixel=on",
                    "-derotate=on", "-derotate_search=2.5",
                },
                changed.ToSwitches());
        });
    }

    [Fact]
    public void AStoredSearchOutsideTheRange_IsClampedInTheControl_NotThrownWhileOpening()
    {
        AppTestSupport.RunSta(() =>
        {
            var stored = new FinalImageSettings
            {
                Enabled = true,
                Derotate = true,
                DerotateSearchDegrees = 90.0,
            };

            using var dialog = new FinalImageDialog(stored);
            _ = dialog.Handle;

            Assert.Equal(45m, dialog.DerotateSearchNumericForTest.Value);
            Assert.Equal(45.0, dialog.Settings.DerotateSearchDegrees);
        });
    }
}
