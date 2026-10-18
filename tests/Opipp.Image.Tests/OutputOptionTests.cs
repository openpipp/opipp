namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Opipp.Core.Quality;
using Xunit;

/// <summary>
/// Pipeline wiring for the output-stage switches: <c>-save_dark</c>/<c>-save_flat</c>, <c>-rgb</c>,
/// <c>-convmono</c> and the <c>-qold</c>/<c>-qhisto</c> selector. The engine's file names and
/// locations were taken from <c>pipp_core.exe</c> on a 32x24 capture:
/// <c>&lt;od&gt;/logs/pipp_master_dark.tif</c> for <c>-save_dark</c>,
/// <c>&lt;od&gt;/logs/pipp_master_flat.tif</c> for a three-channel flat (and
/// <c>pipp_master_flat_dark.tif</c> for a one-channel flat), and <c>r_/g_/b_</c> prefixed files for
/// <c>-rgb</c>, with containers named <c>r_&lt;name&gt;..ser</c>.
/// </summary>
public class OutputOptionTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteMonoSer(string dir, int width, int height, int frames)
    {
        string path = Path.Combine(dir, "capture.ser");
        using var writer = new SerWriter(path, width, height, 1, 8);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 1, 8);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    frame.Samples[(y * width) + x] = (ushort)(x * 5 + y * 3 + f * 7);
                }
            }

            writer.Write(frame);
        }

        return path;
    }

    /// <summary>Two frames of an 8-bit colour SER: red ramp on frame 1, blue ramp on frame 2.</summary>
    private static string WriteColourSer(string dir, int width, int height, int frames)
    {
        string path = Path.Combine(dir, "colour.ser");
        using var writer = new SerWriter(path, width, height, 3, 8);
        for (int f = 0; f < frames; f++)
        {
            var frame = new Frame(width, height, 3, 8);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = ((y * width) + x) * 3;
                    frame.Samples[i] = (ushort)(20 + f * 40 + x);      // R
                    frame.Samples[i + 1] = (ushort)(60 + y);           // G
                    frame.Samples[i + 2] = (ushort)(100 + x + y);      // B
                }
            }

            writer.Write(frame);
        }

        return path;
    }

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {path}");
    }

    // ------------------------------------------------------------ calibration masters

    [Fact]
    public void SaveMasterDark_WritesLogsTiffWithTheLoadedMaster()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMonoSer(dir, 16, 12, 2);
            string outDir = Path.Combine(dir, "out");
            var master = new Frame(16, 12, 1, 8);
            for (int i = 0; i < master.Samples.Length; i++) master.Samples[i] = (ushort)(i % 251);

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                MasterDark = master,
                SaveMasterDark = true,
            };

            new Processor(settings).Run([input]);

            string path = Path.Combine(outDir, "logs", "pipp_master_dark.tif");
            Assert.True(File.Exists(path), $"missing {path}");
            Frame saved = ReadTiff(path);
            Assert.Equal(1, saved.Channels);
            Assert.Equal(8, saved.BitDepth);
            Assert.Equal(master.Samples, saved.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveMasterFlat_UsesTheEnginesOneChannelNameForAMonoMaster()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMonoSer(dir, 16, 12, 1);
            string outDir = Path.Combine(dir, "out");
            var flat = new Frame(16, 12, 1, 8);
            Array.Fill(flat.Samples, (ushort)200);

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                MasterFlat = flat,
                SaveMasterFlat = true,
            };

            new Processor(settings).Run([input]);

            // Engine quirk: a one-channel flat is saved as pipp_master_flat_dark.tif.
            Assert.True(File.Exists(Path.Combine(outDir, "logs", "pipp_master_flat_dark.tif")));
            Assert.False(File.Exists(Path.Combine(outDir, "logs", "pipp_master_flat.tif")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveMasterFlat_UsesThePlainNameForAColourMaster()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMonoSer(dir, 16, 12, 1);
            string outDir = Path.Combine(dir, "out");
            var flat = new Frame(16, 12, 3, 8);
            Array.Fill(flat.Samples, (ushort)180);

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                MasterFlat = flat,
                SaveMasterFlat = true,
            };

            new Processor(settings).Run([input]);

            string path = Path.Combine(outDir, "logs", "pipp_master_flat.tif");
            Assert.True(File.Exists(path));
            Assert.Equal(3, ReadTiff(path).Channels);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveMasterFlagsOff_WriteNoMasterFiles()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMonoSer(dir, 16, 12, 1);
            string outDir = Path.Combine(dir, "out");
            var master = new Frame(16, 12, 1, 8);

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                MasterDark = master,
                MasterFlat = master,
            };

            new Processor(settings).Run([input]);

            Assert.False(File.Exists(Path.Combine(outDir, "logs", "pipp_master_dark.tif")));
            Assert.False(File.Exists(Path.Combine(outDir, "logs", "pipp_master_flat.tif")));
            Assert.False(File.Exists(Path.Combine(outDir, "logs", "pipp_master_flat_dark.tif")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------ rgb split

    [Fact]
    public void SplitRgb_Stills_WriteOneFilePerChannelWithTheSourceValues()
    {
        string dir = TempDir();
        try
        {
            string input = WriteColourSer(dir, 8, 6, 2);
            string outDir = Path.Combine(dir, "out");

            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                SplitRgb = true,
            };

            var report = new Processor(settings).Run([input]);

            string frameDir = Path.Combine(outDir, "colour");
            foreach (string letter in new[] { "r", "g", "b" })
            {
                Assert.True(File.Exists(Path.Combine(frameDir, $"{letter}_1.tif")));
                Assert.True(File.Exists(Path.Combine(frameDir, $"{letter}_2.tif")));
            }

            Frame red = ReadTiff(Path.Combine(frameDir, "r_1.tif"));
            Frame green = ReadTiff(Path.Combine(frameDir, "g_1.tif"));
            Frame blue = ReadTiff(Path.Combine(frameDir, "b_1.tif"));
            Assert.Equal(1, red.Channels);
            Assert.Equal((ushort)(20 + 3), red.Samples[3]);   // R = 20 + x, x = 3
            Assert.Equal((ushort)(60 + 0), green.Samples[0]); // G = 60 + y
            Assert.Equal((ushort)(100 + 3), blue.Samples[3]); // B = 100 + x + y

            // One output frame per source frame even though three files were written.
            Assert.Equal(2, report.OutputFrames);
            Assert.Equal(6, report.OutputFiles.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SplitRgb_MonoInput_WritesThreeIdenticalChannelFiles()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMonoSer(dir, 8, 6, 1);
            string outDir = Path.Combine(dir, "out");

            new Processor(new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                SplitRgb = true,
            }).Run([input]);

            string frameDir = Path.Combine(outDir, "capture");
            Frame red = ReadTiff(Path.Combine(frameDir, "r_1.tif"));
            Frame green = ReadTiff(Path.Combine(frameDir, "g_1.tif"));
            Frame blue = ReadTiff(Path.Combine(frameDir, "b_1.tif"));

            Assert.Equal(red.Samples, green.Samples);
            Assert.Equal(red.Samples, blue.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SplitRgb_SerContainer_WritesTheEnginesDoubledDotNames()
    {
        string dir = TempDir();
        try
        {
            string input = WriteColourSer(dir, 8, 6, 2);
            string outDir = Path.Combine(dir, "out");

            new Processor(new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".ser",
                SplitRgb = true,
            }).Run([input]);

            foreach (string letter in new[] { "r", "g", "b" })
            {
                Assert.True(File.Exists(Path.Combine(outDir, $"{letter}_colour..ser")), $"missing {letter}_colour..ser");
            }

            using var reader = new SerReader(Path.Combine(outDir, "r_colour..ser"));
            Assert.Equal(1, reader.Channels);
            Frame frame = reader.ReadFrame()!;
            Assert.Equal((ushort)20, frame.Samples[0]);   // R of pixel 0
            Assert.Equal(2, reader.FrameCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------ convmono / quality

    [Theory]
    [InlineData(MonoMode.All, 30196)]
    [InlineData(MonoMode.RedGreen, 27423)]
    [InlineData(MonoMode.RedBlue, 45013)]
    [InlineData(MonoMode.GreenBlue, 24972)]
    public void ConvMono_16Bit_UsesTheEngineIntegerWeights(MonoMode mode, int expected)
    {
        // Oracle values for the 16-bit triple (42445, 19772, 51750); modes 4-6 divide by 886/413/701.
        var frame = new Frame(1, 1, 3, 16, [42445, 19772, 51750]);

        Frame result = MonoConvert.Apply(frame, mode);

        Assert.Equal((ushort)expected, result.Samples[0]);
    }

    [Theory]
    [InlineData(QualityAlgorithm.Ninox, "Old (As taken from Ninox)")]
    [InlineData(QualityAlgorithm.Histogram, "Histogram")]
    public void QualityAlgorithmSwitch_IsNamedInTheLog(QualityAlgorithm algorithm, string label)
    {
        string dir = TempDir();
        try
        {
            string input = WriteMonoSer(dir, 16, 12, 3);
            string outDir = Path.Combine(dir, "out");

            new Processor(new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                QualityAlgorithm = algorithm,
                KeepFrames = 1,
            }).Run([input]);

            string log = File.ReadAllText(Path.Combine(outDir, "logs", "capture_pipp_log.txt"));
            Assert.Contains($"Quality Algorithm: {label}", log);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
