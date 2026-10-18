using System.Globalization;
using System.Text;
using Opipp.Core.Alignment;
using Opipp.Core.Diagnostics;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Processing;
using Opipp.Core.Quality;
using Opipp.Core.Stacking;

namespace Opipp.Core.Pipeline;

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

    /// <summary>Frames dropped because the tracked displacement pushed the AOI off the frame.</summary>
    public int DiscardedNoAoi { get; init; }

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
    /// <summary>Normalise each Bayer phase separately when dividing by the flat (<c>-flat_bayer</c>).</summary>
    public bool FlatHasBayer { get; set; }

    public Frame? MasterFlat { get; set; }

    /// <summary>
    /// Master flat-dark frame (<c>-flat_dark_tmp</c>). When set, it is subtracted from the master flat
    /// (clamped at zero) before the flat is used, which is what <c>-flat_dark</c> asks the engine to do.
    /// </summary>
    public Frame? MasterFlatDark { get; set; }

    /// <summary>Write <c>logs/pipp_master_flat_dark.tif</c> from the loaded flat-dark (<c>-save_flat_dark</c>).</summary>
    public bool SaveMasterFlatDark { get; set; }

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
    public bool OpippLayout { get; set; } = true;

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

    /// <summary>
    /// Area of interest (<c>-aoi</c> and <c>-aoi_x/y/width/height</c>). The box confines the tracking
    /// region and, with <see cref="AreaOfInterestCrop"/>, becomes the delivered frame size.
    /// </summary>
    public PixelRect? AreaOfInterest { get; set; }

    /// <summary>
    /// Crop every delivered frame to <see cref="AreaOfInterest"/> (<c>-aoi_crop</c>). The engine only
    /// crops inside its surface-tracking path; a plain run with <c>-aoi_crop</c> writes a quarter-filled
    /// buffer (verified), so this implementation always performs the intended crop instead.
    /// </summary>
    public bool AreaOfInterestCrop { get; set; }

    /// <summary>
    /// Discard frames whose tracked displacement would carry <see cref="AreaOfInterest"/> outside the
    /// frame (<c>-aoi_reject</c>). Recovered from the engine's fine tracker (<c>FUN_1400a5f40</c>):
    /// the comparison is between the <em>moved AOI rectangle</em> and the frame, not between the AFB
    /// box and the AOI, and a rejection is reported as <c>no_aoi</c>.
    /// </summary>
    public bool AreaOfInterestReject { get; set; }

    /// <summary>Resize target; a dimension of -1 keeps the aspect ratio.</summary>
    public (int Width, int Height)? Resize { get; set; }

    /// <summary>Quality estimator.</summary>
    public QualityAlgorithm QualityAlgorithm { get; set; } = QualityAlgorithm.PippDefault;

    /// <summary>Keep only the best N frames per input file.</summary>
    public int? KeepFrames { get; set; }

    /// <summary>Keep only the best percentage of frames per input file.</summary>
    public double? KeepPercent { get; set; }

    /// <summary>
    /// Quality weighting (<c>-qw</c>): triplicate the top quarter of the selected frames and duplicate
    /// the second quarter, appending <c>a</c>/<c>b</c> to the frame number. The engine's help text and
    /// a 4/7/8/10-frame oracle sweep agree on the <c>n/4</c> and <c>n/2</c> boundaries.
    /// </summary>
    public bool QualityWeighting { get; set; }

    /// <summary>
    /// Process one frame into <c>pipp_input_frame.bmp</c> and <c>pipp_output_frame.bmp</c> at the output
    /// root instead of writing the requested output format (<c>-test_run</c>).
    /// </summary>
    public bool TestRun { get; set; }

    /// <summary>SER bit-depth override (<c>-ser_bpp</c>); null trusts the container header.</summary>
    public int? SerBitDepth { get; set; }

    /// <summary>
    /// Do not hold selected frames in RAM (<c>-no_ram_buff</c>). The engine's only observable effect is
    /// that quality selection is delivered in source order rather than score order; reproduced here.
    /// </summary>
    public bool NoRamBuffers { get; set; }

    /// <summary>
    /// Render the detection overlay instead of the frame (<c>-sdp</c>): mid-grey background, red pixels
    /// at or above the detection threshold, and a dashed dark-red box around the detected object.
    /// </summary>
    public bool ShowDetectedPixels { get; set; }

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

    /// <summary>
    /// Animation options (<c>-anim_*</c>). They only reshape multi-frame outputs: the engine builds
    /// its animation for AVI and GIF. With every field at its default the output is the plain frame
    /// order (one forward pass).
    /// </summary>
    public AnimationOptions Animation { get; set; } = new();

    /// <summary>
    /// WinJUPOS-friendly filename mode (<c>-wjpos_time</c>): 0 mid-point, 1 earliest, 2 first frame.
    /// <see langword="null"/> leaves naming off; the other <c>-wjpos_*</c> switches only supply the
    /// timestamp and never enable it on their own.
    /// </summary>
    public int? WinJuposTimeMode { get; set; }

    /// <summary>Date/time mask used to read a timestamp out of each input file name (<c>-wjpos_dtmask</c>).</summary>
    public string? WinJuposDateTimeMask { get; set; }

    /// <summary>Convert the extracted local timestamp to UT (<c>-wjpos_lt2ut</c>).</summary>
    public bool WinJuposLocalToUtc { get; set; }

    /// <summary>Frame rate used to step per-frame timestamps (<c>-wjpos_ipfps</c>); 0 disables stepping.</summary>
    public int WinJuposInputFps { get; set; }

    /// <summary>Write the legacy AVI layout (<c>-avi_old</c> / <c>-avi_old4</c>).</summary>
    public bool AviLegacyLayout { get; set; }
}

