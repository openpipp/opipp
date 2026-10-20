namespace Opipp.Image.Tests;

using System.Buffers.Binary;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Opipp.Core.Stacking;

public class StackingBayerTests
{
    public static IEnumerable<object[]> MatchingPatterns()
    {
        foreach (StackMethod method in Enum.GetValues<StackMethod>())
        foreach (BayerPattern pattern in Enum.GetValues<BayerPattern>())
            yield return [method, pattern];
    }

    [Theory]
    [MemberData(nameof(MatchingPatterns))]
    public void MatchingPatterns_PreserveTheSubmittedMosaic(StackMethod method, BayerPattern pattern)
    {
        var stacker = new FrameStacker(2, 1, 1, 8, method);
        var first = SampleFrame(pattern, 10);
        stacker.Add(first);
        stacker.Add(SampleFrame(pattern, 30));
        first.Bayer = BayerPattern.BGGR; // Metadata is snapshotted just like submitted samples.

        Frame result = stacker.Result();

        Assert.Equal(pattern, result.Bayer);
        Assert.Equal(method == StackMethod.Sum ? new ushort[] { 40, 40 } : [20, 20], result.Samples);
    }

    public static IEnumerable<object[]> MismatchedPatterns()
    {
        foreach (StackMethod method in Enum.GetValues<StackMethod>())
        {
            yield return [method, BayerPattern.RGGB, BayerPattern.GRBG];
            yield return [method, BayerPattern.RGGB, BayerPattern.BGGR];
            yield return [method, BayerPattern.RGGB, BayerPattern.None];
            yield return [method, BayerPattern.None, BayerPattern.RGGB];
        }
    }

    [Theory]
    [MemberData(nameof(MismatchedPatterns))]
    public void MismatchedPatterns_RejectWithoutChangingSamplesCountOrProvenance(
        StackMethod method, BayerPattern firstPattern, BayerPattern rejectedPattern)
    {
        var stacker = new FrameStacker(2, 1, 1, 8, method);
        stacker.Add(SampleFrame(firstPattern, 10));
        Frame rejected = SampleFrame(rejectedPattern, 200);
        rejected.SourceName = "rejected.ser";

        Assert.Throws<BayerPhaseException>(() => stacker.Add(rejected));

        Assert.Equal(1, stacker.FrameCount);
        Assert.Equal(new ushort[] { 10, 10 }, stacker.Result().Samples);
        Assert.Null(stacker.Result().SourceName);
        Frame accepted = SampleFrame(firstPattern, 30);
        accepted.SourceName = "accepted.ser";
        stacker.Add(accepted);
        Frame result = stacker.Result();
        Assert.Equal(2, stacker.FrameCount);
        Assert.Equal(firstPattern, result.Bayer);
        Assert.Equal("accepted.ser", result.SourceName);
        Assert.Equal(method == StackMethod.Sum ? new ushort[] { 40, 40 } : [20, 20], result.Samples);
    }

    [Fact]
    public void ColourFrameWithBayerTag_IsRejectedBeforeItCanSeedTheStack()
    {
        var stacker = new FrameStacker(1, 1, 3, 8);
        var invalid = new Frame(1, 1, 3, 8, [10, 20, 30]) { Bayer = BayerPattern.RGGB };

        Assert.Throws<ArgumentException>(() => stacker.Add(invalid));

        Assert.Equal(0, stacker.FrameCount);
        stacker.Add(new Frame(1, 1, 3, 8, [30, 40, 50]));
        Assert.Equal(BayerPattern.None, stacker.Result().Bayer);
        Assert.Equal(new ushort[] { 30, 40, 50 }, stacker.Result().Samples);
    }

