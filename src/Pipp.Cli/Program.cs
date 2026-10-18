using System.Globalization;
using System.Runtime.CompilerServices;
using Pipp.Core.Alignment;
using Pipp.Core.Cli;
using Pipp.Core.Diagnostics;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Pipp.Core.Pipeline;
using Pipp.Core.Processing;
using Pipp.Core.Quality;

[assembly: InternalsVisibleTo("Pipp.Cli.Tests")]

namespace Pipp.Cli;

/// <summary>
/// Managed replacement for <c>pipp_core.exe</c>. Parses the same switch vocabulary as the original
/// engine, runs <see cref="Processor"/>, prints a PIPP-style summary and, with <c>-gui</c>, emits the
/// same <c>@TOKEN</c> progress protocol the WinForms front-end already understands.
/// </summary>
internal sealed class DelegateProgress(Action<PipelineProgress> handler) : IProgress<PipelineProgress>
{
    public void Report(PipelineProgress value) => handler(value);
}

internal static class Program
{
    internal static int Main(string[] args)
    {
        // The WinForms front-end launches the engine with no argv and writes the option lines
        // followed by the input file paths to stdin (see Form1.send_arguments_to_pipp_process).
        // Accept both that protocol and a conventional command line.
        var effectiveArgs = args.Length > 0 ? args : ReadStandardInputArguments();
        var parsed = CommandLineParser.Parse(effectiveArgs);

        if (parsed.WantsHelp)
        {
            Console.WriteLine(PippHelpText.Text);
            return 0;
        }

        if (!parsed.Success)
        {
            foreach (var error in parsed.Errors)
            {
                Console.Error.WriteLine($"Error: {error}");
            }
            Console.Error.WriteLine("Use '-help' to display help");
            // the original exits 127 on an unrecognised switch; keep that contract detectable
            return parsed.Errors.Any(e => e.StartsWith("Unknown switch", StringComparison.Ordinal)) ? 127 : 2;
        }

        var options = parsed.Options;
        var inputs = parsed.Inputs;
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("Error: No input files specified");
            Console.Error.WriteLine("Use '-help' to display help");
            return 2;
        }

