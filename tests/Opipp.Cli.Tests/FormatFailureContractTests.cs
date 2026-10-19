using System.Globalization;
using System.Text;
using Opipp.Core.Cli;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Processing;

namespace Opipp.Cli.Tests;

/// <summary>
/// End-to-end checks for the CLI's format-failure contract: malformed and truncated FITS, a Bayer
/// phase conflict in a joined container, and the quick <c>-check</c> validation depth. Every case
/// must end with a documented exit code and a diagnostic that names the input, never with an
/// unhandled exception or a success report.
/// </summary>
/// <remarks>
/// Console redirection is process-wide, so this class joins the non-parallel collection used by the
/// GUI transcript snapshots. The tests drive <see cref="Program.Main(string[])"/>, so the catch
/// filters and the help text are what is under test.
/// </remarks>
[Collection("gui-stdout")]
public class FormatFailureContractTests
{
    private static readonly object ConsoleGate = new();

    // ---------------------------------------------------------------------------------------------
    // Exception -> exit-code classification
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FormatBayerAndCalibrationExceptions_AreControlledFailures()
    {
        Assert.True(FailureClassifier.IsControlled(new FitsFormatException("bad header")));
        Assert.True(FailureClassifier.IsControlled(new FitsTruncationException("short payload", 256, 64)));
        Assert.True(FailureClassifier.IsControlled(new BayerPhaseException("mixed phases")));
        Assert.True(FailureClassifier.IsControlled(new CalibrationConfigurationException("wrong master")));
        Assert.True(FailureClassifier.IsControlled(new InvalidDataException("bad container")));
    }

    [Fact]
    public void LastResort_NeverMasksCancellation()
    {
        // Cancellation has its own outcome (130). The catch-all must not claim it, or a cancelled
        // run would be reported as an incomplete/failed run instead.
        Assert.False(FailureClassifier.IsLastResort(new OperationCanceledException()));
        Assert.False(FailureClassifier.IsLastResort(new TaskCanceledException()));

        // Anything genuinely unexpected is still handled by the last-resort path.
        Assert.True(FailureClassifier.IsLastResort(new InvalidOperationException("unexpected")));
    }

