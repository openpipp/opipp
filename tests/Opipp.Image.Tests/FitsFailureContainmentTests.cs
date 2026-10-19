namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Xunit;

/// <summary>
/// FITS reports bad structure and short pixel payloads with its own exception types, and a source
/// reader can reject a CFA phase while opening or decoding. Those are per-input failures: the run
/// records the file in <see cref="ProcessingReport.FailedFiles"/> and keeps whatever output it could
/// produce, instead of letting the exception escape as an unhandled crash.
/// </summary>
public class FitsFailureContainmentTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-fitsfail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Frame Constant(ushort value)
    {
        var frame = new Frame(8, 6, 1, 8);
        Array.Fill(frame.Samples, value);
        return frame;
    }

    private static string WriteFits(string dir, params ushort[] values)
    {
        string path = Path.Combine(dir, "capture.fits");
        using var writer = new FitsWriter(path);
        foreach (ushort value in values) writer.Write(Constant(value));
        return path;
    }

    [Fact]
    public void CorruptFitsAsTheOnlyInput_IsAReportedFailure_NotAnException()
    {
        string dir = TempDir();
        try
        {
            string input = Path.Combine(dir, "corrupt.fits");
            File.WriteAllText(input, "this is not a FITS file");
            string outputDirectory = Path.Combine(dir, "out");

            var report = new Processor(new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".tif",
            }).Run([input]);

            Assert.Equal(new[] { input }, report.FailedFiles);
            Assert.Equal(0, report.OutputFrames);
            Assert.Equal(0, report.FilesProcessed);
            Assert.Empty(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TruncatedFitsAfterAValidFrame_WritesThePartialOutput_AndNamesTheFailure()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFits(dir, 1, 2);
            // Each image is one 2880-byte header block plus one 2880-byte data block; dropping the
            // last block leaves frame 2's header intact but its pixel payload empty.
            long truncatedLength = new FileInfo(input).Length - 2880;
            using (var stream = new FileStream(input, FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(truncatedLength);
            }

            string outputDirectory = Path.Combine(dir, "out");
            var report = new Processor(new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".tif",
            }).Run([input]);

            Assert.Equal(new[] { input }, report.FailedFiles);
            Assert.Equal(1, report.InputFrames);
            Assert.Equal(1, report.OutputFrames);

            string[] outputs = Directory.GetFiles(Path.Combine(outputDirectory, "capture"), "*.tif");
            Assert.Single(outputs);
            using var reader = new TiffReader(outputs[0]);
            Frame partial = reader.ReadFrame()!;
            Assert.Equal(1, partial.Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(FitsFormatException))]
    [InlineData(typeof(FitsTruncationException))]
    [InlineData(typeof(BayerPhaseException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(NotSupportedException))]
    public void DecodeFailurePolicy_CoversTheFitsAndBayerExceptionFamilies(Type exceptionType)
    {
        Exception exception = exceptionType == typeof(FitsTruncationException)
            ? new FitsTruncationException("short payload", 100, 50)
            : (Exception)Activator.CreateInstance(exceptionType, "failed")!;

        Assert.True(Processor.IsInputDecodeFailure(exception));
    }

    [Fact]
    public void DecodeFailurePolicy_DoesNotSwallowCancellation()
    {
        Assert.False(Processor.IsInputDecodeFailure(new OperationCanceledException()));
        Assert.False(Processor.IsInputDecodeFailure(new TaskCanceledException()));
    }
}
