using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Opipp.Core.Frames;
using Opipp.Core.Formats;

namespace Opipp.Parity;

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
    internal enum CompareMode
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

    internal enum CaseOutcome { Match, ExpectedDifference, Gap, Fail }

    internal sealed record ParityCase(
        string Name,
        string[] Switches,
        string Input = "ser",
        CompareMode Mode = CompareMode.Exact,
        string? Note = null,
        // Emptiness is never inferred: a case whose output legitimately contains zero frames must
        // say so here, otherwise the verdict treats it as a failure rather than a silent match.
        bool AllowEmptyOutput = false);

    private static int Main(string[] args)
    {
        string repo = FindRepoRoot();
        string oracle = Arg(args, "--oracle") ?? Path.Combine(repo, "bin", "pipp_core.exe");
        string managed = Arg(args, "--managed") ?? Path.Combine(repo, "src", "Opipp.Cli", "bin", "Debug", "net10.0", "Opipp.Cli.exe");
        string work = Arg(args, "--work") ?? Path.Combine(repo, "parity");
        bool includeRealData = Array.Exists(args, a => a == "--real");
        // Acknowledged gaps normally never run either engine. --measure executes them anyway and
        // prints the full pixel statistics, so a gap can be quantified rather than merely named.
        // The outcome stays Gap/ExpectedDifference: measuring a gap does not make it a match.
        bool measure = Array.Exists(args, a => a == "--measure");
        string? only = Arg(args, "--only");
        string realInput = Arg(args, "--real-input") ?? Path.Combine(repo, "samples", "saturn30.ser");

        Console.WriteLine($"oracle : {oracle}");
        Console.WriteLine($"managed: {managed}");
        Console.WriteLine($"work   : {work}");

        if (!File.Exists(oracle)) { Console.Error.WriteLine("oracle not found"); return 2; }
        if (!File.Exists(managed)) { Console.Error.WriteLine("managed engine not found (build Opipp.Cli first)"); return 2; }
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
            // Real debayering (-db + pattern): bilinear with truncating integer division plus a
            // dcraw-style border_interpolate(1) pass over the outer ring. That recipe is
            // byte-identical to the original on this fixture (and on the same mosaic read as all
            // four patterns); before it was found the result differed by up to 3 LSB on 37% of
            // pixels. Use --measure to re-quantify if this ever regresses.
            new ParityCase("debayer-interpolated", ["-tif", "-db", "-db=2", "-db_type=0"], Input: "bayer"),
            // colour stretch: the colour fixture has different per-channel tails (240/200/120), so the
            // smallest-gain rule and the -hsrgb per-channel gains are both observable
            new ParityCase("stretch-colour", ["-tif", "-hs=204"], Input: "colour"),
            new ParityCase("stretch-colour-rgb", ["-tif", "-hs=204", "-hsrgb"], Input: "colour"),
            // the engine's default 15px minimum diameter rejects this disc, so relax it to reach
            // the centring transform itself
            new ParityCase("centre", ["-tif", "-ctr", "-ms=5"], Input: "big"),
            new ParityCase("quality", ["-tif", "-q=2"], Mode: CompareMode.SetEquality),
            // The two alternative metrics: Ninox must match numerically, Histogram keeps +inf scores.
            new ParityCase("quality-old", ["-tif", "-q=2", "-qold"], Mode: CompareMode.SetEquality),
            new ParityCase("quality-histo", ["-tif", "-q=2", "-qhisto"], Mode: CompareMode.SetEquality),
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

        if (only is not null)
        {
            var filtered = cases.Where(c => c.Name.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0)
            {
                Console.Error.WriteLine($"no such case: {only}");
                return 2;
            }
            Console.WriteLine($"only   : {only}");
            cases = filtered;
        }

        int matched = 0, expected = 0, gaps = 0, failed = 0;
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
            switch (RunCase(c, oracle, managed, work, caseInput, measure))
            {
                case CaseOutcome.Match: matched++; break;
                case CaseOutcome.ExpectedDifference: expected++; break;
                case CaseOutcome.Gap: gaps++; break;
                default: failed++; break;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"compared: {matched + failed} | matched: {matched} | expected differences: {expected} | gaps: {gaps} | failed: {failed}");
        Console.WriteLine(failed == 0 ? "NO UNEXPECTED DIVERGENCES" : $"{failed} case(s) diverged");
        return failed == 0 ? 0 : 1;
    }

    internal static CaseOutcome RunCase(ParityCase c, string oracle, string managed, string work, string input,
        bool measure, TimeSpan? timeout = null, string[]? oracleSwitches = null, string[]? managedSwitches = null)
    {
        string caseDir = Path.Combine(work, c.Name);
        string oracleOut = Path.Combine(caseDir, "oracle");
        string managedOut = Path.Combine(caseDir, "managed");
        if (Directory.Exists(caseDir)) Directory.Delete(caseDir, recursive: true);
        Directory.CreateDirectory(oracleOut);
        Directory.CreateDirectory(managedOut);

        bool acknowledged = c.Mode is CompareMode.KnownDivergence or CompareMode.KnownGap;
        Console.WriteLine();
        Console.WriteLine($"=== {c.Name}: {string.Join(' ', c.Switches)} ===");
        if (acknowledged)
        {
            // Deliberate differences and acknowledged gaps are reported, never counted as matches:
            // neither engine runs, so this case proves nothing about parity.
            Console.WriteLine($"  ({c.Mode}: {c.Note})");
            if (!measure) return c.Mode == CompareMode.KnownDivergence ? CaseOutcome.ExpectedDifference : CaseOutcome.Gap;
            Console.WriteLine("  MEASURE: running both engines to quantify the acknowledged difference");
        }

        // oracleSwitches/managedSwitches exist for fake-engine tests, where the two sides need
        // different behaviour from the same fixture executable; the CLI never supplies them.
        var oracleRun = Run(oracle, [.. oracleSwitches ?? c.Switches, "-od=" + oracleOut, input], work, timeout);
        var managedRun = Run(managed, [.. managedSwitches ?? c.Switches, "-od=" + managedOut, input], work, timeout);

        Console.WriteLine($"  oracle  exit={ExitText(oracleRun)} files={CountFiles(oracleOut)}");
        Console.WriteLine($"  managed exit={ExitText(managedRun)} files={CountFiles(managedOut)}");

        // An engine that did not exit successfully did not produce a trustworthy artifact, even if
        // it happened to write files and those files happen to match. Fail the side explicitly.
        bool processesSucceeded = true;
        if (!oracleRun.Succeeded)
        {
            processesSucceeded = false;
            Console.WriteLine($"  DIVERGENCE: oracle {FailureText(oracleRun)}");
            PrintProcessOutput("oracle", oracleRun);
        }
        if (!managedRun.Succeeded)
        {
            processesSucceeded = false;
            Console.WriteLine($"  DIVERGENCE: managed {FailureText(managedRun)}");
            PrintProcessOutput("managed", managedRun);
        }

        var oracleFiles = ListOutputs(oracleOut);
        var managedFiles = ListOutputs(managedOut);

        if (oracleFiles.Count == 0)
        {
            Console.WriteLine("  DIVERGENCE: oracle produced no files");
            return acknowledged ? Acknowledged(c) : CaseOutcome.Fail;
        }

        Console.WriteLine($"  oracle names : {string.Join(", ", oracleFiles.Select(f => Path.GetRelativePath(oracleOut, f)))}");
        Console.WriteLine($"  managed names: {string.Join(", ", managedFiles.Select(f => Path.GetRelativePath(managedOut, f)))}");

        if (oracleFiles.Count != managedFiles.Count)
        {
            Console.WriteLine("  DIVERGENCE: file count differs");
            return acknowledged ? Acknowledged(c) : CaseOutcome.Fail;
        }

        bool allMatch = processesSucceeded;
        var oracleDescriptions = oracleFiles.Select(Describe).ToList();
        var managedDescriptions = managedFiles.Select(Describe).ToList();

        if (c.Mode == CompareMode.SetEquality)
        {
            // Quality selection may legitimately keep the same frames in a different order, so a
            // positional pass would reject a correct result. Compare the multisets of full frame
            // descriptions (kind, geometry, depth and hash) instead; nothing here depends on order.
            Console.WriteLine("  (positional pairing ignored; frames compared as a multiset)");
            bool setsMatch = MultisetEqual(oracleDescriptions, managedDescriptions, c.AllowEmptyOutput);
            Console.WriteLine($"  frame multiset: {(setsMatch ? "MATCH" : "DIVERGENCE")} ({oracleDescriptions.Count} frames)");
            if (!setsMatch) ReportMultisetDifference(oracleDescriptions, managedDescriptions);
            allMatch &= setsMatch;
        }
        else
        {
            for (int i = 0; i < oracleFiles.Count; i++)
            {
                var a = oracleDescriptions[i];
                var b = managedDescriptions[i];
                bool same = DescriptionsMatch(a, b, c.AllowEmptyOutput);
                Console.WriteLine($"  [{i}] oracle  {Path.GetFileName(oracleFiles[i]),-24} {a}");
                Console.WriteLine($"      managed {Path.GetFileName(managedFiles[i]),-24} {b}");
                if (!same)
                {
                    allMatch = false;
                    Console.WriteLine("      DIVERGENCE");
                    if (a.Hash == EmptyHash || b.Hash == EmptyHash)
                    {
                        Console.WriteLine(c.AllowEmptyOutput
                            ? "      (empty output is declared expected, but the descriptions differ)"
                            : "      (empty output: zero frames decoded, so no image sample was compared)");
                    }
                    else if (!a.HasHash || !b.HasHash)
                    {
                        Console.WriteLine("      (unreadable output; content cannot be compared)");
                    }
                    else if (a.Kind == b.Kind)
                    {
                        if (measure) ReportDetailedDiff(oracleFiles[i], managedFiles[i], a.Width, a.Height, a.Channels, a.BitDepth);
                        else ReportPixelDiff(oracleFiles[i], managedFiles[i], a.Width, a.Channels);
                    }
                }
            }
        }

        bool layoutMatch = CompareTrees(oracleOut, managedOut);
        allMatch &= layoutMatch;

        if (acknowledged)
        {
            // Measurement never changes the verdict: a gap stays a gap until a fix makes the case
            // a real comparison, at which point its Mode is changed deliberately.
            Console.WriteLine(allMatch
                ? "  MEASURE: outputs are identical; this case can be promoted to a real comparison"
                : $"  MEASURE: quantified above; remains {c.Mode}");
            return Acknowledged(c);
        }

        return allMatch ? CaseOutcome.Match : CaseOutcome.Fail;
    }

    /// <summary>The verdict for an acknowledged case: never a match, never a failure.</summary>
    private static CaseOutcome Acknowledged(ParityCase c) =>
        c.Mode == CompareMode.KnownDivergence ? CaseOutcome.ExpectedDifference : CaseOutcome.Gap;

    internal sealed record RunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false, TimeSpan Timeout = default)
    {
        /// <summary>Only a clean exit with code 0 is a success; a killed timeout never is.</summary>
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    internal static RunResult Run(string exe, string[] switches, string workDir, TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(120);
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

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidOperationException($"could not start {exe}");

        // Start draining both pipes before doing anything that can block. A child that fills
        // stdout or stderr (or writes a lot before hanging) must not be able to wedge the harness
        // ahead of the timeout: the reads run on their own tasks while we wait with a deadline.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        // Feeding stdin can block too, if the child never reads it and the pipe buffer fills, so it
        // also runs on its own task; a stuck child is killed by the timeout below.
        Task stdinTask = Task.Run(() => FeedStdin(process, switches));

        int waitMs = (int)Math.Clamp(limit.TotalMilliseconds, 1, int.MaxValue);
        bool exited = process.WaitForExit(waitMs);
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // the process won the race and exited on its own; nothing left to kill
            }

            process.WaitForExit(10_000);
            string partialOut = Drain(stdoutTask);
            string partialErr = Drain(stderrTask);
            WaitQuietly(stdinTask);
            string timeoutNote = $"timeout after {limit.TotalSeconds:0.###}s; process killed";
            return new RunResult(-1, partialOut,
                string.IsNullOrEmpty(partialErr) ? timeoutNote : partialErr + Environment.NewLine + timeoutNote,
                TimedOut: true, Timeout: limit);
        }

        string stdout = Drain(stdoutTask);
        string stderr = Drain(stderrTask);
        WaitQuietly(stdinTask);

        return new RunResult(process.ExitCode, stdout, stderr, Timeout: limit);
    }

    /// <summary>
    /// Drives the engine through stdin the way the WinForms front-end does: switches first, then
    /// input files, terminated by the literal line <c>DONE</c>. Expected failures (the child exiting
    /// early or being killed) are swallowed; they are reported through the exit code, not a crash.
    /// </summary>
    private static void FeedStdin(Process process, string[] switches)
    {
        try
        {
            var input = process.StandardInput;
            foreach (var token in switches.Where(t => t.StartsWith('-'))) input.WriteLine(token);
            foreach (var token in switches.Where(t => !t.StartsWith('-'))) input.WriteLine(token);
            input.WriteLine("DONE");
            input.Flush();
            input.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // the process exited (or was killed) before reading stdin; that is fine
        }
    }

    /// <summary>Collects a reader task's text, but never waits forever for a pipe that stays open.</summary>
    private static string Drain(Task<string> task)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(5)) ? task.Result : string.Empty;
        }
        catch (AggregateException)
        {
            return string.Empty;
        }
    }

    private static void WaitQuietly(Task task)
    {
        try { task.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }

    private static string ExitText(RunResult run) =>
        run.TimedOut ? "TIMEOUT" : run.ExitCode.ToString(CultureInfo.InvariantCulture);

    private static string FailureText(RunResult run) => run.TimedOut
        ? $"timed out after {run.Timeout.TotalSeconds:0.###}s and was killed (exit code {run.ExitCode})"
        : $"exited with code {run.ExitCode}";

    private static void PrintProcessOutput(string label, RunResult run)
    {
        foreach (var line in Tail(run.StdOut, 8)) Console.WriteLine($"    {label} out: {line}");
        foreach (var line in Tail(run.StdErr, 8)) Console.WriteLine($"    {label} err: {line}");
    }

    internal sealed record Description(string Kind, int Width, int Height, int Channels, int BitDepth, string Hash)
    {
        /// <summary>SHA-256 is 64 hex characters; anything else is a sentinel, not a real digest.</summary>
        public bool HasHash => Hash.Length == 64;

        public override string ToString() =>
            $"{Kind} {Width}x{Height}x{Channels}@{BitDepth} sha256={ShortHash(Hash)}";
    }

    /// <summary>Sentinels are never valid digests, so <see cref="Description.HasHash"/> rejects them.</summary>
    internal const string UnreadableHash = "<unreadable>";

    internal const string EmptyHash = "<empty>";

    /// <summary>Shortens a real digest for display, but prints sentinels (and any non-digest) whole.</summary>
    private static string ShortHash(string hash) => hash.Length > 16 ? hash[..16] : hash;

    /// <summary>
    /// Compares two descriptions. Empty outputs are never equal by default: a zero-frame container
    /// carries no image sample, so counting it as a match would turn "nothing was compared" into a
    /// passing comparison. Only a case that explicitly declares <see cref="ParityCase.AllowEmptyOutput"/>
    /// may treat two empties (with identical kind and geometry) as equal.
    /// </summary>
    private static bool DescriptionsMatch(Description a, Description b, bool allowEmpty)
    {
        bool sameGeometry = a.Kind == b.Kind && a.Width == b.Width && a.Height == b.Height &&
                            a.Channels == b.Channels && a.BitDepth == b.BitDepth;
        if (!sameGeometry) return false;
        if (a.HasHash && b.HasHash) return a.Hash == b.Hash;
        return allowEmpty && a.Hash == EmptyHash && b.Hash == EmptyHash;
    }

    /// <summary>
    /// Order-independent comparison of frame descriptions. Unlike a sorted hash list this also
    /// compares kind, geometry and bit depth, and it refuses to treat two unreadable files (or two
    /// empty ones, unless the case declares them) as a match just because both carry a sentinel.
    /// </summary>
    private static bool MultisetEqual(List<Description> left, List<Description> right, bool allowEmpty = false)
    {
        if (left.Count != right.Count) return false;

        var counts = new Dictionary<Description, int>();
        foreach (var d in left) counts[d] = counts.GetValueOrDefault(d) + 1;

        foreach (var d in right)
        {
            bool comparable = d.HasHash || (allowEmpty && d.Hash == EmptyHash);
            if (!comparable || !counts.TryGetValue(d, out int remaining) || remaining == 0) return false;
            if (remaining == 1) counts.Remove(d);
            else counts[d] = remaining - 1;
        }

        return counts.Count == 0;
    }

    private static void ReportMultisetDifference(List<Description> oracle, List<Description> managed)
    {
        var left = oracle.Where(d => d.HasHash).GroupBy(d => d.Hash).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var right = managed.Where(d => d.HasHash).GroupBy(d => d.Hash).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int shown = 0;
        foreach (string hash in left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal))
        {
            left.TryGetValue(hash, out int a);
            right.TryGetValue(hash, out int b);
            if (a == b) continue;
            if (shown++ < 4) Console.WriteLine($"      frame {ShortHash(hash)}: oracle {a}, managed {b}");
        }

        int unreadable = oracle.Count(d => !d.HasHash) + managed.Count(d => !d.HasHash);
        if (unreadable > 0) Console.WriteLine($"      {unreadable} unreadable or empty output(s)");
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
            return new Description(kind + " (unreadable: " + ex.GetType().Name + ")", 0, 0, 0, 0, UnreadableHash);
        }

        if (frame is null) return new Description(kind + " (empty)", 0, 0, 0, 0, EmptyHash);

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

    /// <summary>
    /// Quantifies a pixel difference: how many pixels differ, by how much, in which channels, and
    /// whether the error concentrates on edges or is spread uniformly. Used by --measure so a
    /// known gap carries numbers instead of an adjective.
    /// </summary>
    private static void ReportDetailedDiff(string oraclePath, string managedPath, int width, int height, int channels, int bitDepth)
    {
        var left = LoadSamples(oraclePath);
        var right = LoadSamples(managedPath);
        if (left is null || right is null || left.Length != right.Length)
        {
            Console.WriteLine($"        (could not align samples: {left?.Length} vs {right?.Length})");
            return;
        }

        int pixels = width * height;
        if (left.Length != pixels * channels || channels is < 1 or > 3)
        {
            Console.WriteLine($"        (unexpected sample count {left.Length} for {width}x{height}x{channels})");
            return;
        }

        var perChannelDiff = new int[channels];
        var perChannelSum = new long[channels];
        var perChannelMax = new int[channels];
        var histogram = new int[10]; // index = |delta|, last bucket is 8+
        int diffPixels = 0, maxDelta = 0;
        long sumDelta = 0;

        var luma = new int[pixels];
        for (int p = 0; p < pixels; p++)
        {
            int idx = p * channels;
            int sum = 0;
            bool differs = false;
            for (int c = 0; c < channels; c++)
            {
                int d = Math.Abs(left[idx + c] - right[idx + c]);
                if (d != 0)
                {
                    differs = true;
                    perChannelDiff[c]++;
                    perChannelSum[c] += d;
                    if (d > perChannelMax[c]) perChannelMax[c] = d;
                    if (d > maxDelta) maxDelta = d;
                    sumDelta += d;
                    histogram[Math.Min(d, 9)]++;
                }
                sum += left[idx + c];
            }
            if (differs) diffPixels++;
            luma[p] = channels == 1 ? left[idx] : sum / channels;
        }

        // Local gradient of the oracle image: separates "error at detail" from "error everywhere".
        int threshold = bitDepth <= 8 ? 24 : 6000;
        int edgePixels = 0, edgeDiffPixels = 0, borderDiffPixels = 0;
        long edgeDiffSum = 0, flatDiffSum = 0;
        int flatDiffPixels = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(width - 1, x + 1);
                int y0 = Math.Max(0, y - 1), y1 = Math.Min(height - 1, y + 1);
                int g = Math.Abs(luma[y * width + x1] - luma[y * width + x0]) +
                        Math.Abs(luma[y1 * width + x] - luma[y0 * width + x]);
                bool isEdge = g >= threshold;
                if (isEdge) edgePixels++;

                int idx = p * channels;
                int pixelDelta = 0;
                for (int c = 0; c < channels; c++) pixelDelta += Math.Abs(left[idx + c] - right[idx + c]);
                if (pixelDelta == 0) continue;

                if (isEdge) { edgeDiffPixels++; edgeDiffSum += pixelDelta; }
                else { flatDiffPixels++; flatDiffSum += pixelDelta; }
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) borderDiffPixels++;
            }
        }

        int totalSamples = left.Length;
        int diffSamples = perChannelDiff.Sum();
        double pct(double n, double d) => d == 0 ? 0 : 100 * n / d;

        Console.WriteLine($"        pixels {width}x{height}x{channels}@{bitDepth}");
        Console.WriteLine($"        differing pixels : {diffPixels} of {pixels} ({pct(diffPixels, pixels):F1}%)");
        Console.WriteLine($"        differing samples: {diffSamples} of {totalSamples} ({pct(diffSamples, totalSamples):F1}%)  " +
                          string.Join(" ", Enumerable.Range(0, channels).Select(c => $"{(channels == 1 ? "Y" : "RGB"[c])}={perChannelDiff[c]}")));
        Console.WriteLine($"        |delta| max {maxDelta}, mean {((double)sumDelta / Math.Max(1, diffSamples)):F2} over differing samples, " +
                          $"{((double)sumDelta / Math.Max(1, totalSamples)):F3} over all samples");
        Console.WriteLine("        |delta| histogram: " + string.Join(" ", Enumerable.Range(1, 8).Select(d => $"{d}:{histogram[d]}")) +
                          $" 8+:{histogram[9]}");
        Console.WriteLine("        mean |delta| per channel: " +
                          string.Join(" ", Enumerable.Range(0, channels).Select(c =>
                              $"{(channels == 1 ? "Y" : "RGB"[c])}={((double)perChannelSum[c] / Math.Max(1, perChannelDiff[c])):F2} (max {perChannelMax[c]})")));

        if (diffPixels > 0)
        {
            Console.WriteLine($"        edges: {edgeDiffPixels} of {diffPixels} differing pixels sit on a local edge " +
                              $"(gradient>={threshold}; {pct(edgePixels, pixels):F1}% of all pixels are edge pixels)");
            Console.WriteLine($"        mean |delta| on edges {((double)edgeDiffSum / Math.Max(1, edgeDiffPixels)):F2}, " +
                              $"in flat areas {((double)flatDiffSum / Math.Max(1, flatDiffPixels)):F2} ({flatDiffPixels} differing pixels)");
            Console.WriteLine($"        image border: {borderDiffPixels} of {diffPixels} differing pixels");
        }
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

    /// <summary>
    /// Hashes every frame of a multi-frame container (SER/AVI) as one payload. A container that
    /// decoded zero frames reports <see cref="EmptyHash"/> instead of the digest of the empty byte
    /// sequence: two empty containers share that digest and the same header geometry, which would
    /// otherwise be reported as a matched comparison although no image sample was examined.
    /// </summary>
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
            if (frames == 0)
                return new Description("container x0", width, height, channels, bitDepth, EmptyHash);
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

    private static string FindRepoRoot() => FindRepoRoot(AppContext.BaseDirectory);

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to the directory holding the solution file.
    /// The marker is <c>OPIPP.slnx</c>: the project was renamed from PIPP, and there is deliberately
    /// no PIPP fallback, so a stale file in an ancestor cannot silently supply the wrong root.
    /// </summary>
    internal static string FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OPIPP.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
