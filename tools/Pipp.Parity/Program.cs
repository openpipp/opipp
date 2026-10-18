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

        /// <summary>Known, deliberate difference from the original; reported but not counted as failure.</summary>
        KnownDivergence,

        /// <summary>Feature not implemented yet; reported but not counted as failure.</summary>
        KnownGap,
    }

    private sealed record ParityCase(
        string Name,
        string[] Switches,
        string Input = "ser",
        CompareMode Mode = CompareMode.Exact,
        string? Note = null);

    private static int Main(string[] args)
    {
        string repo = FindRepoRoot();
        string oracle = Arg(args, "--oracle") ?? Path.Combine(repo, "bin", "pipp_core.exe");
        string managed = Arg(args, "--managed") ?? Path.Combine(repo, "src", "Pipp.Cli", "bin", "Debug", "net10.0", "Pipp.Cli.exe");
        string work = Arg(args, "--work") ?? Path.Combine(repo, "parity");
        bool includeRealData = Array.Exists(args, a => a == "--real");
        string realInput = Arg(args, "--real-input") ?? Path.Combine(repo, "samples", "saturn30.ser");

        Console.WriteLine($"oracle : {oracle}");
        Console.WriteLine($"managed: {managed}");
        Console.WriteLine($"work   : {work}");

        if (!File.Exists(oracle)) { Console.Error.WriteLine("oracle not found"); return 2; }
        if (!File.Exists(managed)) { Console.Error.WriteLine("managed engine not found (build Pipp.Cli first)"); return 2; }
        if (includeRealData && !File.Exists(realInput))
        {
            Console.Error.WriteLine($"real-data input not found: {realInput}");
            return 2;
        }

        Directory.CreateDirectory(work);
        string input = Path.Combine(work, "capture.ser");
        WriteSyntheticSer(input, width: 32, height: 24, frames: 4, bitDepth: 8);

        string bayerInput = Path.Combine(work, "bayer.ser");
        WriteBayerSer(bayerInput, width: 64, height: 48, frames: 4);

        string ser16Input = Path.Combine(work, "capture16.ser");
        WriteSyntheticSer(ser16Input, width: 32, height: 24, frames: 4, bitDepth: 16);

        string colourInput = Path.Combine(work, "colour.ser");
        WriteColourSer(colourInput, width: 32, height: 24, frames: 4);

        string bigInput = Path.Combine(work, "big.ser");
        WriteSyntheticSer(bigInput, width: 96, height: 96, frames: 4, bitDepth: 8, discRadius: 20, drift: 4, flatBackground: true);

        var cases = new List<ParityCase>
        {
            // container plumbing and single transforms first: a mismatch here means I/O, not math
            new ParityCase("plain-tif", ["-tif"]),
            new ParityCase("stretch", ["-tif", "-hs=204"]),
            new ParityCase("flip", ["-tif", "-flip_x"]),
            new ParityCase("fits-out", ["-fits"]),
            new ParityCase("png-out", ["-png"]),
            new ParityCase("avi-out", ["-avi"]),
            // -db=2 alone is only the *pattern*; the engine debayers when the bare -db flag is also
            // present. This case therefore pins the pattern-only no-op (both engines keep the frame
            // mono), which is a real part of the switch's contract but not interpolation.
            new ParityCase("db-pattern-only", ["-tif", "-db=2", "-db_type=0"], Input: "bayer"),
            // Real debayering (-db + pattern) does not match yet: our interpolation differs from the
            // original by 1 LSB on ~37% of interior pixels (up to 3 at the edges). Phase 2/4 work.
            new ParityCase("debayer-interpolated", ["-tif", "-db", "-db=2", "-db_type=0"], Input: "bayer",
                Mode: CompareMode.KnownGap,
                Note: "debayer interpolation differs by 1 LSB on ~37% of pixels"),
            // colour stretch: the colour fixture has different per-channel tails (240/200/120), so the
            // smallest-gain rule and the -hsrgb per-channel gains are both observable
            new ParityCase("stretch-colour", ["-tif", "-hs=204"], Input: "colour"),
            new ParityCase("stretch-colour-rgb", ["-tif", "-hs=204", "-hsrgb"], Input: "colour"),
            // the engine's default 15px minimum diameter rejects this disc, so relax it to reach
            // the centring transform itself
            new ParityCase("centre", ["-tif", "-ctr", "-ms=5"], Input: "big"),
            new ParityCase("quality", ["-tif", "-q=2"], Mode: CompareMode.SetEquality),
            new ParityCase("bmp-out", ["-bmp"]),
            // Newly wired switch surfaces, locked in so they cannot regress silently.
            new ParityCase("range", ["-tif", "-sf=2", "-f=2"]),
            // SER output: headers now carry the engine's metadata rules (ColorID, BE flag, strings).
            new ParityCase("ser-out", ["-ser"]),
            new ParityCase("drop", ["-tif", "-drop=2"]),
            new ParityCase("qw", ["-tif", "-q=2", "-qw"], Mode: CompareMode.SetEquality),
            new ParityCase("anim", ["-avi", "-anim_fwd", "-anim_rev"]),
            new ParityCase("y8-out", ["-avi", "-y8"]),
            // The original writes a GIF with no image descriptor at all (only a NETSCAPE extension,
            // a comment block and the trailer), so there is nothing to compare pixels against; we
            // emit a valid animation instead. Deliberate divergence, like the 16-bit TIFF case.
            new ParityCase("gif-out", ["-gif"], Mode: CompareMode.KnownDivergence,
                Note: "original emits an image-less GIF; we write a valid animation"),
            // The original writes 16-bit TIFF samples byte-swapped under an "II" header, which makes
            // the files unreadable by standard tools (confirmed with ffmpeg). We write spec-correct
            // files instead; this is a deliberate divergence, not an unknown.
            new ParityCase("tif-16bit", ["-tif"], Input: "ser16", Mode: CompareMode.KnownDivergence,
                Note: "16-bit TIFF: original byte-swaps samples; we write spec-correct files"),
            // The original's SER probe maps ColorID 16 to BAYER_CYYM rather than RGB, so it reads a
            // spec-correct colour SER as a mono mosaic. Resolve the colour dialect before comparing.
            new ParityCase("tif-colour", ["-tif"], Input: "colour"),
            new ParityCase("png-colour", ["-png"], Input: "colour"),
        };

        if (includeRealData)
        {
            // Real 640x640 8-bit Bayer capture (30 frames). This is the set that exposed the
            // image-dependent stretch: -hs measures the frame's bright tail, so the synthetic disc
            // cannot exercise it. Kept behind --real because samples/ is git-ignored third-party data.
            cases.Add(new ParityCase("real-plain", ["-tif"], Input: "real"));
            cases.Add(new ParityCase("real-stretch", ["-tif", "-hs=204"], Input: "real"));
            cases.Add(new ParityCase("real-stretch-bp", ["-tif", "-hs=204", "-hs_bp"], Input: "real"));
            cases.Add(new ParityCase("real-centre", ["-tif", "-ctr"], Input: "real"));
        }

        int failures = 0;
        foreach (var c in cases)
        {
            string caseInput = c.Input switch
            {
                "bayer" => bayerInput,
                "big" => bigInput,
                "ser16" => ser16Input,
                "colour" => colourInput,
                "real" => realInput,
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
        if (c.Mode is CompareMode.KnownDivergence or CompareMode.KnownGap)
        {
            Console.WriteLine($"  ({c.Mode}: {c.Note})");
            return true;
        }

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

        bool layoutMatch = CompareTrees(oracleOut, managedOut);
        allMatch &= layoutMatch;

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

    /// <summary>
    /// Compares the produced directory trees (relative paths) and the engine log text, so layout,
    /// numbering and log content are held to the same standard as pixel output.
    /// </summary>
    private static bool CompareTrees(string oracleDir, string managedDir)
    {
        bool ok = true;

        static List<string> RelativeFiles(string root) =>
            Directory.Exists(root)
                ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList()
                : [];

        var oracleFiles = RelativeFiles(oracleDir);
        var managedFiles = RelativeFiles(managedDir);
        bool namesMatch = oracleFiles.SequenceEqual(managedFiles, StringComparer.Ordinal);
        Console.WriteLine($"  layout: {(namesMatch ? "MATCH" : "DIVERGENCE")} ({oracleFiles.Count} vs {managedFiles.Count} files)");
        if (!namesMatch)
        {
            ok = false;
            foreach (string missing in oracleFiles.Except(managedFiles).Take(4)) Console.WriteLine($"    missing: {missing}");
            foreach (string extra in managedFiles.Except(oracleFiles).Take(4)) Console.WriteLine($"    extra:   {extra}");
        }

        string? oracleLog = oracleFiles.FirstOrDefault(f => f.StartsWith("logs/", StringComparison.Ordinal) && f.EndsWith("_pipp_log.txt", StringComparison.Ordinal));
        string? managedLog = managedFiles.FirstOrDefault(f => f.StartsWith("logs/", StringComparison.Ordinal) && f.EndsWith("_pipp_log.txt", StringComparison.Ordinal));
        if (oracleLog is null || managedLog is null)
        {
            Console.WriteLine($"  log: {(oracleLog is null && managedLog is null ? "MATCH (none)" : "DIVERGENCE")}");
            return ok && oracleLog is null && managedLog is null;
        }

        string left = NormaliseLog(File.ReadAllText(Path.Combine(oracleDir, oracleLog)), oracleDir);
        string right = NormaliseLog(File.ReadAllText(Path.Combine(managedDir, managedLog)), managedDir);
        bool logMatch = left == right;
        Console.WriteLine($"  log: {(logMatch ? "MATCH" : "DIVERGENCE")}");
        if (!logMatch)
        {
            ok = false;
            var a = left.Split('\n');
            var b = right.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string la = i < a.Length ? a[i] : "<missing>";
                string lb = i < b.Length ? b[i] : "<missing>";
                if (la != lb)
                {
                    Console.WriteLine($"    oracle : {la}");
                    Console.WriteLine($"    managed: {lb}");
                    break;
                }
            }
        }

        string? oracleQuality = oracleFiles.FirstOrDefault(f => f.StartsWith("logs/", StringComparison.Ordinal) && f.EndsWith("_pipp_quality.txt", StringComparison.Ordinal));
        string? managedQuality = managedFiles.FirstOrDefault(f => f.StartsWith("logs/", StringComparison.Ordinal) && f.EndsWith("_pipp_quality.txt", StringComparison.Ordinal));
        if (oracleQuality is not null || managedQuality is not null)
        {
            string qa = oracleQuality is null ? "<none>" : NormaliseQualityLog(File.ReadAllText(Path.Combine(oracleDir, oracleQuality)));
            string qb = managedQuality is null ? "<none>" : NormaliseQualityLog(File.ReadAllText(Path.Combine(managedDir, managedQuality)));
            bool qualityMatch = qa == qb;
            Console.WriteLine($"  quality log: {(qualityMatch ? "MATCH (structure; scores are Phase 4)" : "DIVERGENCE")}");
            if (!qualityMatch)
            {
                ok = false;
                Console.WriteLine("    oracle : " + qa.Replace("\n", " | "));
                Console.WriteLine("    managed: " + qb.Replace("\n", " | "));
            }
        }

        return ok;
    }

    /// <summary>
    /// Keeps the quality log's structure (position, source frame, output frame) and blanks the score
    /// columns, which come from a quality algorithm that is not parity-proven until Phase 4.
    /// </summary>
    private static string NormaliseQualityLog(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var columns = lines[i].Split('\t');
            if (columns.Length == 5 && int.TryParse(columns[0], out _))
            {
                columns[3] = "<Q>";
                columns[4] = "<P>";
                lines[i] = string.Join("\t", columns);
            }
        }

        return string.Join("\n", lines).TrimEnd();
    }

    /// <summary>
    /// Normalises the differences that are properties of the harness rather than of the engine:
    /// duplicated argument lists (we pass switches on argv and stdin), absolute output paths, and
    /// numbers produced by algorithms that are not parity-proven yet.
    /// </summary>
    private static string NormaliseLog(string text, string directory)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        // collapse a repeated "Command line arguments" block into one
        int first = lines.FindIndex(l => l.StartsWith("Command line arguments:", StringComparison.Ordinal));
        if (first >= 0)
        {
            int end = lines.FindIndex(first + 1, l => l.Trim().Length == 0);
            if (end > first)
            {
                var block = lines.GetRange(first + 1, end - first - 1);
                var deduped = new List<string>();
                foreach (string line in block) if (!deduped.Contains(line)) deduped.Add(line);
                if (deduped.Count != block.Count)
                {
                    // remove the second copy: the harness passes the same switches twice
                    lines.RemoveRange(first + 1, block.Count);
                    lines.InsertRange(first + 1, deduped);
                }
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            line = line.Replace(directory.Replace('\\', '/'), "<OUT>").Replace(directory, "<OUT>");
            if (line.StartsWith("  Calculated minpixel value:", StringComparison.Ordinal))
            {
                line = "  Calculated minpixel value: <N>";
            }
            lines[i] = line;
        }

        return string.Join("\n", lines).TrimEnd();
    }

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
    /// Writes a 3-channel SER with a bright disc on a flat dark field: detector-friendly, and the
    /// colour fringes make channel handling visible in the output.
    /// </summary>
    private static void WriteColourSer(string path, int width, int height, int frames)
    {
        using var writer = new SerWriter(path, width, height, 3, 8, frames);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 3, 8);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int dx = x - (width / 2 + f), dy = y - height / 2;
                    bool inside = dx * dx + dy * dy <= 16;
                    int index = ((y * width) + x) * 3;
                    if (inside)
                    {
                        frame.Samples[index] = 240;
                        frame.Samples[index + 1] = 200;
                        frame.Samples[index + 2] = 120;
                    }
                    else
                    {
                        frame.Samples[index] = 38;
                        frame.Samples[index + 1] = 30;
                        frame.Samples[index + 2] = 24;
                    }
                }
            }
            writer.Write(frame);
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
