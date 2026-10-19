using System.Globalization;
using System.Runtime.CompilerServices;
using Opipp.Core.Alignment;
using Opipp.Core.Cli;
using Opipp.Core.Diagnostics;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Opipp.Core.Quality;

[assembly: InternalsVisibleTo("Opipp.Cli.Tests")]

namespace Opipp.Cli;

/// <summary>
/// Managed replacement for <c>pipp_core.exe</c>. Parses the same switch vocabulary as the original
/// engine, runs <see cref="Processor"/>, prints a PIPP-style summary and, with <c>-gui</c>, emits the
/// same <c>@TOKEN</c> progress protocol the WinForms front-end already understands.
/// </summary>
internal sealed class DelegateProgress(Action<PipelineProgress> handler) : IProgress<PipelineProgress>
{
    public void Report(PipelineProgress value) => handler(value);
}

/// <summary>
/// How a processing run ended. A run is only <see cref="Success"/> when every input was read and
/// at least one output frame was produced; any input the pipeline recorded as failed makes the
/// result at least <see cref="PartialFailure"/>, even when frames were written before the failure.
/// </summary>
internal enum RunOutcome
{
    /// <summary>Every input was read and output was produced.</summary>
    Success,

    /// <summary>Some frames were written, but one or more inputs could not be read; the output is incomplete.</summary>
    PartialFailure,

    /// <summary>No output was produced.</summary>
    Failure,

    /// <summary>The run was cancelled before it completed.</summary>
    Cancelled,
}

/// <summary>
/// Process exit codes. These are part of the engine contract the WinForms front-end reads:
/// 0 means success, 2 a command-line/configuration error detected before processing, and -1 the
/// original engine's "all frames discarded" result. 1 is the partial-result code: output exists but
/// one or more inputs failed, so the frames on disk are explicitly incomplete. 130 is the
/// conventional cancelled-process code (no cancellation source is wired into the CLI yet).
/// </summary>
internal static class ExitCodes
{
    internal const int Success = 0;
    internal const int PartialFailure = 1;
    internal const int ConfigurationError = 2;
    internal const int NoOutput = -1;
    internal const int Cancelled = 130;
    internal const int UnknownSwitch = 127;

    internal static int For(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Success => Success,
        RunOutcome.PartialFailure => PartialFailure,
        RunOutcome.Failure => NoOutput,
        RunOutcome.Cancelled => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unknown run outcome"),
    };
}

/// <summary>One input the pipeline could not read, with the diagnostic the CLI reports for it.</summary>
internal sealed record FailedInput(string Path, string Reason, bool CouldNotOpen = true);

internal static class Program
{
    internal static int Main(string[] args)
    {
        // The WinForms front-end launches the engine with no argv and writes the option lines
        // followed by the input file paths to stdin (see Form1.send_arguments_to_pipp_process).
        // Accept both that protocol and a conventional command line.
        var effectiveArgs = args.Length > 0 ? args : ReadStandardInputArguments();
        var parsed = CommandLineParser.Parse(effectiveArgs);

        // -check is a validation-only mode: the parser records it even when another token is bad,
        // so a check request with a malformed command line still produces a check report instead of
        // falling through to the generic parse-error path.
        bool checkOnly = parsed.Options.CheckOnly;

        if (parsed.WantsHelp)
        {
            Console.WriteLine(OpippHelpText.Text);
            return ExitCodes.Success;
        }

        if (!parsed.Success)
        {
            if (checkOnly) return ReportCheckFailure(parsed.Errors);

            foreach (var error in parsed.Errors)
            {
                Console.Error.WriteLine($"Error: {error}");
            }
            Console.Error.WriteLine("Use '-help' to display help");
            // the original exits 127 on an unrecognised switch; keep that contract detectable
            return parsed.Errors.Any(e => e.StartsWith("Unknown switch", StringComparison.Ordinal))
                ? ExitCodes.UnknownSwitch
                : ExitCodes.ConfigurationError;
        }

        var options = parsed.Options;
        var inputs = parsed.Inputs;
        if (inputs.Count == 0)
        {
            if (checkOnly) return ReportCheckFailure(["No input files specified"]);

            Console.Error.WriteLine("Error: No input files specified");
            Console.Error.WriteLine("Use '-help' to display help");
            return ExitCodes.ConfigurationError;
        }

        PipelineSettings settings;
        try
        {
            if (options.DebugAvi && !checkOnly)
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

                return ExitCodes.Success;
            }

            settings = Map(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException)
        {
            if (checkOnly) return ReportCheckFailure([ex.Message]);

            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.ConfigurationError;
        }

        // -check validates the same settings Map just built, plus the inputs they name, and stops
        // before Processor.Run. Nothing is created: no output directory, no log, no science file.
        if (checkOnly) return RunCheck(options, inputs, settings);

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
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Error: Processing was cancelled");
                return ExitCodes.For(RunOutcome.Cancelled);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException or CalibrationConfigurationException)
            {
                // A calibration master that cannot be applied is a run-level configuration failure,
                // not a per-frame rejection; the pipeline deliberately lets it escape the frame loop.
                // Exit 1 is this path's defined runtime-failure code, matching the errors above.
                Console.Error.WriteLine($"Error: {ex.Message}");
                return ExitCodes.PartialFailure;
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
            Console.WriteLine($"OPIPP (managed engine) {typeof(Program).Assembly.GetName().Version}");
            Console.WriteLine($"Processing: {(options.Join ? "(Join mode)" : "(Batch mode)")}");
            Console.WriteLine($"  Input files: {inputs.Count}");
        }

