using System.Globalization;
using System.Text;

namespace Pipp.Cli.Tests;

/// <summary>
/// End-to-end checks that <c>-gui</c> stdout is byte-identical to the original engine's.
/// </summary>
/// <remarks>
/// The golden files were captured from <c>bin/pipp_core.exe</c> with the same switches and a
/// stdin stream terminated by the literal line <c>DONE</c>. Input paths and the output directory
/// are tokenised as <c>{REPO}</c> / <c>{OUT}</c> so the snapshots do not embed this machine's
/// layout; everything else, including the CRLF endings and the lone carriage return before
/// "  Input file:", is compared verbatim.
/// </remarks>
[CollectionDefinition("gui-stdout", DisableParallelization = true)]
public sealed class GuiStdoutCollection;

[Collection("gui-stdout")]
public class GuiTranscriptTests
{
    private static readonly object ConsoleGate = new();
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string GoldenRoot = Path.Combine(AppContext.BaseDirectory, "Golden");

    public static TheoryData<string, string[], string[]> Scenarios => new()
    {
        { "01-base", ["-tif"], ["parity/capture.ser"] },
        { "02-q-saturn", ["-tif", "-q=2"], ["samples/saturn30.ser"] },
        { "03-ctr", ["-tif", "-ctr"], ["samples/saturn30.ser"] },
        { "04-anim", ["-avi", "-anim_fwd", "-anim_rev"], ["parity/capture.ser"] },
        { "05-multi", ["-tif"], ["parity/capture.ser", "parity/colour.ser"] },
        { "06-join", ["-tif", "-join"], ["parity/capture.ser", "parity/colour.ser"] },
        { "07-q-small", ["-tif", "-q=2"], ["parity/capture.ser"] },
        { "08-qpcnt", ["-tif", "-qpcnt=50"], ["parity/capture.ser"] },
        { "09-gif", ["-gif", "-anim_fwd", "-anim_rev"], ["parity/capture.ser"] },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void GuiStdoutMatchesOracle(string scenario, string[] switches, string[] inputs)
    {
        string goldenPath = Path.Combine(GoldenRoot, scenario + ".txt");
        string[] inputPaths = inputs.Select(relative => Path.Combine(RepoRoot, relative.Replace('/', '\\'))).ToArray();

        // saturn30.ser is third-party evaluation data kept out of version control; the smaller
        // parity fixtures still exercise the other scenarios when it is absent.
        if (inputPaths.Any(path => !File.Exists(path))) return;

        string outputDirectory = Path.Combine(
            Path.GetTempPath(), "pipp-gui-tests", scenario, Guid.NewGuid().ToString("N"));
        try
        {
            var arguments = new List<string> { "-gui" };
            arguments.AddRange(switches);
            arguments.Add("-od=" + outputDirectory);
            arguments.AddRange(inputPaths);

            (int exitCode, string stdout, _) = Run(arguments.ToArray());
            Assert.Equal(0, exitCode);

            string expected = File.ReadAllText(goldenPath, Encoding.UTF8);
            string actual = stdout
                .Replace(outputDirectory.Replace('\\', '/'), "{OUT}", StringComparison.Ordinal)
                .Replace(RepoRoot, "{REPO}", StringComparison.Ordinal);
            Assert.Equal(expected, actual);
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string[] arguments)
    {
        lock (ConsoleGate)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            var stdout = new StringWriter(CultureInfo.InvariantCulture);
            var stderr = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Program.Main(arguments);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        // Accept any solution name at the root: the project was renamed from PIPP to OPIPP, and a
        // hard-coded file name turns a harmless rename into a wall of test failures.
        static bool IsRepoRoot(string path) =>
            Directory.Exists(Path.Combine(path, "src", "Pipp.Core")) &&
            Directory.EnumerateFiles(path, "*.slnx").Any();

        while (directory is not null && !IsRepoRoot(directory.FullName))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("repository root (src/Pipp.Core next to a .slnx) not found above the test output");
    }
}
