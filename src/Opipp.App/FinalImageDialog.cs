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
    private readonly CheckBox _derotateBox;
    private readonly NumericUpDown _derotateSearch;
    private readonly Label _note;

    /// <summary>Creates the dialog showing the default choice (mean, crop, subpixel on, derotate off).</summary>
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
        ClientSize = new Size(442, 346);
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

        _derotateBox = new CheckBox
        {
            Text = "Correct field rotation (derotate)",
            Location = new Point(16, 132),
            Size = new Size(246, 20),
            Checked = shown.Derotate,
        };

        var derotateSearchLabel = new Label
        {
            Text = "Search (deg):",
            Location = new Point(268, 136),
            Size = new Size(82, 18),
        };
        _derotateSearch = new NumericUpDown
        {
            DecimalPlaces = 1,
            Minimum = (decimal)FinalImageSettings.MinDerotateSearchDegrees,
            Maximum = (decimal)FinalImageSettings.MaxDerotateSearchDegrees,
            Increment = 0.5m,
            Location = new Point(352, 132),
            Size = new Size(74, 22),
        };
        // A stored value outside the engine's range (a hand-edited profile, say) is clamped rather
        // than allowed to throw while the dialog opens; ToSwitches still refuses the file's value.
        _derotateSearch.Value = (decimal)Math.Clamp(
            shown.DerotateSearchDegrees,
            FinalImageSettings.MinDerotateSearchDegrees,
            FinalImageSettings.MaxDerotateSearchDegrees);

        _note = new Label
        {
            Text = "Frames are accumulated in double precision and one output file is produced "
                 + "for each input file. Coverage decides whether the border only the first frame "
                 + "covers is kept.\r\n\r\n"
                 + "Derotation estimates a rotation angle per frame, rotates each frame about the "
                 + "image centre, and crops every frame to one rectangle that all the rotations "
                 + "fill, so the result is a little smaller. It requires final-image integration; "
                 + "ordinary conversion is unaffected.",
            Location = new Point(16, 162),
            Size = new Size(410, 130),
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(252, 302),
            Size = new Size(84, 26),
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(344, 302),
            Size = new Size(84, 26),
        };

        Controls.AddRange(new Control[]
        {
            _enableBox, methodLabel, _methodBox, coverageLabel, _coverageBox, _subpixelBox,
            _derotateBox, derotateSearchLabel, _derotateSearch, _note,
            okButton, cancelButton,
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;

        _enableBox.CheckedChanged += (_, _) => UpdateEnabledState();
        _derotateBox.CheckedChanged += (_, _) => UpdateEnabledState();
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
        Derotate = _derotateBox.Checked,
        DerotateSearchDegrees = (double)_derotateSearch.Value,
    };

    /// <summary>
    /// Enables the dependent controls only while integration is requested, and the search range only
    /// while derotation is requested too: the range means nothing without it.
    /// </summary>
    private void UpdateEnabledState()
    {
        bool enabled = _enableBox.Checked;
        _methodBox.Enabled = enabled;
        _coverageBox.Enabled = enabled;
        _subpixelBox.Enabled = enabled;
        _derotateBox.Enabled = enabled;
        _derotateSearch.Enabled = enabled && _derotateBox.Checked;
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

    /// <summary>The derotation check box.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal CheckBox DerotateCheckBoxForTest => _derotateBox;

    /// <summary>The derotation search-range spinner.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal NumericUpDown DerotateSearchNumericForTest => _derotateSearch;

    /// <summary>The explanatory note shown above the buttons.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string NoteTextForTest => _note.Text;

    /// <summary>Selects a complete choice through the controls, as an operator would.</summary>
    internal void ChooseForTest(
        bool enabled,
        FinalImageMethod method,
        FinalImageCoverage coverage,
        bool subpixel,
        bool derotate = false,
        double derotateSearchDegrees = FinalImageSettings.DefaultDerotateSearchDegrees)
    {
        _enableBox.Checked = enabled;
        _methodBox.SelectedIndex = (int)method;
        _coverageBox.SelectedIndex = (int)coverage;
        _subpixelBox.Checked = subpixel;
        _derotateBox.Checked = derotate;
        _derotateSearch.Value = (decimal)Math.Clamp(
            derotateSearchDegrees,
            FinalImageSettings.MinDerotateSearchDegrees,
            FinalImageSettings.MaxDerotateSearchDegrees);
    }
}
