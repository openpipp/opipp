using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pipp.Core.Frames;
using Pipp.Core.Formats;

namespace Pipp.Parity;

/// <summary>
/// Differential harness: runs the original <c>pipp_core.exe</c> and the managed engine on the same
/// synthetic capture with the same switches, then compares the produced files.
/// </summary>
/// <remarks>
/// The original engine is the oracle. Nothing here modifies it: it is launched from a copy inside the
/// repository and writes only into the work directory. Arguments are supplied both as argv (the
/// documented <c>pipp [options] input_file(s)</c> form) and on stdin (the form the WinForms
/// front-end uses), so either protocol works.
/// </remarks>
internal static class Program
{
    private enum CompareMode
    {
        /// <summary>Frames are compared positionally by numeric name.</summary>
        Exact,

        /// <summary>Frames are compared as a multiset (quality selection may reorder them).</summary>
        SetEquality,
    }

    private sealed record ParityCase(string Name, string[] Switches, string Input = "ser", CompareMode Mode = CompareMode.Exact);

    private static int Main(string[] args)
    {
        string repo = FindRepoRoot();
        string oracle = Arg(args, "--oracle") ?? Path.Combine(repo, "bin", "pipp_core.exe");
        string managed = Arg(args, "--managed") ?? Path.Combine(repo, "src", "Pipp.Cli", "bin", "Debug", "net10.0", "Pipp.Cli.exe");
        string work = Arg(args, "--work") ?? Path.Combine(repo, "parity");

        Console.WriteLine($"oracle : {oracle}");
        Console.WriteLine($"managed: {managed}");
        Console.WriteLine($"work   : {work}");

        if (!File.Exists(oracle)) { Console.Error.WriteLine("oracle not found"); return 2; }
        if (!File.Exists(managed)) { Console.Error.WriteLine("managed engine not found (build Pipp.Cli first)"); return 2; }

        Directory.CreateDirectory(work);
        string input = Path.Combine(work, "capture.ser");
        WriteSyntheticSer(input, width: 32, height: 24, frames: 4, bitDepth: 8);

        string bayerInput = Path.Combine(work, "bayer.ser");
        WriteBayerSer(bayerInput, width: 64, height: 48, frames: 4);

        string bigInput = Path.Combine(work, "big.ser");
        WriteSyntheticSer(bigInput, width: 96, height: 96, frames: 4, bitDepth: 8, discRadius: 20, drift: 4, flatBackground: true);

        var cases = new[]
        {
            // container plumbing and single transforms first: a mismatch here means I/O, not math
            new ParityCase("plain-tif", ["-tif"]),
            new ParityCase("stretch", ["-tif", "-hs=204"]),
            new ParityCase("flip", ["-tif", "-flip_x"]),
            new ParityCase("fits-out", ["-fits"]),
            new ParityCase("png-out", ["-png"]),
            new ParityCase("avi-out", ["-avi"]),
            new ParityCase("debayer", ["-tif", "-db=2", "-db_type=0"], Input: "bayer"),
            // the engine's default 15px minimum diameter rejects this disc, so relax it to reach
            // the centring transform itself
            new ParityCase("centre", ["-tif", "-ctr", "-ms=5"], Input: "big"),
            new ParityCase("quality", ["-tif", "-q=2"], Mode: CompareMode.SetEquality),
        };

        int failures = 0;
        foreach (var c in cases)
        {
            string caseInput = c.Input switch
            {
                "bayer" => bayerInput,
                "big" => bigInput,
                _ => input,
            };
            failures += RunCase(c, oracle, managed, work, caseInput) ? 0 : 1;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL CASES MATCH" : $"{failures} case(s) diverged");
        return failures == 0 ? 0 : 1;
    }

    private static bool RunCase(ParityCase c, string oracle, string managed, string work, string input)
    {
        string caseDir = Path.Combine(work, c.Name);
        string oracleOut = Path.Combine(caseDir, "oracle");
        string managedOut = Path.Combine(caseDir, "managed");
        if (Directory.Exists(caseDir)) Directory.Delete(caseDir, recursive: true);
        Directory.CreateDirectory(oracleOut);
        Directory.CreateDirectory(managedOut);

        Console.WriteLine();
        Console.WriteLine($"=== {c.Name}: {string.Join(' ', c.Switches)} ===");

        var oracleRun = Run(oracle, [.. c.Switches, "-od=" + oracleOut, input], work);
        var managedRun = Run(managed, [.. c.Switches, "-od=" + managedOut, input], work);

        Console.WriteLine($"  oracle  exit={oracleRun.ExitCode} files={CountFiles(oracleOut)}");
        Console.WriteLine($"  managed exit={managedRun.ExitCode} files={CountFiles(managedOut)}");
        if (oracleRun.ExitCode != 0)
        {
            Console.WriteLine("  oracle output:");
            foreach (var line in Tail(oracleRun.StdOut, 8)) Console.WriteLine("    " + line);
        }

        var oracleFiles = ListOutputs(oracleOut);
        var managedFiles = ListOutputs(managedOut);

        if (oracleFiles.Count == 0)
        {
            Console.WriteLine("  DIVERGENCE: oracle produced no files");
            return false;
        }

        Console.WriteLine($"  oracle names : {string.Join(", ", oracleFiles.Select(f => Path.GetRelativePath(oracleOut, f)))}");
        Console.WriteLine($"  managed names: {string.Join(", ", managedFiles.Select(f => Path.GetRelativePath(managedOut, f)))}");

        if (oracleFiles.Count != managedFiles.Count)
        {
            Console.WriteLine("  DIVERGENCE: file count differs");
            return false;
        }

        bool allMatch = true;
        for (int i = 0; i < oracleFiles.Count; i++)
        {
            var a = Describe(oracleFiles[i]);
            var b = Describe(managedFiles[i]);
            bool same = a.Kind == b.Kind && a.Width == b.Width && a.Height == b.Height &&
                        a.Channels == b.Channels && a.BitDepth == b.BitDepth && a.Hash == b.Hash;
            Console.WriteLine($"  [{i}] oracle  {Path.GetFileName(oracleFiles[i]),-24} {a}");
            Console.WriteLine($"      managed {Path.GetFileName(managedFiles[i]),-24} {b}");
            if (!same)
            {
                allMatch = false;
                Console.WriteLine("      DIVERGENCE");
                if (a.Kind == b.Kind)
                {
                    ReportPixelDiff(oracleFiles[i], managedFiles[i], a.Width, a.Channels);
                }
            }
        }

        if (c.Mode == CompareMode.SetEquality)
        {
            // Quality selection may legitimately keep the same frames in a different order,
            // so compare the multisets of frame hashes rather than positional pairs.
            var oracleHashes = oracleFiles.Select(f => Describe(f).Hash).OrderBy(h => h, StringComparer.Ordinal).ToList();
            var managedHashes = managedFiles.Select(f => Describe(f).Hash).OrderBy(h => h, StringComparer.Ordinal).ToList();
            bool setsMatch = oracleHashes.SequenceEqual(managedHashes, StringComparer.Ordinal);
            Console.WriteLine($"  frame multiset: {(setsMatch ? "MATCH" : "DIVERGENCE")}");
            allMatch &= setsMatch;
        }

        return allMatch;
    }

    private sealed record RunResult(int ExitCode, string StdOut, string StdErr);

    private static RunResult Run(string exe, string[] switches, string workDir)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var s in switches) psi.ArgumentList.Add(s);

