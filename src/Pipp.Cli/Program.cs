using System.Globalization;
using Pipp.Core.Alignment;
using Pipp.Core.Cli;
using Pipp.Core.Diagnostics;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Pipp.Core.Pipeline;
using Pipp.Core.Processing;
using Pipp.Core.Quality;

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
    private static int Main(string[] args)
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
            settings = Map(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        // The log echoes the switch list only; input paths appear under "Source image files".
        settings.LogArguments = effectiveArgs.Where(a => !inputs.Contains(a, StringComparer.Ordinal)).ToArray();

        var protocol = options.Gui ? new EngineProtocolWriter(Console.Out) : null;
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
        return report.OutputFrames > 0 ? 0 : 1;
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

        return settings;
    }

    private static Frame? LoadMaster(string? tmpFile, string? pgmFile, string kind, bool enabled)
    {
        if (!enabled) return null;

        foreach (var candidate in new[] { pgmFile, tmpFile })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;
            try
            {
                return PgmFile.Read(candidate);
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
        Console.WriteLine($"    Output Directory:                {settings.OutputDirectory}");

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
