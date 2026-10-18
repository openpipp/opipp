using System.Globalization;
using Pipp.Core.Pipeline;

namespace Pipp.Core.Diagnostics;

/// <summary>
/// Reproduces the exact stdout transcript <c>pipp_core.exe</c> writes in <c>-gui</c> mode.
/// </summary>
/// <remarks>
/// The original interleaves <c>@TOKEN=value</c> lines with human-readable text; the WinForms
/// front-end parses the tokens and shows the text in its log pane. Every line is terminated with
/// CRLF and every number is formatted with the invariant culture, matching the native engine
/// byte-for-byte (verified against <c>bin/pipp_core.exe</c> for single-file, quality-selection,
/// centring, animated-AVI, multi-file and <c>-join</c> runs).
/// </remarks>
public sealed class GuiTranscript
{
    private readonly TextWriter _out;

    public GuiTranscript(TextWriter output) =>
        _out = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>Writes the fixed five-token preamble (<c>@PFILE=0</c> ... <c>@QSORT=0</c>).</summary>
    public void Begin(int fileCount)
    {
        Token("PFILE", 0);
        Line(string.Create(CultureInfo.InvariantCulture, $"@PFILELAB=File 0 of {fileCount}"));
        Line("@FNAMELAB=none");
        Line("@ANIM=0");
        Line("@QSORT=0");
    }

    /// <summary>
    /// Writes one batch-mode input section plus the "Processing N frames" header and the fixed
    /// token block that precedes the first frame.
    /// </summary>
    /// <param name="fileIndex">1-based index of this input in the run.</param>
    /// <param name="fileCount">Total number of inputs.</param>
    /// <param name="path">Input path exactly as it appeared on the command line.</param>
    /// <param name="totalFrames">Frame count reported by "Estimating input frames".</param>
    /// <param name="processFrames">Frames this run will process (the frame window's limit).</param>
    public void BeginBatchFile(int fileIndex, int fileCount, string path, int totalFrames, int processFrames)
    {
        Line("Processing: (Batch mode)");
        Line("\r  Input file:");
        Line("    " + path);
        Line("  Estimating input frames:");
        Line(string.Create(CultureInfo.InvariantCulture, $"    Total input frames: {totalFrames}"));
        Line("@FNAMELAB=" + Path.GetFileName(path));
        Token("PFILE", filePercent(fileIndex, fileCount));
        Line(string.Create(CultureInfo.InvariantCulture, $"@PFILELAB=File {fileIndex} of {fileCount}"));
        Line(string.Create(CultureInfo.InvariantCulture, $"  Processing {processFrames} frames:"));
        StartProcessing(processFrames);
    }

    /// <summary>Writes the join-mode header: every input listed, one summed estimate.</summary>
    public void BeginJoin(IReadOnlyList<string> paths, int totalFrames)
    {
        Line("Processing: (Join mode)");
        Line("  Input files:");
        foreach (string path in paths) Line("    " + path);
        Line("  Estimating input frames:");
        Line(string.Create(CultureInfo.InvariantCulture, $"    Total input frames: {totalFrames}"));
        Line("@FNAMELAB=" + (paths.Count > 0 ? Path.GetFileName(paths[0]) : string.Empty));
        Token("PFILE", filePercent(1, paths.Count));
        Line(string.Create(CultureInfo.InvariantCulture, $"@PFILELAB=File 1 of {paths.Count}"));
        Line(string.Create(CultureInfo.InvariantCulture, $"  Processing {totalFrames} frames:"));
        StartProcessing(totalFrames);
    }

    private void StartProcessing(int frameCount)
    {
        Token("PROGRESS", 0);
        Token("PFRAMELAB", frameCount);
        Token("QSFRAMESLAB", 0);
        Token("QSORT", 0);
        Token("ANIM", 0);
    }

    /// <summary>
    /// Writes one frame-progress token. The engine reports at the <em>start</em> of each frame,
    /// i.e. 0% for the first of N and (N-1)*100/N for the last, followed by a blank line.
    /// </summary>
    /// <param name="frameIndex">1-based frame index reported by the pipeline.</param>
    /// <param name="frameCount">Frame-window limit.</param>
    public void Frame(int frameIndex, int frameCount)
    {
        Token("PROGRESS", (int)(100.0 * (frameIndex - 1) / Math.Max(1, frameCount)));
        Line();
    }