/// <summary>
/// The engine's animation switches (<c>-anim_*</c>). The sequence is a forward block, an optional
/// reverse block, and optional holds of each block's last frame; the whole thing can be played
/// several times. Every rule below was measured against <c>pipp_core.exe</c> on a four-frame capture.
/// </summary>
public sealed class AnimationOptions
{
    /// <summary>Play the frames forwards (<c>-anim_fwd</c>).</summary>
    public bool Forward { get; set; }

    /// <summary>Play the frames backwards (<c>-anim_rev</c>).</summary>
    public bool Reverse { get; set; }

    /// <summary>Extra copies of the last forward frame (<c>-anim_fwd_lf_rep</c>).</summary>
    public int ForwardLastFrameRepeats { get; set; }

    /// <summary>Extra copies of the last reverse frame (<c>-anim_rev_lf_rep</c>).</summary>
    public int ReverseLastFrameRepeats { get; set; }

    /// <summary>Extra plays of the whole animation (<c>-anim_all_rep</c>); the engine plays n+1 times.</summary>
    public int AllRepeats { get; set; }

    /// <summary>Leave the reverse block out of the final play (<c>-no_anim_rev_last_rep</c>).</summary>
    /// <remarks>
    /// Parsed and carried for round-tripping only. The 2.5.9 engine accepts the switch but writes the
    /// same AVI frame count (14/21/28 for <c>-anim_all_rep=1/2/3</c>) and the same GIF image count and
    /// loop value with and without it, so honouring it here would diverge.
    /// </remarks>
    public bool NoReverseInLastRepeat { get; set; }

    /// <summary>Loop the GIF forever (<c>-anim_all_rep_always</c>).</summary>
    public bool RepeatForever { get; set; }

    /// <summary>True when at least one block was requested, i.e. the output is an animation.</summary>
    public bool IsActive => Forward || Reverse;

    /// <summary>
    /// The repeat count written into the GIF's NETSCAPE loop block: <c>-anim_all_rep</c> verbatim,
    /// 0 ("forever") with <c>-anim_all_rep_always</c>, and 1 when repetitions were not requested.
    /// </summary>
    public int GifLoopCount => RepeatForever ? 0 : Math.Max(1, AllRepeats);
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
            discardedNoFeature = 0, discardedNoAoi = 0, discardedStart = 0, discardedInputDrop = 0, discardedOutputDrop = 0,
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
            int noAoiBefore = discardedNoAoi;
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
                if (_settings.TestRun)
                {
                    // -test_run inspects exactly one frame (the engine still prints the full estimate).
                    window = new FrameWindow(window.Start, window.Start + 1, window.InputDrop);
                }
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