        ProcessingReport report;
        try
        {
            report = processor.Run(inputs);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Error: Processing was cancelled");
            return ExitCodes.For(RunOutcome.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException or CalibrationConfigurationException)
        {
            // See the -gui catch above: a calibration configuration error exits 1 like the other
            // runtime failures, and its message is the controlled diagnostic, never a stack trace.
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitCodes.PartialFailure;
        }

        // A joined run reports its one unit under the first input's name even when a later file is
        // the one that could not be read; resolve the real culprit before reporting anything.
        IReadOnlyList<FailedInput> failures = ResolveFailedInputs(report, inputs, settings);
        PrintSummary(report, inputs, options, settings, failures);

        if (failures.Count > 0)
        {
            WriteFailureDiagnostics(failures, inputs.Count, report.OutputFrames, settings);
        }

        // The engine exits -1 (0xFFFFFFFF) and says so on stderr when every frame was discarded;
        // keep that contract so the GUI can distinguish "nothing produced" from a crash. A partial
        // result is different: output exists, but it is explicitly labelled as incomplete. When a
        // failure is the reason nothing was written, the per-input diagnostic above already says so.
        if (report.OutputFrames == 0)
        {
            if (failures.Count == 0)
            {
                Console.Error.WriteLine($"Error: All frames have been discarded from {report.InputFrames} input frames");
            }

            return ExitCodes.For(RunOutcome.Failure);
        }

        return failures.Count > 0
            ? ExitCodes.For(RunOutcome.PartialFailure)
            : ExitCodes.For(RunOutcome.Success);
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
        int totalOutput = 0;
        var failures = new List<FailedInput>();

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

            IReadOnlyList<FailedInput> unitFailures = ResolveFailedInputs(report, inputs, settings);
            failures.AddRange(unitFailures);
            totalOutput += report.OutputFrames;

            if (unitFailures.Count == 0 && report.OutputFrames == 0)
            {
                Console.Error.WriteLine($"Error: All frames have been discarded from {report.InputFrames} input frames");
                zeroOutputs++;
            }

            WriteGuiFailureDiagnostics(failures, fileCount, totalOutput, settings);
            return ExitCodes.For(GuiOutcome(totalOutput, zeroOutputs, failures.Count));
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

            IReadOnlyList<FailedInput> unitFailures = ResolveFailedInputs(report, [inputs[index]], settings);
            failures.AddRange(unitFailures);
            totalOutput += report.OutputFrames;

            if (unitFailures.Count == 0 && report.OutputFrames == 0)
            {
                Console.Error.WriteLine($"Error: All frames have been discarded from {report.InputFrames} input frames");
                zeroOutputs++;
            }
        }

