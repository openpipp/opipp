using System.Globalization;
using System.Text;

namespace Opipp.Cli.Tests;

[Collection("gui-stdout")]
public class FitsScanBoundaryContractTests
{
    [Theory]
    [InlineData("short-tail")]
    [InlineData("short-header-with-end")]
    [InlineData("different-shape")]
    [InlineData("non-image")]
    public void TruncatedSkippedStructure_CheckAndProcessingBothFailNamingTheHdu(string fault)
    {
        string dir = Path.Combine(Path.GetTempPath(), "opipp-fits-boundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = RawFitsFixture.WriteThree(Path.Combine(dir, "input.fits"), FitsImageFault.None);
            byte[] valid = File.ReadAllBytes(path);
            byte[] badTail;
            if (fault == "short-tail")
            {
                badTail = Encoding.ASCII.GetBytes("XTENSION".PadRight(79));
            }
            else if (fault == "short-header-with-end")
            {
                badTail = valid[5760..6400];
                Encoding.ASCII.GetBytes(("XTENSION= " + "'BINTABLE'".PadLeft(20)).PadRight(80))
                    .CopyTo(badTail, 0);
                Encoding.ASCII.GetBytes(("NAXIS   = " + "0".PadLeft(20)).PadRight(80))
                    .CopyTo(badTail, 2 * 80);
            }
            else
            {
                badTail = valid[5760..8643]; // The second header and only half its six-byte payload.
                string keyword = fault == "different-shape" ? "NAXIS1" : "XTENSION";
                string value = fault == "different-shape" ? "4" : "'BINTABLE'";
                int offset = fault == "different-shape" ? 3 * 80 : 0;
                byte[] replacement = Encoding.ASCII.GetBytes(
                    (keyword.PadRight(8) + "= " + value.PadLeft(20)).PadRight(80));
                replacement.CopyTo(badTail, offset);
            }

            File.WriteAllBytes(path, [.. valid[..5760], .. badTail]);
            string output = Path.Combine(dir, "out");

            (int checkExit, string checkOut, _) = Run(["-check", "-tif", path]);
            (int runExit, string runOut, string runError) = Run(["-tif", "-od=" + output, path]);

            Assert.Equal(2, checkExit);
            Assert.Contains("HDU 2", checkOut, StringComparison.Ordinal);
            Assert.NotEqual(0, runExit);
            Assert.DoesNotContain("SUCCESS", runOut, StringComparison.Ordinal);
            Assert.Contains("HDU 2", runError, StringComparison.Ordinal);
            Assert.Contains(path, runError, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir, "*.tif", SearchOption.AllDirectories));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static (int Exit, string Out, string Error) Run(string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
