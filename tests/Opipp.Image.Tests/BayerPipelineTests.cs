using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;

namespace Opipp.Image.Tests;

/// <summary>
/// Bayer handling end to end: a demosaiced frame carries no CFA pattern, an explicit <c>-db=N</c>
/// selection is authoritative, geometry keeps the marker in step with the pixels, and the saved
/// flat / flat-dark artifacts cannot overwrite each other.
/// </summary>
/// <remarks>
/// Mosaic fixtures encode the site colour in the sample (R = 10, G = 20, B = 30) so an output's
/// pixels can be checked against the pattern its header claims. Reference outputs are computed with
/// the processing primitives directly, which pins which pattern the pipeline actually used.
/// </remarks>
public class BayerPipelineTests
{
    private const ushort Red = 10;
    private const ushort Green = 20;
    private const ushort Blue = 30;

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-bayer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static int ColourOf(BayerPattern pattern, int x, int y) => pattern switch
    {
        BayerPattern.RGGB => ((x & 1, y & 1) switch { (0, 0) => 0, (1, 0) => 1, (0, 1) => 1, _ => 2 }),
        BayerPattern.GRBG => ((x & 1, y & 1) switch { (0, 0) => 1, (1, 0) => 0, (0, 1) => 2, _ => 1 }),
        BayerPattern.GBRG => ((x & 1, y & 1) switch { (0, 0) => 1, (1, 0) => 2, (0, 1) => 0, _ => 1 }),
        BayerPattern.BGGR => ((x & 1, y & 1) switch { (0, 0) => 2, (1, 0) => 1, (0, 1) => 1, _ => 0 }),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "not a Bayer pattern"),
    };

    private static ushort ValueOf(int colour) => colour switch { 0 => Red, 1 => Green, _ => Blue };

    /// <summary>A mosaic whose sample values say which colour each site carries.</summary>
    private static Frame Mosaic(int width, int height, BayerPattern pattern)
    {
        var frame = new Frame(width, height, 1, 8) { Bayer = pattern };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Samples[(y * width) + x] = ValueOf(ColourOf(pattern, x, y));
            }
        }

        return frame;
    }

    /// <summary>
    /// A SER capture whose header declares <paramref name="pattern"/>. The pixels are that mosaic
    /// unless <paramref name="dataPattern"/> says otherwise, which models a header that names the
    /// wrong pattern for the data it holds.
    /// </summary>
    private static string WriteMosaicSer(
        string dir, string name, int width, int height, BayerPattern pattern, int frames = 1,
        BayerPattern? dataPattern = null)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, width, height, 1, 8, frames, pattern);
        for (int f = 0; f < frames; f++)
        {
            writer.Write(Mosaic(width, height, dataPattern ?? pattern));
        }

        return path;
    }

    /// <summary>A mono TIFF holding a mosaic but no CFA tag - the "untagged mosaic" case.</summary>
    private static string WriteUntaggedMosaicTiff(string dir, string name, int width, int height)
    {
        string path = Path.Combine(dir, name);
        using var writer = new TiffWriter(path, width, height, 1, 8);
        writer.Write(Mosaic(width, height, BayerPattern.RGGB));
        return path;
    }

    /// <summary>The run's single image output, wherever the default <c>-opipp</c> layout put it.</summary>
    private static string SingleImage(string outputRoot, string extension) =>
        Directory.GetFiles(outputRoot, "*" + extension, SearchOption.AllDirectories)
            .Single(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static Frame ReadTiff(string path)
    {
        using var reader = new TiffReader(path);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {path}");
    }

    private static Frame ReadFits(string path)
    {
        using var reader = new FitsReader(path);
        return reader.ReadFrame() ?? throw new InvalidOperationException($"no frame in {path}");
    }

    // ---------------------------------------------------------------- A: debayer -> colour output

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DebayerToFits_WritesColourOutputWithNoCfaPattern(bool debayer, bool forceColour)
    {
        string dir = TempDir();
        try
        {
            string input = WriteMosaicSer(dir, "capture.ser", 8, 6, BayerPattern.RGGB);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".fits",
                Debayer = debayer,
                ForceColour = forceColour,
                DebayerPattern = BayerPattern.RGGB,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(1, report.OutputFrames);
            string path = SingleImage(outDir, ".fits");

            Frame output = ReadFits(path);
            Assert.Equal(3, output.Channels);
            Assert.Equal(BayerPattern.None, output.Bayer);
            Assert.Equal(BayerPattern.None, ReadFitsBayer(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static BayerPattern ReadFitsBayer(string path)
    {
        using var reader = new FitsReader(path);
        return reader.Bayer;
    }

    // ---------------------------------------------------------------- B: explicit pattern

    [Fact]
    public void ExplicitPattern_DebayersAnUntaggedMosaic()
    {
        string dir = TempDir();
        try
        {
            string input = WriteUntaggedMosaicTiff(dir, "capture.tif", 8, 6);
            Assert.Equal(BayerPattern.None, ReadTiff(input).Bayer); // the fixture really is untagged

            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                Debayer = true,
                DebayerPattern = BayerPattern.RGGB,
            };

            new Processor(settings).Run([input]);

            Frame output = ReadTiff(SingleImage(outDir, ".tif"));
            Frame expected = Debayer.ToColour(Mosaic(8, 6, BayerPattern.RGGB));

            Assert.Equal(3, output.Channels);
            Assert.Equal(expected.Samples, output.Samples);
            Assert.Equal(BayerPattern.None, output.Bayer);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExplicitPattern_OverridesTheContainerHeader()
    {
        string dir = TempDir();
        try
        {
            // The header says BGGR; the mosaic is RGGB. The explicit selection must win, which is
            // the only way to correct a wrong header.
            string input = WriteMosaicSer(
                dir, "capture.ser", 8, 6, BayerPattern.BGGR, dataPattern: BayerPattern.RGGB);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                Debayer = true,
                DebayerPattern = BayerPattern.RGGB,
            };

            new Processor(settings).Run([input]);

            Frame output = ReadTiff(SingleImage(outDir, ".tif"));

            // The same pixels, demosaiced with the pattern the selection names versus the one the
            // (wrong) header names: only the explicit selection recovers the true colours.
            Frame asRggb = Debayer.ToColour(Mosaic(8, 6, BayerPattern.RGGB));
            Frame lyingHeader = Mosaic(8, 6, BayerPattern.RGGB);
            lyingHeader.Bayer = BayerPattern.BGGR;
            Frame asBggr = Debayer.ToColour(lyingHeader);

            Assert.Equal(asRggb.Samples, output.Samples);
            Assert.NotEqual(asBggr.Samples, output.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WithoutAnExplicitSelection_TheHeaderPatternIsUsed()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMosaicSer(dir, "capture.ser", 8, 6, BayerPattern.BGGR);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                Debayer = true,
            };

            new Processor(settings).Run([input]);

            Frame output = ReadTiff(SingleImage(outDir, ".tif"));

            Assert.Equal(Debayer.ToColour(Mosaic(8, 6, BayerPattern.BGGR)).Samples, output.Samples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- C: geometry keeps the phase

    [Fact]
    public void FlipHorizontal_UpdatesTheSavedBayerPhase()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMosaicSer(dir, "capture.ser", 4, 6, BayerPattern.RGGB);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".fits",
                FlipX = true,
            };

            new Processor(settings).Run([input]);

            string path = SingleImage(outDir, ".fits");
            Frame output = ReadFits(path);

            // An even-width RGGB mosaic mirrored is GRBG, and the pixel at (0,0) is a green site.
            Assert.Equal(BayerPattern.GRBG, ReadFitsBayer(path));
            Assert.Equal(BayerPattern.GRBG, output.Bayer);
            Assert.Equal(Green, output.Samples[0]);
            Assert.Equal(Red, output.Samples[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CropWithAnOddOrigin_UpdatesTheSavedBayerPhase()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMosaicSer(dir, "capture.ser", 6, 6, BayerPattern.RGGB);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".fits",
                Crop = new PixelRect(1, 1, 4, 4),
            };

            new Processor(settings).Run([input]);

            string path = SingleImage(outDir, ".fits");
            Frame output = ReadFits(path);

            // (1,1) of an RGGB mosaic is blue, so the crop's first site is blue.
            Assert.Equal(BayerPattern.BGGR, ReadFitsBayer(path));
            Assert.Equal(Blue, output.Samples[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Binning_DropsTheBayerPatternFromTheSavedHeader()
    {
        string dir = TempDir();
        try
        {
            string input = WriteMosaicSer(dir, "capture.ser", 4, 4, BayerPattern.RGGB);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".fits",
                Binning = 2,
            };

            new Processor(settings).Run([input]);

            string path = SingleImage(outDir, ".fits");
            Frame output = ReadFits(path);

            // Every binned sample mixes a 2x2 block of all three colours: no mosaic is left.
            Assert.Equal(BayerPattern.None, ReadFitsBayer(path));
            Assert.Equal(BayerPattern.None, output.Bayer);
            Assert.Equal(1, output.Channels);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void JoinedInputsWithDifferentPatterns_AreRejectedWithATypedError()
    {
        string dir = TempDir();
        try
        {
            // One container header cannot describe two different mosaics, and the frames cannot be
            // rewritten without changing their pixels: refuse rather than write a lying header.
            string first = WriteMosaicSer(dir, "a.ser", 4, 6, BayerPattern.RGGB);
            string second = WriteMosaicSer(dir, "b.ser", 4, 6, BayerPattern.GRBG);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                JoinMode = true,
            };

            var ex = Assert.Throws<BayerPhaseException>(() => new Processor(settings).Run([first, second]));
            Assert.Contains("GRBG", ex.Message, StringComparison.Ordinal);
            Assert.Contains("RGGB", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- D: saved master names

    [Fact]
    public void SavingBothMasters_KeepsTheFlatAndTheFlatDarkUnderTheirOwnNames()
    {
        string dir = TempDir();
        try
        {
            string input = Path.Combine(dir, "capture.ser");
            using (var writer = new SerWriter(input, 2, 1, 1, 8, 1))
            {
                var science = new Frame(2, 1, 1, 8);
                Array.Fill(science.Samples, (ushort)100);
                writer.Write(science);
            }

            var flat = new Frame(2, 1, 1, 8, [150, 100]);
            var flatDark = new Frame(2, 1, 1, 8, [50, 0]);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".tif",
                MasterFlat = flat,
                MasterFlatDark = flatDark,
                SaveMasterFlat = true,
                SaveMasterFlatDark = true,
            };

            new Processor(settings).Run([input]);

            string flatPath = Path.Combine(outDir, "logs", "pipp_master_flat.tif");
            string flatDarkPath = Path.Combine(outDir, "logs", "pipp_master_flat_dark.tif");
            Assert.True(File.Exists(flatPath), $"missing {flatPath}");
            Assert.True(File.Exists(flatDarkPath), $"missing {flatDarkPath}");

            Assert.Equal([150, 100], ReadTiff(flatPath).Samples.Select(v => (int)v).ToArray());
            Assert.Equal([50, 0], ReadTiff(flatDarkPath).Samples.Select(v => (int)v).ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
