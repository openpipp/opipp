using System.Globalization;
using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Cli.Tests;

/// <summary>
/// End-to-end checks that the CLI turns engine failures into the controlled error path instead of
/// letting them escape <see cref="Program.Main(string[])"/> as an unhandled exception.
/// </summary>
/// <remarks>
/// These tests drive the real entry point (not <see cref="Opipp.Core.Pipeline.Processor"/>) so the
/// catch filters in <c>Program.cs</c> are what is under test. Console redirection is process-wide,
/// so this class joins the non-parallel collection used by the GUI transcript snapshots.
/// </remarks>
[Collection("gui-stdout")]
public class CliErrorHandlingTests
{
    /// <summary>
    /// A calibration master whose geometry does not match the capture is a configuration error, not
    /// a per-frame rejection. Both entry paths must report it as <c>Error: &lt;message&gt;</c> and
    /// exit 1 — the runtime-failure code shared with the other processing errors (2 is reserved for
    /// command-line/settings errors detected before any frame is read).
    /// </summary>
    [Theory]
    [InlineData(false)] // console mode
    [InlineData(true)]  // -gui / engine protocol mode
    public void CalibrationMasterWithWrongGeometry_IsReportedAsAControlledError(bool gui)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string input = WriteCapture(dir);
            string master = WriteWrongGeometryMaster(dir);
            string outputDirectory = Path.Combine(dir, "out");

            var arguments = new List<string>();
            if (gui) arguments.Add("-gui");
            arguments.Add("-tif");
            arguments.Add("-dark");
            arguments.Add("-dark_pgm=" + master);
            arguments.Add("-od=" + outputDirectory);
            arguments.Add(input);

            (int exitCode, _, string stderr) = Run(arguments.ToArray());

            Assert.Equal(1, exitCode);

            // Same presentation as the other handled engine errors: the fault, then the input and
            // the fact that nothing on disk can be treated as a complete result. A stack trace or
            // type name means the exception escaped the catch filter.
            string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("Error: ", lines[0], StringComparison.Ordinal);
            Assert.Contains("-dark_tmp master is 8x8x1@8 but the capture is 16x16x1@8", lines[0], StringComparison.Ordinal);
            Assert.Contains("must match the capture geometry", lines[0], StringComparison.Ordinal);
            Assert.Contains("Incomplete run", lines[1], StringComparison.Ordinal);
            Assert.Contains(input, lines[1], StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(Opipp.Core.Processing.CalibrationConfigurationException), stderr, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Writes a three-frame 16x16 8-bit SER whose frames contain a small bright disc.</summary>
    private static string WriteCapture(string dir)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, 16, 16, 1, 8);
        for (int f = 0; f < 3; f++)
        {
            var frame = new Frame(16, 16, 1, 8);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    double dx = x - (8 + f % 3), dy = y - 8;
                    bool inDisc = dx * dx + dy * dy <= 16;
                    frame.Samples[y * 16 + x] = (ushort)(inDisc ? 255 : 40);
                }
            }
            writer.Write(frame);
        }
        return path;
    }

    /// <summary>
    /// Writes a valid, readable P5 PGM master that is 8x8 instead of the capture's 16x16. Written by
    /// hand so the test does not depend on the production PGM writer.
    /// </summary>
    private static string WriteWrongGeometryMaster(string dir)
    {
        string path = Path.Combine(dir, "dark.pgm");
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("P5\n8 8\n255\n"));
        bytes.AddRange(new byte[8 * 8]);
        File.WriteAllBytes(path, bytes.ToArray());
        return path;
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

    private static readonly object ConsoleGate = new();
}
