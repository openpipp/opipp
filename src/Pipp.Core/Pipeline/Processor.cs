using System.Globalization;
using System.Text;
using Pipp.Core.Alignment;
using Pipp.Core.Diagnostics;
using Pipp.Core.Formats;
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

    /// <summary>Frames dropped because no object was detected (<c>-chk</c> return -1).</summary>
    public int DiscardedNoObject { get; init; }

    /// <summary>Frames dropped because the object touched a border.</summary>
    public int DiscardedPartialObject { get; init; }

    /// <summary>Frames dropped because the object was below the minimum size.</summary>
    public int DiscardedTooSmall { get; init; }

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

    /// <summary>
    /// Subdirectory created under <see cref="OutputDirectory"/> (<c>-osubdir</c>); everything the run
    /// writes, including <c>logs/</c> and <c>rejected/</c>, lands below it.
    /// </summary>
    public string? OutputSubdirectory { get; set; }

    /// <summary>
    /// First frame of the processed range, 1-based (<c>-sf</c>). <see langword="null"/> means "from
    /// frame 1"; an explicit 0 reproduces the engine's <c>-sf=0</c>, which starts one frame early.
    /// </summary>
    public int? StartFrame { get; set; }

    /// <summary>
    /// Number of frames to process (<c>-f</c>), counted from <see cref="StartFrame"/> or the centre of
    /// the capture. 0 (the default) means "to the end of the input".
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>Take <see cref="FrameCount"/> frames from the centre of the input (<c>-ctrf</c>).</summary>
    public bool FramesFromCentre { get; set; }

    /// <summary>
    /// Input frame dropping (<c>-ifd</c>): keep one frame, then discard this many, repeating. The
    /// engine's own help calls it "drop frames at the start", but the shipped binary staggers the
    /// whole capture (verified on <c>saturn30.ser</c>: <c>-ifd=5</c> keeps frames 1, 7, 13, 19, 25).
    /// </summary>
    public int InputFrameDrop { get; set; }

    /// <summary>
    /// Write frames rejected by detection or overexposure to <c>rejected/&lt;name&gt;_rejected/</c>
    /// as TIFF instead of discarding them (<c>-sr</c>).
    /// </summary>
    public bool SaveRejectedFrames { get; set; }

    /// <summary>
    /// Treat all inputs as one continuous stream with a single output and a single log (<c>-join</c>).
    /// The output is named after the first input.
    /// </summary>
    public bool JoinMode { get; set; }

    /// <summary>Append <c>_quality_&lt;pct&gt;%</c> to still-image filenames after quality selection (<c>-incqv</c>).</summary>
    public bool IncludeQualityValue { get; set; }

    /// <summary>
    /// Output frame dropping (<c>-drop</c>): write one frame, then skip this many, repeating. 0 writes
    /// every frame.
    /// </summary>
    public int OutputFrameDrop { get; set; }

    /// <summary>Reject frames whose anchor feature cannot be found (<c>-freject</c>).</summary>
    public bool AnchorFeatureReject { get; set; }

    /// <summary>Output file extension, e.g. <c>.ser</c>, <c>.fits</c>, <c>.avi</c>, <c>.bmp</c>, <c>.tif</c>, <c>.png</c>.</summary>
    public string OutputExtension { get; set; } = ".ser";

    /// <summary>GIF encoder options (<c>-gif_*</c>); the delay is derived from <see cref="Fps"/> at write time.</summary>
    public GifOptions GifOptions { get; set; } = GifOptions.Default;

    /// <summary>
    /// Split colour output into one R, one G and one B file per frame (<c>-rgb</c>). Stills are named
    /// <c>r_&lt;n&gt;.tif</c> inside the per-input folder; containers are three separate files named
    /// <c>r_&lt;name&gt;.&lt;ext&gt;</c>, including the engine's doubled dot.
    /// </summary>
    /// <remarks>
    /// The engine emits nothing at all for <c>-rgb -keepimgname</c> (its name builder fails); this
    /// implementation writes the natural <c>r_&lt;baseName&gt;_f&lt;n&gt;</c> instead, a documented
    /// divergence. GIF output ignores the split, exactly like the engine.
    /// </remarks>
    public bool SplitRgb { get; set; }

    /// <summary>AVI codec requested with <c>-avi_codec</c>; only <see cref="AviCodec.Dib"/> is writable.</summary>
    public AviCodec AviCodec { get; set; } = AviCodec.Dib;

    /// <summary>AVI codec quality, 1..10 (<c>-avi_quality</c>); unused by the uncompressed path.</summary>
    public int AviQuality { get; set; } = 10;

    /// <summary>
    /// Re-emit the loaded master dark as <c>logs/pipp_master_dark.tif</c> (<c>-save_dark</c>).
    /// </summary>
    /// <remarks>
    /// The engine writes the file only when <c>-dark</c> is on and a master was loaded from
    /// <c>-dark_tmp</c> (a <c>-dark_pgm</c>-only run saves nothing). This implementation re-emits
    /// whichever master it loaded, so a readable <c>-dark_pgm</c> also produces the file; the name,
    /// location, orientation and pixels are oracle-verified.
    /// </remarks>
    public bool SaveMasterDark { get; set; }

    /// <summary>
    /// Re-emit the loaded master flat as <c>logs/pipp_master_flat.tif</c> (<c>-save_flat</c>), or
    /// <c>logs/pipp_master_flat_dark.tif</c> for a one-channel master, matching the engine's quirk.
    /// </summary>
    public bool SaveMasterFlat { get; set; }

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

    /// <summary>Minimum object size in pixels (<c>-ms</c>), compared with both box dimensions.</summary>
    /// <remarks>
    /// The engine compares it with <c>min(box width, box height)</c>. When <c>-ms</c> is absent the
    /// work image's field keeps its initial 50 (bisected against the shipped binary: a synthetic
    /// object with a 49-pixel minimum dimension is rejected, a 50-pixel one is accepted).
    /// </remarks>
    public int MinSize { get; set; } = 50;

    /// <summary>Smallest quality stride (<c>-qmin</c>).</summary>
    public int QualityMinDownsample { get; set; } = 3;

    /// <summary>Largest quality stride (<c>-qmax</c>).</summary>
    public int QualityMaxDownsample { get; set; } = 5;

    /// <summary>Quality stride step (<c>-qinc</c>).</summary>
    public int QualityIncrement { get; set; } = 1;

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

    /// <summary>
    /// Enable surface-feature tracking within <see cref="AlignmentBox"/> (<c>-en_surface_feature_tracking</c>).
    /// </summary>
    /// <remarks>
    /// The reference is the first accepted frame; every later frame is correlated against it inside the
    /// AFB and shifted by the negated measurement. A run without an <see cref="AlignmentBox"/> is a
    /// no-op, which is what the engine does when the <c>-afb_*</c> quartet is absent.
    /// </remarks>
    public bool SurfaceTracking { get; set; }

    /// <summary>
    /// Stabilise while tracking (<c>-en_ss</c>). The engine needs both switches: with only
    /// <c>-en_surface_feature_tracking</c> its output is byte-identical to a plain run.
    /// </summary>
    public bool EnableStabilisation { get; set; }

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
        int inputFrames = 0, outputFrames = 0, discardedQuality = 0,
            discardedNoObject = 0, discardedPartial = 0, discardedTooSmall = 0,
            discardedNoFeature = 0, discardedStart = 0, discardedInputDrop = 0, discardedOutputDrop = 0,
            filesProcessed = 0;

        // One metric instance for the whole run: the engine's mask buffer lives in its work image
        // and is reused (never cleared) across frames, which is exactly what the score depends on.
        var quality = new EngineQuality(
            _settings.QualityMinDownsample,
            _settings.QualityMaxDownsample,
            _settings.QualityIncrement);

        // -osubdir wraps the complete output layout, logs and rejected frames included; the oracle
        // writes <od>/<subdir>/logs/ and <od>/<subdir>/<input>/.
        string outputRoot = EffectiveOutputDirectory();
        Directory.CreateDirectory(outputRoot);

        // -save_dark / -save_flat: the engine re-emits the master it loaded into logs/ before any
        // frame is processed (oracle: <od>/[sub/]logs/pipp_master_dark.tif). Nothing is written
        // when the calibration switch is off or no master was loaded.
        SaveMasterFrames(outputRoot);

        // -join collapses the inputs into one continuous stream: one output and one log, named
        // after the first input (verified: capture.ser + real1.ser -> capture/1.tif..5.tif).
        var units = BuildUnits(files);
        int fileIndex = 0;

        foreach (var unit in units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileIndex++;
            int noObjectBefore = discardedNoObject;
            int partialBefore = discardedPartial;
            int tooSmallBefore = discardedTooSmall;
            int noFeatureBefore = discardedNoFeature;
            int inputDropBefore = discardedInputDrop;
            int outputDropBefore = discardedOutputDrop;
            int outputBefore = outputFrames;
            _protocol?.WriteFileLabel(fileIndex, units.Count);
            Report(0, unit.Name, 0, 0, $"Processing {Path.GetFileName(unit.Name)}");

            IFrameSource source;
            try
            {
                source = unit.Open();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                failed.Add(unit.Name);
                _protocol?.WriteLine($"Error: could not open file '{unit.Name}': {ex.Message}");
                continue;
            }

            using (source)
            {
                var accepted = new List<(Frame Frame, double Score, AlignmentOffset Offset)>();
                int? detectionThreshold = null;
                Frame? reference = null;
                Frame? sample = null;

                // Surface tracking zero-fills the vacated border in the engine; centring fills it with
                // the frame's median. Stacking and the still/container writers all shift the same way.
                bool stabilise = _settings.SurfaceTracking && _settings.EnableStabilisation;
                ushort? shiftFill = stabilise && !_settings.Centre ? (ushort)0 : null;
                int index = 0;

                // -sf / -f / -ctrf / -ifd: the engine builds a frame window per input file
                // (pipp_core FUN_14007e8e0: start = sf-1 or (n-f)/2, end = min(f+start, n)).
                var window = FrameWindow.Compute(source.FrameCount, _settings);
                discardedStart += Math.Max(0, window.Start);

                string baseName = _settings.Prefix + Path.GetFileNameWithoutExtension(unit.Name) + _settings.Suffix;
                string rejectedDirectory = Path.Combine(outputRoot, "rejected", baseName + "_rejected");
                int rejectedDigits = Math.Max(1, source.FrameCount.ToString(CultureInfo.InvariantCulture).Length);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // The window is an upper bound on frames *read*: frames before the start and the
                    // ones -ifd staggers out still count against it, exactly like the engine.
                    if (index >= window.Limit) break;

                    Frame? raw;
                    try
                    {
                        raw = source.ReadFrame();
                    }
                    catch (InvalidDataException ex)
                    {
                        failed.Add(unit.Name);
                        _protocol?.WriteLine($"Error: {ex.Message}");
                        break;
                    }

                    if (raw is null) break;
                    inputFrames++;
                    index++;
                    raw.Index = index - 1;
                    raw.SourceName = unit.Name;

                    if (index - 1 < window.Start)
                    {
                        continue;
                    }

                    // -ifd keeps one frame and then discards `InputFrameDrop`, restarting at the first
                    // frame of the window (oracle: -sf=5 -ifd=2 keeps 5, 8, 11, ...).
                    if (window.InputDrop > 0 && (index - 1 - window.Start) % (window.InputDrop + 1) != 0)
                    {
                        discardedInputDrop++;
                        continue;
                    }

                    Frame frame;
                    try
                    {
                        frame = Transform(raw);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        // a frame that cannot be transformed is a rejected frame, not a crash
                        discardedNoObject++;
                        SaveRejected(raw, "no_object", index, rejectedDirectory, rejectedDigits);
                        _protocol?.WriteLine($"  Frame {index} rejected: {ex.Message}");
                        continue;
                    }

                    if (_settings.MaxOverexposedPixels is int maxOver)
                    {
                        int over = Statistics.CountOverexposed(frame, ResolveOverexposedThreshold(frame)).Sum();
                        if (over > maxOver)
                        {
                            discardedNoObject++;
                            SaveRejected(frame, "overexposed", index, rejectedDirectory, rejectedDigits);
                            continue;
                        }
                    }

                    if (raw.Bayer != BayerPattern.None)
                    {
                        frame.Bayer = raw.Bayer;
                    }

                    sample = frame;

                    // The engine derives the detection threshold once, from the first frame it
                    // prepares, and reuses it for the rest of the file (pipp_core FUN_14009ab40).
                    detectionThreshold ??= _settings.MinPixelValue ?? DetectionThreshold.ComputeOrDefault(frame);

                    if (_settings.CheckForPlanet)
                    {
                        PlanetDetection detection = PlanetDetector.Detect(frame, detectionThreshold.Value, _settings.MinSize);
                        if (!detection.Found)
                        {
                            switch (detection.Status)
                            {
                                case PlanetDetectionStatus.PartialObject:
                                    discardedPartial++;
                                    break;
                                case PlanetDetectionStatus.TooSmall:
                                    discardedTooSmall++;
                                    break;
                                default:
                                    discardedNoObject++;
                                    break;
                            }

                            // Every failure path in the finder zeroes the box the metric reads.
                            quality.ClearDetectedBox();
                            string reason = DetectionLabel(detection.Status);
                            SaveRejected(frame, RejectPrefix(detection.Status), index, rejectedDirectory, rejectedDigits);
                            _protocol?.WriteLine($"  Frame {index} rejected: {reason}");
                            Report(Percent(index, window), unit.Name, index, window.Limit,
                                $"Frame {index} of {window.Limit} ({reason})");
                            continue;
                        }

                        // The score measures the box the finder wrote, grown by one pixel per side.
                        quality.SetDetectedBox(detection.Left, detection.Top, detection.Right, detection.Bottom);
                    }

                    double score = _settings.QualityAlgorithm == QualityAlgorithm.PippDefault
                        ? quality.Score(frame, detectionThreshold.Value)
                        : QualityMetrics.Compute(frame, _settings.QualityAlgorithm);

                    AlignmentOffset offset = AlignmentOffset.Zero;
                    if (_settings.Centre)
                    {
                        // -ctr puts the object at the engine's fixed target in every frame rather
                        // than aligning frames to each other, so each frame is measured on its own.
                        offset = FrameAligner.CentreOffset(frame, detectionThreshold.Value);
                    }
                    else if (stabilise)
                    {
                        // The engine's GUI always emits the AFB quartet alongside the switch, and a run
                        // without one leaves the frames untouched (verified on saturn30: the switch
                        // alone is byte-identical to a plain run). Mirror that instead of guessing a box.
                        reference ??= frame;
                        if (!ReferenceEquals(reference, frame) && _settings.AlignmentBox is { } box)
                        {
                            // Measure returns the displacement of the tracked content, i.e.
                            // reference(box) == frame(box + offset), so aligning means shifting by -offset.
                            // ZNCC is the feature-tracking measurement; the cosine aligner is only a
                            // fallback for boxes too flat or too occluded for ZNCC to accept.
                            var measured = SurfaceTracker.Measure(reference, frame, _settings.AlignmentSearchRadius, box)
                                ?? FrameAligner.EstimateTranslation(reference, frame, _settings.AlignmentSearchRadius, box);

                            if (measured is { } m)
                            {
                                offset = new AlignmentOffset(-m.Dx, -m.Dy);
                            }
                            else if (_settings.AnchorFeatureReject)
                            {
                                // -freject: the engine counts these as "no AFB found" and drops the frame
                                // (the mirror of the log line pipp_core prints for the counter).
                                discardedNoFeature++;
                                SaveRejected(frame, "no_afb", index, rejectedDirectory, rejectedDigits);
                                _protocol?.WriteLine($"  Frame {index} rejected: no AFB found");
                                continue;
                            }
                        }
                    }

                    accepted.Add((frame, score, offset));
                    Report(Percent(index, window), unit.Name, index, window.Limit,
                        $"Frame {index} of {window.Limit}");
                }

                if (accepted.Count == 0)
                {
                    // The engine still writes both logs when every frame was rejected; the summary
                    // then carries the discard counters and "Total output frames: 0".
                    if (_settings.WriteLogFile)
                    {
                        WriteLogFiles(
                            unit.Sources,
                            baseName,
                            window.Limit,
                            0,
                            0,
                            discardedNoObject - noObjectBefore,
                            discardedPartial - partialBefore,
                            discardedTooSmall - tooSmallBefore,
                            discardedNoFeature - noFeatureBefore,
                            window.Start,
                            discardedInputDrop - inputDropBefore,
                            discardedOutputDrop - outputDropBefore,
                            sample,
                            detectionThreshold,
                            []);
                    }

                    filesProcessed++;
                    continue;
                }

                var selected = Select(accepted, out int dropped);
                discardedQuality += dropped;

                // -drop writes one frame and then skips `OutputFrameDrop`, repeating
                // (pipp_core: counter++ then reset when counter > drop; oracle -drop=2 keeps 1, 4, 7).
                var written = selected;
                if (_settings.OutputFrameDrop > 0)
                {
                    written = new List<(Frame Frame, double Score, AlignmentOffset Offset)>(selected.Count);
                    for (int i = 0; i < selected.Count; i++)
                    {
                        if (i % (_settings.OutputFrameDrop + 1) == 0) written.Add(selected[i]);
                        else discardedOutputDrop++;
                    }
                }

                double bestScore = 0;
                foreach (var entry in selected)
                {
                    if (entry.Score > bestScore) bestScore = entry.Score;
                }

                // -nr keeps the source order even when quality selection ran: the best N frames are
                // chosen but written oldest-first (oracle: -q=10 -nr wrote source frames
                // 1, 2, 4, 10, 11, 15, 17, 18, 19, 20 where -q=10 wrote them in score order).
                bool qualitySelection = _settings.KeepFrames is not null || _settings.KeepPercent is not null;
                if (!_settings.RenumberFrames && qualitySelection)
                {
                    written = written.OrderBy(entry => entry.Frame.Index).ToList();
                }

                if (_settings.Stack is StackMethod method)
                {
                    string masterPath = Path.Combine(outputRoot, baseName + _settings.OutputExtension);
                    var first = selected[0].Frame;
                    var stacker = new FrameStacker(first.Width, first.Height, first.Channels, first.BitDepth, method);
                    foreach (var (frame, _, offset) in selected)
                    {
                        stacker.Add(offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.ShiftWithBackground(frame, offset, shiftFill));
                    }
                    var master = stacker.Result();
                    using var sink = FrameSinkFactory.Create(masterPath, master, 1, _settings.Fps,
                        _settings.AviCodec, _settings.AviQuality);
                    sink.Write(master);
                    outputFrames++;
                    outputs.Add(masterPath);
                    _protocol?.WriteLine($"  {selected.Count} frames stacked -> {Path.GetFileName(masterPath)}");
                }
                else if (_settings.OutputExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    // One animated file at the output root: unlike TIFF/PNG the engine does not use a
                    // per-input folder for GIF. The original writes an image-less GIF (no image
                    // descriptors at all), so writing a valid animation is a deliberate divergence.
                    // -rgb has no effect on GIF in the engine (one file), and neither do -gif_border_tol
                    // or -gif_lossy_comp, which only ever shaped image blocks that were never written.
                    string gifPath = Path.Combine(outputRoot, baseName + ".gif");
                    var gifFrames = new List<Frame>(written.Count);
                    foreach (var (frame, _, offset) in written)
                    {
                        gifFrames.Add(offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.ShiftWithBackground(frame, offset, shiftFill));
                        outputFrames++;
                    }

                    int delay = Math.Max(1, (int)Math.Round(100.0 / Math.Max(1, _settings.Fps)));
                    GifWriter.Write(gifPath, gifFrames, _settings.GifOptions with { DelayCentiseconds = delay });
                    outputs.Add(gifPath);
                }
                else if (IsMultiFrameContainer(_settings.OutputExtension))
                {
                    // SER/AVI hold the whole run in one file; joining inputs of different geometry
                    // is exactly the case the engine refuses ("Frames of different sizes ...").
                    var template = written[0].Frame;
                    foreach (var (frame, _, _) in written)
                    {
                        if (frame.Width != template.Width || frame.Height != template.Height)
                        {
                            throw new InvalidDataException(
                                "Frames of different sizes cannot written to an AVI or SER file");
                        }
                    }

                    if (_settings.SplitRgb)
                    {
                        // -rgb writes three containers, one per channel. The engine's name is
                        // <channel>_<prefix><name>.<suffix><ext> — with no suffix this yields the
                        // doubled dot seen in the oracle ("r_colour..ser"), reproduced here.
                        string stem = Path.GetFileNameWithoutExtension(unit.Name);
                        string splitBase = _settings.Prefix + stem + "." + _settings.Suffix + _settings.OutputExtension;
                        var sinks = new List<IFrameSink>(3);
                        try
                        {
                            foreach ((char letter, int channel) in RgbChannels)
                            {
                                Frame part = ExtractChannel(template, channel);
                                string path = Path.Combine(outputRoot, letter + "_" + splitBase);
                                sinks.Add(FrameSinkFactory.Create(path, part, written.Count, _settings.Fps,
                                    _settings.AviCodec, _settings.AviQuality));
                                outputs.Add(path);
                            }

                            foreach (var (frame, _, offset) in written)
                            {
                                var output = offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.ShiftWithBackground(frame, offset, shiftFill);
                                for (int channel = 0; channel < sinks.Count; channel++)
                                {
                                    sinks[channel].Write(ExtractChannel(output, channel));
                                }

                                outputFrames++;
                            }
                        }
                        finally
                        {
                            foreach (IFrameSink sink in sinks) sink.Dispose();
                        }
                    }
                    else
                    {
                        string outputPath = Path.Combine(outputRoot, baseName + _settings.OutputExtension);
                        using var sink = FrameSinkFactory.Create(outputPath, template, written.Count, _settings.Fps,
                            _settings.AviCodec, _settings.AviQuality);
                        foreach (var (frame, _, offset) in written)
                        {
                            sink.Write(offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.ShiftWithBackground(frame, offset, shiftFill));
                            outputFrames++;
                        }
                        outputs.Add(outputPath);
                    }
                }
                else
                {
                    // Still-image formats write one numbered file per frame. The engine puts them in
                    // a per-input folder, numbers them from 1 (0 when quality selection ran) and pads
                    // only as far as the frame count requires: 4 frames -> 1.tif, 12 frames -> 01.tif.
                    string frameDirectory = _settings.PippLayout
                        ? Path.Combine(outputRoot, baseName)
                        : outputRoot;
                    if (_settings.PippLayout)
                    {
                        Directory.CreateDirectory(frameDirectory);
                    }

                    int firstNumber = _settings.PippLayout ? (qualitySelection ? 0 : 1) : 0;

                    // Padding follows the engine: without quality selection it is the width of the
                    // source frame count (a 30-frame capture writes 01.tif..05.tif for -f=5), with
                    // selection it is the width of the kept count (verified with the oracle: -q=3
                    // writes 0.tif..2.tif while -q=10 writes 00.tif..09.tif, and -q=10 -drop=1 keeps
                    // the 2-digit names because ten frames were selected before dropping). -nr
                    // switches the basis back to the source count (-q=3 -nr wrote 00.tif..02.tif).
                    int basis = qualitySelection && _settings.RenumberFrames ? Math.Max(1, selected.Count) : source.FrameCount;
                    int digits = Math.Max(1, basis.ToString(CultureInfo.InvariantCulture).Length);
                    string numberFormat = "D" + digits.ToString(CultureInfo.InvariantCulture);
                    int number = firstNumber;
                    foreach (var (frame, score, offset) in written)
                    {
                        var output = offset.Dx == 0 && offset.Dy == 0 ? frame : FrameAligner.ShiftWithBackground(frame, offset, shiftFill);
                        // -nr names a plain run's frames after their source number (1-based:
                        // -f=4 -nr wrote 01.tif..04.tif), but a quality run stays sequential because
                        // the engine has already reordered the stream itself.
                        string stem = _settings.RenumberFrames || qualitySelection
                            ? number.ToString(numberFormat, CultureInfo.InvariantCulture)
                            : (output.Index + 1).ToString(numberFormat, CultureInfo.InvariantCulture);

                        // -incqv: the engine appends the frame's score as a percentage of the best,
                        // printed with four significant digits (oracle: "1_quality_76.32%.tif").
                        if (_settings.IncludeQualityValue && qualitySelection)
                        {
                            double percent = bestScore > 0 ? score / bestScore * 100.0 : 100.0;
                            stem += "_quality_" + percent.ToString("G4", CultureInfo.InvariantCulture) + "%";
                        }

                        string fileName = _settings.KeepImageNames
                            ? $"{baseName}_f{stem}{_settings.OutputExtension}"
                            : $"{stem}{_settings.OutputExtension}";

                        if (_settings.SplitRgb)
                        {
                            // -rgb: one file per channel, named <channel>_<normal file name>; the
                            // frame number, zero padding and -incqv suffix are shared by all three
                            // (oracle: r_0_quality_100%.fit, b_1.tif). Mono frames are replicated.
                            foreach ((char letter, int channel) in RgbChannels)
                            {
                                Frame part = ExtractChannel(output, channel);
                                string partPath = Path.Combine(frameDirectory, letter + "_" + fileName);
                                using var partSink = FrameSinkFactory.Create(
                                    partPath, part, 1, _settings.Fps, _settings.AviCodec, _settings.AviQuality);
                                partSink.Write(part);
                                outputs.Add(partPath);
                            }
                        }
                        else
                        {
                            string outputPath = Path.Combine(frameDirectory, fileName);
                            using var sink = FrameSinkFactory.Create(
                                outputPath, output, 1, _settings.Fps, _settings.AviCodec, _settings.AviQuality);
                            sink.Write(output);
                            outputs.Add(outputPath);
                        }

                        outputFrames++;
                        number++;
                    }
                }

                if (_settings.WriteLogFile)
                {
                    WriteLogFiles(
                        unit.Sources,
                        baseName,
                        window.Limit,
                        outputFrames - outputBefore,
                        dropped,
                        discardedNoObject - noObjectBefore,
                        discardedPartial - partialBefore,
                        discardedTooSmall - tooSmallBefore,
                        discardedNoFeature - noFeatureBefore,
                        window.Start,
                        discardedInputDrop - inputDropBefore,
                        discardedOutputDrop - outputDropBefore,
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
            DiscardedNoObject = discardedNoObject,
            DiscardedPartialObject = discardedPartial,
            DiscardedTooSmall = discardedTooSmall,
            FilesProcessed = filesProcessed,
            FailedFiles = failed,
            OutputFiles = outputs,
        };
    }

    /// <summary>
    /// Writes the engine's two log files: <c>logs/&lt;name&gt;_pipp_log.txt</c> for every run and
    /// <c>logs/&lt;name&gt;_pipp_quality.txt</c> when frames were selected by quality.
    /// The layout and wording mirror the original's logs line for line, including the three
    /// detection discard counters and a quality log with nothing but its header when every frame
    /// was rejected.
    /// </summary>
    private void WriteLogFiles(
        IReadOnlyList<string> sourceFiles,
        string baseName,
        int inputFrameCount,
        int outputFrameCount,
        int droppedByQuality,
        int droppedNoObject,
        int droppedPartial,
        int droppedTooSmall,
        int droppedNoFeature,
        int discardedForStart,
        int discardedByInputDrop,
        int discardedByOutputDrop,
        Frame? sample,
        int? detectionThreshold,
        List<(Frame Frame, double Score, AlignmentOffset Offset)> ranked)
    {
        string outputRoot = EffectiveOutputDirectory();
        string logDirectory = Path.Combine(outputRoot, "logs");
        Directory.CreateDirectory(logDirectory);

        string directory = outputRoot.Replace('\\', '/');
        if (!directory.EndsWith('/')) directory += "/";

        var text = new StringBuilder();
        text.Append("PIPP (").Append(_settings.EngineVersion).Append(") Logfile\r\n\r\n");
        text.Append("Command line arguments:\r\n");
        foreach (string argument in _settings.LogArguments ?? [])
        {
            text.Append("  ").Append(argument).Append("\r\n");
        }

        text.Append("\r\nSource image files:\r\n");
        foreach (string source in sourceFiles)
        {
            text.Append("  ").Append(source).Append("\r\n");
        }

        if (sample is not null && (_settings.Centre || _settings.CheckForPlanet || droppedNoObject > 0))
        {
            text.Append("\r\nProcessing Details:\r\n");
            int shownThreshold = detectionThreshold
                ?? _settings.MinPixelValue
                ?? DetectionThreshold.ComputeOrDefault(sample);
            text.Append("  Calculated minpixel value: ")
                .Append(shownThreshold.ToString(CultureInfo.InvariantCulture))
                .Append((char)13).Append((char)10);
        }

        text.Append("\r\nSummary:\r\n");
        text.Append("PIPP version: ").Append(_settings.EngineVersion).Append("\r\n");
        text.Append("  Total input frames: ").Append(inputFrameCount.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        if (discardedForStart > 0)
        {
            text.Append("    Frames discarded for Start Frame option: ").Append(discardedForStart.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (discardedByInputDrop > 0)
        {
            text.Append("    Frames discarded for Input Frame Dropping option: ").Append(discardedByInputDrop.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (droppedNoObject > 0)
        {
            text.Append("  Frames discarded with no planet detected: ").Append(droppedNoObject.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (droppedPartial > 0)
        {
            text.Append("  Frames discarded with partial planet detected: ").Append(droppedPartial.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (droppedNoFeature > 0)
        {
            text.Append("  Frames discarded with no AFB found: ").Append(droppedNoFeature.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (droppedTooSmall > 0)
        {
            text.Append("  Frames discarded with planet diameter below minimum: ").Append(droppedTooSmall.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (droppedByQuality > 0 || _settings.KeepFrames is not null || _settings.KeepPercent is not null)
        {
            // the engine prints the line (with 0) whenever quality selection ran
            text.Append("  Frames discarded by quality: ").Append(droppedByQuality.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        if (_settings.OutputFrameDrop > 0)
        {
            text.Append("  Frames discarded by output frame drop option: ").Append(discardedByOutputDrop.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
        }
        text.Append("  Total output frames: ").Append(outputFrameCount.ToString(CultureInfo.InvariantCulture)).Append(":\r\n");
        if (_settings.KeepFrames is not null || _settings.KeepPercent is not null)
        {
            text.Append("  Quality Algorithm: ").Append(QualityAlgorithmLabel(_settings.QualityAlgorithm)).Append("\r\n");
        }
        // With no prepared frame the engine falls back to its "Colour" default (oracle: -sf=31).
        text.Append("  Output Frame Type: ").Append(sample is null || sample.Channels == 3 || _settings.ForceColour ? "Colour" : "Monochrome").Append("\r\n");
        text.Append("  Output Frame Format: ").Append(FormatLabel(_settings.OutputExtension)).Append("\r\n");
        text.Append("  Output Directory: ").Append(directory).Append("\r\n");

        File.WriteAllText(Path.Combine(logDirectory, baseName + "_pipp_log.txt"), text.ToString());

        if (_settings.KeepFrames is not null || _settings.KeepPercent is not null)
        {
            // The engine writes the header even when every frame was rejected.
            double best = 0;
            foreach (var entry in ranked)
            {
                if (entry.Score > best) best = entry.Score;
            }

            var quality = new StringBuilder();
            quality.Append("PIPP (").Append(_settings.EngineVersion).Append(") Quality Log\r\n\r\n");
            quality.Append("Pos\tSource\tOutput\tQuality\t% of Best\r\n");
            quality.Append("   \tFrame \tFrame \tValue  \tQuality\r\n");
            for (int position = 0; position < ranked.Count; position++)
            {
                var entry = ranked[position];
                // the engine truncates the percentage (74.9% prints as 74%)
                int percent = best > 0 ? (int)(entry.Score / best * 100.0) : 100;
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

    /// <summary>Human-readable form of a detection rejection, used in the console/protocol log.</summary>
    private static string DetectionLabel(PlanetDetectionStatus status) => status switch
    {
        PlanetDetectionStatus.PartialObject => "partial planet detected",
        PlanetDetectionStatus.TooSmall => "planet diameter below minimum",
        _ => "no planet detected",
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

        // Without quality selection the engine keeps the source order. With it, it ranks even when
        // nothing is dropped: the output numbering and the log both follow quality order.
        if (_settings.KeepFrames is null && _settings.KeepPercent is null)
        {
            return accepted;
        }

        var ranked = QualityMetrics.Rank(accepted.Select(a => a.Score).ToArray());
        var result = new List<(Frame, double, AlignmentOffset)>(keep);
        for (int i = 0; i < keep; i++)
        {
            var entry = ranked[i];
            result.Add(accepted[entry.Index]);
        }
        return result;
    }

    /// <summary>Output root: <c>&lt;od&gt;/&lt;osubdir&gt;</c> when <c>-osubdir</c> is set, else <c>-od</c>.</summary>
    private string EffectiveOutputDirectory()
    {
        string root = _settings.OutputDirectory;
        if (string.IsNullOrWhiteSpace(_settings.OutputSubdirectory)) return root;
        string sub = _settings.OutputSubdirectory!;
        return Path.IsPathRooted(sub) ? sub : Path.Combine(root, sub);
    }

    /// <summary>
    /// One unit of work: a single input, or every input joined into one stream (<c>-join</c>).
    /// The unit decides how the source is opened so the loop body stays unaware of the difference.
    /// </summary>
    private List<InputUnit> BuildUnits(List<string> files)
    {
        if (_settings.JoinMode && files.Count > 1)
        {
            return [new InputUnit(files[0], files, () => new JoinedFrameSource(files))];
        }

        return files.Select(f => new InputUnit(f, [f], () => FrameSourceFactory.Open(f))).ToList();
    }

    private sealed record InputUnit(string Name, IReadOnlyList<string> Sources, Func<IFrameSource> Open);

    /// <summary>
    /// The engine's per-input frame window. <paramref name="Start"/> is the 0-based first frame,
    /// <paramref name="Limit"/> the exclusive upper bound on frames <em>read</em> and
    /// <paramref name="InputDrop"/> the <c>-ifd</c> stride.
    /// </summary>
    /// <remarks>
    /// Bisected against <c>pipp_core.exe</c> on a 30-frame capture (frame numbers 1-based):
    /// <c>-f=5</c> reads 1..5; <c>-sf=3</c> starts at 3; <c>-ctrf -f=5</c> starts at 13 = (30-5)/2+1;
    /// <c>-ctrf</c> alone starts at 16 = 30/2+1; <c>-ifd=5</c> keeps 1, 7, 13, 19, 25.
    /// The <c>-f</c>/<c>-ifd</c> alignment quirk is reproduced as observed, not as the help text
    /// describes it: <c>pipp_core</c> ignores <c>-f</c> unless <c>f = 1 + k*(ifd+1)</c>, so
    /// <c>-ifd=2 -f=10</c> reads 10 frames while <c>-ifd=2 -f=11</c> reads the whole capture.
    /// </remarks>
    private readonly record struct FrameWindow(int Start, int Limit, int InputDrop)
    {
        public static FrameWindow Compute(int total, PipelineSettings settings)
        {
            int frames = settings.FrameCount;
            int start = settings.FramesFromCentre
                ? Math.Max(0, (total - frames) / 2)
                : (settings.StartFrame ?? 1) - 1;
            // A container that does not declare a frame count falls back to "read to EOF".
            int limit = total > 0 ? total : int.MaxValue;

            if (frames != 0)
            {
                bool honour = settings.InputFrameDrop <= 0
                    || (frames - 1) % (settings.InputFrameDrop + 1) == 0;
                if (honour) limit = total > 0 ? Math.Min(start + frames, total) : start + frames;
            }

            return new FrameWindow(start, Math.Max(0, limit), settings.InputFrameDrop);
        }
    }

    /// <summary>Progress percentage inside the window rather than the whole container.</summary>
    private static int Percent(int index, FrameWindow window) =>
        (int)(100.0 * index / Math.Max(1, window.Limit));

    /// <summary>The engine's rejected-frame filename prefix for a detection failure.</summary>
    private static string RejectPrefix(PlanetDetectionStatus status) => status switch
    {
        PlanetDetectionStatus.PartialObject => "partial_object",
        PlanetDetectionStatus.TooSmall => "too_small",
        _ => "no_object",
    };

    /// <summary>
    /// Writes one rejected frame the way <c>-sr</c> does in the engine: under
    /// <c>rejected/&lt;name&gt;_rejected/</c>, named <c>&lt;reason&gt;_&lt;source frame&gt;.tif</c>, always a
    /// three-channel TIFF at the frame's bit depth (a mono frame is replicated across R, G and B).
    /// </summary>
    /// <remarks>
    /// Verified with the oracle: <c>-sr -chk -ms=10000</c> on a 30-frame capture produced
    /// <c>rejected/saturn30_rejected/too_small_01.tif</c>, a 640x640x3 8-bit TIFF whose bytes equal
    /// the corresponding normal output frame; on a 4-frame capture the names are
    /// <c>too_small_1.tif</c>..<c>too_small_4.tif</c>. The extension is always <c>.tif</c> even for
    /// <c>-png</c>/<c>-ser</c> output.
    /// </remarks>
    private void SaveRejected(Frame frame, string reason, int sourceFrame, string directory, int digits)
    {
        if (!_settings.SaveRejectedFrames) return;

        Directory.CreateDirectory(directory);
        string suffix = "_" + sourceFrame.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, reason + suffix + ".tif");

        Frame output = frame;
        if (frame.Channels == 1)
        {
            output = new Frame(frame.Width, frame.Height, 3, frame.BitDepth);
            for (int i = 0; i < frame.Samples.Length; i++)
            {
                ushort value = frame.Samples[i];
                output.Samples[i * 3] = value;
                output.Samples[i * 3 + 1] = value;
                output.Samples[i * 3 + 2] = value;
            }
        }

        using var sink = FrameSinkFactory.Create(path, output, 1, _settings.Fps);
        sink.Write(output);
    }

    /// <summary>
    /// Writes the engine's saved master frames: <c>logs/pipp_master_dark.tif</c> for
    /// <c>-save_dark</c> and <c>logs/pipp_master_flat.tif</c> (or, for a one-channel flat,
    /// <c>logs/pipp_master_flat_dark.tif</c>) for <c>-save_flat</c>.
    /// </summary>
    /// <remarks>
    /// Verified with the oracle on a 32x24 capture: with <c>-dark -dark_tmp=&lt;master&gt; -save_dark</c>
    /// the engine writes a real TIFF under <c>&lt;od&gt;/logs/</c> before processing, and writes
    /// nothing when <c>-dark</c> is absent or no master path was supplied. The saved dark is always
    /// one channel; the flat's channel count is preserved, and the engine names a one-channel flat
    /// <c>pipp_master_flat_dark.tif</c> (an engine quirk this reproduces). <c>-osubdir</c> nests the
    /// same way the log does.
    /// </remarks>
    private void SaveMasterFrames(string outputRoot)
    {
        if (!_settings.SaveMasterDark && !_settings.SaveMasterFlat)
        {
            return;
        }

        string logDirectory = Path.Combine(outputRoot, "logs");
        if (_settings.SaveMasterDark && _settings.MasterDark is { } dark)
        {
            Frame master = dark.Channels == 1
                ? dark
                : new Frame(dark.Width, dark.Height, 1, dark.BitDepth, QualityMetrics.ToLuma(dark));
            MasterFrameBuilder.WriteMaster(master, Path.Combine(logDirectory, "pipp_master_dark.tif"));
        }

        if (_settings.SaveMasterFlat && _settings.MasterFlat is { } flat)
        {
            string name = flat.Channels == 3 ? "pipp_master_flat.tif" : "pipp_master_flat_dark.tif";
            MasterFrameBuilder.WriteMaster(flat, Path.Combine(logDirectory, name));
        }
    }

    /// <summary>The channel order <c>-rgb</c> uses for its file prefixes: <c>r_</c>, <c>g_</c>, <c>b_</c>.</summary>
    private static readonly (char Letter, int Channel)[] RgbChannels = [('r', 0), ('g', 1), ('b', 2)];

    /// <summary>
    /// Extracts one colour plane as a one-channel frame; a mono frame is cloned for every channel,
    /// which is what the engine's <c>-rgb</c> does with mono input (three identical files).
    /// </summary>
    private static Frame ExtractChannel(Frame frame, int channel)
    {
        if (frame.Channels == 1)
        {
            return frame.Clone();
        }

        int pixels = frame.Width * frame.Height;
        var samples = new ushort[pixels];
        for (int i = 0; i < pixels; i++)
        {
            samples[i] = frame.Samples[(i * 3) + channel];
        }

        return new Frame(frame.Width, frame.Height, 1, frame.BitDepth, samples);
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

/// <summary>
/// Presents several containers as one continuous frame stream: the meaning of <c>-join</c> in the
/// engine, which produces a single output and a single log named after the first input.
/// </summary>
/// <remarks>
/// Verified with the oracle: <c>-tif -join</c> over a 4-frame 32x24 capture and a 1-frame 640x640
/// capture writes <c>capture/1.tif</c>..<c>capture/5.tif</c> and one <c>capture_pipp_log.txt</c>
/// whose "Source image files" section lists both paths and whose "Total input frames" is 5.
/// Mixing geometries in a SER/AVI output is an error, exactly like the engine's
/// "Frames of different sizes cannot written to an AVI or SER file".
/// </remarks>
public sealed class JoinedFrameSource : IFrameSource
{
    private readonly List<IFrameSource> _sources = [];
    private int _current;

    /// <summary>Opens every input and concatenates its frames.</summary>
    public JoinedFrameSource(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            foreach (string file in files) _sources.Add(FrameSourceFactory.Open(file));
        }
        catch
        {
            Dispose();
            throw;
        }

        if (_sources.Count == 0) throw new ArgumentException("no input files", nameof(files));
    }

    /// <inheritdoc />
    public int Width => _sources[0].Width;

    /// <inheritdoc />
    public int Height => _sources[0].Height;

    /// <inheritdoc />
    public int Channels => _sources[0].Channels;

    /// <inheritdoc />
    public int BitDepth => _sources[0].BitDepth;

    /// <inheritdoc />
    public int FrameCount => _sources.Sum(source => source.FrameCount);

    /// <inheritdoc />
    public BayerPattern Bayer => _sources[0].Bayer;

    /// <inheritdoc />
    public Frame? ReadFrame()
    {
        while (_current < _sources.Count)
        {
            Frame? frame = _sources[_current].ReadFrame();
            if (frame is not null) return frame;
            _current++;
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var source in _sources) source.Dispose();
        _sources.Clear();
    }
}
