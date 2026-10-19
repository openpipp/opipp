namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Stacking;
using Xunit;

/// <summary>
/// A stacked master is still an image of the same capture, so its SER identity fields must not be
/// blank. The output writer takes Observer/Telescope/Instrument from the capture named by
/// <see cref="Frame.SourceName"/>; the stacker therefore has to put the selected frames' provenance
/// on its result instead of dropping it with the per-frame copies.
/// </summary>
/// <remarks>
/// Rule: the master carries the first accepted frame's non-empty <c>SourceName</c>; frames without
/// provenance are skipped. When selected frames name different captures, the first one's values win,
/// because a single output header must not mix an observer from one capture with an instrument from
/// another.
/// </remarks>
public class StackIdentityMetadataTests
{
    private const int Width = 8;
    private const int Height = 6;

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-stackid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteCapture(
        string dir, string fileName, string observer, string telescope, string instrument, params ushort[] values)
    {
        string path = Path.Combine(dir, fileName);
        SerHeader header = SerHeader.Create(Width, Height, 1, 8, values.Length);
        header.Observer = observer;
        header.Telescope = telescope;
        header.Instrument = instrument;
        using var writer = new SerWriter(path, header);
        foreach (ushort value in values)
        {
            var frame = new Frame(Width, Height, 1, 8);
            Array.Fill(frame.Samples, value);
            writer.Write(frame);
        }

        return path;
    }

    private static Frame CaptureFrame(string sourceName, ushort value)
    {
        var frame = new Frame(Width, Height, 1, 8);
        Array.Fill(frame.Samples, value);
        frame.SourceName = sourceName;
        return frame;
    }

    private static void AssertIdentity(string path, string observer, string telescope, string instrument)
    {
        SerInfo info = SerProbe.Probe(path);
        Assert.Equal(observer, info.Observer);
        Assert.Equal(telescope, info.Telescope);
        Assert.Equal(instrument, info.Instrument);
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(1L)] // force every accepted frame through the spill store
    public void ProcessorStack_KeepsTheCapturesObserverTelescopeAndInstrument(long retentionBudget)
    {
        string dir = TempDir();
        try
        {
            string input = WriteCapture(dir, "saturn.ser", "Ada", "Dobsonian", "ASI174MM", 10, 20, 30);
            string outputDirectory = Path.Combine(dir, retentionBudget == 1L ? "out-spilled" : "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outputDirectory,
                OutputExtension = ".ser",
                Stack = StackMethod.Mean,
                RetentionBudgetBytes = retentionBudget,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Empty(report.FailedFiles);
            Assert.Equal(1, report.OutputFrames);
            AssertIdentity(Path.Combine(outputDirectory, "saturn.ser"), "Ada", "Dobsonian", "ASI174MM");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Result_ThreeFramesWithMetadata_KeepsThemOnTheMaster()
    {
        string dir = TempDir();
        try
        {
            string source = WriteCapture(dir, "capture.ser", "Grace", "Newtonian", "IMX571", 10, 20, 30);
            var stacker = new FrameStacker(Width, Height, 1, 8, StackMethod.Mean);
            stacker.Add(CaptureFrame(source, 10));
            stacker.Add(CaptureFrame(source, 20));
            stacker.Add(CaptureFrame(source, 30));

            var master = stacker.Result();

            Assert.Equal(source, master.SourceName);
            string masterPath = Path.Combine(dir, "master.ser");
            using (IFrameSink sink = FrameSinkFactory.Create(masterPath, master, 1, 25))
            {
                sink.Write(master);
            }

            AssertIdentity(masterPath, "Grace", "Newtonian", "IMX571");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Result_DisagreeingSources_KeepsTheFirstFramesValues()
    {
        string dir = TempDir();
        try
        {
            string first = WriteCapture(dir, "first.ser", "FirstObserver", "FirstScope", "FirstCamera", 10);
            string second = WriteCapture(dir, "second.ser", "SecondObserver", "SecondScope", "SecondCamera", 20);
            string third = WriteCapture(dir, "third.ser", "ThirdObserver", "ThirdScope", "ThirdCamera", 30);

            var stacker = new FrameStacker(Width, Height, 1, 8, StackMethod.Mean);
            stacker.Add(CaptureFrame(first, 10));
            stacker.Add(CaptureFrame(second, 20));
            stacker.Add(CaptureFrame(third, 30));

            var master = stacker.Result();

            Assert.Equal(first, master.SourceName);
            string masterPath = Path.Combine(dir, "master.ser");
            using (IFrameSink sink = FrameSinkFactory.Create(masterPath, master, 1, 25))
            {
                sink.Write(master);
            }

            // One header cannot describe three captures: the first frame's identity is kept whole,
            // rather than aggregating fields into a combination no capture ever had.
            AssertIdentity(masterPath, "FirstObserver", "FirstScope", "FirstCamera");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Result_LeadingFrameWithoutProvenance_UsesTheFirstNamedSource()
    {
        string dir = TempDir();
        try
        {
            string named = WriteCapture(dir, "named.ser", "NamedObserver", "NamedScope", "NamedCamera", 10);
            var stacker = new FrameStacker(Width, Height, 1, 8, StackMethod.Mean);
            var anonymous = CaptureFrame(named, 10);
            anonymous.SourceName = null;
            stacker.Add(anonymous);
            stacker.Add(CaptureFrame(named, 20));

            Assert.Equal(named, stacker.Result().SourceName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(StackMethod.Mean)]
    [InlineData(StackMethod.Sum)]
    [InlineData(StackMethod.Median)]
    public void Result_KeepsProvenanceForEveryStackMethod(StackMethod method)
    {
        string dir = TempDir();
        try
        {
            string source = WriteCapture(dir, "capture.ser", "Observer", "Scope", "Camera", 1, 2, 3);
            var stacker = new FrameStacker(Width, Height, 1, 8, method);
            for (int i = 1; i <= 3; i++) stacker.Add(CaptureFrame(source, (ushort)i));

            Assert.Equal(source, stacker.Result().SourceName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
