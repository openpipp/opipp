namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Opipp.Core.Quality;
using Xunit;

/// <summary>
/// Wiring tests for the AOI, calibration-detail and runtime-option batch. Every expectation comes
/// from an oracle run recorded in the notes: the <c>-qw</c> duplication boundaries (<c>n/4</c> and
/// <c>n/2</c>), the <c>-test_run</c> TIFF payloads named <c>.bmp</c>, the <c>-ser_bpp</c> override,
/// the <c>-no_ram_buff</c> source order, and the <c>-sdp</c> palette.
/// </summary>
public class SwitchBatchTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>One frame per input frame; the sample value encodes the source frame number.</summary>
    private static string WriteFrameNumbers(string dir, int frames, int width = 16, int height = 12)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, width, height, 1, 8);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, 8);
            Array.Fill(frame.Samples, (ushort)(40 + (f * 20)));
            writer.Write(frame);
        }

        return path;
    }

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {path}");
    }

    private static List<string> OutputNames(string frameDirectory) =>
        Directory.GetFiles(frameDirectory, "*.tif")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList()!;

    // ------------------------------------------------------------------ -qw

    [Fact]
    public void QualityWeighting_TriplicatesTopQuarterAndDuplicatesNextQuarter()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 8);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                KeepFrames = 8,
                QualityWeighting = true,
            };

            var report = new Processor(settings).Run([input]);

            // 8 selected: n/4 = 2 triplicated (+4), n/2 = 4 so two more duplicated (+2) -> 14.
            Assert.Equal(14, report.OutputFrames);
            List<string> names = OutputNames(Path.Combine(outDir, "capture"));
            Assert.Equal(14, names.Count);
            Assert.Equal(["0", "0a", "0b", "1", "1a", "1b", "2", "2a", "3", "3a", "4", "5", "6", "7"], names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void QualityWeighting_LogSeparatesUniqueAndDuplicatedCounts()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 4);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                KeepFrames = 4,
                QualityWeighting = true,
            };

            new Processor(settings).Run([input]);

            string log = File.ReadAllText(Path.Combine(outDir, "logs", "capture_pipp_log.txt"));
            Assert.Contains("Total unique output frames: 4:", log);
            Assert.Contains("Total duplicated output frames: 3:", log);
            Assert.Contains("Total output frames: 7:", log);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void QualityWeighting_WithoutQualitySelection_IsInert()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 4);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                QualityWeighting = true,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(4, report.OutputFrames);
            Assert.False(Directory.GetFiles(Path.Combine(outDir, "capture"), "*a.tif").Length > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------ -no_ram_buff

    [Fact]
    public void NoRamBuffers_DeliversSelectedFramesInSourceOrder()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 3);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                QualityAlgorithm = QualityAlgorithm.Histogram,
                KeepFrames = 3,
                NoRamBuffers = true,
            };

            new Processor(settings).Run([input]);

            // The histogram rank is not the source order; -no_ram_buff writes 40, 60, 80 anyway.
            string frameDir = Path.Combine(outDir, "capture");
            Assert.Equal(40, ReadTiff(Path.Combine(frameDir, "0.tif")).Samples[0]);
            Assert.Equal(60, ReadTiff(Path.Combine(frameDir, "1.tif")).Samples[0]);
            Assert.Equal(80, ReadTiff(Path.Combine(frameDir, "2.tif")).Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------ -aoi

    [Fact]
    public void AreaOfInterestCrop_CropsEveryDeliveredFrame()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 2, width: 20, height: 16);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                AreaOfInterest = new PixelRect(4, 3, 10, 8),
                AreaOfInterestCrop = true,
            };

            new Processor(settings).Run([input]);

            Frame frame = ReadTiff(Path.Combine(outDir, "capture", "1.tif"));
            Assert.Equal(10, frame.Width);
            Assert.Equal(8, frame.Height);
            Assert.Equal((ushort)40, frame.Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AreaOfInterestCrop_ClampsToTheFrame()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 1, width: 20, height: 16);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                AreaOfInterest = new PixelRect(15, 10, 50, 50),
                AreaOfInterestCrop = true,
            };

            new Processor(settings).Run([input]);

            Frame frame = ReadTiff(Path.Combine(outDir, "capture", "1.tif"));
            Assert.Equal(5, frame.Width);
            Assert.Equal(6, frame.Height);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------- -test_run

    [Fact]
    public void TestRun_WritesTiffPayloadsNamedBmpAndStopsAfterOneFrame()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 4);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                TestRun = true,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(1, report.InputFrames);
            Assert.Equal(1, report.OutputFrames);
            string inputPath = Path.Combine(outDir, "pipp_input_frame.bmp");
            string outputPath = Path.Combine(outDir, "pipp_output_frame.bmp");
            Assert.True(File.Exists(inputPath));
            Assert.True(File.Exists(outputPath));
            // Engine quirk: .bmp names with TIFF payloads.
            Assert.Equal("II*", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(inputPath), 0, 3));
            Assert.False(Directory.Exists(Path.Combine(outDir, "capture")));
            Assert.Equal(40, ReadTiff(inputPath).Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------- -ser_bpp

    [Fact]
    public void SerBitDepthOverride_ReadsASixteenBitFileAsEightBit()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "capture16.ser");
            using (var writer = new SerWriter(path, 8, 6, 1, 16))
            {
                var frame = new Frame(8, 6, 1, 16);
                Array.Fill(frame.Samples, (ushort)0x1234);
                writer.Write(frame);
            }

            using var source = FrameSourceFactory.Open(path, serBitDepthOverride: 8);
            Frame? read = source.ReadFrame();

            Assert.NotNull(read);
            Assert.Equal(8, read.BitDepth);
            // The 16-bit little-endian 0x1234 is consumed as two 8-bit samples (0x34, 0x12).
            Assert.Equal(0x34, read.Samples[0]);
            Assert.Equal(0x12, read.Samples[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------- flat-dark calibration

    [Fact]
    public void FlatDark_IsSubtractedBeforeTheFlatIsNormalised()
    {
        // flat 200/100, flat-dark 50/0 -> prepared 150/100, mean 125.
        var flat = new Frame(2, 1, 1, 8, [200, 100]);
        var flatDark = new Frame(2, 1, 1, 8, [50, 0]);
        var frame = new Frame(2, 1, 1, 8, [125, 125]);

        Frame prepared = Calibration.SubtractDark(flat, flatDark);
        Assert.Equal([150, 100], prepared.Samples.Select(v => (int)v).ToArray());

        Frame withDark = Calibration.DivideFlat(frame, prepared);
        Frame withoutDark = Calibration.DivideFlat(frame, flat);

        // 125 * 125 / 150 = 104, 125 * 125 / 100 = 156 (rounded).
        Assert.Equal([104, 156], withDark.Samples.Select(v => (int)v).ToArray());
        // Without the flat dark the flat's own mean (150) is used: 94, 188.
        Assert.Equal([94, 188], withoutDark.Samples.Select(v => (int)v).ToArray());
    }

    [Fact]
    public void SaveMasterFlatDark_WritesTheLoadedFlatDark()
    {
        string dir = TempDir();
        try
        {
            string input = WriteFrameNumbers(dir, 1);
            string outDir = Path.Combine(dir, "out");
            var flatDark = new Frame(16, 12, 1, 8);
            Array.Fill(flatDark.Samples, (ushort)33);

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                MasterFlatDark = flatDark,
                SaveMasterFlatDark = true,
            };

            new Processor(settings).Run([input]);

            string path = Path.Combine(outDir, "logs", "pipp_master_flat_dark.tif");
            Assert.True(File.Exists(path), $"missing {path}");
            Assert.Equal(33, ReadTiff(path).Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------ -sdp

    [Fact]
    public void ShowDetectedPixels_PaintsThresholdMaskBoxAndDimmedBackground()
    {
        var frame = new Frame(40, 30, 1, 8);
        for (int y = 9; y < 21; y++)
        {
            for (int x = 14; x < 26; x++)
            {
                frame.Samples[(y * 40) + x] = 200;
            }
        }

        Frame overlay = DetectionOverlay.Render(frame, 100, minSize: 5);

        Assert.Equal(3, overlay.Channels);
        Assert.Equal(8, overlay.BitDepth);

        int Pixel(int x, int y) => overlay.Samples[((y * 40) + x) * 3];

        // Background 0 -> (3*0+255)/4 = 63.
        Assert.Equal(63, Pixel(0, 0));
        // Mask pixel -> red.
        Assert.Equal(255, Pixel(20, 15));
        Assert.Equal(0, overlay.Samples[(((15 * 40) + 20) * 3) + 1]);
        // The dashed box sits one pixel outside the 12x12 object (top border row y=8).
        Assert.Equal(207, Pixel(13, 8));
    }

    [Fact]
    public void ShowDetectedPixels_NoBoxWhenTheObjectIsTooSmall()
    {
        var frame = new Frame(40, 30, 1, 8);
        frame.Samples[(15 * 40) + 20] = 200;

        Frame overlay = DetectionOverlay.Render(frame, 100, minSize: 5);

        bool hasBox = false;
        for (int i = 0; i < overlay.Samples.Length; i += 3)
        {
            if (overlay.Samples[i] == 207) hasBox = true;
        }

        Assert.False(hasBox);
        Assert.Equal(255, overlay.Samples[((15 * 40) + 20) * 3]);
    }
}
