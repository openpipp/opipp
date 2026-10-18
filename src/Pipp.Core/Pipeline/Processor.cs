using System.Globalization;
using System.Text;
using Pipp.Core.Alignment;
using Pipp.Core.Diagnostics;
using Pipp.Core.Frames;
using Pipp.Core.Processing;
using Pipp.Core.Quality;
using Pipp.Core.Stacking;

namespace Pipp.Core.Pipeline;

/// <summary>Progress notification raised while a pipeline run advances.</summary>
/// <param name="Percent">Overall completion, 0..100.</param>
/// <param name="File">Current input file, if any.</param>
/// <param name="FrameIndex">Current frame within the file (0-based).</param>
/// <param name="FrameCount">Total frames in the file, or 0 when unknown.</param>
/// <param name="Message">Human-readable status.</param>
public readonly record struct PipelineProgress(int Percent, string? File, int FrameIndex, int FrameCount, string Message);

/// <summary>Summary of a pipeline run, mirroring the counters PIPP prints in its processing summary.</summary>
public sealed record ProcessingReport
{
    /// <summary>Frames read from all inputs.</summary>
    public int InputFrames { get; init; }

    /// <summary>Frames written (or stacked).</summary>
    public int OutputFrames { get; init; }

    /// <summary>Frames dropped by quality selection.</summary>
    public int DiscardedByQuality { get; init; }

    /// <summary>Frames dropped because the object was missing or too small.</summary>
    public int DiscardedNoObject { get; init; }

    /// <summary>Input files processed.</summary>
    public int FilesProcessed { get; init; }

    /// <summary>Files that could not be opened or decoded.</summary>
    public IReadOnlyList<string> FailedFiles { get; init; } = Array.Empty<string>();

    /// <summary>Files created.</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = Array.Empty<string>();
}

/// <summary>Options for one pipeline run.</summary>
public sealed class PipelineSettings
{
    /// <summary>Output directory; created when missing.</summary>
    public string OutputDirectory { get; set; } = ".";

    /// <summary>Output file extension, e.g. <c>.ser</c>, <c>.fits</c>, <c>.avi</c>, <c>.bmp</c>, <c>.tif</c>, <c>.png</c>.</summary>
    public string OutputExtension { get; set; } = ".ser";

    /// <summary>Filename prefix applied to generated files.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Filename suffix applied to generated files.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>Force all input to mono (PIPP <c>-mono</c>).</summary>
    public bool ForceMono { get; set; }

    /// <summary>Force colour interpretation of a single-plane source (PIPP <c>-colour</c>).</summary>
    public bool ForceColour { get; set; }

    /// <summary>Debayer single-plane Bayer input.</summary>
    public bool Debayer { get; set; }

    /// <summary>Bayer pattern used when the container does not declare one.</summary>
    public BayerPattern DebayerPattern { get; set; } = BayerPattern.RGGB;

    /// <summary>Use the smooth-hue debayer refinement (PIPP <c>-sh</c>).</summary>
    public bool SmoothHue { get; set; }

    /// <summary>Binning factor, 1 disables binning.</summary>
    public int Binning { get; set; } = 1;

    /// <summary>Sum binned pixels instead of averaging them.</summary>
    public bool BinningSum { get; set; }

    /// <summary>Master dark frame, subtracted before anything else when set.</summary>
    public Frame? MasterDark { get; set; }

    /// <summary>Master flat frame; frames are divided by it and rescaled by its mean.</summary>
    public Frame? MasterFlat { get; set; }

    /// <summary>Apply a 3x3 median filter.</summary>
    public bool MedianFilter { get; set; }

    /// <summary>Colour-to-mono conversion mode (0..6), when set.</summary>
    public int? MonoMode { get; set; }

    /// <summary>Input gain multiplier (1 = unchanged).</summary>
    public double Gain { get; set; } = 1.0;

    /// <summary>Gamma exponent (1 = unchanged).</summary>
    public double Gamma { get; set; } = 1.0;

    /// <summary>Histogram stretch white point (1..255), when set.</summary>
    public int? StretchWhitePoint { get; set; }

    /// <summary>Measure and subtract the frame's black point instead of clipping it (<c>-hs_bp</c>).</summary>
    public bool StretchPreserveBlackPoint { get; set; }

    /// <summary>Keep a separate stretch gain per colour channel rather than the smallest (<c>-hsrgb</c>).</summary>
    public bool StretchPerChannelGains { get; set; }