    /// <summary>Writes the RAM-buffer notice quality selection prints before the 25% frame.</summary>
    public void BufferNotice(int buffers) =>
        Line(string.Create(CultureInfo.InvariantCulture,
            $"    Using {buffers} RAM buffers for quality selection"));

    /// <summary>
    /// Writes the name/progress update the joined stream emits when it crosses into the next input.
    /// </summary>
    public void FileTransition(int fileIndex, int fileCount, string path)
    {
        Line("@FNAMELAB=" + Path.GetFileName(path));
        Token("PFILE", filePercent(fileIndex, fileCount));
        Line(string.Create(CultureInfo.InvariantCulture, $"@PFILELAB=File {fileIndex} of {fileCount}"));
    }

    /// <summary>Writes the final <c>@PROGRESS=100</c> (no trailing blank line).</summary>
    public void Complete() => Token("PROGRESS", 100);

    /// <summary>Writes the quality-sort pass: label, "Quality processing" and QSORT walk.</summary>
    public void Quality(int reordered)
    {
        Token("QSFRAMESLAB", reordered);
        Line("  Quality processing:");
        int denominator = Math.Max(1, reordered);
        for (int i = 0; i <= reordered; i++)
        {
            Token("QSORT", (int)(100.0 * i / denominator));
        }

        Line(string.Create(CultureInfo.InvariantCulture, $"    Reordered {reordered} images"));
    }

    /// <summary>
    /// Writes the animated-output progress walk (<c>@ANIM</c> per written frame). AVI appends a
    /// final 100 after the last written frame; GIF's walk ends on the 100 of its last image.
    /// </summary>
    public void Animation(int animationFrames, bool trailingHundred)
    {
        if (animationFrames <= 0)
        {
            Token("ANIM", 100);
            return;
        }

        for (int i = 1; i <= animationFrames; i++)
        {
            Token("ANIM", (int)(100.0 * i / animationFrames));
        }

        if (trailingHundred) Token("ANIM", 100);
    }

    /// <summary>Writes the processing-details and summary block for one input (CRLF throughout).</summary>
    public void Summary(
        ProcessingReport report,
        string frameType,
        string format,
        string outputDirectory,
        string version,
        string qualityAlgorithm,
        bool qualityRan,
        int? detectionThreshold,
        bool showDetails)
    {
        if (showDetails && detectionThreshold is int threshold)
        {
            Line("  Processing Details: ");
            Line(string.Create(CultureInfo.InvariantCulture,
                $"    Calculated '-minpixel' value: {threshold}"));
        }

        Line("  Summary:");
        Line("    PIPP version: " + version);
        Line(string.Create(CultureInfo.InvariantCulture, $"    Total input frames: {report.InputFrames}"));
        if (report.DiscardedNoObject > 0)
            Line(string.Create(CultureInfo.InvariantCulture,
                $"    Frames discarded with no planet detected: {report.DiscardedNoObject}"));
        if (report.DiscardedPartialObject > 0)
            Line(string.Create(CultureInfo.InvariantCulture,
                $"    Frames discarded with partial planet detected: {report.DiscardedPartialObject}"));
        if (report.DiscardedTooSmall > 0)
            Line(string.Create(CultureInfo.InvariantCulture,
                $"    Frames discarded with planet diameter below minimum: {report.DiscardedTooSmall}"));
        if (report.DiscardedByQuality > 0 || qualityRan)
            Line(string.Create(CultureInfo.InvariantCulture,
                $"    Frames discarded by quality: {report.DiscardedByQuality}"));
        Line(string.Create(CultureInfo.InvariantCulture, $"    Total output frames: {report.OutputFrames}"));
        if (qualityRan) Line("    Quality Algorithm: " + qualityAlgorithm);
        Line("    Output Frame Type: " + frameType);
        Line("    Output Frame Format: " + format);
        Line("    Output Directory: " + outputDirectory + " ");
        Line(" ");
    }

    private static int filePercent(int fileIndex, int fileCount) =>
        fileCount <= 0 ? 0 : fileIndex * 100 / fileCount;

    private void Token(string name, int value) =>
        Line(string.Create(CultureInfo.InvariantCulture, $"@{name}={value}"));

    private void Line(string text = "")
    {
        _out.Write(text);
        _out.Write("\r\n");
    }
}