        PipelineSettings settings;
        try
        {
            if (options.DebugAvi)
            {
                // -debugavi is a standalone diagnostic dump: the engine prints the banner and stops
                // without writing any output (verified on SER and AVI inputs).
                foreach (string input in inputs)
                {
                    Console.WriteLine("*** Debug AVI mode ***");
                    Console.WriteLine();
                    Console.WriteLine($"Filename: '{input}'");
                    Console.WriteLine();
                }

                return 0;
            }

            settings = Map(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        // The log echoes the switch list only; input paths appear under "Source image files".
        settings.LogArguments = effectiveArgs.Where(a => !inputs.Contains(a, StringComparer.Ordinal)).ToArray();

        // -gui drives the WinForms front-end: the transcript is a fixed token/text protocol, not
        // the console report, and it has to match pipp_core.exe byte-for-byte (see GuiTranscript).
        if (options.Gui)
        {
            try
            {
                return RunGui(inputs, settings);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        // The console path never emits protocol tokens; -gui returned above with its own transcript.
        EngineProtocolWriter? protocol = null;
        // Progress<T> posts callbacks to the thread pool, which reorders the percentage lines;
        // the pipeline reports from a single thread, so invoke the handler inline.
        var progress = new DelegateProgress(p =>
        {
            if (options.Gui)
            {
                return; // the protocol writer carries progress; keep stdout machine-clean
            }
            Console.Write(string.Create(CultureInfo.InvariantCulture, $"\r{p.Percent,3}%  {p.Message,-45}"));
            if (p.Percent >= 100) Console.WriteLine();
        });

        var processor = new Processor(settings, progress, protocol);

        if (!options.Gui)
        {
            Console.WriteLine($"PIPP (managed engine) {typeof(Program).Assembly.GetName().Version}");
            Console.WriteLine($"Processing: {(options.Join ? "(Join mode)" : "(Batch mode)")}");
            Console.WriteLine($"  Input files: {inputs.Count}");
        }

        ProcessingReport report;
        try
        {
            report = processor.Run(inputs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        PrintSummary(report, inputs, options, settings);

        // The engine exits -1 (0xFFFFFFFF) and says so on stderr when every frame was discarded;
        // keep that contract so the GUI can distinguish "nothing produced" from a crash.
        if (report.OutputFrames == 0)
        {
            Console.Error.WriteLine($"Error: All frames have been discarded from {report.InputFrames} input frames");
            return -1;
        }

        return 0;
    }

    /// <summary>
    /// Runs the pipeline in the WinForms engine protocol (<c>-gui</c>). The original prints a fixed
    /// preamble, a per-input estimate block, one progress token per frame and a summary; the
    /// front-end consumes exactly that sequence, so <see cref="GuiTranscript"/> owns all formatting.
    /// </summary>
    private static int RunGui(IReadOnlyList<string> inputs, PipelineSettings settings)
    {
        var transcript = new GuiTranscript(Console.Out);
        int fileCount = inputs.Count;
        transcript.Begin(fileCount);

        var probes = new List<InputProbe>(fileCount);
        foreach (string input in inputs) probes.Add(Probe(input, settings));

        bool qualityRan = settings.KeepFrames is not null || settings.KeepPercent is not null;
        string format = FormatLabel(settings.OutputExtension);
        string directory = OutputDirectoryText(settings);
        string qualityLabel = QualityLabel(settings.QualityAlgorithm);
        bool animation = (settings.OutputExtension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
                          settings.OutputExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase)) &&
                         settings.Animation.IsActive;
        bool aviAnimation = settings.OutputExtension.Equals(".avi", StringComparison.OrdinalIgnoreCase);
        int zeroOutputs = 0;

        // -join presents every container as one stream: one estimate, one "Processing N frames",
        // one summary, but the name/progress tokens advance when the stream crosses an input.
        if (settings.JoinMode && fileCount > 1)
        {
            int totalFrames = probes.Sum(p => p.Frames);
            transcript.BeginJoin(inputs, totalFrames);
            int buffers = qualityRan ? BufferCount(settings, totalFrames) : 0;
            var reporter = new JoinedProgress(transcript, probes, inputs, buffers);
            var processor = new Processor(settings, new DelegateProgress(reporter.Report), null);
            ProcessingReport report = processor.Run(inputs);

            transcript.Complete();
            if (qualityRan) transcript.Quality(report.OutputFrames);
            if (animation) transcript.Animation(AnimationFrameCount(totalFrames, settings.Animation), aviAnimation);
            transcript.Summary(
                report,
                FrameType(probes[0], settings),
                format,
                directory,
                settings.EngineVersion,
                qualityLabel,
                qualityRan,
                probes[0].DetectionThreshold,
                settings.Centre || settings.CheckForPlanet || report.DiscardedNoObject > 0);

            if (report.OutputFrames == 0)
            {
                Console.Error.WriteLine($"Error: All frames have been discarded from {report.InputFrames} input frames");
                zeroOutputs++;
            }

            return zeroOutputs > 0 ? -1 : 0;
        }

        // Batch mode is one independent run per input: the original prints an estimate block and a
        // summary for every file, and progress percentages restart at 0 for each.
        for (int index = 0; index < fileCount; index++)
        {
            InputProbe probe = probes[index];
            int processFrames = WindowLimit(probe.Frames, settings);
            transcript.BeginBatchFile(index + 1, fileCount, inputs[index], probe.Frames, processFrames);

            int buffers = qualityRan ? BufferCount(settings, probe.Frames) : 0;
            var reporter = new SingleFileProgress(transcript, buffers);
            var processor = new Processor(settings, new DelegateProgress(reporter.Report), null);
            ProcessingReport report = processor.Run([inputs[index]]);

            transcript.Complete();
            if (qualityRan) transcript.Quality(report.OutputFrames);
            if (animation) transcript.Animation(AnimationFrameCount(probe.Frames, settings.Animation), aviAnimation);
            transcript.Summary(
                report,
                FrameType(probe, settings),
                format,
                directory,
                settings.EngineVersion,
                qualityLabel,
                qualityRan,
                probe.DetectionThreshold,
                settings.Centre || settings.CheckForPlanet || report.DiscardedNoObject > 0);

            if (report.OutputFrames == 0)
            {
                Console.Error.WriteLine($"Error: All frames have been discarded from {report.InputFrames} input frames");
                zeroOutputs++;
            }
        }

        return zeroOutputs > 0 ? -1 : 0;
    }

    /// <summary>The frame count, channel count and detection threshold a GUI run reports up front.</summary>
    private sealed record InputProbe(int Frames, bool Colour, int? DetectionThreshold);

    private static InputProbe Probe(string path, PipelineSettings settings)
    {
        try
        {
            using IFrameSource source = FrameSourceFactory.Open(path, settings.SerBitDepth);
            int frames = source.FrameCount;
            bool colour = source.Channels == 3;
            Frame? first = source.ReadFrame();
            int? threshold = first is null
                ? null
                : settings.MinPixelValue ?? DetectionThreshold.ComputeOrDefault(first);
            return new InputProbe(frames, colour, threshold);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            // The engine still prints the estimate block and then fails the open; the caller emits
            // the error, so a zero estimate keeps the transcript shape.
            return new InputProbe(0, false, null);
        }
    }

    private static string FrameType(InputProbe probe, PipelineSettings settings) =>
        settings.ForceColour || (!settings.ForceMono && probe.Colour) ? "Colour" : "Monochrome";

    /// <summary>
    /// The input frame window's exclusive limit, mirroring the pipeline's <c>-sf</c>/<c>-f</c>/
    /// <c>-ctrf</c>/<c>-ifd</c> rules, so "Processing N frames" shows what is actually read.
    /// </summary>
    private static int WindowLimit(int total, PipelineSettings settings)
    {
        int frames = settings.FrameCount;
        int start = settings.FramesFromCentre
            ? Math.Max(0, (total - frames) / 2)
            : (settings.StartFrame ?? 1) - 1;
        int limit = total > 0 ? total : int.MaxValue;
        if (frames != 0)
        {
            bool honour = settings.InputFrameDrop <= 0 ||
                          (frames - 1) % (settings.InputFrameDrop + 1) == 0;
            if (honour) limit = total > 0 ? Math.Min(start + frames, total) : start + frames;
        }

        return Math.Max(0, limit);
    }

    /// <summary>
    /// Number of images the animation writers emit for one input: forward block, reverse block
    /// (the shared frame counted once), extra last-frame copies and whole-play repeats.
    /// </summary>
    private static int AnimationFrameCount(int sourceFrames, AnimationOptions animation)
    {
        if (sourceFrames <= 0) return 0;
        int length = 0;
        if (animation.Forward) length += sourceFrames + animation.ForwardLastFrameRepeats;
        if (animation.Reverse) length += sourceFrames + animation.ReverseLastFrameRepeats;
        if (animation.Forward && animation.Reverse) length -= 1;
        return length * (animation.AllRepeats + 1);
    }

    /// <summary>Number of frames quality selection keeps in RAM; the engine caps it at the input size.</summary>
    private static int BufferCount(PipelineSettings settings, int totalFrames)
    {
        if (totalFrames <= 0) return Math.Max(1, settings.KeepFrames ?? 1);
        if (settings.KeepFrames is int keep) return Math.Clamp(keep, 1, totalFrames);
        if (settings.KeepPercent is double percent)
            return Math.Clamp((int)Math.Round(totalFrames * Math.Clamp(percent, 0, 100) / 100.0), 1, totalFrames);
        return 1;
    }

    private static string OutputDirectoryText(PipelineSettings settings)
    {
        string root = settings.OutputDirectory;
        if (!string.IsNullOrWhiteSpace(settings.OutputSubdirectory))
        {
            string sub = settings.OutputSubdirectory!;
            root = Path.IsPathRooted(sub) ? sub : Path.Combine(root, sub);
        }

        string directory = root.Replace('\\', '/');
        return directory.EndsWith('/') ? directory : directory + "/";
    }

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

    private static string QualityLabel(QualityAlgorithm algorithm) => algorithm switch
    {
        QualityAlgorithm.Histogram => "PIPP Histogram",
        QualityAlgorithm.Ninox => "PIPP Old",
        _ => "PIPP Default",
    };

    /// <summary>Translates pipeline progress into the batch-mode token sequence.</summary>
    private sealed class SingleFileProgress(GuiTranscript transcript, int buffers)
    {
        private bool _bufferNoticeWritten;

        public void Report(PipelineProgress progress)
        {
            if (progress.File is null || progress.FrameCount <= 0) return; // unit start / run complete
            if (!_bufferNoticeWritten && buffers > 0 && progress.FrameIndex >= 2)
            {
                transcript.BufferNotice(buffers);
                _bufferNoticeWritten = true;
            }

            transcript.Frame(progress.FrameIndex, progress.FrameCount);
        }
    }

    /// <summary>Translates joined-stream progress, inserting the per-input name/progress updates.</summary>
    private sealed class JoinedProgress(
        GuiTranscript transcript,
        IReadOnlyList<InputProbe> probes,
        IReadOnlyList<string> files,
        int buffers)
    {
        private int _nextFile = 1;
        private int _boundary = probes.Count > 0 ? probes[0].Frames : 0;
        private bool _bufferNoticeWritten;

        public void Report(PipelineProgress progress)
        {
            if (progress.File is null || progress.FrameCount <= 0) return;

            while (_nextFile < probes.Count && progress.FrameIndex > _boundary)
            {
                transcript.FileTransition(_nextFile + 1, probes.Count, files[_nextFile]);
                _boundary += probes[_nextFile].Frames;
                _nextFile++;
            }

            if (!_bufferNoticeWritten && buffers > 0 && progress.FrameIndex >= 2)
            {
                transcript.BufferNotice(buffers);
                _bufferNoticeWritten = true;
            }

            transcript.Frame(progress.FrameIndex, progress.FrameCount);
        }
    }

    /// <summary>Reads one argument per line from a redirected stdin, as the GUI protocol does.</summary>
    private static string[] ReadStandardInputArguments()
    {
        if (!Console.IsInputRedirected) return Array.Empty<string>();

        var lines = new List<string>();
        while (Console.In.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            // The front-end terminates its stdin stream with "DONE" (Form1.do_processing_thread).
            if (trimmed.Equals("DONE", StringComparison.OrdinalIgnoreCase)) break;
            if (trimmed.Length > 0) lines.Add(trimmed);
        }
        return lines.ToArray();
    }

    private static PipelineSettings Map(PippOptions o)
    {
        // The engine's parser accepts -shdw 1..8; 0 is rejected with this exact wording.
        if (o.ShadowEdge is 0)
            throw new ArgumentException("Illegal edge in shadow value");

        // The engine's parser accepts narrower ranges for the GIF options than this model's parser
        // does; enforce them here so a command line the original rejects fails with its wording.
        if (o.GifColorTableDepth is < 4 or > 8)
            throw new ArgumentException($"Illegal gif_col_tbl_depth value ({o.GifColorTableDepth}); expected 4-8.");
        if (o.GifBorderTolerance is < -1 or > 50)
            throw new ArgumentException($"Illegal gif_border_tol value ({o.GifBorderTolerance}); expected -1-50.");
        if (o.GifTransparentPixelTolerance is < -1 or > 50)
            throw new ArgumentException($"Illegal gif_trns_pxl_tol value ({o.GifTransparentPixelTolerance}); expected -1-50.");
        if (o.GifLossyCompression is < 0 or > 50)
            throw new ArgumentException($"Illegal gif_lossy_comp value ({o.GifLossyCompression}); expected 0-50.");

        // -anim_all_rep=0 (or a negative count) is rejected by the engine before any frame is read:
        // "Error: Illegal anim_all_rep value". Every other animation switch is tolerant of zero.
        if (o.RepeatAll is <= 0)
            throw new ArgumentException($"Illegal anim_all_rep value ({o.RepeatAll}); expected 1 or more.");

        // Only DIB is implementable for AVI output; fail before any frame is processed rather than
        // half-way through a run. (The writer repeats this check for API callers.)
        if (o.AviCodec != PippAviCodec.Dib && ChooseExtension(o.Formats) == ".avi")
        {
            throw new ArgumentException(
                $"AVI codec {o.AviCodec} is not supported by this engine: it stores uncompressed DIB/Y800 " +
                "frames only and contains no Ut Video or MPEG-4 encoder. Use -avi_codec=0 (DIB).");
        }

        var settings = new PipelineSettings
        {
            OutputDirectory = string.IsNullOrWhiteSpace(o.OutputDirectory) ? DefaultOutputDirectory() : o.OutputDirectory!,
            OutputExtension = ChooseExtension(o.Formats),
            Prefix = o.OutputPrefix ?? string.Empty,
            Suffix = o.OutputSuffix ?? string.Empty,

            ForceMono = o.ColourMode == PippColourMode.Monochrome,
            ForceColour = o.ColourMode == PippColourMode.Colour,
            Debayer = o.Debayer,
            DebayerPattern = o.DebayerPattern is PippDebayerPattern p ? Debayer.FromPippIndex((int)p) : BayerPattern.None,
            SmoothHue = o.DebayerAlgorithm == PippDebayerAlgorithm.SmoothHue,

            Binning = Math.Clamp(o.Binning ?? 1, 1, 8),
            BinningSum = o.BinningCalculation == PippBinningCalculation.Sum,

            MedianFilter = o.MedianFilter,
            MonoMode = o.ConvertToMono,
            Gain = o.InputGain <= 0 ? 1.0 : o.InputGain,
            Gamma = o.Gamma <= 0 ? 1.0 : o.Gamma,
            StretchWhitePoint = o.HistogramStretch is int wp and >= 1 and <= 255 ? wp : null,
            StretchPreserveBlackPoint = o.HistogramStretchNoBlackPoint,
            StretchPerChannelGains = o.HistogramStretchRgb,
            MaxOverexposedPixels = o.OverexposureThreshold,

            FlipX = o.FlipX,
            FlipY = o.FlipY,
            Rotation = o.Rotation switch
            {
                PippRotation.Rotate90 => 90,
                PippRotation.Rotate180 => 180,
                PippRotation.Rotate270 => 270,
                _ => 0,
            },
            Crop = BuildCrop(o),
            Resize = o.ResizeFrames && o.ResizeWidth is not null
                ? (o.ResizeWidth.Value, o.ResizeHeight ?? -1)
                : null,

            QualityAlgorithm = o.QualityAlgorithm switch
            {
                PippQualityAlgorithm.Ninox => QualityAlgorithm.Ninox,
                PippQualityAlgorithm.Histogram => QualityAlgorithm.Histogram,
                _ => QualityAlgorithm.PippDefault,
            },
            KeepFrames = o.QualityFrameCount,
            KeepPercent = o.QualityPercent,
            QualityMinDownsample = o.QualityMinDownsample ?? 3,
            QualityMaxDownsample = o.QualityMaxDownsample ?? 5,
            QualityIncrement = o.QualityIncrement ?? 1,

            Centre = o.Centre,
            MinPixelValue = o.MinPixelValue,
            CheckForPlanet = o.CheckForPlanet,
            MinSize = o.MinPlanetSize ?? 50,
            KeepImageNames = o.KeepImageFilenames,
            SurfaceTracking = o.SurfaceFeatureTracking,
            EnableStabilisation = o.EnableStabilisation,
            AnchorFeatureReject = o.AnchorFeatureReject,

            // Frame window (-sf / -f / -ctrf / -ifd): the engine's fields are 0 when the switch is
            // absent, so StartFrame stays nullable to keep "absent" distinct from an explicit -sf=0.
            StartFrame = o.StartFrame,
            FrameCount = o.FrameCount ?? 0,
            FramesFromCentre = o.FramesFromCentre,
            InputFrameDrop = o.InputFrameDrop,

            // Output layout and selection (-osubdir / -sr / -join / -incqv / -drop).
            OutputSubdirectory = string.IsNullOrWhiteSpace(o.OutputSubdirectory) ? null : o.OutputSubdirectory,
            SaveRejectedFrames = o.SaveRejectedFrames,
            JoinMode = o.Join,
            IncludeQualityValue = o.IncludeQualityValue,
            OutputFrameDrop = o.OutputFrameDrop,

            // GIF encoder options: -gif_col_tbl_depth and -gif_trns_pxl_tol have observable engine
            // effects (global colour-table size and the reserved entry 1); -gif_quant_type selects
            // the live quantiser here because the engine never encodes an image block.
            GifOptions = new GifOptions
            {
                ColorTableDepth = o.GifColorTableDepth,
                Quantisation = o.GifQuantisation == PippGifQuantisation.MedianCut
                    ? GifQuantisation.MedianCut
                    : GifQuantisation.NeuralNet,
                BorderTolerance = o.GifBorderTolerance,
                TransparentPixelTolerance = o.GifTransparentPixelTolerance,
                LossyCompression = o.GifLossyCompression,
            },

            // -rgb writes three files per frame; -save_dark/-save_flat re-emit the loaded masters.
            SplitRgb = o.SplitRgb,
            SaveMasterDark = o.SaveMasterDark,
            SaveMasterFlat = o.SaveMasterFlat,
            SaveMasterFlatDark = o.SaveMasterFlatDark,

            // Area of interest. The box is only a rectangle; -aoi itself does not gate processing
            // (verified: without surface tracking the engine output is byte-identical with or without
            // it), and -aoi_crop is the switch that changes the delivered frame size.
            AreaOfInterest = BuildAoi(o),
            AreaOfInterestCrop = o.AreaOfInterestCrop,
            AreaOfInterestReject = o.AreaOfInterestReject,
            FlatHasBayer = o.FlatHasBayer,

            // -qw duplicates the top quarter twice and the next quarter once.
            QualityWeighting = o.QualityWeighting,

            // Animation sequencing (-anim_*). The engine rejects an all-repeat count of zero with
            // "Illegal anim_all_rep value"; every other combination is accepted and shapes the AVI
            // and GIF frame order in the output stage.
            Animation = new AnimationOptions
            {
                Forward = o.AnimateForward,
                Reverse = o.AnimateReverse,
                ForwardLastFrameRepeats = Math.Max(0, o.RepeatForwardLast ?? 0),
                ReverseLastFrameRepeats = Math.Max(0, o.RepeatReverseLast ?? 0),
                AllRepeats = o.RepeatAll ?? 0,
                NoReverseInLastRepeat = o.NoReverseInLastRepeat,
                RepeatForever = o.RepeatAllForever,
            },

            // WinJUPOS-friendly names (-wjpos_*): -wjpos_time alone enables the prefix; the mask and
            // frame rate only supply the timestamp when the input has no SER trailer.
            WinJuposTimeMode = o.WinJuposTime is PippWinJuposTimeMode timeMode ? (int)timeMode : null,
            WinJuposDateTimeMask = string.IsNullOrWhiteSpace(o.WinJuposDateTimeMask) ? null : o.WinJuposDateTimeMask,
            WinJuposLocalToUtc = o.WinJuposLocalToUtc,
            WinJuposInputFps = o.WinJuposInputFps ?? 0,

            // -avi_old and -avi_old4 select the same legacy layout; they differ only in the engine's
            // >2 GiB size arithmetic, which our 4 GiB AVI 1.0 guard already covers.
            AviLegacyLayout = o.AviOldFormat != PippAviOldFormat.None,

            // -test_run writes one input/output BMP pair at the output root.
            TestRun = o.TestRun,

            // -ser_bpp overrides the SER header's per-sample depth (verified: 8 on a 16-bit file
            // produces 8-bit output; 16 on an 8-bit file fails with the engine's "too short" error).
            SerBitDepth = o.SerBitDepth,

            // -no_ram_buff delivers quality-selected frames in source order (verified with -q=3).
            NoRamBuffers = o.NoRamBuffers,

            // -sdp renders the threshold mask and detected box as a colour overlay.
            ShowDetectedPixels = o.ShowDetectedPixels,

            // -avi_codec is carried into the writer, which only implements DIB. -avi_quality is
            // validated against the engine's real range (1..10, not the parser's 1..100) so a value
            // the original rejects fails here too; the uncompressed path has no quality parameter.
            AviCodec = o.AviCodec switch
            {
                PippAviCodec.Ulrg => AviCodec.Ulrg,
                PippAviCodec.Fmp4 => AviCodec.Fmp4,
                _ => AviCodec.Dib,
            },
            AviQuality = o.AviQuality is >= 1 and <= 10
                ? o.AviQuality
                : throw new ArgumentException($"Illegal avi_quality value ({o.AviQuality}); expected 1-10."),

            AlignmentBox = o.AnchorFeatureX is int ax && o.AnchorFeatureY is int ay && o.AnchorFeatureWidth is int aw && o.AnchorFeatureHeight is int ah
                ? new PixelRect(ax, ay, aw, ah)
                : null,

            RenumberFrames = !o.NoRenumber,
            LeadingZeros = !o.NoLeadingZeros,
            Fps = o.FpsScale > 0 ? (double)o.FpsRate / o.FpsScale : 0,
            GuiProtocol = o.Gui,
        };

        // A calibration master is only usable when the GUI/engine handed us a decodable PGM.
        settings.MasterDark = LoadMaster(o.MasterDarkTmpFile, o.MasterDarkPgmFile, "dark", o.DarkSubtraction);
        settings.MasterFlat = LoadMaster(o.MasterFlatTmpFile, null, "flat", o.FlatCalibration);
        settings.MasterFlatDark = LoadMaster(o.MasterFlatDarkTmpFile, null, "flat dark", o.FlatDarkSubtraction);

        return settings;
    }

    /// <summary>
    /// Builds the area-of-interest rectangle. The engine accepts the coordinates independently of
    /// <c>-aoi</c> (a <c>-aoi_reject</c> run with only <c>-aoi_x/y/width/height</c> still checks the
    /// box), so any complete box is honoured.
    /// </summary>
    private static PixelRect? BuildAoi(PippOptions o)
    {
        bool requested = o.AreaOfInterest || o.AreaOfInterestCrop ||
                         o.AreaOfInterestX is not null || o.AreaOfInterestY is not null ||
                         o.AreaOfInterestWidth is not null || o.AreaOfInterestHeight is not null;
        if (!requested) return null;
        if (o.AreaOfInterestWidth is not int width || o.AreaOfInterestHeight is not int height) return null;
        if (width <= 0 || height <= 0) return null;
        return new PixelRect(o.AreaOfInterestX ?? 0, o.AreaOfInterestY ?? 0, width, height);
    }

    private static Frame? LoadMaster(string? tmpFile, string? pgmFile, string kind, bool enabled)
    {
        if (!enabled) return null;

        foreach (var candidate in new[] { pgmFile, tmpFile })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;
            try
            {
                // The engine's masters are its private .tmp container (recovered in
                // Formats/MasterTmpFile); PGM is only the legacy fallback.
                bool isTmp = Path.GetExtension(candidate).Equals(".tmp", StringComparison.OrdinalIgnoreCase);
                return isTmp ? MasterTmpFile.Read(candidate) : PgmFile.Read(candidate);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                Console.Error.WriteLine(
                    $"Warning: master {kind} frame '{candidate}' is not a readable PGM ({ex.Message}); continuing without it");
            }
        }

        return null;
    }

    private static PixelRect? BuildCrop(PippOptions o)
    {
        int? width = o.CropWidth;
        int? height = o.CropHeight;
        if (width is null && height is null) return null;

        return new PixelRect(o.OffsetX ?? 0, o.OffsetY ?? 0, width ?? 0, height ?? 0);
    }

    private static string ChooseExtension(PippOutputFormats formats)
    {
        // One container per run in this implementation; pick the most specific format requested.
        if (formats.HasFlag(PippOutputFormats.Ser)) return ".ser";
        if (formats.HasFlag(PippOutputFormats.Avi)) return ".avi";
        // the original names FITS output .fit, not .fits
        if (formats.HasFlag(PippOutputFormats.Fits)) return ".fit";
        if (formats.HasFlag(PippOutputFormats.Tiff)) return ".tif";
        if (formats.HasFlag(PippOutputFormats.Png)) return ".png";
        if (formats.HasFlag(PippOutputFormats.Bmp)) return ".bmp";
        if (formats.HasFlag(PippOutputFormats.Gif)) return ".gif";
        return ".ser";
    }

    private static string DefaultOutputDirectory() =>
        Path.Combine(Environment.CurrentDirectory, DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + "_pipp");

    private static void PrintSummary(ProcessingReport report, IReadOnlyList<string> inputs, PippOptions options, PipelineSettings settings)
    {
        if (options.Gui) return;

        Console.WriteLine();
        Console.WriteLine("  Processing Details:");
        Console.WriteLine($"    Input files:                     {inputs.Count}");
        Console.WriteLine($"    Files not found:                 {report.FailedFiles.Count}");
        Console.WriteLine($"    Total input frames:              {report.InputFrames}");
        Console.WriteLine($"    Frames discarded for quality:    {report.DiscardedByQuality}");
        Console.WriteLine($"    Frames discarded with no object: {report.DiscardedNoObject}");
        Console.WriteLine($"    Total output frames:             {report.OutputFrames}");
        Console.WriteLine();
        Console.WriteLine("  Summary:");
        Console.WriteLine($"    Output frame format:             {settings.OutputExtension}");
        Console.WriteLine($@"    Output Directory:                {(string.IsNullOrWhiteSpace(settings.OutputSubdirectory) ? settings.OutputDirectory : Path.Combine(settings.OutputDirectory, settings.OutputSubdirectory!))}");

        foreach (var file in report.FailedFiles)
        {
            Console.WriteLine($"    Failed: {file}");
        }
        foreach (var file in report.OutputFiles)
        {
            Console.WriteLine($"    Written: {file}");
        }
    }
}