    /// <summary>Reject frames with more than this many overexposed pixels.</summary>
    public int? MaxOverexposedPixels { get; set; }

    /// <summary>Overexposed pixel threshold; defaults to the frame's maximum value.</summary>
    public int OverexposedThreshold { get; set; }

    /// <summary>Explicit detection floor (-mp); when null the pipeline derives one from the first frame.</summary>
    public int? MinPixelValue { get; set; }

    /// <summary>Reject frames with no detectable object (<c>-chk</c>); also makes the log carry the threshold.</summary>
    public bool CheckForPlanet { get; set; }

    /// <summary>Write the engine's layout: frames in a per-input folder plus a logs folder.</summary>
    public bool PippLayout { get; set; } = true;

    /// <summary>Write <c>logs/&lt;name&gt;_pipp_log.txt</c> (and the quality log when selecting).</summary>
    public bool WriteLogFile { get; set; } = true;

    /// <summary>Name frames <c>&lt;name&gt;_f&lt;n&gt;</c> instead of bare numbers (-keepimgname).</summary>
    public bool KeepImageNames { get; set; }

    /// <summary>Arguments echoed in the log's "Command line arguments" section.</summary>
    public IReadOnlyList<string>? LogArguments { get; set; }

    /// <summary>Version string printed in logs, matching the original binary.</summary>
    public string EngineVersion { get; set; } = "v2.5.9";

    /// <summary>Mirror horizontally.</summary>
    public bool FlipX { get; set; }

    /// <summary>Mirror vertically.</summary>
    public bool FlipY { get; set; }

    /// <summary>Rotation in degrees: 0, 90, 180 or 270 (clockwise).</summary>
    public int Rotation { get; set; }

    /// <summary>Crop rectangle applied after geometry changes.</summary>
    public PixelRect? Crop { get; set; }

    /// <summary>Resize target; a dimension of -1 keeps the aspect ratio.</summary>
    public (int Width, int Height)? Resize { get; set; }

    /// <summary>Quality estimator.</summary>
    public QualityAlgorithm QualityAlgorithm { get; set; } = QualityAlgorithm.PippDefault;

    /// <summary>Keep only the best N frames per input file.</summary>
    public int? KeepFrames { get; set; }

    /// <summary>Keep only the best percentage of frames per input file.</summary>
    public double? KeepPercent { get; set; }

    /// <summary>Enable image centring.</summary>
    public bool Centre { get; set; }

    /// <summary>Enable surface-feature tracking within <see cref="AlignmentBox"/>.</summary>
    public bool SurfaceTracking { get; set; }

    /// <summary>Alignment feature box (PIPP's AFB).</summary>
    public PixelRect? AlignmentBox { get; set; }

    /// <summary>Maximum alignment search radius in pixels.</summary>
    public int AlignmentSearchRadius { get; set; } = 32;

    /// <summary>When set, accepted frames are combined into one master frame.</summary>
    public StackMethod? Stack { get; set; }

    /// <summary>Frames per second metadata for video outputs.</summary>
    public double Fps { get; set; }

    /// <summary>Renumber output frames sequentially (PIPP default; <c>-nr</c> disables it).</summary>
    public bool RenumberFrames { get; set; } = true;

    /// <summary>Zero-pad output frame numbers (PIPP default; <c>-nlz</c> disables it).</summary>
    public bool LeadingZeros { get; set; } = true;

    /// <summary>Emit the engine's <c>@TOKEN</c> progress protocol on stdout.</summary>
    public bool GuiProtocol { get; set; }
}

/// <summary>
/// The processing pipeline: decode, calibrate, process, score, select, align, stack and write.
/// The stage order follows the original engine's documented order (see <c>notes/engine-behaviour.md</c>).
/// </summary>
public sealed class Processor
{
    private readonly PipelineSettings _settings;
    private readonly IProgress<PipelineProgress>? _progress;
    private readonly EngineProtocolWriter? _protocol;

    /// <summary>Creates a processor.</summary>
    /// <param name="settings">Run options.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="protocol">Optional legacy stdout protocol writer (used by the WinForms front-end).</param>
    public Processor(PipelineSettings settings, IProgress<PipelineProgress>? progress = null, EngineProtocolWriter? protocol = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _progress = progress;
        _protocol = protocol;
    }