        using var process = Process.Start(psi)!;
        try
        {
            // The WinForms front-end drives the engine purely through stdin and terminates the
            // stream with the literal line "DONE" (Form1.do_processing_thread). Without that
            // terminator the engine blocks forever waiting for more input, which is why an
            // EOF-terminated pipe appears to hang. Switches come first, then the input files.
            var switchTokens = switches.Where(t => t.StartsWith('-')).ToArray();
            var fileTokens = switches.Where(t => !t.StartsWith('-')).ToArray();
            foreach (var token in switchTokens) process.StandardInput.WriteLine(token);
            foreach (var token in fileTokens) process.StandardInput.WriteLine(token);
            process.StandardInput.WriteLine("DONE");
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // the process exited before reading stdin; that is fine
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new RunResult(-1, stdout, "timeout");
        }

        return new RunResult(process.ExitCode, stdout, stderr);
    }

    private sealed record Description(string Kind, int Width, int Height, int Channels, int BitDepth, string Hash)
    {
        public override string ToString() =>
            $"{Kind} {Width}x{Height}x{Channels}@{BitDepth} sha256={Hash[..16]}";
    }

    private static string FormatFamily(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".fit" or ".fits" => ".fits",
            ".tif" or ".tiff" => ".tif",
            _ => ext,
        };
    }

    private static Description Describe(string path)
    {
        Frame? frame = null;
        string kind = FormatFamily(path);
        try
        {
            switch (kind)
            {
                case ".tif" or ".tiff":
                    using (var r = new TiffReader(path)) frame = r.ReadFrame();
                    break;
                case ".fits" or ".fit":
                    using (var r = new FitsReader(path)) frame = r.ReadFrame();
                    break;
                case ".png":
                    using (var r = new PngReader(path)) frame = r.ReadFrame();
                    break;
                case ".bmp":
                    using (var r = new BmpReader(path)) frame = r.ReadFrame();
                    break;
                case ".ser":
                    return DescribeContainer(new SerReader(path));
                case ".avi":
                    return DescribeContainer(new AviReader(path));
            }
        }
        catch (Exception ex)
        {
            return new Description(kind + " (unreadable: " + ex.GetType().Name + ")", 0, 0, 0, 0, "n/a");
        }

        if (frame is null) return new Description(kind + " (empty)", 0, 0, 0, 0, "n/a");

        var bytes = new byte[frame.Samples.Length * 2];
        for (int i = 0; i < frame.Samples.Length; i++)
        {
            bytes[i * 2] = (byte)(frame.Samples[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(frame.Samples[i] >> 8);
        }
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new Description(kind, frame.Width, frame.Height, frame.Channels, frame.BitDepth, hash);
    }

    /// <summary>
    /// Finds comparison frames under an output directory. The engine writes into a per-input
    /// subdirectory (<c>&lt;od&gt;/&lt;name&gt;/1.tif</c>) plus a <c>logs</c> directory; this
    /// implementation writes flat files. Both shapes are accepted and ordered by frame number.
    /// </summary>
    private static List<string> ListOutputs(string dir)
    {
        if (!Directory.Exists(dir)) return [];

        var candidates = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "logs" + Path.DirectorySeparatorChar,
                                    StringComparison.OrdinalIgnoreCase))
            .Where(f => IsFrameFile(f))
            .ToList();

        return candidates.OrderBy(NumericKey).ThenBy(f => f, StringComparer.Ordinal).ToList();
    }

    /// <summary>Prints the first differing samples so a mismatch is diagnosable without extra tooling.</summary>
    private static void ReportPixelDiff(string oraclePath, string managedPath, int width, int channels)
    {
        var left = LoadSamples(oraclePath);
        var right = LoadSamples(managedPath);
        if (left is null || right is null || left.Length != right.Length)
        {
            Console.WriteLine($"        (could not align samples: {left?.Length} vs {right?.Length})");
            return;
        }

        int shown = 0, total = 0;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i]) continue;
            total++;
            if (shown++ < 5)
            {
                int pixel = i / channels;
                Console.WriteLine($"        sample {i} (x={pixel % width},y={pixel / width}) oracle={left[i]} managed={right[i]}");
            }
        }
        Console.WriteLine($"        differing samples: {total} of {left.Length}");
    }

    private static int[]? LoadSamples(string path)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".tif" or ".tiff":
                    using (var r = new TiffReader(path)) return ToInts(r.ReadFrame()?.Samples);
                case ".fits" or ".fit":
                    using (var r = new FitsReader(path)) return ToInts(r.ReadFrame()?.Samples);
                case ".png":
                    using (var r = new PngReader(path)) return ToInts(r.ReadFrame()?.Samples);
                case ".bmp":
                    using (var r = new BmpReader(path)) return ToInts(r.ReadFrame()?.Samples);
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static int[]? ToInts(ushort[]? samples) =>
        samples?.Select(v => (int)v).ToArray();

    private static bool IsFrameFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff" or ".fits" or ".fit" or ".png" or ".bmp" or ".ser" or ".avi";

    /// <summary>Sort key: the numeric stem when the file is renumbered, else the name.</summary>
    private static long NumericKey(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var digits = new string(stem.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out long n) ? n : long.MaxValue;
    }

    private static int CountFiles(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length : 0;

    private static IEnumerable<string> Tail(string text, int lines) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines).Select(l => l.TrimEnd('\r'));

    /// <summary>Hashes every frame of a multi-frame container (SER/AVI) as one payload.</summary>
    private static Description DescribeContainer(IFrameSource source)
    {
        using (source)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int frames = 0, width = source.Width, height = source.Height, channels = source.Channels, bitDepth = source.BitDepth;
            while (source.ReadFrame() is { } frame)
            {
                var bytes = new byte[frame.Samples.Length * 2];
                for (int i = 0; i < frame.Samples.Length; i++)
                {
                    bytes[i * 2] = (byte)(frame.Samples[i] & 0xFF);
                    bytes[i * 2 + 1] = (byte)(frame.Samples[i] >> 8);
                }
                hash.AppendData(bytes);
                frames++;
            }
            string hashText = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new Description($"container x{frames}", width, height, channels, bitDepth, hashText);
        }
    }

    /// <summary>
    /// Writes a single-plane Bayer SER (ColorID 8, RGGB) containing a colour disc on a dark field:
    /// enough structure for the engine's detector to keep the frames, and enough mosaic detail for
    /// the debayer result to differ from a plain copy.
    /// </summary>
    private static void WriteBayerSer(string path, int width, int height, int frames)
    {
        using var writer = new SerWriter(path, width, height, 1, 8, frames, BayerPattern.RGGB);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, 8) { Bayer = BayerPattern.RGGB };
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int dx = x - (width / 2 + f), dy = y - height / 2;
                    bool inside = dx * dx + dy * dy <= 100;
                    int value;
                    if (inside)
                    {
                        // colour fringes inside the disc so interpolation is observable
                        bool redSite = (y % 2 == 0) == (x % 2 == 0);
                        value = redSite ? 230 : (y % 2 == 0 ? 180 : 120);
                    }
                    else
                    {
                        value = 18 + (x + y) % 7;
                    }
                    frame.Samples[y * width + x] = (byte)value;
                }
            }
            writer.Write(frame);
        }
    }

    private static void WriteSyntheticSer(string path, int width, int height, int frames, int bitDepth,
        int discRadius = 4, int drift = 1, bool flatBackground = false)
    {
        using var writer = new SerWriter(path, width, height, 1, bitDepth);
        int max = bitDepth == 8 ? 255 : 65535;
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, bitDepth);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int dx = x - (width / 2 + f * drift), dy = y - height / 2;
                    bool inside = dx * dx + dy * dy <= discRadius * discRadius;
                    int background = (int)(max * 0.15);
                    // a ramp in the background makes stretch/flip differences visible, but the
                    // engine's detector needs a flat dark field to accept a large disc
                    int value = inside ? max : (flatBackground ? background : background + (x * 2 + y));
                    frame.Samples[y * width + x] = (ushort)Math.Clamp(value, 0, max);
                }
            }
            writer.Write(frame);
        }
    }

    private static string? Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PIPP.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
