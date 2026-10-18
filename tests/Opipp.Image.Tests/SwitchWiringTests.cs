namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Formats;
using Opipp.Core.Pipeline;

/// <summary>
/// Pipeline-level tests for the frame-window, output-layout and selection switches that used to be
/// parsed but ignored: <c>-f</c>, <c>-sf</c>, <c>-ctrf</c>, <c>-ifd</c>, <c>-osubdir</c>, <c>-sr</c>,
/// <c>-join</c>, <c>-incqv</c> and <c>-drop</c>.
/// </summary>
/// <remarks>
/// The expected values are the ones the shipped <c>pipp_core.exe</c> produced for the same switches
/// on a synthetic capture (every frame is a distinct constant, so the surviving frames can be read
/// back by value). See <c>notes/switch-wiring.md</c> for the audit and the oracle commands.
/// </remarks>
public class SwitchWiringTests
{
    private const int Width = 16;
    private const int Height = 12;

    /// <summary>A capture whose frame <c>i</c> is filled with <c>10 + i</c>, so frames are identifiable.</summary>
    private static (string Path, Frame[] Frames) WriteCapture(string directory, string name, int frames, int first = 0)
    {
        string path = Path.Combine(directory, name);
        var built = new Frame[frames];
        var random = new Random(7 + first);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(Width, Height, 1, 8);
            // A textured but deterministic marker per frame keeps detection/quality paths exercised
            // without making frames indistinguishable.
            for (int i = 0; i < frame.Samples.Length; i++)
            {
                frame.Samples[i] = (ushort)(20 + ((first + f) * 7 + random.Next(0, 3)) % 200);
            }

            frame.Samples[0] = (ushort)(first + f);
            built[f] = frame;
        }