    /// <summary>Runs the pipeline over the given input files.</summary>
    /// <param name="inputFiles">Input containers.</param>
    /// <param name="cancellationToken">Cancels between frames.</param>
    public ProcessingReport Run(IEnumerable<string> inputFiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);
        var files = inputFiles.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        var outputs = new List<string>();
        var failed = new List<string>();
        int inputFrames = 0, outputFrames = 0, discardedQuality = 0, discardedObject = 0, filesProcessed = 0;

        Directory.CreateDirectory(_settings.OutputDirectory);
        int fileIndex = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileIndex++;
            int objectDiscardsBefore = discardedObject;
            _protocol?.WriteFileLabel(fileIndex, files.Count);
            Report(0, file, 0, 0, $"Processing {Path.GetFileName(file)}");

            IFrameSource source;
            try
            {
                source = FrameSourceFactory.Open(file);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                failed.Add(file);
                _protocol?.WriteLine($"Error: could not open file '{file}': {ex.Message}");
                continue;
            }

            using (source)
            {
                var accepted = new List<(Frame Frame, double Score, AlignmentOffset Offset)>();
                int? detectionThreshold = null;
                Frame? reference = null;
                int index = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Frame? raw;
                    try
                    {
                        raw = source.ReadFrame();
                    }
                    catch (InvalidDataException ex)
                    {
                        failed.Add(file);
                        _protocol?.WriteLine($"Error: {ex.Message}");
                        break;
                    }

                    if (raw is null) break;
                    inputFrames++;
                    index++;
                    raw.Index = index - 1;
                    raw.SourceName = file;

                    Frame frame;
                    try
                    {
                        frame = Transform(raw);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        // a frame that cannot be transformed is a rejected frame, not a crash
                        discardedObject++;
                        _protocol?.WriteLine($"  Frame {index} rejected: {ex.Message}");
                        continue;
                    }

                    if (_settings.MaxOverexposedPixels is int maxOver)
                    {
                        int over = Statistics.CountOverexposed(frame, ResolveOverexposedThreshold(frame)).Sum();
                        if (over > maxOver)
                        {
                            discardedObject++;
                            continue;
                        }
                    }

                    if (raw.Bayer != BayerPattern.None)
                    {
                        frame.Bayer = raw.Bayer;
                    }

                    double score = QualityMetrics.Compute(frame, _settings.QualityAlgorithm);
                    AlignmentOffset offset = AlignmentOffset.Zero;

                    // The engine derives the detection threshold once, from the first frame it
                    // prepares, and reuses it for the rest of the file (pipp_core FUN_14009ab40).
                    detectionThreshold ??= _settings.MinPixelValue ?? DetectionThreshold.ComputeOrDefault(frame);

                    if (_settings.Centre)
                    {
                        // -ctr puts the object at the engine's fixed target in every frame rather
                        // than aligning frames to each other, so each frame is measured on its own.
                        offset = FrameAligner.CentreOffset(frame, detectionThreshold.Value);
                    }
                    else if (_settings.SurfaceTracking)
                    {
                        reference ??= frame;
                        if (!ReferenceEquals(reference, frame))
                        {
                            var measured = FrameAligner.EstimateTranslation(
                                reference, frame, _settings.AlignmentSearchRadius, _settings.AlignmentBox);
                            offset = measured ?? AlignmentOffset.Zero;
                        }
                    }

                    accepted.Add((frame, score, offset));
                    Report((int)(100.0 * index / Math.Max(1, source.FrameCount)), file, index, source.FrameCount,
                        $"Frame {index} of {source.FrameCount}");
                }

                if (accepted.Count == 0)
                {
                    filesProcessed++;
                    continue;
                }

                var selected = Select(accepted, out int dropped);
                discardedQuality += dropped;

                string baseName = _settings.Prefix + Path.GetFileNameWithoutExtension(file) + _settings.Suffix;

