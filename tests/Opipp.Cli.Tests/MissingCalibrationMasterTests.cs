using System.Globalization;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Cli.Tests;

/// <summary>
/// A calibration master that was named on the command line must exist. Skipping the calibration
/// silently would deliver uncalibrated science frames, so a missing named master is a hard error
/// before any output is written. A bare calibration flag stays valid: the GUI emits
/// <c>-flat_dark</c> without a path when it has already applied that correction itself.
/// </summary>
/// <remarks>
/// Console redirection is process-wide, so this class joins the non-parallel collection used by the
/// other CLI transcript tests.
/// </remarks>
[Collection("gui-stdout")]
public class MissingCalibrationMasterTests
{
    private static readonly object ConsoleGate = new();

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-missingmaster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A one-frame 8x8 mono SER: enough for a real run, cheap to build.</summary>
    private static string WriteScience(string dir)
    {
        string path = Path.Combine(dir, "science.ser");
        using var writer = new SerWriter(path, 8, 8, 1, 8);
        var frame = new Frame(8, 8, 1, 8);
        Array.Fill(frame.Samples, (ushort)125);
        writer.Write(frame);
        return path;
    }

    /// <summary>A valid flat master for the cases where the flat loads and the flat-dark is missing.</summary>
    private static string WriteFlatMaster(string dir)
    {
        string path = Path.Combine(dir, "pipp_master_flat.tmp");
        var frame = new Frame(8, 8, 1, 8);
        Array.Fill(frame.Samples, (ushort)200);
        MasterTmpFile.Write(frame, path);
        return path;
    }

    /// <summary>No science output means no file of any kind under the output directory.</summary>
    private static void AssertNoOutput(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory)) return;
        Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories));
    }

    private static void AssertTiffWritten(string outputDirectory)
    {
        Assert.Single(Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories));
    }

    [Fact]
    public void MissingDarkTmp_StopsBeforeAnyOutput()
    {
        string dir = TempDir();
        try
        {
            string science = WriteScience(dir);
            string outputDirectory = Path.Combine(dir, "out");
            string missing = Path.Combine(dir, "no_such_dark.tmp");

            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-dark",
                "-dark_tmp=" + missing,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("Error: ", stderr, StringComparison.Ordinal);
            Assert.Contains(missing, stderr, StringComparison.Ordinal);
            Assert.Contains("-dark_tmp", stderr, StringComparison.Ordinal);
            Assert.Contains("does not exist", stderr, StringComparison.Ordinal);
            AssertNoOutput(outputDirectory);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void MissingFlatTmp_StopsBeforeAnyOutput()
    {
        string dir = TempDir();
        try
        {
            string science = WriteScience(dir);
            string outputDirectory = Path.Combine(dir, "out");
            string missing = Path.Combine(dir, "no_such_flat.tmp");

            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-flat",
                "-flat_tmp=" + missing,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("Error: ", stderr, StringComparison.Ordinal);
            Assert.Contains(missing, stderr, StringComparison.Ordinal);
            Assert.Contains("-flat_tmp", stderr, StringComparison.Ordinal);
            Assert.Contains("does not exist", stderr, StringComparison.Ordinal);
            AssertNoOutput(outputDirectory);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void MissingFlatDarkTmp_StopsBeforeAnyOutput()
    {
        string dir = TempDir();
        try
        {
            string science = WriteScience(dir);
            string flatMaster = WriteFlatMaster(dir);
            string outputDirectory = Path.Combine(dir, "out");
            string missing = Path.Combine(dir, "no_such_flat_dark.tmp");

            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-flat",
                "-flat_tmp=" + flatMaster,
                "-flat_dark",
                "-flat_dark_tmp=" + missing,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("Error: ", stderr, StringComparison.Ordinal);
            Assert.Contains(missing, stderr, StringComparison.Ordinal);
            Assert.Contains("-flat_dark_tmp", stderr, StringComparison.Ordinal);
            Assert.Contains("does not exist", stderr, StringComparison.Ordinal);
            AssertNoOutput(outputDirectory);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void MissingDarkPgm_OnThePgmOnlyHandoff_StopsBeforeAnyOutput()
    {
        string dir = TempDir();
        try
        {
            string science = WriteScience(dir);
            string outputDirectory = Path.Combine(dir, "out");
            string missing = Path.Combine(dir, "no_such_dark.pgm");

            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-dark",
                "-dark_pgm=" + missing,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("Error: ", stderr, StringComparison.Ordinal);
            Assert.Contains(missing, stderr, StringComparison.Ordinal);
            Assert.Contains("-dark_pgm", stderr, StringComparison.Ordinal);
            Assert.Contains("does not exist", stderr, StringComparison.Ordinal);
            AssertNoOutput(outputDirectory);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void NoMasterOptions_RunNormally()
    {
        string dir = TempDir();
        try
        {
            string science = WriteScience(dir);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, _, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertTiffWritten(outputDirectory);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Theory]
    [InlineData("-dark")]
    [InlineData("-flat")]
    [InlineData("-flat_dark")]
    public void BareCalibrationFlag_RunsWithoutAMaster(string flag)
    {
        string dir = TempDir();
        try
        {
            string science = WriteScience(dir);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, _, string stderr) = Run([
                "-tif",
                flag,
                "-od=" + outputDirectory,
                science,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertTiffWritten(outputDirectory);
        }
        finally
        {
            Delete(dir);
        }
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
