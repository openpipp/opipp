#nullable enable

using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace PIPP_Application;

/// <summary>
/// Members the STA tests use to drive <see cref="Form1"/> without a visible desktop.
/// </summary>
/// <remarks>
/// Kept in a partial file so the ported designer file stays untouched. Everything here is
/// <c>internal</c>, and the only production effect is the three override properties, which are null
/// (so the real behaviour runs) unless a test installs them.
/// </remarks>
public partial class Form1
{
    /// <summary>
    /// Replaces <see cref="MasterFrameBuild"/> when a test needs a build it can cancel or inspect.
    /// Null means the production builder.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal IMasterFrameBuild? MasterBuildOverride { get; set; }

    /// <summary>
    /// Replaces the modal "Processing In Progress. Confirm Quit?" prompt. Null shows the prompt.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool>? QuitConfirmationOverride { get; set; }

    /// <summary>
    /// Receives modal error text instead of a MessageBox when a test installs it. Null shows the
    /// MessageBox.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string, string>? ErrorDialogOverride { get; set; }

    /// <summary>Status label text, e.g. <c>COMPLETE</c>, <c>CANCELLED</c> or <c>FAILED WITH ERRORS</c>.</summary>
    internal string StatusTextForTest => statusstateLAB.Text;

    /// <summary>True when the last run ended in a failure state.</summary>
    internal bool ProcessingFailedForTest => processing_failed;

    /// <summary>True while a run owns the form (set from Start Processing until the exit status is shown).</summary>
    internal bool ProcessingRunningForTest => pipp_process_running;

    /// <summary>True when Start Processing is clickable (the control the cancel defect left disabled).</summary>
    internal bool RunEnabledForTest => runBTN.Enabled;

    /// <summary>Exit code captured from the last engine process, or 0 when none ran.</summary>
    internal int LastExitCodeForTest => last_exit_code;

    /// <summary>The session temp directory that holds the cached masters.</summary>
    internal string TempDirectoryForTest => temp_directory;

    /// <summary>The output directory the last run resolved.</summary>
    internal string OutputDirectoryForTest => output_directory;

    /// <summary>Text of the processing transcript (useful in a failed-start assertion message).</summary>
    internal string OutputTextForTest => outputTB.Text;

    /// <summary>Marks the form as owning a run, for the quit-confirmation path.</summary>
    internal void SetProcessingRunningForTest(bool running) => pipp_process_running = running;

    /// <summary>
    /// Stops the engine's asynchronous readers from marshalling their final notifications into this
    /// form. A test disposes the form as soon as its assertions finish, while the just-exited
    /// engine's stream readers may still deliver a trailing line; detaching first (and then pumping
    /// the loop) keeps that delivery from racing the disposal.
    /// </summary>
    internal void DetachEngineCallbacksForTest()
    {
        pipp_process.OutputDataReceived -= pipp_process_OutputDataReceived;
        pipp_process.ErrorDataReceived -= pipp_process_ErrorDataReceived;
        pipp_process.Exited -= pipp_process_Exited;
        pipp_process.SynchronizingObject = null;
    }

    /// <summary>
    /// Fills the form with a runnable calibration setup the way the file dialogs and checkboxes
    /// would, without opening any UI.
    /// </summary>
    /// <param name="imageFiles">Science frames (real files: the engine reads them).</param>
    /// <param name="outputDirectory">Destination directory; null leaves the default alone.</param>
    /// <param name="flatFiles">Flat frames, if any.</param>
    /// <param name="flatDarkFiles">Flat-dark frames, if any.</param>
    /// <param name="darkFiles">Dark frames, if any.</param>
    /// <param name="flatCalibration">State of "Enable Flat Frame Correction".</param>
    /// <param name="flatDarkSubtraction">State of "Enable Flat Dark Frame Subtraction".</param>
    /// <param name="darkSubtraction">State of "Enable Dark Frame Subtraction".</param>
    /// <param name="saveFlatDark">State of "Save Master Flat Dark Frame".</param>
    internal void PrepareRunForTest(
        string[] imageFiles,
        string? outputDirectory = null,
        string[]? flatFiles = null,
        string[]? flatDarkFiles = null,
        string[]? darkFiles = null,
        bool flatCalibration = true,
        bool flatDarkSubtraction = true,
        bool darkSubtraction = true,
        bool saveFlatDark = false)
    {
        // Adding the first source file makes the form auto-start a test run (see
        // source_filelist_FileListUpdated). A test drives runs itself, so suppress that one-shot
        // transition by making the list look like it already had frames.
        last_source_file_frame_count = -1;
        image_filelist.add_source(imageFiles);
        last_source_file_frame_count = image_filelist.total_frame_count;

        if (flatFiles is { Length: > 0 })
        {
            flat_filelist.add_source(flatFiles);
        }

        if (flatDarkFiles is { Length: > 0 })
        {
            flat_dark_filelist.add_source(flatDarkFiles);
        }

        if (darkFiles is { Length: > 0 })
        {
            dark_filelist.add_source(darkFiles);
        }

        flat_frame_calibrationCB.Checked = flatCalibration;
        flat_dark_frame_subtractionCB.Checked = flatDarkSubtraction;
        dark_frame_subtractionCB.Checked = darkSubtraction;
        save_master_flat_dark_CB.Checked = saveFlatDark;

        // The run needs a concrete output directory and a file name change, or Start Processing
        // refuses to produce files in the source directory.
        outputtifRB.Checked = true;
        create_output_subdirCB.Checked = false;
        outputprefixTB.Text = "";
        outputsuffixTB.Text = "_out";
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            outputdirectoryTB.Text = outputDirectory;
        }
    }

    /// <summary>Starts a run on the calling (UI) thread, exactly as Start Processing does.</summary>
    internal void StartProcessingForTest() => start_processing();

    /// <summary>Presses Cancel, exactly as the Cancel button does.</summary>
    internal void CancelProcessingForTest() => cancel_processing();

    /// <summary>Raises <see cref="Form1_FormClosing"/> without entering the form's close lifecycle.</summary>
    internal void RaiseFormClosingForTest(FormClosingEventArgs e) => Form1_FormClosing(this, e);
}