                if (_settings.Stack is StackMethod method)
                {
                    string masterPath = Path.Combine(_settings.OutputDirectory, baseName + _settings.OutputExtension);
                    var first = selected[0].Frame;
                    var stacker = new FrameStacker(first.Width, first.Height, first.Channels, first.BitDepth, method);
                    foreach (var (frame, _, offset) in selected)
                    {
                        stacker.Add(offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.Shift(frame, offset));
                    }
                    var master = stacker.Result();
                    using var sink = FrameSinkFactory.Create(masterPath, master, 1, _settings.Fps);
                    sink.Write(master);
                    outputFrames++;
                    outputs.Add(masterPath);
                    _protocol?.WriteLine($"  {selected.Count} frames stacked -> {Path.GetFileName(masterPath)}");
                }
                else if (IsMultiFrameContainer(_settings.OutputExtension))
                {
                    // SER/AVI hold the whole run in one file.
                    string outputPath = Path.Combine(_settings.OutputDirectory, baseName + _settings.OutputExtension);
                    var template = selected[0].Frame;
                    using var sink = FrameSinkFactory.Create(outputPath, template, selected.Count, _settings.Fps);
                    foreach (var (frame, _, offset) in selected)
                    {
                        sink.Write(offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.Shift(frame, offset));
                        outputFrames++;
                    }
                    outputs.Add(outputPath);
                }
                else
                {
                    // Still-image formats write one numbered file per frame. The engine puts them in
                    // a per-input folder, numbers them from 1 (0 when quality selection ran) and pads
                    // only as far as the frame count requires: 4 frames -> 1.tif, 12 frames -> 01.tif.
                    string frameDirectory = _settings.PippLayout
                        ? Path.Combine(_settings.OutputDirectory, baseName)
                        : _settings.OutputDirectory;
                    if (_settings.PippLayout)
                    {
                        Directory.CreateDirectory(frameDirectory);
                    }

                    bool qualitySelection = _settings.KeepFrames is not null || _settings.KeepPercent is not null;
                    int firstNumber = _settings.PippLayout ? (qualitySelection ? 0 : 1) : 0;
                    int digits = Math.Max(1, (firstNumber + selected.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
                    string numberFormat = "D" + digits.ToString(CultureInfo.InvariantCulture);
                    int number = firstNumber;
                    foreach (var (frame, _, offset) in selected)
                    {
                        var written = offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.Shift(frame, offset);
                        string stem = _settings.RenumberFrames
                            ? number.ToString(numberFormat, CultureInfo.InvariantCulture)
                            : written.Index.ToString(numberFormat, CultureInfo.InvariantCulture);
                        string fileName = _settings.KeepImageNames
                            ? $"{baseName}_f{stem}{_settings.OutputExtension}"
                            : $"{stem}{_settings.OutputExtension}";
                        string outputPath = Path.Combine(frameDirectory, fileName);
                        using var sink = FrameSinkFactory.Create(outputPath, written, 1, _settings.Fps);
                        sink.Write(written);
                        outputFrames++;
                        outputs.Add(outputPath);
                        number++;
                    }
                }

                if (_settings.WriteLogFile)
                {
                    WriteLogFiles(
                        file,
                        baseName,
                        source.FrameCount,
                        selected.Count,
                        dropped,
                        discardedObject - objectDiscardsBefore,
                        selected[0].Frame,
                        detectionThreshold,
                        selected);
                }

                filesProcessed++;
            }
        }

