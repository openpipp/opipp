using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Opipp.Core.Stacking;
using Xunit;

namespace Opipp.Core.Tests;

/// <summary>
/// End-to-end tests: build a container on disk, run <see cref="Processor"/> over it and read the
/// result back with an independent reader. These are the tests that prove the reconstruction can
/// actually process a capture, not just round-trip individual codecs.
/// </summary>
public sealed class EndToEndTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes a SER file whose frames contain a moving bright disc on a dark gradient.</summary>
    private static string WriteSyntheticSer(string dir, int width, int height, int frames, int bitDepth = 16)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, width, height, 1, bitDepth);
        int max = bitDepth == 8 ? 255 : 65535;
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, bitDepth);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int background = (int)(max * 0.15);
                    double dx = x - (width / 2 + f % 3), dy = y - height / 2;
                    bool inDisc = dx * dx + dy * dy <= 16;
                    frame.Samples[y * width + x] = (ushort)(inDisc ? max : background);
                }
            }
            writer.Write(frame);
        }
        return path;
    }

    [Fact]
    public void SerToFits_StackMean_WritesExpectedGeometryAndBrightness()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 32, 24, 5);
            string outDir = Path.Combine(dir, "out");

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".fits",
                Stack = StackMethod.Mean,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(5, report.InputFrames);
            Assert.Equal(1, report.OutputFrames);
            Assert.Single(report.OutputFiles);

            string output = report.OutputFiles[0];
            Assert.True(File.Exists(output), $"missing output {output}");

            using var reader = new FitsReader(output);
            var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(32, frame!.Width);
            Assert.Equal(24, frame.Height);

            // the synthetic disc is saturated, so the stacked maximum must be at full scale
            int peak = frame.Samples.Max();
            Assert.True(peak >= 65000, $"expected a saturated peak, got {peak}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OpippLayout_WritesNumberedSubfolderAndLogFile()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 4, bitDepth: 8);
            string output = Path.Combine(dir, "out");
            var settings = new PipelineSettings { OutputDirectory = output, OutputExtension = ".tif" };

            var report = new Processor(settings).Run([input]);

            var names = report.OutputFiles.Select(Path.GetFileName).ToArray();
            Assert.Equal(new[] { "1.tif", "2.tif", "3.tif", "4.tif" }, names);

            string log = File.ReadAllText(Path.Combine(output, "logs", "capture_pipp_log.txt"));
            Assert.Contains("PIPP (v2.5.9) Logfile", log);
            Assert.Contains("Source image files:", log);
            Assert.Contains("Total input frames: 4", log);
            Assert.Contains("Total output frames: 4:", log);
            Assert.Contains("Output Frame Type: Monochrome", log);
            Assert.Contains("Output Frame Format: TIF", log);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OpippLayout_PadsFrameNumbersToTheFrameCount()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 12, bitDepth: 8);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".tif",
            };

            var report = new Processor(settings).Run([input]);

            // measured against the original: 4 frames -> 1.tif, 12 frames -> 01.tif
            Assert.Equal("01.tif", Path.GetFileName(report.OutputFiles[0]));
            Assert.Equal("12.tif", Path.GetFileName(report.OutputFiles[^1]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SerToTiff_QualityLimit_KeepsRequestedFrameCount()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 6, bitDepth: 8);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".tif",
                KeepFrames = 3,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(6, report.InputFrames);
            Assert.Equal(3, report.OutputFrames);
            Assert.Equal(3, report.DiscardedByQuality);

            // Still-image formats write one numbered file per kept frame in a per-input folder,
            // numbered from 0 when quality selection ran (measured against the original engine).
            Assert.Equal(3, report.OutputFiles.Count);
            Assert.EndsWith(Path.Combine("capture", "0.tif"), report.OutputFiles[0]);
            Assert.EndsWith(Path.Combine("capture", "2.tif"), report.OutputFiles[2]);

            using var reader = new TiffReader(report.OutputFiles[0]);
            var first = reader.ReadFrame();
            Assert.NotNull(first);
            Assert.Equal(16, first!.Width);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Calibration_WithFullResolutionMaster_AppliesBeforeBinning()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 3, bitDepth: 8);
            var pixels = new ushort[16 * 16];
            Array.Fill(pixels, (ushort)10);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".tif",
                Binning = 2,
                MasterDark = new Opipp.Core.Frames.Frame(16, 16, 1, 8, pixels),
            };

            var report = new Processor(settings).Run([input]);

            // Before the fix, binning ran first, the 16x16 master no longer matched the 8x8 frame,
            // and every frame was recorded as "no object": zero output from a valid capture.
            Assert.Equal(3, report.OutputFrames);
            Assert.Equal(3, report.OutputFiles.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Calibration_MasterWithWrongGeometry_FailsAsConfigurationError()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 3, bitDepth: 8);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".tif",
                MasterDark = new Opipp.Core.Frames.Frame(8, 8, 1, 8, new ushort[64]),
            };

            var ex = Assert.Throws<Opipp.Core.Processing.CalibrationConfigurationException>(
                () => new Processor(settings).Run([input]));

            Assert.Contains("capture", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void QualityLimit_ZeroFrames_WritesNothingAndDoesNotThrow()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 6, bitDepth: 8);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".tif",
                KeepFrames = 0,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(6, report.InputFrames);
            Assert.Equal(0, report.OutputFrames);
            Assert.Equal(6, report.DiscardedByQuality);
            Assert.Empty(report.OutputFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void QualityPercent_RoundingToZero_WithContainerOutput_DoesNotThrow()
    {
        string dir = TempDir();
        try
        {
            string input = WriteSyntheticSer(dir, 16, 16, 6, bitDepth: 8);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                KeepPercent = 1.0,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(6, report.InputFrames);
            Assert.Equal(0, report.OutputFrames);
            Assert.Empty(report.OutputFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SerToSer_WithHistogramStretch_AppliesMeasuredGain()
    {
        string dir = TempDir();
        try
        {
            string input = Path.Combine(dir, "flat.ser");
            using (var writer = new SerWriter(input, 8, 8, 1, 8))
            {
                var frame = new Frame(8, 8, 1, 8);
                for (int i = 0; i < frame.Samples.Length; i++) frame.Samples[i] = 51;
                // a saturated tail of 25 samples pins the engine's measured white point at 255, so the
                // gain is exactly 2.04; on a flat field the same switch would normalise to 51
                for (int i = 0; i < HistogramStretch.TailSampleCount; i++) frame.Samples[i] = 255;
                writer.Write(frame);
            }

            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                StretchWhitePoint = 204,
            };

            var report = new Processor(settings).Run([input]);
            using var reader = new SerReader(report.OutputFiles[0]);
            var stretched = reader.ReadFrame();
            Assert.NotNull(stretched);
            // -hs=204 with a saturated tail is the engine's 2.04x gain: trunc(51*2.04) = 104
            Assert.All(stretched!.Samples.Skip(HistogramStretch.TailSampleCount), v => Assert.Equal(104, v));
            Assert.All(stretched.Samples.Take(HistogramStretch.TailSampleCount), v => Assert.Equal(255, v));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Calibration_SubtractDarkAndDivideFlat_PreserveGeometry()
    {
        var frame = new Frame(4, 4, 1, 16);
        var dark = new Frame(4, 4, 1, 16);
        var flat = new Frame(4, 4, 1, 16);
        for (int i = 0; i < 16; i++)
        {
            frame.Samples[i] = 1000;
            dark.Samples[i] = 100;
            flat.Samples[i] = 2000;
        }

        var corrected = Calibration.SubtractDark(frame, dark);
        Assert.All(corrected.Samples, v => Assert.Equal(900, v));

        var divided = Calibration.DivideFlat(corrected, flat);
        // flat is uniform, so a mean-normalised divide must return the input unchanged
        Assert.All(divided.Samples, v => Assert.InRange(v, 899, 901));
    }

    [Fact]
    public void MasterFrameBuilder_AveragesFilesAndWritesTiffAndPgm()
    {
        string dir = TempDir();
        try
        {
            // three single-frame SER files with constant values 10, 20 and 30 -> master 20
            var files = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                string path = Path.Combine(dir, $"cal{i}.ser");
                using var writer = new SerWriter(path, 8, 8, 1, 16);
                var frame = new Frame(8, 8, 1, 16);
                for (int s = 0; s < frame.Samples.Length; s++) frame.Samples[s] = (ushort)(10 * (i + 1));
                writer.Write(frame);
                files.Add(path);
            }

            string tiff = Path.Combine(dir, "master.tif");
            string pgm = Path.Combine(dir, "master.pgm");
            var percents = new List<int>();

            int stacked = MasterFrameBuilder.Build(files, null, tiff, pgm, 3, percents.Add);

            Assert.Equal(3, stacked);
            Assert.True(File.Exists(tiff), "master TIFF was not written");
            Assert.True(File.Exists(pgm), "master PGM was not written");
            Assert.Equal(100, percents[^1]);

            var master = PgmFile.Read(pgm);
            Assert.All(master.Samples, v => Assert.Equal(20, v));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MasterFrameBuilder_MismatchedGeometry_ReturnsFailure()
    {
        string dir = TempDir();
        try
        {
            string a = Path.Combine(dir, "a.ser");
            string b = Path.Combine(dir, "b.ser");
            using (var w = new SerWriter(a, 8, 8, 1, 8)) w.Write(new Frame(8, 8, 1, 8));
            using (var w = new SerWriter(b, 16, 16, 1, 8)) w.Write(new Frame(16, 16, 1, 8));

            int stacked = MasterFrameBuilder.Build([a, b], null, null, null, 0);

            Assert.Equal(-1, stacked);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