        using var writer = new SerWriter(path, Width, Height, 1, 8);
        foreach (var frame in built) writer.Write(frame);
        return (path, built);
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "pipp-sw-" + Guid.NewGuid().ToString("N"));

    /// <summary>Reads every still written into <paramref name="directory"/>, ordered by file name.</summary>
    private static List<Frame> ReadStills(string directory)
    {
        var frames = new List<Frame>();
        foreach (string path in Directory.GetFiles(directory, "*.tif").OrderBy(p => p, StringComparer.Ordinal))
        {
            using var reader = new TiffReader(path);
            frames.Add(reader.ReadFrame()!);
        }

        return frames;
    }

    private static PipelineSettings StillSettings(string outputDirectory) => new()
    {
        OutputDirectory = outputDirectory,
        OutputExtension = ".tif",
        WriteLogFile = false,
    };

    [Fact]
    public void FrameCount_LimitsTheRunToTheFirstFrames()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.FrameCount = 3;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(3, report.OutputFrames);
            Assert.Equal(3, report.InputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(3, written.Count);
            for (int i = 0; i < 3; i++) Assert.Equal(source[i].Samples, written[i].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartFrame_SkipsTheLeadingFrames()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.StartFrame = 3;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(6, report.OutputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(6, written.Count);
            // -sf is 1-based and inclusive: the first written frame is source frame 3.
            Assert.Equal(source[2].Samples, written[0].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartFrameAndFrameCount_SelectAnInclusiveRange()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.StartFrame = 3;
            settings.FrameCount = 2;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(2, report.OutputFrames);
            // The engine reads sf + f - 1 frames and then drops the leading ones, so the log's
            // "total input frames" is 4 for -sf=3 -f=2.
            Assert.Equal(4, report.InputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(source[2].Samples, written[0].Samples);
            Assert.Equal(source[3].Samples, written[1].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FramesFromCentre_TakesTheRangeFromTheMiddle()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.FramesFromCentre = true;
            settings.FrameCount = 2;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(2, report.OutputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            // start = (8 - 2) / 2 = 3 (0-based), i.e. frames 4 and 5.
            Assert.Equal(source[3].Samples, written[0].Samples);
            Assert.Equal(source[4].Samples, written[1].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FramesFromCentre_WithoutFrameCount_StartsAtTheMidpoint()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.FramesFromCentre = true;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(4, report.OutputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(source[4].Samples, written[0].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FramesFromCentre_OverridesStartFrame()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.FramesFromCentre = true;
            settings.FrameCount = 2;
            settings.StartFrame = 2;

            new Processor(settings).Run([input]);

            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(source[3].Samples, written[0].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InputFrameDrop_KeepsOneFrameThenDiscardsN()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.InputFrameDrop = 2;

            var report = new Processor(settings).Run([input]);

            // -ifd=2 keeps frames 1, 4 and 7 of an 8-frame capture, which is what the oracle did
            // with saturn30.ser (frames 1, 7, 13, 19, 25 for -ifd=5).
            Assert.Equal(3, report.OutputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(source[0].Samples, written[0].Samples);
            Assert.Equal(source[3].Samples, written[1].Samples);
            Assert.Equal(source[6].Samples, written[2].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InputFrameDrop_RestartsAfterTheStartFrame()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.StartFrame = 3;
            settings.InputFrameDrop = 2;

            new Processor(settings).Run([input]);

            // The ifd counter starts at the first frame of the window: frames 3, 6 (1-based).
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(2, written.Count);
            Assert.Equal(source[2].Samples, written[0].Samples);
            Assert.Equal(source[5].Samples, written[1].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FrameCount_IsIgnoredWhenItDoesNotAlignWithTheInputDropStride()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 8);
            var settings = StillSettings(directory);
            settings.InputFrameDrop = 2;
            settings.FrameCount = 5; // (5 - 1) % (2 + 1) != 0

            var report = new Processor(settings).Run([input]);

            // pipp_core only honours -f when f = 1 + k*(ifd+1); otherwise the whole capture is read
            // (oracle: -ifd=2 -f=11 on 30 frames kept all 10 survivors, -ifd=2 -f=10 kept 4).
            Assert.Equal(3, report.OutputFrames);
            Assert.Equal(8, report.InputFrames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OutputSubdirectory_WrapsOutputsAndLogs()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, _) = WriteCapture(directory, "capture.ser", 4);
            var settings = StillSettings(directory);
            settings.OutputSubdirectory = "pipp_2026";
            settings.WriteLogFile = true;

            new Processor(settings).Run([input]);

            // Oracle: -osubdir=pipp_2026 wrote <od>/pipp_2026/saturn30/01.tif and
            // <od>/pipp_2026/logs/saturn30_pipp_log.txt.
            Assert.True(File.Exists(Path.Combine(directory, "pipp_2026", "capture", "1.tif")));
            Assert.True(File.Exists(Path.Combine(directory, "pipp_2026", "logs", "capture_pipp_log.txt")));
            Assert.False(Directory.Exists(Path.Combine(directory, "capture")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveRejectedFrames_WritesThreeChannelTiffsUnderTheRejectedFolder()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, _) = WriteCapture(directory, "capture.ser", 4);
            var settings = StillSettings(directory);
            settings.CheckForPlanet = true;
            settings.MinPixelValue = 255; // nothing reaches the threshold: no object in any frame
            settings.SaveRejectedFrames = true;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(0, report.OutputFrames);
            string rejected = Path.Combine(directory, "rejected", "capture_rejected");
            var files = Directory.GetFiles(rejected, "*.tif").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.Equal(4, files.Length);
            Assert.EndsWith("no_object_1.tif", files[0]);
            Assert.EndsWith("no_object_4.tif", files[3]);

            // The engine always writes rejected frames as three-channel TIFF, even for mono input
            // (a 640x640 8-bit frame became a 1,228,976-byte RGB TIFF in the oracle run).
            using var reader = new TiffReader(files[0]);
            var frame = reader.ReadFrame()!;
            Assert.Equal(3, frame.Channels);
            Assert.Equal(8, frame.BitDepth);
            Assert.Equal(frame.Samples[0], frame.Samples[1]);
            Assert.Equal(frame.Samples[0], frame.Samples[2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveRejectedFrames_IsSilentWhenNothingIsRejected()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, _) = WriteCapture(directory, "capture.ser", 3);
            var settings = StillSettings(directory);
            settings.SaveRejectedFrames = true;

            var report = new Processor(settings).Run([input]);

            Assert.Equal(3, report.OutputFrames);
            Assert.False(Directory.Exists(Path.Combine(directory, "rejected")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JoinMode_ProducesOneOutputAndOneLogAcrossInputs()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (first, firstFrames) = WriteCapture(directory, "first.ser", 4);
            var (second, secondFrames) = WriteCapture(directory, "second.ser", 3, first: 100);
            var settings = StillSettings(directory);
            settings.JoinMode = true;
            settings.WriteLogFile = true;

            var report = new Processor(settings).Run([first, second]);

            Assert.Equal(7, report.OutputFrames);
            // One log, named after the first input (the oracle wrote only capture_pipp_log.txt).
            Assert.True(File.Exists(Path.Combine(directory, "logs", "first_pipp_log.txt")));
            Assert.False(File.Exists(Path.Combine(directory, "logs", "second_pipp_log.txt")));
            // One output folder, named after the first input, holding the concatenated stream.
            var written = ReadStills(Path.Combine(directory, "first"));
            Assert.Equal(7, written.Count);
            Assert.Equal(firstFrames[0].Samples, written[0].Samples);
            Assert.Equal(secondFrames[0].Samples, written[4].Samples);
            Assert.False(Directory.Exists(Path.Combine(directory, "second")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JoinMode_OffWritesOneOutputPerInput()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (first, _) = WriteCapture(directory, "first.ser", 4);
            var (second, _) = WriteCapture(directory, "second.ser", 3, first: 100);
            var settings = StillSettings(directory);

            var report = new Processor(settings).Run([first, second]);

            Assert.Equal(7, report.OutputFrames);
            Assert.True(Directory.Exists(Path.Combine(directory, "first")));
            Assert.True(Directory.Exists(Path.Combine(directory, "second")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IncludeQualityValue_AppendsThePercentageToTheFrameName()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, _) = WriteCapture(directory, "capture.ser", 6);
            var settings = StillSettings(directory);
            settings.KeepFrames = 2;
            settings.IncludeQualityValue = true;

            new Processor(settings).Run([input]);

            var names = Directory.GetFiles(Path.Combine(directory, "capture")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(2, names.Length);
            // Oracle: 0_quality_100%.tif, 1_quality_76.32%.tif. Four significant digits, no padding.
            Assert.EndsWith("_quality_100%.tif", names[0]);
            Assert.StartsWith("1_quality_", names[1]);
            Assert.EndsWith("%.tif", names[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OutputFrameDrop_WritesOneFrameInN_AndRenumbers()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, source) = WriteCapture(directory, "capture.ser", 9);
            var settings = StillSettings(directory);
            settings.OutputFrameDrop = 2;

            var report = new Processor(settings).Run([input]);

            // The engine keeps the first frame and then skips two (oracle: -drop=2 wrote source
            // frames 1, 4, 7, 10, ... and renumbered them 01, 02, 03, ...).
            Assert.Equal(3, report.OutputFrames);
            var written = ReadStills(Path.Combine(directory, "capture"));
            Assert.Equal(3, written.Count);
            Assert.Equal(source[0].Samples, written[0].Samples);
            Assert.Equal(source[3].Samples, written[1].Samples);
            Assert.Equal(source[6].Samples, written[2].Samples);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnchorFeatureReject_DiscardsFramesWithoutAUsableFeature()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            // A flat capture has no anchor feature to track: the engine's -freject switch exists to
            // drop exactly those frames (the log carries "Frames discarded with no AFB found").
            string input = Path.Combine(directory, "flat.ser");
            using (var writer = new SerWriter(input, Width, Height, 1, 8))
            {
                for (int f = 0; f < 3; f++)
                {
                    var frame = new Frame(Width, Height, 1, 8);
                    for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = 100;
                    writer.Write(frame);
                }
            }

            var settings = StillSettings(directory);
            settings.SurfaceTracking = true;
            settings.EnableStabilisation = true;
            settings.AlignmentBox = new PixelRect(4, 3, 8, 6);
            settings.AnchorFeatureReject = true;
            settings.SaveRejectedFrames = true;

            var report = new Processor(settings).Run([input]);

            // The first frame is the reference and is always kept; the other two cannot be measured.
            Assert.Equal(1, report.OutputFrames);
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "flat"), "*.tif"));
            var rejected = Directory.GetFiles(Path.Combine(directory, "rejected", "flat_rejected"), "*.tif");
            Assert.Equal(2, rejected.Length);
            Assert.All(rejected, path => Assert.Contains("no_afb_", Path.GetFileName(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnchorFeatureReject_OffKeepsUnmeasurableFrames()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "flat.ser");
            using (var writer = new SerWriter(input, Width, Height, 1, 8))
            {
                for (int f = 0; f < 3; f++)
                {
                    var frame = new Frame(Width, Height, 1, 8);
                    for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = 100;
                    writer.Write(frame);
                }
            }

            var settings = StillSettings(directory);
            settings.SurfaceTracking = true;
            settings.EnableStabilisation = true;
            settings.AlignmentBox = new PixelRect(4, 3, 8, 6);

            var report = new Processor(settings).Run([input]);

            Assert.Equal(3, report.OutputFrames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void QualitySelection_PadsNamesToTheKeptCount()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var (input, _) = WriteCapture(directory, "capture.ser", 12);
            var settings = StillSettings(directory);
            settings.KeepFrames = 3;

            new Processor(settings).Run([input]);

            // Oracle: -q=3 on a 30-frame capture wrote 0.tif..2.tif, while -q=10 wrote 00.tif..09.tif.
            var names = Directory.GetFiles(Path.Combine(directory, "capture")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "0.tif", "1.tif", "2.tif" }, names);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
