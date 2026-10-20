#nullable enable

using System;
using System.Drawing;
using System.Windows.Forms;

namespace PIPP_Application;

/// <summary>
/// Modal editor for <see cref="FinalImageSettings"/>, built in code rather than from a designer
/// file so it can be constructed on any STA thread (including a test thread) without touching
/// <see cref="Form1"/> or its resources.
/// </summary>
public sealed class FinalImageDialog : Form
{
    private readonly CheckBox _enableBox;
    private readonly ComboBox _methodBox;
    private readonly ComboBox _coverageBox;
    private readonly CheckBox _subpixelBox;
    private readonly Label _note;

    /// <summary>Creates the dialog showing the default choice (mean, crop, subpixel on).</summary>
    public FinalImageDialog()
        : this(new FinalImageSettings())
    {
    }

    /// <summary>Creates the dialog showing <paramref name="settings"/>.</summary>
    public FinalImageDialog(FinalImageSettings settings)
    {
        FinalImageSettings shown = settings ?? new FinalImageSettings();

        Text = "Final image integration";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(442, 250);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;

        _enableBox = new CheckBox
        {
            Text = "Combine each input's selected frames into one final image",
            Location = new Point(16, 14),
            Size = new Size(404, 20),
            Checked = shown.Enabled,
        };

        var methodLabel = new Label { Text = "Method:", Location = new Point(16, 46), Size = new Size(90, 18) };
        _methodBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 42),
            Size = new Size(150, 22),
        };
        foreach (FinalImageMethod method in new[]
                 {
                     FinalImageMethod.Mean, FinalImageMethod.Sum, FinalImageMethod.Median,
                 })
        {
            _methodBox.Items.Add(FinalImageSettings.MethodLabel(method));
        }

        var coverageLabel = new Label { Text = "Coverage:", Location = new Point(16, 76), Size = new Size(90, 18) };
        _coverageBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 72),
            Size = new Size(210, 22),
        };
        foreach (FinalImageCoverage coverage in new[]
                 {
                     FinalImageCoverage.CropToOverlap, FinalImageCoverage.KeepCanvas,
                 })
        {
            _coverageBox.Items.Add(FinalImageSettings.CoverageChoiceLabel(coverage));
        }

        _subpixelBox = new CheckBox
        {
            Text = "Align at subpixel precision",
            Location = new Point(16, 106),
            Size = new Size(300, 20),
            Checked = shown.Subpixel,
        };

        _note = new Label
        {
            Text = "Frames are accumulated in double precision and one output file is produced "
                 + "for each input file. Coverage decides whether the border only the first frame "
                 + "covers is kept.",
            Location = new Point(16, 136),
            Size = new Size(410, 56),
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(252, 206),
            Size = new Size(84, 26),
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(344, 206),
            Size = new Size(84, 26),
        };

        Controls.AddRange(new Control[]
        {
            _enableBox, methodLabel, _methodBox, coverageLabel, _coverageBox, _subpixelBox, _note,
            okButton, cancelButton,
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;

        _enableBox.CheckedChanged += (_, _) => UpdateEnabledState();
        UpdateEnabledState();

        // Restore the shown choice. The combo indices are the enum values' declaration order, which
        // the lists above build explicitly, so an out-of-range value cannot select a wrong method.
        _methodBox.SelectedIndex = (int)shown.Method;
        _coverageBox.SelectedIndex = (int)shown.Coverage;
    }

    /// <summary>
    /// The choice the dialog currently shows, as a fresh settings object. Only meaningful after
    /// <see cref="DialogResult.OK"/> in the real UI; tests read it at any time.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public FinalImageSettings Settings => new()
    {
        Enabled = _enableBox.Checked,
        Method = (FinalImageMethod)_methodBox.SelectedIndex,
        Coverage = (FinalImageCoverage)_coverageBox.SelectedIndex,
        Subpixel = _subpixelBox.Checked,
    };

    /// <summary>Enables the dependent controls only while integration is requested.</summary>
    private void UpdateEnabledState()
    {
        bool enabled = _enableBox.Checked;
        _methodBox.Enabled = enabled;
        _coverageBox.Enabled = enabled;
        _subpixelBox.Enabled = enabled;
    }

    /// <summary>The enable checkbox (tests drive the dialog through the real controls).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal CheckBox EnableCheckBoxForTest => _enableBox;

    /// <summary>The method combo box.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ComboBox MethodComboBoxForTest => _methodBox;

    /// <summary>The coverage combo box.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ComboBox CoverageComboBoxForTest => _coverageBox;

    /// <summary>The subpixel check box.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal CheckBox SubpixelCheckBoxForTest => _subpixelBox;

    /// <summary>The explanatory note shown above the buttons.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string NoteTextForTest => _note.Text;

    /// <summary>Selects a complete choice through the controls, as an operator would.</summary>
    internal void ChooseForTest(bool enabled, FinalImageMethod method, FinalImageCoverage coverage, bool subpixel)
    {
        _enableBox.Checked = enabled;
        _methodBox.SelectedIndex = (int)method;
        _coverageBox.SelectedIndex = (int)coverage;
        _subpixelBox.Checked = subpixel;
    }
}
