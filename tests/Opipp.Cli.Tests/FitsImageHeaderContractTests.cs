using System.Globalization;
using System.Text;
using Opipp.Core.Formats;

namespace Opipp.Cli.Tests;

/// <summary>Which image-header fault a raw three-HDU fixture carries.</summary>
public enum FitsImageFault
{
    None,
    SecondBScaleZero,
    SecondPCount,
    SecondGCount,
    FirstBScaleZero,
    FirstPCount,
    FirstGCount,
}

/// <summary>
/// The CLI contract for image headers that the frame reader rejects. A file with a valid first and
/// third IMAGE HDU and a malformed second one used to write frame 1, drop frame 3 and exit 0 with
/// SUCCESS; <c>-check</c> called it valid because the audit missed the constraint. Every fault must
/// now be exit 2 with the HDU named by <c>-check</c>, and a non-zero, non-SUCCESS processing run
/// that names the input and the HDU. The valid control still writes all three frames.
/// </summary>
/// <remarks>
/// Fixtures are raw FITS bytes (see <see cref="RawFitsFixture"/>), so this file does not depend on
/// <c>FitsWriter</c> agreeing with the reader about the malformed keywords.
/// </remarks>
[Collection("gui-stdout")]
public class FitsImageHeaderContractTests
{
    private static readonly object ConsoleGate = new();

    // ------------------------------------------------------------------ the control

    [Fact]
    public void Check_WithThreeValidImageHdus_ReportsThreeImages()
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(Path.Combine(dir, "three.fits"), FitsImageFault.None);

            (int exitCode, string stdout, string stderr) = Run(["-check", "-fits", fits]);