    // ---------------------------------------------------------------------------------------------
    // Corrupt primary FITS
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Check_WithCorruptFits_ReportsFailedCheckInsteadOfThrowing()
    {
        string dir = TempDir();
        try
        {
            string corrupt = WriteCorruptFits(Path.Combine(dir, "corrupt.fits"));

            (int exitCode, string stdout, string stderr) = Run(["-check", "-fits", corrupt]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains($"'{corrupt}'", stdout, StringComparison.Ordinal);
            Assert.Contains("structurally invalid", stdout, StringComparison.Ordinal);
            Assert.Contains("HDU 1", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithCorruptFitsAfterAValidInput_IsReportedAsIncomplete()
    {
        string dir = TempDir();
        try
        {
            string good = WritePng(Path.Combine(dir, "first.png"));
            string corrupt = WriteCorruptFits(Path.Combine(dir, "corrupt.fits"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                good,
                corrupt,
            ]);

            // The frames accepted before the failure do not turn the run into a success, and the
            // failure is reported in terms of the input that could not be decoded.
            Assert.Equal(1, exitCode);
            Assert.Contains(corrupt, stderr, StringComparison.Ordinal);
            Assert.Contains("incomplete", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithOnlyACorruptFits_IsAControlledNoOutputFailure()
    {
        string dir = TempDir();
        try
        {
            string corrupt = WriteCorruptFits(Path.Combine(dir, "corrupt.fits"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                corrupt,
            ]);

            // Nothing could be decoded, so the run keeps the engine's documented "no output" code
            // (-1) instead of crashing; the failed input is named in the report.
            Assert.Equal(-1, exitCode);
            Assert.Contains(corrupt, stdout + stderr, StringComparison.Ordinal);
            Assert.Contains("no output was produced", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Valid first HDU, truncated second HDU
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Check_WalksEveryHduAndFailsOnATruncatedLaterHdu()
    {
        string dir = TempDir();
        try
        {
            string fits = WriteTwoHduFits(Path.Combine(dir, "two-hdus.fits"));
            TruncateSecondHdu(fits);

            (int exitCode, string stdout, _) = Run(["-check", "-fits", fits]);

            // Frame 1 decodes fine; only the whole-file structural walk can see HDU 2 is short.
            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains($"'{fits}'", stdout, StringComparison.Ordinal);
            Assert.Contains("HDU 2", stdout, StringComparison.Ordinal);
            Assert.Contains("truncated", stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Check_WithCompleteMultiHduFits_ReportsTheStructuralDepth()
    {
        string dir = TempDir();
        try
        {
            string fits = WriteTwoHduFits(Path.Combine(dir, "two-hdus.fits"));

            (int exitCode, string stdout, _) = Run(["-check", "-fits", fits]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Command line check: OK", stdout, StringComparison.Ordinal);
            Assert.Contains("FITS structure: 2 image HDU(s) in 2 HDU(s) checked", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithATruncatedLaterHdu_IsNeverReportedAsSuccess()
    {
        string dir = TempDir();
        try
        {
            string fits = WriteTwoHduFits(Path.Combine(dir, "two-hdus.fits"));
            TruncateSecondHdu(fits);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                fits,
            ]);

            // The pipeline records the short payload as a failed input and still writes the frames
            // accepted before it, so the run is a partial result (1), not a success and not a crash.
            Assert.Equal(1, exitCode);
            Assert.Contains("PARTIAL FAILURE", stdout, StringComparison.Ordinal);
            Assert.Contains("incomplete", stdout, StringComparison.Ordinal);
            Assert.Contains(fits, stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithARejectedSecondImageHeader_WritesNoFrameFromThatFile()
    {
        string dir = TempDir();
        try
        {
            // Three valid-geometry image HDUs whose second header has BSCALE=0. The old scan caught
            // the format error, stopped at HDU 2 and kept frame 1 of 3 as a "success".
            string fits = RawFitsFixture.WriteThree(
                Path.Combine(dir, "later-bscale.fits"),
                FitsImageFault.SecondBScaleZero);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                fits,
            ]);

            Assert.NotEqual(0, exitCode);
            Assert.DoesNotContain("SUCCESS", stdout, StringComparison.Ordinal);
            Assert.Contains("HDU 2", stderr, StringComparison.Ordinal);
            Assert.Contains(fits, stderr, StringComparison.Ordinal);

            // The truncation is gone: the reader refuses the file instead of yielding its first
            // frame, so nothing from this input is presented as a complete conversion.
            string[] written = Directory.Exists(outputDirectory)
                ? Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories)
                : [];
            Assert.Empty(written);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Bayer phase conflict in a joined run
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void JoinedRun_WithMixedBayerPhases_IsAControlledIncompleteRun()
    {
        string dir = TempDir();
        try
        {
            string rggb = WriteBayerSer(Path.Combine(dir, "rggb.ser"), BayerPattern.RGGB);
            string grbg = WriteBayerSer(Path.Combine(dir, "grbg.ser"), BayerPattern.GRBG);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-ser",
                "-join",
                "-od=" + outputDirectory,
                rggb,
                grbg,
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains(rggb, stderr, StringComparison.Ordinal);
            Assert.Contains(grbg, stderr, StringComparison.Ordinal);
            Assert.Contains("Bayer", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("incomplete", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // A normal run is still a success
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void NormalMultiHduFitsRun_StillExitsZero()
    {
        string dir = TempDir();
        try
        {
            string fits = WriteTwoHduFits(Path.Combine(dir, "two-hdus.fits"));
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                fits,
            ]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("Result:                          SUCCESS", stdout, StringComparison.Ordinal);
            Assert.Equal(2, Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // F5: help and the implemented AVI contract agree
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 2)]
    public void AviCodec_HelpAndBehaviourAgree(string codec, int expectedExitCode)
    {
        string dir = TempDir();
        try
        {
            string input = WritePng(Path.Combine(dir, "capture.png"));

            (int exitCode, string stdout, _) = Run([
                "-check",
                "-avi",
                "-avi_codec=" + codec,
                "-od=" + Path.Combine(dir, "out"),
                input,
            ]);

            Assert.Equal(expectedExitCode, exitCode);
            if (expectedExitCode == 0)
            {
                Assert.Contains("Command line check: OK", stdout, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
                Assert.Contains("not supported by this engine", stdout, StringComparison.Ordinal);
                Assert.Contains("DIB", stdout, StringComparison.Ordinal);
            }
        }
        finally
        {
            Delete(dir);
        }
    }

    [Theory]
    [InlineData("10", 0)]
    [InlineData("11", 2)]
    [InlineData("100", 2)]
    public void AviQuality_HelpAndBehaviourAgree(string quality, int expectedExitCode)
    {
        string dir = TempDir();
        try
        {
            string input = WritePng(Path.Combine(dir, "capture.png"));

            (int exitCode, string stdout, _) = Run([
                "-check",
                "-avi",
                "-avi_quality=" + quality,
                "-od=" + Path.Combine(dir, "out"),
                input,
            ]);

            Assert.Equal(expectedExitCode, exitCode);
            if (expectedExitCode == 0)
            {
                Assert.Contains("Command line check: OK", stdout, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
                Assert.Contains("outside the allowed range 1..10", stdout, StringComparison.Ordinal);
            }
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Help_AdvertisesTheImplementedAviRangeOnly()
    {
        string help = OpippHelpText.Text;

        Assert.Contains("-avi_quality=<1..10>", help, StringComparison.Ordinal);
        Assert.DoesNotContain("-avi_quality=<1..100>", help, StringComparison.Ordinal);

        // The compatibility note names the codecs the original engine offered and this one rejects.
        Assert.Contains("0 DIB", help, StringComparison.Ordinal);
        Assert.Contains("ULRG", help, StringComparison.Ordinal);
        Assert.Contains("FMP4", help, StringComparison.Ordinal);
        Assert.Contains("rejected", help, StringComparison.Ordinal);

        // The check help documents quick structural validation of every HDU.
        Assert.Contains("every HDU", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without decoding pixels", help, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    /// <summary>Writes a two-frame, two-HDU 16x16 8-bit FITS file.</summary>
    private static string WriteTwoHduFits(string path)
    {
        using (var writer = new FitsWriter(path))
        {
            for (int f = 0; f < 2; f++)
            {
                var frame = new Frame(16, 16, 1, 8);
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        double dx = x - 8, dy = y - 8;
                        frame.Samples[(y * 16) + x] = (ushort)(dx * dx + dy * dy <= 16 ? 255 : 40);
                    }
                }

                writer.Write(frame);
            }
        }

        return path;
    }

    /// <summary>
    /// Cuts the file inside the second HDU's pixel payload: the second header is complete, so only
    /// a structural data-length check can see the truncation.
    /// </summary>
    private static void TruncateSecondHdu(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = 0;
        FitsHeader.Read(stream, out int firstHeaderBytes);
        long firstDataBytes = 16 * 16; // 8-bit samples
        long secondHeader = firstHeaderBytes + RoundUp(firstDataBytes, 2880);
        stream.SetLength(secondHeader + 2880 + 40);
    }

    private static long RoundUp(long value, long block)
    {
        long remainder = value % block;
        return remainder == 0 ? value : value + block - remainder;
    }

    /// <summary>Writes a header that starts like FITS but stops before its END card.</summary>
    private static string WriteCorruptFits(string path)
    {
        string simple = "SIMPLE  =                    T / conforms to FITS standard".PadRight(80);
        string bitPix = "BITPIX  =                    8".PadRight(80);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(simple + bitPix));
        return path;
    }

    /// <summary>Writes a one-frame 16x16 8-bit SER whose header declares <paramref name="bayer"/>.</summary>
    private static string WriteBayerSer(string path, BayerPattern bayer)
    {
        using (var writer = new SerWriter(path, 16, 16, 1, 8, frameCount: 1, bayer: bayer))
        {
            var frame = new Frame(16, 16, 1, 8);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    double dx = x - 8, dy = y - 8;
                    frame.Samples[(y * 16) + x] = (ushort)(dx * dx + dy * dy <= 16 ? 255 : 40);
                }
            }

            writer.Write(frame);
        }

        return path;
    }

    /// <summary>Writes a one-frame 16x16 8-bit greyscale PNG.</summary>
    private static string WritePng(string path)
    {
        var frame = new Frame(16, 16, 1, 8);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                double dx = x - 8, dy = y - 8;
                frame.Samples[(y * 16) + x] = (ushort)(dx * dx + dy * dy <= 16 ? 255 : 40);
            }
        }

        using (var writer = new PngWriter(path, 16, 16, 1, 8))
        {
            writer.Write(frame);
        }

        return path;
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-cli-format", Guid.NewGuid().ToString("N"));
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