    [Fact]
    public void RejectedFirstFrame_DoesNotChooseTheStacksPattern()
    {
        var stacker = new FrameStacker(2, 1, 1, 8);
        var wrongSize = new Frame(3, 1, 1, 8) { Bayer = BayerPattern.RGGB };

        Assert.Throws<ArgumentException>(() => stacker.Add(wrongSize));
        stacker.Add(SampleFrame(BayerPattern.GRBG, 10));

        Assert.Equal(BayerPattern.GRBG, stacker.Result().Bayer);
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(1L)]
    public void ProcessorStack_WritesTheTransformedCfaPatternToSer(long retentionBudget)
    {
        string dir = TempDir();
        try
        {
            string input = WriteCapture(dir, "capture.ser", BayerPattern.RGGB, Mosaic(0), Mosaic(2));
            string outputDirectory = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                Stack = StackMethod.Mean,
                FlipX = true,
                RetentionBudgetBytes = retentionBudget,
            };

            ProcessingReport report = new Processor(settings).Run([input]);

            Assert.Equal(1, report.OutputFrames);
            byte[] output = File.ReadAllBytes(Path.Combine(outputDirectory, "capture.ser"));
            // SER ColorID 9 is GRBG: the even-width horizontal flip moves the red site to x=1.
            Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(18, 4)));
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(38, 4)));
            Assert.Equal(new byte[] { 108, 54, 192, 108 },
                new[] { output[178], output[179], output[186], output[187] });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.None)]
    public void ProcessorStack_JoinedIncompatiblePatternsWriteNoOutput(BayerPattern secondPattern)
    {
        string dir = TempDir();
        try
        {
            string first = WriteCapture(dir, "first.ser", BayerPattern.RGGB, Mosaic(0));
            string second = WriteCapture(dir, "second.ser", secondPattern, Mosaic(0));
            string outputDirectory = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                Stack = StackMethod.Mean,
                JoinMode = true,
            };

            Assert.Throws<BayerPhaseException>(() => new Processor(settings).Run([first, second]));

            Assert.Empty(Directory.GetFiles(outputDirectory, "*.ser", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ProcessorStack_OddRelativeCentreShiftRejectsMixedPhases()
    {
        string dir = TempDir();
        try
        {
            var first = new Frame(40, 30, 1, 8);
            var second = new Frame(40, 30, 1, 8);
            // The detected objects are one pixel apart, requiring shifts of opposite parity.
            for (int y = 10; y < 20; y++)
            for (int x = 10; x < 26; x++)
            {
                first[x, y] = 200;
                second[x + 1, y] = 200;
            }
            string input = WriteCapture(dir, "capture.ser", BayerPattern.RGGB, first, second);
            string outputDirectory = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                Stack = StackMethod.Mean,
                Centre = true,
                MinPixelValue = 100,
            };

            Assert.Throws<BayerPhaseException>(() => new Processor(settings).Run([input]));

            Assert.Empty(Directory.GetFiles(outputDirectory, "*.ser", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CalibrationMixedPatterns_ReturnFailureAndPreserveExistingOutputs()
    {
        string dir = TempDir();
        try
        {
            string first = WriteCapture(dir, "first.ser", BayerPattern.RGGB, Mosaic(0));
            string second = WriteCapture(dir, "second.ser", BayerPattern.GRBG, Mosaic(0));
            string masterPath = Path.Combine(dir, "master.tmp");
            string pgmPath = Path.Combine(dir, "master.pgm");
            byte[] previousMaster = [1, 2, 3];
            byte[] previousPgm = [4, 5, 6];
            File.WriteAllBytes(masterPath, previousMaster);
            File.WriteAllBytes(pgmPath, previousPgm);

            MasterBuildResult result = MasterFrameBuilder.Build(
                [first, second], null, masterPath, pgmPath, 0);

            Assert.False(result.Success);
            Assert.Contains("Bayer", result.Error);
            Assert.Equal(previousMaster, File.ReadAllBytes(masterPath));
            Assert.Equal(previousPgm, File.ReadAllBytes(pgmPath));
            Assert.Empty(Directory.GetFiles(dir, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UntaggedSubtractionMaster_PreservesTheInputsBayerPattern()
    {
        var stacker = new FrameStacker(2, 1, 1, 8);

        stacker.Add(SampleFrame(BayerPattern.RGGB, 30), SampleFrame(BayerPattern.None, 10));

        Assert.Equal(BayerPattern.RGGB, stacker.Result().Bayer);
        Assert.Equal(new ushort[] { 20, 20 }, stacker.Result().Samples);
    }

    private static Frame SampleFrame(BayerPattern pattern, ushort value) =>
        new(2, 1, 1, 8, [value, value]) { Bayer = pattern };

    private static Frame Mosaic(int offset)
    {
        var frame = new Frame(8, 6, 1, 8);
        for (int y = 0; y < 6; y++)
        for (int x = 0; x < 8; x++)
            frame[x, y] = (ushort)(offset + ((x % 2, y % 2) switch
            {
                (0, 0) => 53,
                (1, 1) => 191,
                _ => 107,
            }));
        return frame;
    }

    private static string WriteCapture(string dir, string name, BayerPattern pattern, params Frame[] frames)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, frames[0].Width, frames[0].Height, 1, 8, frames.Length, pattern);
        foreach (Frame frame in frames) writer.Write(frame);
        return path;
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-stackbayer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