                    if (_settings.TestRun)
                    {
                        // -test_run: one raw BMP and one processed BMP at the output root, no log-only
                        // folder and no requested output format (verified against the oracle).
                        WriteTestRunFrames(raw, frame, outputRoot);
                        accepted.Add((frame, 0, AlignmentOffset.Zero));
                        break;
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
                                // -aoi_reject (engine FUN_1400a5f40): the measurement moves the AOI
                                // rectangle with the tracked content, and the frame is discarded when
                                // the moved rectangle no longer fits in the frame. The test is on the
                                // AOI itself, not on the AFB box, so a full-frame AOI rejects every
                                // frame that moves at all (or even one that does not: its right edge
                                // lands on W, one past the last column).
                                if (_settings.AreaOfInterestReject && _settings.AreaOfInterest is PixelRect aoi &&
                                    MovesAreaOfInterestOutsideFrame(aoi, frame.Width, frame.Height, m))
                                {
                                    discardedNoAoi++;
                                    SaveRejected(frame, "no_aoi", index, rejectedDirectory, rejectedDigits);
                                    _protocol?.WriteLine($"  Frame {index} rejected: no AOI found");
                                    continue;
                                }

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

                if (_settings.TestRun)
                {
                    outputFrames++;
                    if (_settings.WriteLogFile)
                    {
                        WriteLogFiles(
                            unit.Sources,
                            baseName,
                            1,
                            1,
                            1,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            accepted.Count > 0 ? accepted[0].Frame : null,
                            null,
                            []);
                    }

                    filesProcessed++;
                    continue;
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
                            0,
                            0,
                            discardedNoObject - noObjectBefore,
                            discardedPartial - partialBefore,
                            discardedTooSmall - tooSmallBefore,
                            discardedNoFeature - noFeatureBefore,
                            discardedNoAoi - noAoiBefore,
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
                // -no_ram_buff has the same observable effect: the engine cannot sort buffers it did
                // not keep, so selected frames are delivered in source order (verified with -q=3).
                bool qualitySelection = _settings.KeepFrames is not null || _settings.KeepPercent is not null;
                if (qualitySelection && (!_settings.RenumberFrames || _settings.NoRamBuffers))
                {
                    written = written.OrderBy(entry => entry.Frame.Index).ToList();
                }

                // -qw duplicates the top quarter twice and the next quarter once, naming the copies
                // with a/b suffixes (oracle: -q=10 -> 00,00a,00b for the top two, 02a,03a,04a for the
                // next three, 05..09 single; "Created 7 duplicate images").
                var queue = new List<(Frame Frame, double Score, AlignmentOffset Offset, string Suffix)>(written.Count);
                int duplicated = 0;
                if (_settings.QualityWeighting && qualitySelection && written.Count > 0)
                {
                    int top = written.Count / 4;
                    int second = written.Count / 2;
                    for (int i = 0; i < written.Count; i++)
                    {
                        var entry = written[i];
                        queue.Add((entry.Frame, entry.Score, entry.Offset, string.Empty));
                        if (i < top)
                        {
                            queue.Add((entry.Frame, entry.Score, entry.Offset, "a"));
                            queue.Add((entry.Frame, entry.Score, entry.Offset, "b"));
                            duplicated += 2;
                        }
                        else if (i < second)
                        {
                            queue.Add((entry.Frame, entry.Score, entry.Offset, "a"));
                            duplicated++;
                        }
                    }
                }
                else
                {
                    foreach (var entry in written)
                    {
                        queue.Add((entry.Frame, entry.Score, entry.Offset, string.Empty));
                    }
                }

                if (_settings.Stack is StackMethod method)
                {
                    string masterPath = Path.Combine(outputRoot, baseName + _settings.OutputExtension);
                    var first = ApplyGeometry(queue[0].Frame, queue[0].Offset, shiftFill);
                    var stacker = new FrameStacker(first.Width, first.Height, first.Channels, first.BitDepth, method);
                    foreach (var (frame, _, offset, _) in queue)
                    {
                        stacker.Add(ApplyGeometry(frame, offset, shiftFill));
                    }
                    var master = ApplyOverlay(stacker.Result(), detectionThreshold);
                    using var sink = FrameSinkFactory.Create(masterPath, master, 1, _settings.Fps,
                        _settings.AviCodec, _settings.AviQuality);
                    sink.Write(master);
                    outputFrames++;
                    outputs.Add(masterPath);
                    _protocol?.WriteLine($"  {queue.Count} frames stacked -> {Path.GetFileName(masterPath)}");
                }
                else if (_settings.OutputExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    // One animated file at the output root: unlike TIFF/PNG the engine does not use a
                    // per-input folder for GIF. The original writes an image-less GIF (no image
                    // descriptors at all), so writing a valid animation is a deliberate divergence.
                    // -rgb has no effect on GIF in the engine (one file), and neither do -gif_border_tol
                    // or -gif_lossy_comp, which only ever shaped image blocks that were never written.
                    string gifPath = Path.Combine(outputRoot, baseName + ".gif");
                    var gifFrames = new List<Frame>(queue.Count);
                    foreach (var (frame, _, offset, _) in queue)
                    {
                        gifFrames.Add(ApplyOverlay(ApplyGeometry(frame, offset, shiftFill), detectionThreshold));
                    }

                    // The engine's animation blocks apply to GIF too, but with two differences
                    // measured against pipp_core: last-frame holds are collapsed (identical
                    // consecutive frames produce one image) and the ping-pong's closing frame is
                    // dropped because it repeats the first image at the loop seam. Repetitions never
                    // duplicate frames; they become the NETSCAPE loop count instead.
                    GifOptions gifOptions = _settings.GifOptions;
                    if (_settings.Animation.IsActive)
                    {
                        gifFrames = BuildGifAnimation(gifFrames);
                        gifOptions = gifOptions with { LoopCount = _settings.Animation.GifLoopCount };
                    }

                    outputFrames += gifFrames.Count;
                    int delay = Math.Max(1, (int)Math.Round(100.0 / Math.Max(1, _settings.Fps)));
                    GifWriter.Write(gifPath, gifFrames, gifOptions with { DelayCentiseconds = delay });
                    outputs.Add(gifPath);
                }
                else if (IsMultiFrameContainer(_settings.OutputExtension))
                {
                    // SER/AVI hold the whole run in one file; joining inputs of different geometry
                    // is exactly the case the engine refuses ("Frames of different sizes ...").
                    var template = ApplyGeometry(queue[0].Frame, queue[0].Offset, shiftFill);
                    foreach (var (frame, _, offset, _) in queue)
                    {
                        var check = ApplyGeometry(frame, offset, shiftFill);
                        if (check.Width != template.Width || check.Height != template.Height)
                        {
                            throw new InvalidDataException(
                                "Frames of different sizes cannot written to an AVI or SER file");
                        }
                    }

                    // Only AVI carries an animation: with -ser the engine writes per-frame .tmp
                    // dumps, reports "Total output frames: 0" and exits non-zero, a defect this
                    // implementation does not reproduce (SER stays a plain frame stream).
                    bool animate = _settings.Animation.IsActive &&
                        _settings.OutputExtension.Equals(".avi", StringComparison.OrdinalIgnoreCase);
                    List<Frame> animated = animate
                        ? BuildAviAnimation(queue, shiftFill, detectionThreshold)
                        : BuildGeometryFrames(queue, shiftFill, detectionThreshold);
                    int frameCount = animated.Count;
                    string containerName = WinJuposName(unit, baseName, window);

                    if (_settings.SplitRgb)
                    {
                        // -rgb writes three containers, one per channel. The engine's name is
                        // <channel>_<prefix><name>.<suffix><ext> — with no suffix this yields the
                        // doubled dot seen in the oracle ("r_colour..ser"), reproduced here.
                        string stem = Path.GetFileNameWithoutExtension(unit.Name);
                        string splitBase = _settings.Prefix + stem + "." + _settings.Suffix + _settings.OutputExtension;
                        string splitPrefix = WinJuposPrefix(unit, window);
                        var sinks = new List<IFrameSink>(3);
                        try
                        {
                            foreach ((char letter, int channel) in RgbChannels)
                            {
                                Frame part = ExtractChannel(template, channel);
                                string path = Path.Combine(outputRoot, splitPrefix + letter + "_" + splitBase);
                                sinks.Add(FrameSinkFactory.Create(path, part, frameCount, _settings.Fps,
                                    _settings.AviCodec, _settings.AviQuality, _settings.AviLegacyLayout));
                                outputs.Add(path);
                            }

                            foreach (Frame output in animated)
                            {
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
                        string outputPath = Path.Combine(outputRoot, containerName + _settings.OutputExtension);
                        using var sink = FrameSinkFactory.Create(outputPath, template, frameCount, _settings.Fps,
                            _settings.AviCodec, _settings.AviQuality, _settings.AviLegacyLayout);
                        foreach (Frame output in animated)
                        {
                            sink.Write(output);
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
                    string frameDirectory = _settings.OpippLayout
                        ? Path.Combine(outputRoot, baseName)
                        : outputRoot;
                    if (_settings.OpippLayout)
                    {
                        Directory.CreateDirectory(frameDirectory);
                    }

                    int firstNumber = _settings.OpippLayout ? (qualitySelection ? 0 : 1) : 0;

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
                    for (int q = 0; q < queue.Count; q++)
                    {
                        var (frame, score, offset, suffix) = queue[q];
                        if (q > 0 && suffix.Length == 0) number++;
                        var output = ApplyOverlay(ApplyGeometry(frame, offset, shiftFill), detectionThreshold);
                        // -nr names a plain run's frames after their source number (1-based:
                        // -f=4 -nr wrote 01.tif..04.tif), but a quality run stays sequential because
                        // the engine has already reordered the stream itself.
                        string stem = _settings.RenumberFrames || qualitySelection
                            ? number.ToString(numberFormat, CultureInfo.InvariantCulture) + suffix
                            : (output.Index + 1).ToString(numberFormat, CultureInfo.InvariantCulture) + suffix;

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
                    }
                }

                if (_settings.WriteLogFile)
                {
                    WriteLogFiles(
                        unit.Sources,
                        baseName,
                        window.Limit,
                        outputFrames - outputBefore,
                        written.Count,
                        duplicated,
                        dropped,
                        discardedNoObject - noObjectBefore,
                        discardedPartial - partialBefore,
                        discardedTooSmall - tooSmallBefore,
                        discardedNoFeature - noFeatureBefore,
                        discardedNoAoi - noAoiBefore,
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
            DiscardedNoAoi = discardedNoAoi,
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
        int uniqueOutputCount,
        int duplicatedCount,
        int droppedByQuality,
        int droppedNoObject,
        int droppedPartial,
        int droppedTooSmall,
        int droppedNoFeature,
        int droppedNoAoi,
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
        if (droppedNoAoi > 0)
        {
            text.Append("  Frames discarded with no AOI found: ").Append(droppedNoAoi.ToString(CultureInfo.InvariantCulture)).Append((char)13).Append((char)10);
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
        if (duplicatedCount > 0)
        {
            // -qw: the engine separates unique and duplicate counts (oracle: 7/4/11).
            text.Append("  Total unique output frames: ").Append(uniqueOutputCount.ToString(CultureInfo.InvariantCulture)).Append(":\r\n");
            text.Append("  Total duplicated output frames: ").Append(duplicatedCount.ToString(CultureInfo.InvariantCulture)).Append(":\r\n");
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
                    .Append(FormatQuality(entry.Score)).Append('\t')
                    .Append(percent.ToString(CultureInfo.InvariantCulture)).Append("%\r\n");
            }

            File.WriteAllText(Path.Combine(logDirectory, baseName + "_pipp_quality.txt"), quality.ToString());
        }
    }

    private static string QualityAlgorithmLabel(QualityAlgorithm algorithm) => algorithm switch
    {
        QualityAlgorithm.Histogram => "Histogram",
        QualityAlgorithm.Ninox => "Old (As taken from Ninox)",
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
            Frame flat = _settings.MasterFlat;
            if (_settings.MasterFlatDark is not null)
            {
                // -flat_dark: the flat-dark is removed from the flat before normalisation.
                flat = Calibration.SubtractDark(flat, _settings.MasterFlatDark);
            }
            frame = _settings.FlatHasBayer
                ? Calibration.DivideFlatBayer(frame, flat)
                : Calibration.DivideFlat(frame, flat);
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
            return [new InputUnit(files[0], files, () => new JoinedFrameSource(files, _settings.SerBitDepth))];
        }

        return files.Select(f => new InputUnit(f, [f], () => FrameSourceFactory.Open(f, _settings.SerBitDepth))).ToList();
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
        if (!_settings.SaveMasterDark && !_settings.SaveMasterFlat && !_settings.SaveMasterFlatDark)
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

        if (_settings.SaveMasterFlatDark && _settings.MasterFlatDark is { } flatDark)
        {
            // -save_flat_dark: the engine's name for the flat-dark master (FUN_140090870).
            MasterFrameBuilder.WriteMaster(flatDark, Path.Combine(logDirectory, "pipp_master_flat_dark.tif"));
        }
    }

    /// <summary>Writes the two BMPs <c>-test_run</c> produces at the output root.</summary>
    /// <remarks>
    /// The engine names the files <c>.bmp</c> but writes TIFF payloads into them (verified: both files
    /// start with <c>II*</c> and are byte-identical to the run's TIFF output). The quirk is reproduced
    /// rather than corrected.
    /// </remarks>
    private static void WriteTestRunFrames(Frame raw, Frame processed, string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        using (var input = new TiffWriter(
            Path.Combine(outputRoot, "pipp_input_frame.bmp"), raw.Width, raw.Height, raw.Channels, raw.BitDepth))
        {
            input.Write(raw);
        }

        using var output = new TiffWriter(
            Path.Combine(outputRoot, "pipp_output_frame.bmp"), processed.Width, processed.Height,
            processed.Channels, processed.BitDepth);
        output.Write(processed);
    }

    // ------------------------------------------------------------------ animation

    /// <summary>Applies the geometry and overlay stages to every queued frame, in queue order.</summary>
    private List<Frame> BuildGeometryFrames(
        List<(Frame Frame, double Score, AlignmentOffset Offset, string Suffix)> queue,
        ushort? shiftFill,
        int? detectionThreshold)
    {
        var frames = new List<Frame>(queue.Count);
        foreach (var (frame, _, offset, _) in queue)
        {
            frames.Add(ApplyOverlay(ApplyGeometry(frame, offset, shiftFill), detectionThreshold));
        }

        return frames;
    }

    /// <summary>
    /// Builds the frame sequence written to AVI. The engine's animation is a forward block, an
    /// optional reverse block that stops one frame short of the turn (so the last forward frame is
    /// not written twice), optional holds of each block's last frame, and <c>-anim_all_rep</c> + 1
    /// plays of the whole thing. Measured on a four-frame capture: <c>-anim_fwd -anim_rev</c> writes
    /// 1 2 3 4 3 2 1, <c>-anim_fwd_lf_rep=2</c> appends two more 4s, <c>-anim_rev_lf_rep=2</c>
    /// appends two more 1s, and <c>-anim_all_rep=2</c> writes three copies.
    /// </summary>
    private List<Frame> BuildAviAnimation(
        List<(Frame Frame, double Score, AlignmentOffset Offset, string Suffix)> queue,
        ushort? shiftFill,
        int? detectionThreshold)
    {
        List<Frame> plain = BuildGeometryFrames(queue, shiftFill, detectionThreshold);
        if (plain.Count == 0)
        {
            return plain;
        }

        List<Frame> sequence = BuildAnimationBlocks(plain);
        int plays = Math.Max(1, _settings.Animation.AllRepeats + 1);
        if (plays == 1)
        {
            return sequence;
        }

        var repeated = new List<Frame>(sequence.Count * plays);
        for (int play = 0; play < plays; play++)
        {
            repeated.AddRange(sequence);
        }

        return repeated;
    }

    /// <summary>
    /// The GIF variant of <see cref="BuildAviAnimation"/>. GIF never duplicates frames for
    /// <c>-anim_all_rep</c> (that becomes the loop count), collapses consecutive identical frames
    /// (which removes the last-frame holds the AVI writer emits), and drops the closing frame of a
    /// ping-pong because it repeats the first image across the loop seam. Verified against
    /// <c>pipp_core.exe</c>: a four-frame capture yields 4 images for <c>-anim_fwd -anim_rev</c>,
    /// 6 for <c>-anim_fwd -anim_rev -anim_rev_lf_rep=3</c>, and 3 for <c>-anim_fwd -anim_fwd_lf_rep=2</c>.
    /// </summary>
    private List<Frame> BuildGifAnimation(List<Frame> frames)
    {
        var blocks = BuildAnimationBlocks(frames);
        var unique = new List<Frame>(blocks.Count);
        foreach (Frame frame in blocks)
        {
            if (unique.Count == 0 || !FramesEqual(unique[^1], frame))
            {
                unique.Add(frame);
            }
        }

        if (unique.Count > 1 && FramesEqual(unique[0], unique[^1]))
        {
            unique.RemoveAt(unique.Count - 1);
        }

        return unique;
    }

    private List<Frame> BuildAnimationBlocks(List<Frame> frames)
    {
        AnimationOptions animation = _settings.Animation;
        var sequence = new List<Frame>(frames.Count * 3);

        if (animation.Forward)
        {
            sequence.AddRange(frames);
            for (int hold = 0; hold < animation.ForwardLastFrameRepeats; hold++)
            {
                sequence.Add(frames[^1]);
            }
        }

        if (animation.Reverse)
        {
            // A ping-pong does not repeat the turn frame: after 1 2 3 4 the reverse starts at 3.
            int start = animation.Forward ? frames.Count - 2 : frames.Count - 1;
            for (int i = start; i >= 0; i--)
            {
                sequence.Add(frames[i]);
            }

            for (int hold = 0; hold < animation.ReverseLastFrameRepeats; hold++)
            {
                sequence.Add(frames[0]);
            }
        }

        return sequence.Count == 0 ? frames : sequence;
    }

    private static bool FramesEqual(Frame left, Frame right) =>
        left.Width == right.Width && left.Height == right.Height && left.Channels == right.Channels &&
        left.BitDepth == right.BitDepth && left.Samples.AsSpan().SequenceEqual(right.Samples);

    // ------------------------------------------------------------------ WinJUPOS names

    /// <summary>
    /// The output stem the engine uses when <c>-wjpos_time</c> is on: a
    /// <c>yyyy-MM-dd-HHmm_t-</c> prefix in front of the normal name. Only single-file containers
    /// (SER and AVI) are renamed; still images keep the numbered PIPP layout and GIF keeps its
    /// plain name, exactly as observed.
    /// </summary>
    private string WinJuposName(InputUnit unit, string baseName, FrameWindow window) =>
        WinJuposPrefix(unit, window) + baseName;

    /// <summary>
    /// The <c>yyyy-MM-dd-HHmm_t-</c> prefix alone (empty when <c>-wjpos_time</c> is off), for the
    /// <c>-rgb</c> names that put their channel letter before the input stem.
    /// </summary>
    private string WinJuposPrefix(InputUnit unit, FrameWindow window)
    {
        if (_settings.WinJuposTimeMode is not int mode)
        {
            return string.Empty;
        }

        DateTime stamp = SelectWinJuposTimestamp(unit, window, mode);
        return FormatWinJuposPrefix(stamp) + "-";
    }

    /// <summary>
    /// Picks the timestamp written into the name. The engine prefers the per-frame timestamps in a
    /// SER trailer (frame count × 8 bytes after the image data), falls back to the input file name
    /// parsed with <c>-wjpos_dtmask</c> stepped by <c>-wjpos_ipfps</c>, and writes
    /// <c>0001-01-01-0000_0</c> when neither source is available. Mode 0 averages the earliest and
    /// latest timestamps, 1 takes the earliest, 2 takes the first frame's timestamp.
    /// </summary>
    private DateTime SelectWinJuposTimestamp(InputUnit unit, FrameWindow window, int mode)
    {
        long[]? trailer = ReadSerTrailerTimestamps(unit.Name);
        int first = Math.Clamp(window.Start, 0, Math.Max(0, trailer?.Length - 1 ?? int.MaxValue));

        DateTime? parsed = null;
        if (trailer is null && _settings.WinJuposDateTimeMask is { Length: > 0 } mask &&
            TryParseWinJuposTimestamp(Path.GetFileName(unit.Name), mask, out DateTime masked))
        {
            parsed = masked;
        }

        double fps = _settings.WinJuposInputFps;
        long TicksAt(int frameIndex)
        {
            if (trailer is not null)
            {
                return frameIndex >= 0 && frameIndex < trailer.Length ? trailer[frameIndex] : 0;
            }

            if (parsed is not { } baseTime || fps <= 0)
            {
                return 0;
            }

            return baseTime.Ticks + (long)Math.Round(frameIndex * TimeSpan.TicksPerSecond / fps);
        }

        int frames = 0;
        if (trailer is not null)
        {
            frames = trailer.Length;
        }
        else if (parsed is not null && fps > 0)
        {
            frames = Math.Max(0, window.Limit - first);
        }

        long min = long.MaxValue, max = long.MinValue, firstTicks = 0;
        for (int i = 0; i < Math.Max(1, frames); i++)
        {
            long ticks = TicksAt(first + i);
            if (i == 0)
            {
                firstTicks = ticks;
            }

            if (ticks < min) min = ticks;
            if (ticks > max) max = ticks;
        }

        long chosen = mode switch
        {
            1 => min,
            2 => firstTicks,
            _ => min == long.MaxValue ? 0 : (min + max) / 2,
        };

        DateTime stamp = chosen > 0 && chosen <= DateTime.MaxValue.Ticks
            ? new DateTime(chosen, DateTimeKind.Unspecified)
            : DateTime.MinValue;

        // -wjpos_lt2ut converts the local time the mask parsed out of the file name; the SER
        // trailer already stores UTC, which is why the switch changes nothing for those files.
        // UTC = local - offset (an EDT offset of -4 h therefore moves 00:55 to 04:55, which is what
        // the engine writes).
        if (_settings.WinJuposLocalToUtc && trailer is null && stamp > DateTime.MinValue)
        {
            stamp -= TimeZoneInfo.Local.GetUtcOffset(stamp);
        }

        return stamp;
    }

    /// <summary>
    /// Formats a timestamp the way the engine's WinJUPOS prefix does: the time is rounded to the
    /// nearest tenth of a minute (6 s, halves up) and written <c>yyyy-MM-dd-HHmm_t</c>. Rounding to
    /// 10 carries into the next minute, hour, day, month and year.
    /// </summary>
    public static string FormatWinJuposPrefix(DateTime stamp)
    {
        long intoMinute = stamp.Ticks % TimeSpan.TicksPerMinute;
        int tenths = (int)((intoMinute + (3 * TimeSpan.TicksPerSecond)) /
                           (6 * TimeSpan.TicksPerSecond));
        DateTime rounded = new DateTime(stamp.Year, stamp.Month, stamp.Day, stamp.Hour, stamp.Minute, 0,
            stamp.Kind).AddSeconds(tenths * 6);

        // tenths == 10 (>= 57 s) has already carried into the next minute above.
        int digit = (int)((rounded.Ticks % TimeSpan.TicksPerMinute) / (6 * TimeSpan.TicksPerSecond));
        return rounded.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + "_" +
               digit.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the SER v3 trailer of per-frame timestamps (8 bytes each, in .NET ticks) when the file
    /// carries one. Returns <see langword="null"/> for other containers, truncated files and files
    /// whose trailer is not exactly frame-count × 8 bytes.
    /// </summary>
    private static long[]? ReadSerTrailerTimestamps(string path)
    {
        if (!Path.GetExtension(path).Equals(".ser", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            SerInfo info = SerProbe.Probe(path);
            if (!info.Header.IsStructurallyValid || info.FileSizeBytes is not long size ||
                info.Header.FrameCount <= 0 || info.ExtraBytes != info.Header.FrameCount * 8L)
            {
                return null;
            }

            var stamps = new long[info.Header.FrameCount];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Seek(size - info.ExtraBytes, SeekOrigin.Begin);
            var buffer = new byte[8];
            for (int i = 0; i < stamps.Length; i++)
            {
                stream.ReadExactly(buffer, 0, 8);
                stamps[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buffer);
            }

            return stamps;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a timestamp out of an input file name using PIPP's date/time mask. Tokens:
    /// <c>YYYY</c>/<c>YY</c> year, <c>MM</c> month, <c>DD</c> day, <c>hh</c> hour, <c>mm</c> minute,
    /// <c>ss</c> second, <c>t</c> one tenth-of-a-minute digit (six seconds) and <c>?</c> any one
    /// character; every other character must appear literally. Text after the mask (the extension)
    /// is ignored, matching the engine's <c>date_time_check</c>.
    /// </summary>
    public static bool TryParseWinJuposTimestamp(string fileName, string mask, out DateTime value)
    {
        value = default;
        string text = Path.GetFileName(fileName);
        int pos = 0, token = 0;
        int year = 1, month = 1, day = 1, hour = 0, minute = 0, second = 0;
        bool sawYear = false;

        bool Digits(int count, out int number)
        {
            number = 0;
            if (pos + count > text.Length)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                char c = text[pos + i];
                if (c is < '0' or > '9')
                {
                    return false;
                }

                number = (number * 10) + (c - '0');
            }

            pos += count;
            return true;
        }

        while (token < mask.Length)
        {
            char c = mask[token];
            switch (c)
            {
                case 'Y':
                {
                    int run = 1;
                    while (token + run < mask.Length && mask[token + run] == 'Y') run++;
                    if (!Digits(run >= 4 ? 4 : 2, out int parsedYear))
                    {
                        return false;
                    }

                    year = run >= 4 ? parsedYear : 2000 + parsedYear;
                    sawYear = true;
                    token += run >= 4 ? 4 : 2;
                    continue;
                }

                case 'M':
                case 'D':
                case 'h':
                case 'm':
                case 's':
                {
                    if (!Digits(2, out int number))
                    {
                        return false;
                    }

                    switch (c)
                    {
                        case 'M': month = number; break;
                        case 'D': day = number; break;
                        case 'h': hour = number; break;
                        case 'm': minute = number; break;
                        default: second = number; break;
                    }

                    token += 2;
                    continue;
                }

                case 't':
                    if (!Digits(1, out int tenth)) return false;
                    second += tenth * 6;
                    token++;
                    continue;

                case 'N':
                    if (!Digits(1, out _)) return false;
                    token++;
                    continue;

                case '?':
                    if (pos >= text.Length) return false;
                    pos++;
                    token++;
                    continue;
            }

            if (pos >= text.Length || text[pos] != c)
            {
                return false;
            }

            pos++;
            token++;
        }

        if (!sawYear || year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) ||
            hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return true;
    }

    /// <summary>
    /// Applies the alignment shift and the area-of-interest crop to one delivered frame. The engine
    /// crops its stabilised frame at <c>(aoi_x - 1, aoi_y + 2)</c> (a constant offset verified over
    /// four boxes); this implementation crops at the requested origin, because our alignment already
    /// differs from the engine's and reproducing the offset would not make the pixels match.
    /// </summary>
    private Frame ApplyGeometry(Frame frame, AlignmentOffset offset, ushort? shiftFill)
    {
        Frame output = offset.Dx == 0 && offset.Dy == 0
            ? frame
            : FrameAligner.ShiftWithBackground(frame, offset, shiftFill);

        if (_settings.AreaOfInterestCrop && _settings.AreaOfInterest is PixelRect aoi &&
            aoi.Width > 0 && aoi.Height > 0)
        {
            int x = Math.Clamp(aoi.X, 0, Math.Max(0, output.Width - 1));
            int y = Math.Clamp(aoi.Y, 0, Math.Max(0, output.Height - 1));
            int width = Math.Min(aoi.Width, output.Width - x);
            int height = Math.Min(aoi.Height, output.Height - y);
            if (width > 0 && height > 0)
            {
                output = GeometricTransforms.Crop(output, x, y, width, height);
            }
        }

        return output;
    }

    /// <summary>
    /// True when translating <paramref name="aoi"/> by the measured content displacement would push
    /// it outside the frame — the engine's <c>-2</c> ("no AOI") result. The engine compares the
    /// <em>moved</em> AOI rectangle with the frame (FUN_1400a5f40: <c>base.x - aoi.x</c> through
    /// <c>frameW + base.x - aoi.x - aoi.w - 1</c>), and uses the same whole-pixel displacement the
    /// shift stage applies.
    /// </summary>
    private static bool MovesAreaOfInterestOutsideFrame(PixelRect aoi, int width, int height, AlignmentOffset measured)
    {
        int dx = (int)Math.Round(measured.Dx, MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round(measured.Dy, MidpointRounding.AwayFromZero);
        return aoi.X + dx < 0
            || aoi.Y + dy < 0
            || (long)aoi.X + dx + aoi.Width > width
            || (long)aoi.Y + dy + aoi.Height > height;
    }

    /// <summary>Renders the <c>-sdp</c> detection overlay when that switch is on.</summary>
    private Frame ApplyOverlay(Frame frame, int? detectionThreshold)
    {
        if (!_settings.ShowDetectedPixels) return frame;
        int threshold = detectionThreshold ?? _settings.MinPixelValue ?? DetectionThreshold.ComputeOrDefault(frame);
        return DetectionOverlay.Render(frame, threshold, _settings.MinSize);
    }

    /// <summary>The channel order <c>-rgb</c> uses for its file prefixes: <c>r_</c>, <c>g_</c>, <c>b_</c>.</summary>
    private static readonly (char Letter, int Channel)[] RgbChannels = [('r', 0), ('g', 1), ('b', 2)];

    /// <summary>
    /// Extracts one colour plane as a one-channel frame; a mono frame is cloned for every channel,
    /// which is what the engine's <c>-rgb</c> does with mono input (three identical files).
    /// </summary>
    /// <summary>Formats a quality score the way the engine's printf does, including <c>1.#INF00</c>.</summary>
    private static string FormatQuality(double score) =>
        double.IsPositiveInfinity(score) ? "1.#INF00"
        : double.IsNegativeInfinity(score) ? "-1.#INF00"
        : double.IsNaN(score) ? "-1.#IND00"
        : score.ToString("0.000000", CultureInfo.InvariantCulture);

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

        return new Frame(frame.Width, frame.Height, 1, frame.BitDepth, samples)
        {
            Index = frame.Index,
            SourceName = frame.SourceName,
            Bayer = frame.Bayer,
        };
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
    /// <param name="files">Input containers, in order.</param>
    /// <param name="serBitDepthOverride">Optional SER bit-depth override (<c>-ser_bpp</c>).</param>
    public JoinedFrameSource(IEnumerable<string> files, int? serBitDepthOverride = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            foreach (string file in files) _sources.Add(FrameSourceFactory.Open(file, serBitDepthOverride));
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