        // The transcript is unchanged for a successful run; a failed or partial result is reported on
        // stderr and through the non-zero exit code, which is what the form treats as failure.
        WriteGuiFailureDiagnostics(failures, fileCount, totalOutput, settings);
        return ExitCodes.For(GuiOutcome(totalOutput, zeroOutputs, failures.Count));
    }

    /// <summary>
    /// Maps the accumulated GUI counters to an outcome. A run that wrote nothing is a failure (the
    /// engine's -1); a unit whose every frame was discarded keeps the same contract; output written
    /// beside one or more unreadable inputs is a partial failure.
    /// </summary>
    private static RunOutcome GuiOutcome(int totalOutput, int zeroOutputUnits, int failureCount)
    {
        if (totalOutput == 0) return RunOutcome.Failure;
        if (zeroOutputUnits > 0) return RunOutcome.Failure;
        return failureCount > 0 ? RunOutcome.PartialFailure : RunOutcome.Success;
    }

    /// <summary>
    /// Emits the partial/failed result on stderr for <c>-gui</c>. Stdout stays byte-identical to
    /// the engine transcript, so the form keeps parsing progress tokens; it already treats a
    /// non-zero exit code as failure.
    /// </summary>
    private static void WriteGuiFailureDiagnostics(
        IReadOnlyList<FailedInput> failures,
        int inputCount,
        int outputFrames,
        PipelineSettings settings)
    {
        if (failures.Count == 0) return;

        // The original engine prints the per-input failure on stdout, where the form appends it to
        // the transcript; keep that line so a failed run is visible in the GUI text itself. The form
        // also treats a non-zero exit code as failure.
        foreach (FailedInput failure in failures)
        {
            string action = failure.CouldNotOpen ? "open" : "read";
            Console.Out.WriteLine($"Error: could not {action} file '{failure.Path}': {failure.Reason}");
        }

        WriteFailureDiagnostics(failures, inputCount, outputFrames, settings);
    }

    /// <summary>
    /// Names every input the pipeline could not read. A joined run records its one unit under the
    /// first input's name, so the inputs are re-walked here (only on the failure path) to name the
    /// file that actually failed and keep the diagnostic useful.
    /// </summary>
    private static IReadOnlyList<FailedInput> ResolveFailedInputs(
        ProcessingReport report,
        IReadOnlyList<string> inputs,
        PipelineSettings settings)
    {
        if (report.FailedFiles.Count == 0) return Array.Empty<FailedInput>();

        if (settings.JoinMode && inputs.Count > 1)
        {
            // JoinedFrameSource opens the inputs in order and the pipeline stops at the first failure,
            // so the first input that cannot be read to its end is the one that broke the stream.
            foreach (string input in inputs)
            {
                IFrameSource source;
                try
                {
                    source = FrameSourceFactory.Open(input, settings.SerBitDepth);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                {
                    return [new FailedInput(input, ex.Message, CouldNotOpen: true)];
                }

                try
                {
                    using (source)
                    {
                        while (source.ReadFrame() is not null)
                        {
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                {
                    return [new FailedInput(input, ex.Message, CouldNotOpen: false)];
                }
            }
        }

        return report.FailedFiles
            .Select(file => new FailedInput(file, "could not be opened or decoded"))
            .ToArray();
    }

    /// <summary>
    /// Reports a partial or failed result on stderr. The wording never calls a partial run a success:
    /// it says how many inputs failed, names each one, and points at the retained (incomplete) output.
    /// </summary>
    private static void WriteFailureDiagnostics(
        IReadOnlyList<FailedInput> failures,
        int inputCount,
        int outputFrames,
        PipelineSettings settings)
    {
        string subject = failures.Count == 1
            ? $"1 of {inputCount} input files could not be read"
            : $"{failures.Count} of {inputCount} input files could not be read";

        if (outputFrames > 0)
        {
            string frames = outputFrames == 1 ? "frame is" : "frames are";
            Console.Error.WriteLine(
                $"Error: Partial result: {subject}; the {outputFrames} written {frames} incomplete.");
            Console.Error.WriteLine($"  Partial output retained in '{EffectiveOutputPath(settings)}'.");
        }
        else
        {
            Console.Error.WriteLine($"Error: Failure: {subject}; no output was produced.");
        }

        foreach (FailedInput failure in failures)
        {
            Console.Error.WriteLine($"  Failed input: '{failure.Path}' ({failure.Reason})");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // -check: validate only, write nothing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Implements <c>-check</c>: validate the parsed command line, calibration masters, input files,
    /// processing geometry and output settings, print one clear result and exit. The pipeline never
    /// runs, so no output directory, log or science file is created.
    /// </summary>
    /// <returns>0 when every check passes, 2 (configuration) when any check fails.</returns>
    private static int RunCheck(OpippOptions options, IReadOnlyList<string> inputs, PipelineSettings settings)
    {
        var errors = new List<string>();
        var decoded = new List<(string Path, Frame Frame)>(inputs.Count);

        foreach (string input in inputs)
        {
            if (!File.Exists(input))
            {
                errors.Add($"input file does not exist: '{input}'");
                continue;
            }

            try
            {
                using IFrameSource source = FrameSourceFactory.Open(input, settings.SerBitDepth);
                Frame? first = source.ReadFrame();
                if (first is null)
                {
                    errors.Add($"input file contains no frames: '{input}'");
                    continue;
                }

                decoded.Add((input, first));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
            {
                errors.Add($"input file cannot be read: '{input}': {ex.Message}");
            }
        }

        foreach ((string path, Frame frame) in decoded)
        {
            errors.AddRange(ValidateGeometry(options, settings, frame, path));
            errors.AddRange(ValidateMasters(settings, frame, path));
        }

        errors.AddRange(ValidateOutputSettings(settings));

        if (errors.Count > 0) return ReportCheckFailure(errors);

        Console.WriteLine("Command line check: OK");
        Console.WriteLine($"  Input files: {inputs.Count}");
        Console.WriteLine($"  Input geometry: {DescribeGeometry(decoded[0].Frame)}");
        Console.WriteLine($"  Output format: {settings.OutputExtension}");
        Console.WriteLine($"  Output directory: {EffectiveOutputPath(settings)} (not created)");
        Console.WriteLine($"  Calibration masters: {DescribeMasters(settings)}");
        return ExitCodes.Success;
    }

    /// <summary>Prints one failed check report and returns the configuration exit code.</summary>
    private static int ReportCheckFailure(IEnumerable<string> errors)
    {
        Console.WriteLine("Command line check: FAILED");
        foreach (string error in errors)
        {
            Console.WriteLine($"  Error: {error}");
        }

        return ExitCodes.ConfigurationError;
    }

    /// <summary>
    /// Validates the geometry switches against the frame the pipeline would actually process: the
    /// raw capture is binned first, then rotated, and only then cropped and resized. A crop that
    /// does not fit would otherwise be swallowed as a per-frame rejection and end in "all frames
    /// discarded" instead of naming the bad switch.
    /// </summary>
    private static IEnumerable<string> ValidateGeometry(
        OpippOptions options,
        PipelineSettings settings,
        Frame frame,
        string path)
    {
        int width = frame.Width / settings.Binning;
        int height = frame.Height / settings.Binning;
        if (settings.Binning > 1 && (width == 0 || height == 0))
        {
            yield return $"-bin={settings.Binning} is larger than the {frame.Width}x{frame.Height} frame of '{path}'";
            yield break;
        }

        if (settings.Rotation is 90 or 270) (width, height) = (height, width);

        bool cropRequested = options.CropWidth is not null || options.CropHeight is not null;
        if (cropRequested)
        {
            int cropWidth = options.CropWidth ?? 0;
            int cropHeight = options.CropHeight ?? 0;
            int x = options.OffsetX ?? 0;
            int y = options.OffsetY ?? 0;

            if (cropWidth <= 0 || cropHeight <= 0)
            {
                yield return $"crop geometry for '{path}' needs both -cx and -cy greater than zero (got {cropWidth}x{cropHeight})";
            }
            else if (x < 0 || y < 0)
            {
                yield return $"crop origin for '{path}' must not be negative (got {x},{y})";
            }
            else if ((long)x + cropWidth > width || (long)y + cropHeight > height)
            {
                yield return $"crop rectangle ({x},{y}) {cropWidth}x{cropHeight} for '{path}' exceeds the {width}x{height} processed frame";
            }
        }

        if (options.ResizeFrames)
        {
            if (options.ResizeWidth is null)
            {
                yield return "-resize_en requires -resize_width";
            }
            else if (options.ResizeHeight is 0)
            {
                yield return "-resize_height must be -1 (keep the aspect ratio) or greater than zero";
            }
        }

        if (options.AreaOfInterestCrop &&
            options.AreaOfInterestWidth is int aoiWidth && aoiWidth > 0 &&
            options.AreaOfInterestHeight is int aoiHeight && aoiHeight > 0)
        {
            int x = options.AreaOfInterestX ?? 0;
            int y = options.AreaOfInterestY ?? 0;
            if (x < 0 || y < 0)
            {
                yield return $"area of interest for '{path}' must not have a negative origin (got {x},{y})";
            }
            else if ((long)x + aoiWidth > width || (long)y + aoiHeight > height)
            {
                yield return $"area of interest ({x},{y}) {aoiWidth}x{aoiHeight} for '{path}' exceeds the {width}x{height} processed frame";
            }
        }
    }

    /// <summary>
    /// Checks every loaded master against the frame it will be applied to. Science masters must
    /// match the raw capture (calibration runs before debayering and binning); a flat dark is
    /// subtracted from the flat, so it must match the flat's geometry instead.
    /// </summary>
    private static IEnumerable<string> ValidateMasters(PipelineSettings settings, Frame frame, string path)
    {
        if (settings.MasterDark is Frame dark && !SameGeometry(dark, frame))
        {
            yield return MasterMismatch("-dark", dark, DescribeGeometry(frame), path);
        }

        if (settings.MasterFlat is Frame flat)
        {
            if (!SameGeometry(flat, frame))
            {
                yield return MasterMismatch("-flat", flat, DescribeGeometry(frame), path);
            }

            if (settings.MasterFlatDark is Frame flatDark && !SameGeometry(flatDark, flat))
            {
                yield return MasterMismatch("-flat_dark", flatDark, DescribeGeometry(flat), $"the flat master used for '{path}'");
            }
        }
    }

    private static bool SameGeometry(Frame a, Frame b) =>
        a.Width == b.Width && a.Height == b.Height && a.Channels == b.Channels && a.BitDepth == b.BitDepth;

    private static string MasterMismatch(string name, Frame master, string expected, string subject) =>
        $"{name} master is {DescribeGeometry(master)} but {subject} is {expected}; " +
        "calibration runs before debayering and binning, so a master must match the capture geometry";

    /// <summary>
    /// Validates the output settings without creating anything: the directory must be a legal path
    /// that is not already a file, and the prefix/suffix must be usable in a file name.
    /// </summary>
    private static IReadOnlyList<string> ValidateOutputSettings(PipelineSettings settings)
    {
        var errors = new List<string>();
        string outputPath = EffectiveOutputPath(settings);

        try
        {
            string full = Path.GetFullPath(outputPath);
            if (File.Exists(full))
            {
                errors.Add($"output directory '{outputPath}' is an existing file");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"output directory '{outputPath}' is not a valid path: {ex.Message}");
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        if (settings.Prefix.IndexOfAny(invalid) >= 0)
        {
            errors.Add($"output prefix '{settings.Prefix}' contains characters that are not valid in a file name");
        }

        if (settings.Suffix.IndexOfAny(invalid) >= 0)
        {
            errors.Add($"output suffix '{settings.Suffix}' contains characters that are not valid in a file name");
        }

        return errors;
    }

    /// <summary>The directory the run would write to: the output root plus <c>-osubdir</c> when set.</summary>
    private static string EffectiveOutputPath(PipelineSettings settings)
    {
        string root = settings.OutputDirectory;
        if (!string.IsNullOrWhiteSpace(settings.OutputSubdirectory))
        {
            string sub = settings.OutputSubdirectory;
            root = Path.IsPathRooted(sub) ? sub : Path.Combine(root, sub);
        }

        return root;
    }

    private static string DescribeGeometry(Frame frame) =>
        string.Create(CultureInfo.InvariantCulture, $"{frame.Width}x{frame.Height}x{frame.Channels}@{frame.BitDepth}");

    private static string DescribeMasters(PipelineSettings settings)
    {
        var names = new List<string>(3);
        if (settings.MasterDark is not null) names.Add("dark");
        if (settings.MasterFlat is not null) names.Add("flat");
        if (settings.MasterFlatDark is not null) names.Add("flat dark");
        return names.Count == 0 ? "none" : string.Join(", ", names);
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

    private static PipelineSettings Map(OpippOptions o)
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

            // The engine's AFB box is addressed in its bottom-up buffer rows (the tracker searches
            // the raw buffer); our frames are top-down, so the Y origin is mirrored here once.
            AlignmentBox = o.AnchorFeatureX is int ax && o.AnchorFeatureY is int ay && o.AnchorFeatureWidth is int aw && o.AnchorFeatureHeight is int ah && aw > 0 && ah > 0
                ? new PixelRect(ax, ay, aw, ah)
                : null,

            RenumberFrames = !o.NoRenumber,
            LeadingZeros = !o.NoLeadingZeros,
            Fps = o.FpsScale > 0 ? (double)o.FpsRate / o.FpsScale : 0,
            GuiProtocol = o.Gui,
        };

        // Calibration masters: the full-fidelity .tmp is authoritative, the PGM is the legacy
        // one-channel fallback used only when the .tmp is absent (see LoadMaster). A null path means
        // the switch was absent; a non-null path is a file the user named, and a missing one is an
        // error rather than a silently uncalibrated run.
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
    private static PixelRect? BuildAoi(OpippOptions o)
    {
        bool requested = o.AreaOfInterest || o.AreaOfInterestCrop ||
                         o.AreaOfInterestX is not null || o.AreaOfInterestY is not null ||
                         o.AreaOfInterestWidth is not null || o.AreaOfInterestHeight is not null;
        if (!requested) return null;
        if (o.AreaOfInterestWidth is not int width || o.AreaOfInterestHeight is not int height) return null;
        if (width <= 0 || height <= 0) return null;
        return new PixelRect(o.AreaOfInterestX ?? 0, o.AreaOfInterestY ?? 0, width, height);
    }

    /// <summary>
    /// Loads a calibration master, preferring the full-fidelity <c>.tmp</c> container over the legacy
    /// PGM handoff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OpippOptions"/> records an absent path switch as <see langword="null"/>: the parser
    /// rejects a switch token with no value, so a non-null string means the operator named that file.
    /// Presence is tested with <c>is not null</c>, never by treating an empty string as absent.
    /// </para>
    /// <para>
    /// Three states are distinct. Calibration disabled, or enabled with no path at all, loads nothing:
    /// the GUI emits bare <c>-flat_dark</c> when it has already subtracted the flat dark itself. A
    /// named path that cannot be resolved — no <c>.tmp</c> and, for the dark, no PGM either — is a
    /// configuration error, because continuing would quietly deliver uncalibrated science frames.
    /// </para>
    /// <para>
    /// The PGM is the engine's old one-channel handoff, so a colour master is reduced to luma there
    /// and a colour capture rejects it with a channel mismatch. The <c>.tmp</c> keeps the real channel
    /// count and sample depth and is therefore authoritative. PGM is used only when no <c>.tmp</c> was
    /// supplied or the supplied <c>.tmp</c> file is absent. A master that exists but cannot be decoded
    /// is also an explicit configuration error.
    /// </para>
    /// </remarks>
    internal static Frame? LoadMaster(string? tmpFile, string? pgmFile, string kind, bool enabled)
    {
        if (!enabled) return null;

        // null means "switch absent" (CommandLineParser refuses a switch with no value); anything
        // else is an explicitly named file. Do not use IsNullOrWhiteSpace here: "-flat_tmp=" names
        // a (nonsensical) empty path, and that is still an explicit request that cannot be honoured.
        bool tmpSupplied = tmpFile is not null;
        bool pgmSupplied = pgmFile is not null;
        if (!tmpSupplied && !pgmSupplied) return null; // bare -dark / -flat / -flat_dark

        if (tmpSupplied && File.Exists(tmpFile))
            return ReadMaster(tmpFile!, kind);

        // Fallback only when the authoritative container is absent.
        if (pgmSupplied && File.Exists(pgmFile))
            return ReadMaster(pgmFile!, kind);

        throw new InvalidDataException(DescribeMissingMaster(kind, tmpFile, pgmFile));
    }

    /// <summary>
    /// Diagnostic for a master the command line named but which is not on disk. It names every
    /// supplied candidate together with the switch that supplied it, so the operator can see exactly
    /// which path is wrong.
    /// </summary>
    private static string DescribeMissingMaster(string kind, string? tmpFile, string? pgmFile)
    {
        var supplied = new List<string>(2);
        if (tmpFile is not null) supplied.Add($"'{tmpFile}' (from {MasterSwitch(kind, pgm: false)})");
        if (pgmFile is not null) supplied.Add($"'{pgmFile}' (from {MasterSwitch(kind, pgm: true)})");

        string subject = supplied.Count == 1
            ? $"master {kind} frame {supplied[0]}"
            : $"master {kind} frames {string.Join(" and ", supplied)}";
        string enablingSwitch = "-" + kind.Replace(' ', '_');
        string remedy = supplied.Count == 1 ? "remove the switch" : "remove the switches";
        return $"{subject} does not exist; {enablingSwitch} is enabled, so the run cannot continue " +
               $"without calibration. Supply an existing master or {remedy}.";
    }

    /// <summary>The command-line switch that supplies one kind of calibration master.</summary>
    private static string MasterSwitch(string kind, bool pgm) => kind switch
    {
        "dark" => pgm ? "-dark_pgm" : "-dark_tmp",
        "flat" => "-flat_tmp",
        "flat dark" => "-flat_dark_tmp",
        _ => "-" + kind.Replace(' ', '_') + (pgm ? "_pgm" : "_tmp"),
    };

    private static Frame ReadMaster(string path, string kind)
    {
        try
        {
            bool isTmp = Path.GetExtension(path).Equals(".tmp", StringComparison.OrdinalIgnoreCase);
            return isTmp ? MasterTmpFile.Read(path) : PgmFile.Read(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"master {kind} frame '{path}' cannot be read: {ex.Message}", ex);
        }
    }

    private static PixelRect? BuildCrop(OpippOptions o)
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

    private static string InputCount(int count) => count == 1 ? "1 input file" : $"{count} input files";

    private static void PrintSummary(
        ProcessingReport report,
        IReadOnlyList<string> inputs,
        OpippOptions options,
        PipelineSettings settings,
        IReadOnlyList<FailedInput> failures)
    {
        if (options.Gui) return;

        string result = failures.Count > 0
            ? (report.OutputFrames > 0
                ? $"PARTIAL FAILURE - {InputCount(failures.Count)} could not be read; output is incomplete"
                : $"FAILURE - {InputCount(failures.Count)} could not be read")
            : report.OutputFrames > 0 ? "SUCCESS" : "FAILURE - no frames written";

        Console.WriteLine();
        Console.WriteLine("  Processing Details:");
        Console.WriteLine($"    Input files:                     {inputs.Count}");
        Console.WriteLine($"    Files not found:                 {failures.Count}");
        Console.WriteLine($"    Total input frames:              {report.InputFrames}");
        Console.WriteLine($"    Frames discarded for quality:    {report.DiscardedByQuality}");
        Console.WriteLine($"    Frames discarded with no object: {report.DiscardedNoObject}");
        Console.WriteLine($"    Total output frames:             {report.OutputFrames}");
        Console.WriteLine();
        Console.WriteLine("  Summary:");
        Console.WriteLine($"    Result:                          {result}");
        Console.WriteLine($"    Output frame format:             {settings.OutputExtension}");
        Console.WriteLine($@"    Output Directory:                {(string.IsNullOrWhiteSpace(settings.OutputSubdirectory) ? settings.OutputDirectory : Path.Combine(settings.OutputDirectory, settings.OutputSubdirectory!))}");

        foreach (FailedInput failure in failures)
        {
            Console.WriteLine($"    Failed: {failure.Path} ({failure.Reason})");
        }
        foreach (var file in report.OutputFiles)
        {
            Console.WriteLine($"    Written: {file}");
        }

        if (failures.Count > 0 && report.OutputFrames > 0)
        {
            Console.WriteLine($"    Partial output:                  {report.OutputFrames} frame(s) retained; incomplete");
        }
    }
}
