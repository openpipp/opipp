using System.Diagnostics;
using Opipp.Parity;

namespace Opipp.Parity.Tests;

/// <summary>
/// Failure-path coverage for the differential harness, driven by a fake engine rather than the
/// original binary: exit codes, timeouts, malformed artifacts and order-independent comparison.
/// </summary>
public sealed class ParityHarnessTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void IdenticalEngines_Match()
    {
        var (outcome, output) = RunFake("match", "match");

        Assert.Equal(Program.CaseOutcome.Match, outcome);
        Assert.Contains("layout: MATCH", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("oracle")]
    [InlineData("managed")]
    public void NonZeroExit_FailsEvenWhenFilesMatch(string failingSide)
    {
        // Both sides write identical frames; only the exit code differs. Before the fix this case
        // was counted as a match because the verdict only looked at files.
        var (outcome, output) = RunFake(
            failingSide == "oracle" ? "exit-nonzero" : "match",
            failingSide == "managed" ? "exit-nonzero" : "match");

        Assert.Equal(Program.CaseOutcome.Fail, outcome);
        Assert.Contains($"{failingSide} exited with code 3", output, StringComparison.Ordinal);
        Assert.Contains("DIVERGENCE", output, StringComparison.Ordinal);
    }

    [Fact]
    public void HungEngine_IsKilledAtTheTimeoutAndFails()
    {
        var stopwatch = Stopwatch.StartNew();
        var (outcome, output) = RunFake("match", "hang", timeout: ShortTimeout);
        stopwatch.Stop();

        Assert.Equal(Program.CaseOutcome.Fail, outcome);
        Assert.Contains("managed timed out after 2s and was killed (exit code -1)", output, StringComparison.Ordinal);
        // The old blocking ReadToEnd ran before WaitForExit, so this test could never finish.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"verdict took {stopwatch.Elapsed}");
    }

    [Fact]
    public void ReorderedFrames_MatchOnlyInMultisetMode()
    {
        // The managed side writes the same three frames under rotated frame numbers.
        var (exactOutcome, exactOutput) = RunFake("match", "reorder", Program.CompareMode.Exact);
        Assert.Equal(Program.CaseOutcome.Fail, exactOutcome);
        Assert.Contains("DIVERGENCE", exactOutput, StringComparison.Ordinal);

        var (setOutcome, setOutput) = RunFake("match", "reorder", Program.CompareMode.SetEquality);
        Assert.Equal(Program.CaseOutcome.Match, setOutcome);
        Assert.Contains("frame multiset: MATCH (3 frames)", setOutput, StringComparison.Ordinal);
        // The positional pass must not even run in this mode.
        Assert.DoesNotContain("      DIVERGENCE", setOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedOutput_FailsWithoutThrowing()
    {
        var (outcome, output) = RunFake("match", "malformed");

        Assert.Equal(Program.CaseOutcome.Fail, outcome);
        Assert.Contains("unreadable", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("content cannot be compared", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PipeBlockedEngine_StillReachesTheTimeout()
    {
        // The fake engine fills stderr, then stdout, then hangs. Reading either pipe to completion
        // before arming the timeout deadlocks here forever.
        string work = NewWorkDirectory();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var run = Program.Run(
                FakeEnginePath(),
                ["-fake=pipe-block", "-od=" + Path.Combine(work, "out")],
                work,
                ShortTimeout);
            stopwatch.Stop();

            Assert.True(run.TimedOut);
            Assert.False(run.Succeeded);
            Assert.Equal(-1, run.ExitCode);
            Assert.Contains("timeout after 2s", run.StdErr, StringComparison.Ordinal);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(45), $"run took {stopwatch.Elapsed}");
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public void UnreadableSentinel_IsNotTruncated()
    {
        // Reproduces the old hash[..16] crash: a sentinel shorter than 16 characters threw while
        // formatting the comparison line.
        var unreadable = new Program.Description(".tif (unreadable: InvalidDataException)", 0, 0, 0, 0, Program.UnreadableHash);
        var empty = new Program.Description(".tif (empty)", 0, 0, 0, 0, Program.EmptyHash);

        Assert.False(unreadable.HasHash);
        Assert.False(empty.HasHash);
        Assert.Contains(Program.UnreadableHash, unreadable.ToString(), StringComparison.Ordinal);
        Assert.Contains(Program.EmptyHash, empty.ToString(), StringComparison.Ordinal);
    }

    private static (Program.CaseOutcome Outcome, string Output) RunFake(
        string oracleScenario,
        string managedScenario,
        Program.CompareMode mode = Program.CompareMode.Exact,
        TimeSpan? timeout = null)
    {
        string work = NewWorkDirectory();
        try
        {
            string fake = FakeEnginePath();
            string input = Path.Combine(work, "input.ser");
            File.WriteAllBytes(input, []);
            var parityCase = new Program.ParityCase("fake", ["-tif"], Mode: mode);

            var original = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                var outcome = Program.RunCase(
                    parityCase,
                    fake,
                    fake,
                    work,
                    input,
                    measure: false,
                    timeout: timeout ?? TimeSpan.FromSeconds(10),
                    oracleSwitches: ["-tif", "-fake=" + oracleScenario],
                    managedSwitches: ["-tif", "-fake=" + managedScenario]);
                return (outcome, writer.ToString());
            }
            finally
            {
                Console.SetOut(original);
            }
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static string FakeEnginePath()
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string configuration = testBin.Parent?.Name ?? "Debug";
        string path = Path.Combine(
            FindRepoRoot(),
            "tools", "Opipp.Parity.Tests", "FakeEngine", "bin", configuration, "net10.0",
            "Opipp.Parity.FakeEngine.exe");

        Assert.True(File.Exists(path), $"fake engine not built at {path}");
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OPIPP.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root (OPIPP.slnx) not found");
    }

    private static string NewWorkDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "opipp-parity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // a killed engine may briefly hold a handle; leaving temp files behind is harmless
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