            Assert.Equal(0, exitCode);
            Assert.Contains("Command line check: OK", stdout, StringComparison.Ordinal);
            Assert.Contains("FITS structure: 3 image HDU(s) in 3 HDU(s) checked", stdout, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr);
            Assert.False(Directory.Exists(Path.Combine(dir, "out")));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithThreeValidImageHdus_WritesAllThreeFrames()
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(Path.Combine(dir, "three.fits"), FitsImageFault.None);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run(["-tif", "-od=" + outputDirectory, fits]);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("Result:                          SUCCESS", stdout, StringComparison.Ordinal);
            Assert.Equal(3, Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ------------------------------------------------------------------ -check rejects each fault

    [Theory]
    [InlineData(FitsImageFault.SecondBScaleZero, 2, "BSCALE")]
    [InlineData(FitsImageFault.SecondPCount, 2, "PCOUNT")]
    [InlineData(FitsImageFault.SecondGCount, 2, "GCOUNT")]
    [InlineData(FitsImageFault.FirstBScaleZero, 1, "BSCALE")]
    [InlineData(FitsImageFault.FirstPCount, 1, "PCOUNT")]
    [InlineData(FitsImageFault.FirstGCount, 1, "GCOUNT")]
    public void Check_RejectsTheImageHeaderTheReaderRejects(FitsImageFault fault, int hdu, string keyword)
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(Path.Combine(dir, "fault.fits"), fault);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-check",
                "-tif",
                "-od=" + outputDirectory,
                fits,
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains($"'{fits}'", stdout, StringComparison.Ordinal);
            Assert.Contains($"HDU {hdu}", stdout, StringComparison.Ordinal);
            Assert.Contains(keyword, stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("FITS structure:", stdout, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputDirectory));
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ------------------------------------------------------------------ processing refuses to truncate

    [Theory]
    [InlineData(FitsImageFault.SecondBScaleZero, 2)]
    [InlineData(FitsImageFault.SecondPCount, 2)]
    [InlineData(FitsImageFault.SecondGCount, 2)]
    [InlineData(FitsImageFault.FirstBScaleZero, 1)]
    [InlineData(FitsImageFault.FirstPCount, 1)]
    [InlineData(FitsImageFault.FirstGCount, 1)]
    public void Run_WithARejectedImageHeader_FailsNamingTheInputAndHdu(FitsImageFault fault, int hdu)
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(Path.Combine(dir, "fault.fits"), fault);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run(["-tif", "-od=" + outputDirectory, fits]);

            // No frame from this file can be trusted, so the run is the engine's "no output" code.
            // The point is that it is never 0 and never claims the file succeeded.
            Assert.NotEqual(0, exitCode);
            Assert.DoesNotContain("SUCCESS", stdout, StringComparison.Ordinal);
            Assert.Contains(fits, stderr, StringComparison.Ordinal);
            Assert.Contains($"HDU {hdu}", stderr, StringComparison.Ordinal);
            Assert.Contains("could not be read", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithAValidInputBeforeARejectedLaterHdu_IsAPartialFailure()
    {
        string dir = TempDir();
        try
        {
            string good = RawFitsFixture.WriteThree(Path.Combine(dir, "good.fits"), FitsImageFault.None);
            string bad = RawFitsFixture.WriteThree(Path.Combine(dir, "bad.fits"), FitsImageFault.SecondBScaleZero);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run([
                "-tif",
                "-od=" + outputDirectory,
                good,
                bad,
            ]);

            // The three frames from the good file are kept and explicitly labelled incomplete.
            Assert.Equal(1, exitCode);
            Assert.Contains("PARTIAL FAILURE", stdout, StringComparison.Ordinal);
            Assert.Contains("incomplete", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(bad, stdout + stderr, StringComparison.Ordinal);
            Assert.Contains("HDU 2", stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("SUCCESS", stdout, StringComparison.Ordinal);
            Assert.Equal(3, Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ------------------------------------------------------------------ truncated later data block

    [Fact]
    public void Check_WithATruncatedSecondDataBlock_ReportsHduTwoAndExitTwo()
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(
                Path.Combine(dir, "truncated.fits"),
                FitsImageFault.None,
                truncateSecondDataAfter: 3);

            (int exitCode, string stdout, _) = Run(["-check", "-fits", fits]);

            Assert.Equal(2, exitCode);
            Assert.Contains("Command line check: FAILED", stdout, StringComparison.Ordinal);
            Assert.Contains("HDU 2", stdout, StringComparison.Ordinal);
            Assert.Contains("truncated", stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Run_WithATruncatedSecondDataBlock_KeepsTheControlledPartialResult()
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(
                Path.Combine(dir, "truncated.fits"),
                FitsImageFault.None,
                truncateSecondDataAfter: 3);
            string outputDirectory = Path.Combine(dir, "out");

            (int exitCode, string stdout, string stderr) = Run(["-tif", "-od=" + outputDirectory, fits]);

            // Frame 1 was readable; frame 2 reports the short payload. That stays a partial result
            // naming the input, never a crash and never SUCCESS.
            Assert.Equal(1, exitCode);
            Assert.Contains("PARTIAL FAILURE", stdout, StringComparison.Ordinal);
            Assert.Contains("incomplete", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(fits, stdout + stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("SUCCESS", stdout, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(outputDirectory, "*.tif", SearchOption.AllDirectories));
        }
        finally
        {
            Delete(dir);
        }
    }

    // ------------------------------------------------------------------ audit and reader agree

    [Theory]
    [InlineData(FitsImageFault.SecondBScaleZero, 2)]
    [InlineData(FitsImageFault.SecondPCount, 2)]
    [InlineData(FitsImageFault.SecondGCount, 2)]
    [InlineData(FitsImageFault.FirstBScaleZero, 1)]
    [InlineData(FitsImageFault.FirstPCount, 1)]
    [InlineData(FitsImageFault.FirstGCount, 1)]
    public void Audit_AndReader_RejectTheSameHdu(FitsImageFault fault, int hdu)
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(Path.Combine(dir, "fault.fits"), fault);

            FitsHduAudit.Result audit = FitsHduAudit.Inspect(fits);
            Assert.NotEmpty(audit.Errors);
            Assert.Contains(audit.Errors, error => error.Contains($"HDU {hdu}", StringComparison.Ordinal));

            FitsFormatException reader = Assert.Throws<FitsFormatException>(() => new FitsReader(fits));
            Assert.Contains($"HDU {hdu}", reader.Message, StringComparison.Ordinal);

            // Every HDU is still counted: the audit did not stop before the malformed header.
            Assert.Equal(3, audit.HduCount);
            Assert.Equal(3, audit.ImageHduCount);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void Audit_WithTheThreeHduControl_HasNoErrors()
    {
        string dir = TempDir();
        try
        {
            string fits = RawFitsFixture.WriteThree(Path.Combine(dir, "three.fits"), FitsImageFault.None);

            FitsHduAudit.Result audit = FitsHduAudit.Inspect(fits);

            Assert.Empty(audit.Errors);
            Assert.Equal(3, audit.HduCount);
            Assert.Equal(3, audit.ImageHduCount);
        }
        finally
        {
            Delete(dir);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-cli-fits-image", Guid.NewGuid().ToString("N"));
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

/// <summary>
/// Builds raw three-HDU 3x2 8-bit FITS files card by card. Nothing here calls the production writer
/// or reader, so a fault fixture is malformed for reasons the test states explicitly.
/// </summary>
internal static class RawFitsFixture
{
    private const int Block = 2880;
    private const int Header = 2880;
    private const int Data = 2880; // 6 payload bytes padded to a block

    internal static string WriteThree(
        string path,
        FitsImageFault fault,
        int? truncateSecondDataAfter = null)
    {
        byte[] first = Image(
            Payload(fault, firstHdu: true),
            primary: true,
            bScale: fault == FitsImageFault.FirstBScaleZero ? "0.0" : null,
            pcount: fault == FitsImageFault.FirstPCount ? 1 : null,
            gcount: fault == FitsImageFault.FirstGCount ? 2 : null);

        byte[] second = Image(
            Payload(fault, firstHdu: false),
            primary: false,
            bScale: fault == FitsImageFault.SecondBScaleZero ? "0.0" : null,
            pcount: fault == FitsImageFault.SecondPCount ? 1 : null,
            gcount: fault == FitsImageFault.SecondGCount ? 2 : null);

        byte[] third = Image([21, 22, 23, 24, 25, 26], primary: false);

        byte[] bytes = Build(first, second, third);
        if (truncateSecondDataAfter is int keep)
        {
            // First HDU: header at 0, data at 2880, next header at 5760. Second data starts at 8640.
            int secondDataOffset = Header + Data + Header;
            bytes = bytes[..(secondDataOffset + keep)];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Payload(FitsImageFault fault, bool firstHdu)
    {
        bool pcount = firstHdu
            ? fault == FitsImageFault.FirstPCount
            : fault == FitsImageFault.SecondPCount;
        bool gcount = firstHdu
            ? fault == FitsImageFault.FirstGCount
            : fault == FitsImageFault.SecondGCount;
        byte start = firstHdu ? (byte)1 : (byte)11;

        // PCOUNT=1 declares one extra value (7 bytes for a 3x2 8-bit image); GCOUNT=2 declares two
        // groups (12 bytes). The fixture stores exactly what the header declares.
        int length = pcount ? 7 : gcount ? 12 : 6;
        var payload = new byte[length];
        for (int i = 0; i < length; i++)
        {
            payload[i] = (byte)(start + i);
        }

        return payload;
    }

    private static byte[] Image(
        byte[] payload,
        bool primary,
        int width = 3,
        int height = 2,
        int bitPix = 8,
        string? bScale = null,
        int? pcount = null,
        int? gcount = null)
    {
        var cards = new List<string>
        {
            primary ? Card("SIMPLE", "T") : Card("XTENSION", "'IMAGE'", quoted: true),
            Card("BITPIX", bitPix.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS", "2"),
            Card("NAXIS1", width.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS2", height.ToString(CultureInfo.InvariantCulture)),
        };

        if (pcount is not null) cards.Add(Card("PCOUNT", pcount.Value.ToString(CultureInfo.InvariantCulture)));
        if (gcount is not null) cards.Add(Card("GCOUNT", gcount.Value.ToString(CultureInfo.InvariantCulture)));
        if (bScale is not null) cards.Add(Card("BSCALE", bScale));
        cards.Add("END".PadRight(80));

        return [.. Encoding.ASCII.GetBytes(string.Concat(cards)), .. payload];
    }

    private static string Card(string keyword, string value, bool quoted = false)
    {
        string field = quoted ? value.PadRight(20) : value.PadLeft(20);
        string card = keyword.PadRight(8) + "= " + field;
        if (card.Length > 80) throw new InvalidOperationException($"card for {keyword} is too long");
        return card.PadRight(80);
    }

    private static byte[] Build(params byte[][] hdus)
    {
        using var stream = new MemoryStream();
        foreach (byte[] hdu in hdus)
        {
            int headerEnd = EndOfHeader(hdu);
            int headerLength = RoundUp(headerEnd);
            stream.Write(hdu, 0, headerEnd);
            stream.Write(new byte[headerLength - headerEnd], 0, headerLength - headerEnd);

            int dataLength = hdu.Length - headerEnd;
            stream.Write(hdu, headerEnd, dataLength);
            int dataPad = RoundUp(dataLength) - dataLength;
            stream.Write(new byte[dataPad], 0, dataPad);
        }

        return stream.ToArray();
    }

    private static int EndOfHeader(byte[] hdu)
    {
        string firstBlock = Encoding.ASCII.GetString(hdu, 0, Math.Min(hdu.Length, Block));
        int index = firstBlock.IndexOf("END".PadRight(80), StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException("fixture header has no END card");
        return index + 80;
    }

    private static int RoundUp(long value) => checked((int)(((value + Block - 1) / Block) * Block));
}