        Report(100, null, 0, 0, "Complete");
        return new ProcessingReport
        {
            InputFrames = inputFrames,
            OutputFrames = outputFrames,
            DiscardedByQuality = discardedQuality,
            DiscardedNoObject = discardedObject,
            FilesProcessed = filesProcessed,
            FailedFiles = failed,
            OutputFiles = outputs,
        };
    }

    /// <summary>
    /// Writes the engine's two log files: <c>logs/&lt;name&gt;_pipp_log.txt</c> for every run and
    /// <c>logs/&lt;name&gt;_pipp_quality.txt</c> when frames were selected by quality.
    /// The layout and wording mirror the original's logs line for line; numeric values that come
    /// from reconstructed algorithms (quality scores, detection threshold) differ until those
    /// algorithms are proven in Phase 4.
    /// </summary>
    private void WriteLogFiles(
        string sourceFile,
        string baseName,
        int inputFrameCount,
        int outputFrameCount,
        int droppedByQuality,
        int droppedNoObject,
        Frame sample,
        int? detectionThreshold,
        List<(Frame Frame, double Score, AlignmentOffset Offset)> ranked)
    {
        string logDirectory = Path.Combine(_settings.OutputDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        string directory = _settings.OutputDirectory.Replace('\\', '/');
        if (!directory.EndsWith('/')) directory += "/";

        var text = new StringBuilder();
        text.Append("PIPP (").Append(_settings.EngineVersion).Append(") Logfile\r\n\r\n");
        text.Append("Command line arguments:\r\n");
        foreach (string argument in _settings.LogArguments ?? [])
        {
            text.Append("  ").Append(argument).Append("\r\n");
        }

        text.Append("\r\nSource image files:\r\n  ").Append(sourceFile).Append("\r\n");

        if (_settings.Centre || _settings.CheckForPlanet || droppedNoObject > 0)
        {
            text.Append("\r\nProcessing Details:\r\n");
            int shownThreshold = detectionThreshold
                ?? _settings.MinPixelValue
                ?? DetectionThreshold.ComputeOrDefault(sample);
            text.Append(_settings.MinPixelValue.HasValue
                    ? "  Calculated '-minpixel' value: "
                    : "  Calculated minpixel value: ")
                .Append(shownThreshold.ToString(CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        text.Append("\r\nSummary:\r\n");
        text.Append("PIPP version: ").Append(_settings.EngineVersion).Append("\r\n");
        text.Append("  Total input frames: ").Append(inputFrameCount.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        if (droppedNoObject > 0)
        {
            text.Append("  Frames discarded with no planet detected: ").Append(droppedNoObject.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        if (droppedByQuality > 0)
        {
            text.Append("  Frames discarded by quality: ").Append(droppedByQuality.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        text.Append("  Total output frames: ").Append(outputFrameCount.ToString(CultureInfo.InvariantCulture)).Append(":\r\n");
        if (_settings.KeepFrames is not null || _settings.KeepPercent is not null)
        {
            text.Append("  Quality Algorithm: ").Append(QualityAlgorithmLabel(_settings.QualityAlgorithm)).Append("\r\n");
        }
        text.Append("  Output Frame Type: ").Append(sample.Channels == 3 || _settings.ForceColour ? "Colour" : "Monochrome").Append("\r\n");
        text.Append("  Output Frame Format: ").Append(FormatLabel(_settings.OutputExtension)).Append("\r\n");
        text.Append("  Output Directory: ").Append(directory).Append("\r\n");

        File.WriteAllText(Path.Combine(logDirectory, baseName + "_pipp_log.txt"), text.ToString());

        if (ranked.Count > 0 && (_settings.KeepFrames is not null || _settings.KeepPercent is not null))
        {
            double best = ranked.Max(r => r.Score);
            var quality = new StringBuilder();
            quality.Append("PIPP (").Append(_settings.EngineVersion).Append(") Quality Log\r\n\r\n");
            quality.Append("Pos\tSource\tOutput\tQuality\t% of Best\r\n");
            quality.Append("   \tFrame \tFrame \tValue  \tQuality\r\n");
            for (int position = 0; position < ranked.Count; position++)
            {
                var entry = ranked[position];
                int percent = best > 0 ? (int)Math.Round(entry.Score / best * 100.0, MidpointRounding.AwayFromZero) : 100;
                quality.Append((position + 1).ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append((entry.Frame.Index + 1).ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append((position + 1).ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(entry.Score.ToString("0.000000", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(percent.ToString(CultureInfo.InvariantCulture)).Append("%\r\n");
            }

            File.WriteAllText(Path.Combine(logDirectory, baseName + "_pipp_quality.txt"), quality.ToString());
        }
    }

    private static string QualityAlgorithmLabel(QualityAlgorithm algorithm) => algorithm switch
    {
        QualityAlgorithm.Histogram => "PIPP Histogram",
        QualityAlgorithm.Ninox => "PIPP Old",
        _ => "PIPP Default",
    };

    private static string FormatLabel(string extension) => extension.ToLowerInvariant() switch
    {
        ".tif" or ".tiff" => "TIF",
        ".fits" or ".fit" => "FITS",
        ".png" => "PNG",
        ".avi" => "AVI",
        ".ser" => "SER",
        ".bmp" => "BMP",
        ".gif" => "GIF",
        _ => extension.TrimStart('.').ToUpperInvariant(),
    };

    private static bool IsMultiFrameContainer(string extension) =>
        extension.Equals(".ser", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".avi", StringComparison.OrdinalIgnoreCase);

    private int ResolveOverexposedThreshold(Frame frame) =>
        _settings.OverexposedThreshold > 0
            ? _settings.OverexposedThreshold
            : frame.BitDepth == 8 ? 255 : 65535;

    /// <summary>Applies the per-frame stage chain.</summary>
    private Frame Transform(Frame raw)
    {
        var frame = raw;

        // 1. colour/mono interpretation
        if (_settings.ForceMono && frame.Channels == 3)
        {
            int mode = _settings.MonoMode ?? 3;
            frame = MonoConvert.Apply(frame, (MonoMode)mode);
        }
        else if (_settings.Debayer && frame.Channels == 1 && frame.Bayer != BayerPattern.None)
        {
            frame = Debayer.ToColour(frame, _settings.SmoothHue);
        }
        else if (_settings.ForceColour && frame.Channels == 1 && frame.Bayer != BayerPattern.None)
        {
            frame = Debayer.ToColour(frame, _settings.SmoothHue);
        }

        // 2. binning
        if (_settings.Binning > 1)
        {
            frame = Binning.Apply(frame, _settings.Binning,
                _settings.BinningSum ? BinningMode.Sum : BinningMode.Average);
        }

        // 3. calibration
        if (_settings.MasterDark is not null)
        {
            frame = Calibration.SubtractDark(frame, _settings.MasterDark);
        }
        if (_settings.MasterFlat is not null)
        {
            frame = Calibration.DivideFlat(frame, _settings.MasterFlat);
        }

        // 4. linear processing
        if (_settings.Gain != 1.0 || _settings.Gamma != 1.0)
        {
            if (_settings.Gain != 1.0) frame = ToneCurve.ApplyGain(frame, _settings.Gain);
            if (_settings.Gamma != 1.0) frame = ToneCurve.ApplyGamma(frame, _settings.Gamma);
        }
        if (_settings.MedianFilter)
        {
            frame = MedianFilter3x3.Apply(frame);
        }
        if (_settings.MonoMode is int mm && frame.Channels == 3)
        {
            frame = MonoConvert.Apply(frame, (MonoMode)mm);
        }
        if (_settings.StretchWhitePoint is int wp)
        {
            frame = HistogramStretch.StretchToWhitePoint(
                frame,
                wp,
                preserveBlackPoint: _settings.StretchPreserveBlackPoint,
                perChannelGains: _settings.StretchPerChannelGains);
        }

        // 5. geometry
        if (_settings.FlipX) frame = GeometricTransforms.FlipHorizontal(frame);
        if (_settings.FlipY) frame = GeometricTransforms.FlipVertical(frame);
        frame = _settings.Rotation switch
        {
            90 => GeometricTransforms.Rotate90Clockwise(frame),
            180 => GeometricTransforms.Rotate180(frame),
            270 => GeometricTransforms.Rotate270(frame),
            _ => frame,
        };
        if (_settings.Crop is PixelRect crop)
        {
            frame = GeometricTransforms.Crop(frame, crop.X, crop.Y, crop.Width, crop.Height);
        }
        if (_settings.Resize is (int rw, int rh))
        {
            frame = rw < 0 || rh < 0
                ? Resize.ResizeKeepAspect(frame, rw, rh)
                : Resize.Bilinear(frame, rw, rh);
        }

        return frame;
    }

    /// <summary>Applies PIPP's quality limiting: absolute count and/or percentage of frames kept.</summary>
    private List<(Frame Frame, double Score, AlignmentOffset Offset)> Select(
        List<(Frame Frame, double Score, AlignmentOffset Offset)> accepted, out int dropped)
    {
        int keep = accepted.Count;
        if (_settings.KeepFrames is int n) keep = Math.Min(keep, Math.Max(0, n));
        if (_settings.KeepPercent is double pct) keep = Math.Min(keep, (int)Math.Round(accepted.Count * Math.Clamp(pct, 0, 100) / 100.0));

        dropped = accepted.Count - keep;
        if (keep >= accepted.Count) return accepted;

        var ranked = QualityMetrics.Rank(accepted.Select(a => a.Score).ToArray());
        var result = new List<(Frame, double, AlignmentOffset)>(keep);
        for (int i = 0; i < keep; i++)
        {
            var entry = ranked[i];
            result.Add(accepted[entry.Index]);
        }
        return result;
    }

    private void Report(int percent, string? file, int frameIndex, int frameCount, string message)
    {
        _progress?.Report(new PipelineProgress(Math.Clamp(percent, 0, 100), file, frameIndex, frameCount, message));
        if (_settings.GuiProtocol && _protocol is not null)
        {
            _protocol.Write(EngineToken.Progress, Math.Clamp(percent, 0, 100));
            if (file is not null) _protocol.Write(EngineToken.File, frameIndex);
            _protocol.Write(EngineToken.FrameNumber, frameIndex);
            if (frameCount > 0) _protocol.Write(EngineToken.FrameCount, frameCount);
        }
    }
}
