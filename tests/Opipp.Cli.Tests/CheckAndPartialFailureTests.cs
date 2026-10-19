using System.Globalization;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Cli.Tests;

/// <summary>
/// End-to-end checks for the two CLI result contracts: a run that could not read one of its inputs
/// is never reported as success (even when the frames accepted before the failure were written),
/// and <c>-check</c> validates the command line and inputs without creating any output.
/// </summary>
/// <remarks>
/// These tests drive <see cref="Program.Main(string[])"/> so the exit-code mapping and the report
/// wording are what is asserted. Console redirection is process-wide, so this class joins the
/// non-parallel collection used by the GUI transcript snapshots.
/// </remarks>
[Collection("gui-stdout")]
public class CheckAndPartialFailureTests
{
    private static readonly object ConsoleGate = new();

    // ---------------------------------------------------------------------------------------------
    // A. Partial failures must not look successful
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void JoinedRun_WithCorruptSecondInput_ExitsNonZeroAndNamesIt()
    {
        string dir = TempDir();
        try
        {
            string good = WritePng(Path.Combine(dir, "first.png"));
            string corrupt = WriteCorruptPng(Path.Combine(dir, "second.png"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-png",
                "-join",
                "-od=" + outputDirectory,
                good,
                corrupt,
            ]);

            // The frames accepted before the failure do not turn the run into a success.
            Assert.Equal(1, exitCode);

            // The partial result is explicit in the console report...
            Assert.Contains("Result:                          PARTIAL FAILURE", stdout, StringComparison.Ordinal);
            Assert.Contains("Partial output:", stdout, StringComparison.Ordinal);
            Assert.Contains("incomplete", stdout, StringComparison.Ordinal);

            // ...and on stderr, where the failed file is named. The joined unit is recorded under the
            // first input's name by the pipeline, so this also proves the CLI resolves the real one.
            Assert.Contains("Partial result", stderr, StringComparison.Ordinal);
            Assert.Contains(corrupt, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(good, stderr, StringComparison.Ordinal);

            // The partial output is preserved (not silently deleted), but is labelled incomplete.
            string[] written = Directory.GetFiles(outputDirectory, "*.png", SearchOption.AllDirectories);
            Assert.Single(written);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void GuiJoinedRun_WithCorruptSecondInput_ExitsNonZeroAndNamesIt()
    {
        string dir = TempDir();
        try
        {
            string good = WritePng(Path.Combine(dir, "first.png"));
            string corrupt = WriteCorruptPng(Path.Combine(dir, "second.png"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-gui",
                "-png",
                "-join",
                "-od=" + outputDirectory,
                good,
                corrupt,
            ]);

            // The form treats a non-zero engine exit code as failure. The per-input diagnostic is
            // in the transcript, and stderr carries the run-level partial-result explanation.
            Assert.Equal(1, exitCode);
            Assert.Contains("Partial result", stderr, StringComparison.Ordinal);
            Assert.Contains(corrupt, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(good, stderr, StringComparison.Ordinal);

            // The protocol transcript still reached the form, and the per-input failure is in the
            // transcript itself (the original engine printed it on stdout too).
            Assert.Contains("@PFILE", stdout, StringComparison.Ordinal);
            Assert.Contains("Error: could not open file", stdout, StringComparison.Ordinal);
            Assert.Contains(corrupt, stdout, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(outputDirectory, "*.png", SearchOption.AllDirectories));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void BatchRun_WithOneMissingInput_ExitsNonZeroAndReportsPartial()
    {
        string dir = TempDir();
        try
        {
            string good = WritePng(Path.Combine(dir, "good.png"));
            string missing = Path.Combine(dir, "missing.png");
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-png",
                "-od=" + outputDirectory,
                good,
                missing,
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("PARTIAL FAILURE", stdout, StringComparison.Ordinal);
            Assert.Contains("Partial result", stderr, StringComparison.Ordinal);
            Assert.Contains(missing, stderr, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(outputDirectory, "*.png", SearchOption.AllDirectories));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void JoinedRun_WithAllValidInputs_StillExitsZero()
    {
        string dir = TempDir();
        try
        {
            string first = WritePng(Path.Combine(dir, "first.png"));
            string second = WritePng(Path.Combine(dir, "second.png"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-png",
                "-join",
                "-od=" + outputDirectory,
                first,
                second,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Result:                          SUCCESS", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("PARTIAL", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(2, Directory.GetFiles(outputDirectory, "*.png", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // B. -check validates and writes nothing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Check_WithValidArguments_ReportsOkAndCreatesNothing()
    {
        string dir = TempDir();
        try
        {
            string input = WritePng(Path.Combine(dir, "capture.png"));
            string outputDirectory = Path.Combine(dir, "out");

            // The same switches without -check would create this directory and write a frame; the
            // assertion below therefore proves the check path stopped before the pipeline ran.
            (int exitCode, string stdout, string stderr) = Run([
                "-check",
                "-png",
                "-od=" + outputDirectory,
                input,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);

            Assert.Contains("Command line check: OK", stdout, StringComparison.Ordinal);
            Assert.Contains("  Input files: 1", stdout, StringComparison.Ordinal);
            Assert.Contains("  Input geometry: 16x16x1@8", stdout, StringComparison.Ordinal);
            Assert.Contains("  Output format: .png", stdout, StringComparison.Ordinal);
            Assert.Contains($"  Output directory: {outputDirectory} (not created)", stdout, StringComparison.Ordinal);
            Assert.Contains("  Calibration masters: none", stdout, StringComparison.Ordinal);

            Assert.False(Directory.Exists(outputDirectory), "-check must not create the output directory");
            Assert.Equal([input], Directory.GetFiles(dir));

            // Positive control: the very same switches without -check do create the directory and
            // write a frame, so the empty result above is the check path stopping, not a no-op run.
            (int runExitCode, _, _) = Run([
                "-png",
                "-od=" + outputDirectory,
                input,
            ]);
            Assert.Equal(0, runExitCode);
            Assert.True(Directory.Exists(outputDirectory));
            Assert.Single(Directory.GetFiles(outputDirectory, "*.png", SearchOption.AllDirectories));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Check_WithMissingCalibrationMaster_FailsWithTheReason()
    {
        string dir = TempDir();
        try
        {
            string input = WritePng(Path.Combine(dir, "capture.png"));
            string missingMaster = Path.Combine(dir, "no-such-dark.pgm");
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, _) = Run([
                "-check",
                "-png",
                "-dark",
                "-dark_pgm=" + missingMaster,
                "-od=" + outputDirectory,
                input,
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains($"'{missingMaster}'", stdout, StringComparison.Ordinal);
            Assert.Contains("does not exist", stdout, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Check_WithCropLargerThanTheFrame_FailsWithTheReason()
    {
        string dir = TempDir();
        try
        {
            string input = WritePng(Path.Combine(dir, "capture.png"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, _) = Run([
                "-check",
                "-png",
                "-cx=100",
                "-cy=100",
                "-od=" + outputDirectory,
                input,
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains("crop rectangle (0,0) 100x100", stdout, StringComparison.Ordinal);
            Assert.Contains("16x16", stdout, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Check_WithCorruptInput_FailsWithTheReason()
    {
        string dir = TempDir();
        try
        {
            string corrupt = WriteCorruptPng(Path.Combine(dir, "capture.png"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, _) = Run([
                "-check",
                "-png",
                "-od=" + outputDirectory,
                corrupt,
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains($"input file cannot be read: '{corrupt}'", stdout, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Check_WithUnknownSwitch_ReportsTheParseError()
    {
        string dir = TempDir();
        try
        {
            string input = WritePng(Path.Combine(dir, "capture.png"));

            (int exitCode, string stdout, _) = Run(["-check", "-bogus=1", input]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains("Unknown switch", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    /// <summary>Writes a one-frame 16x16 8-bit greyscale PNG with a bright centre.</summary>
    private static string WritePng(string path)
    {
        var frame = new Frame(16, 16, 1, 8);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                double dx = x - 8, dy = y - 8;
                frame.Samples[y * 16 + x] = (ushort)(dx * dx + dy * dy <= 16 ? 255 : 40);
            }
        }

        using (var writer = new PngWriter(path, 16, 16, 1, 8))
        {
            writer.Write(frame);
        }

        return path;
    }

    /// <summary>
    /// Writes a file with a valid PNG signature and a truncated IHDR: readers accept the extension
    /// and the signature, then fail while decoding.
    /// </summary>
    private static string WriteCorruptPng(string path)
    {
        byte[] bytes =
        [
            137, 80, 78, 71, 13, 10, 26, 10, // signature
            0, 0, 0, 13,                     // IHDR length
            73, 72, 68, 82,                  // "IHDR"
            0, 0, 0, 16,                     // width, then the file stops
        ];
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-cli-check", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
}
