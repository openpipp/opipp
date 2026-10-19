using System.Buffers.Binary;
using Opipp.Core.Alignment;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;
using Opipp.Core.Processing;
using Map = System.Func<int, int, (int X, int Y)?>;

namespace Opipp.Ser.Tests;

/// <summary>
/// The SER header's ColorID must describe the CFA phase of the frames the run actually delivers,
/// not the pattern named by the input file's header. Geometry moves the phase and binning/resize
/// destroys the mosaic, so a source-header copy labels the written samples with a pattern they do
/// not have.
/// </summary>
/// <remarks>
/// Every check decodes the written bytes directly (header fields at their documented offsets and
/// samples from offset 178), never through <see cref="SerReader"/>, so a writer and reader that
/// share the same mistake cannot agree their way to a pass. Expected pixels come from an index map
/// in the test and expected patterns from the test's own colour table, so neither expectation is
/// produced by the code under test.
/// </remarks>
public class SerCfaPhaseTests
{
    private const int HeaderSize = SerHeader.Size;

    // Site values that make the review's 8x6 RGGB top-left tile [53,107;107,191].
    private const ushort Red = 53;
    private const ushort Green = 107;
    private const ushort Blue = 191;

    public enum Op
    {
        FlipX,
        FlipY,
        Rotate90,
        Rotate180,
        Rotate270,
        CropOddOrigin,
        TranslateOdd,
    }

    // ---------------------------------------------------------------- geometry matrix

    public static IEnumerable<object[]> PhaseCases()
    {
        BayerPattern[] patterns = [BayerPattern.RGGB, BayerPattern.GRBG, BayerPattern.GBRG, BayerPattern.BGGR];
        (int Width, int Height)[] sizes = [(8, 6), (7, 5)];
        Op[] ops = [Op.FlipX, Op.FlipY, Op.Rotate90, Op.Rotate180, Op.Rotate270, Op.CropOddOrigin, Op.TranslateOdd];

        foreach (BayerPattern pattern in patterns)
        {
            foreach ((int width, int height) in sizes)
            {
                foreach (Op op in ops)
                {
                    yield return [pattern, width, height, op];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(PhaseCases))]
    public void Writer_ColorIdDescribesTheDeliveredFrame(BayerPattern pattern, int width, int height, Op op)
    {
        string dir = TempDir();
        try
        {
            Frame source = Mosaic(width, height, pattern);
            string capture = WriteCapture(dir, "capture.ser", width, height, pattern);
            (Frame delivered, Map map) = Apply(source, op);
            delivered.SourceName = capture;   // FrameSinkFactory reads this header for the text fields

            string path = Path.Combine(dir, "output.ser");
            using (IFrameSink sink = FrameSinkFactory.Create(path, delivered, frameCountHint: 1))
            {
                sink.Write(delivered);
            }

            RawSer raw = ReadRaw(path);
            BayerPattern expected = ExpectedPattern(pattern, delivered.Width, delivered.Height, map);

            Assert.Equal(delivered.Width, raw.Width);
            Assert.Equal(delivered.Height, raw.Height);
            Assert.Equal(ExpectedColorId(expected), raw.ColorId);       // the mapping under test
            Assert.Equal(expected, DerivePattern(raw, map));            // the tile really is that phase
            AssertPixels(raw, pattern, map);                            // and the tile is the transformed source
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- the review's repro

    /// <summary>
    /// The review's exact case: an 8x6 RGGB (ColorID 8) capture whose top-left tile is
    /// [53,107;107,191], flipped horizontally. The pixels were already correct; the header still
    /// said RGGB. GRBG (9) is the pattern the written samples have.
    /// </summary>
    [Fact]
    public void ReviewCase_8x6RggbFlipX_WritesGrbgAndTheFlippedTile()
    {
        string dir = TempDir();
        try
        {
            string input = WriteCapture(dir, "capture.ser", 8, 6, BayerPattern.RGGB);

            RawSer source = ReadRaw(input);
            Assert.Equal(8, source.ColorId);
            Assert.Equal(new ushort[] { 53, 107, 107, 191 }, TopLeftTile(source));

            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                FlipX = true,
            };

            var report = new Processor(settings).Run([input]);

            Assert.Equal(1, report.OutputFrames);
            RawSer written = ReadRaw(SingleSer(settings.OutputDirectory));
            Assert.Equal(9, written.ColorId);
            Assert.Equal(new ushort[] { 107, 53, 191, 107 }, TopLeftTile(written));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- mosaic destroyed

    [Fact]
    public void BinningFactor2_DestroysTheMosaic_HeaderIsMono()
    {
        string dir = TempDir();
        try
        {
            string input = WriteCapture(dir, "capture.ser", 8, 6, BayerPattern.RGGB);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                Binning = 2,
            };

            new Processor(settings).Run([input]);

            RawSer written = ReadRaw(SingleSer(settings.OutputDirectory));
            Assert.Equal(4, written.Width);
            Assert.Equal(3, written.Height);
            Assert.Equal(0, written.ColorId);   // 4x3 averages, no CFA lattice left
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resize_DestroysTheMosaic_HeaderIsMono()
    {
        string dir = TempDir();
        try
        {
            string input = WriteCapture(dir, "capture.ser", 8, 6, BayerPattern.RGGB);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                Resize = (4, 3),
            };

            new Processor(settings).Run([input]);

            RawSer written = ReadRaw(SingleSer(settings.OutputDirectory));
            Assert.Equal(4, written.Width);
            Assert.Equal(3, written.Height);
            Assert.Equal(0, written.ColorId);   // bilinear resampling mixes mosaic sites
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MonauralInput_StaysMono()
    {
        string dir = TempDir();
        try
        {
            string input = Path.Combine(dir, "mono.ser");
            var frame = new Frame(8, 6, 1, 8);
            Array.Fill(frame.Samples, (ushort)100);
            using (var writer = new SerWriter(input, 8, 6, 1, 8, frameCount: 1))
            {
                writer.Write(frame);
            }

            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
            };

            new Processor(settings).Run([input]);

            Assert.Equal(0, ReadRaw(SingleSer(settings.OutputDirectory)).ColorId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- colour unchanged

    [Fact]
    public void ColourFrame_KeepsTheColourId()
    {
        string dir = TempDir();
        try
        {
            string input = Path.Combine(dir, "rgb.ser");
            var frame = new Frame(4, 3, 3, 8);
            for (int p = 0; p < 4 * 3; p++)
            {
                frame.Samples[(p * 3) + 0] = 10;   // R
                frame.Samples[(p * 3) + 1] = 20;   // G
                frame.Samples[(p * 3) + 2] = 30;   // B
            }

            using (var writer = new SerWriter(input, 4, 3, 3, 8, frameCount: 1))
            {
                writer.Write(frame);
            }

            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
            };

            new Processor(settings).Run([input]);

            RawSer written = ReadRaw(SingleSer(settings.OutputDirectory));
            Assert.Equal(101, written.ColorId);    // BGR, the dialect the engine writes
            Assert.Equal(new ushort[] { 30, 20, 10 }, written.Samples[..3]);  // stored B,G,R
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- multi-frame contract

    [Fact]
    public void TwoFramesSharingTheDeliveredPhase_WriteOneHeaderForBoth()
    {
        string dir = TempDir();
        try
        {
            string input = WriteCapture(dir, "capture.ser", 8, 6, BayerPattern.RGGB, frames: 2);
            var settings = new PipelineSettings
            {
                OutputDirectory = Path.Combine(dir, "out"),
                OutputExtension = ".ser",
                FlipX = true,
            };

            new Processor(settings).Run([input]);

            RawSer written = ReadRaw(SingleSer(settings.OutputDirectory), expectedFrames: 2);
            Assert.Equal(9, written.ColorId);
            int frameSize = written.Width * written.Height;
            Assert.Equal(new ushort[] { 107, 53, 191, 107 },
                new[] { written.Samples[0], written.Samples[1], written.Samples[8], written.Samples[9] });
            Assert.Equal(new ushort[] { 107, 53, 191, 107 },
                new[]
                {
                    written.Samples[frameSize],
                    written.Samples[frameSize + 1],
                    written.Samples[frameSize + 8],
                    written.Samples[frameSize + 9],
                });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void JoinedMismatchedPhases_ThrowTypedErrorAndWriteNoContainer()
    {
        string dir = TempDir();
        try
        {
            string first = WriteCapture(dir, "a.ser", 8, 6, BayerPattern.RGGB);
            string second = WriteCapture(dir, "b.ser", 8, 6, BayerPattern.GRBG);
            string outDir = Path.Combine(dir, "out");
            var settings = new PipelineSettings
            {
                OutputDirectory = outDir,
                OutputExtension = ".ser",
                JoinMode = true,
            };

            Assert.Throws<BayerPhaseException>(() => new Processor(settings).Run([first, second]));

            // The error fires before the sink exists: no half-labelled container is left behind.
            string[] leftovers = Directory.Exists(outDir)
                ? Directory.GetFiles(outDir, "*.ser", SearchOption.AllDirectories)
                : [];
            Assert.Empty(leftovers);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static int ColourAt(BayerPattern pattern, int x, int y) => pattern switch
    {
        BayerPattern.RGGB => ((x & 1, y & 1) switch { (0, 0) => 0, (1, 0) => 1, (0, 1) => 1, _ => 2 }),
        BayerPattern.GRBG => ((x & 1, y & 1) switch { (0, 0) => 1, (1, 0) => 0, (0, 1) => 2, _ => 1 }),
        BayerPattern.GBRG => ((x & 1, y & 1) switch { (0, 0) => 1, (1, 0) => 2, (0, 1) => 0, _ => 1 }),
        BayerPattern.BGGR => ((x & 1, y & 1) switch { (0, 0) => 2, (1, 0) => 1, (0, 1) => 1, _ => 0 }),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "not a Bayer pattern"),
    };

    private static ushort ValueOf(int colour) => colour switch { 0 => Red, 1 => Green, _ => Blue };

    private static Frame Mosaic(int width, int height, BayerPattern pattern)
    {
        var frame = new Frame(width, height, 1, 8) { Bayer = pattern };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Samples[(y * width) + x] = ValueOf(ColourAt(pattern, x, y));
            }
        }

        return frame;
    }

    /// <summary>
    /// Applies the geometry the pipeline applies and returns the delivered frame together with an
    /// output-to-source index map. The map is the test's own statement of the transform, so the
    /// expected pixels and phase do not come from <see cref="GeometricTransforms"/>.
    /// </summary>
    private static (Frame Frame, Map Map) Apply(Frame source, Op op)
    {
        int w = source.Width, h = source.Height;
        switch (op)
        {
            case Op.FlipX:
            {
                Map map = (x, y) => (w - 1 - x, y);
                return (GeometricTransforms.FlipHorizontal(source), map);
            }

            case Op.FlipY:
            {
                Map map = (x, y) => (x, h - 1 - y);
                return (GeometricTransforms.FlipVertical(source), map);
            }

            case Op.Rotate90:
            {
                Map map = (x, y) => (y, h - 1 - x);
                return (GeometricTransforms.Rotate90Clockwise(source), map);
            }

            case Op.Rotate180:
            {
                Map map = (x, y) => (w - 1 - x, h - 1 - y);
                return (GeometricTransforms.Rotate180(source), map);
            }

            case Op.Rotate270:
            {
                Map map = (x, y) => (w - 1 - y, x);
                return (GeometricTransforms.Rotate270(source), map);
            }

            case Op.CropOddOrigin:
            {
                // Odd origin on both axes: the phase moves by one site in each direction.
                int cw = w - 3, ch = h - 3;
                Map map = (x, y) => (1 + x, 1 + y);
                return (GeometricTransforms.Crop(source, 1, 1, cw, ch), map);
            }

            case Op.TranslateOdd:
            {
                // The same steps Processor.ApplyGeometry takes for an odd whole-pixel shift.
                Frame shifted = FrameAligner.ShiftWithBackground(source, new AlignmentOffset(1, 1));
                shifted.Bayer = GeometricTransforms.TranslatePattern(source.Bayer, 1, 1);
                Map map = (x, y) => x >= 1 && y >= 1 ? (x - 1, y - 1) : null;
                return (shifted, map);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, "unknown geometry op");
        }
    }

    /// <summary>
    /// The one mosaic pattern whose site colours agree with the source over every mapped output
    /// pixel. Brute-forcing the four patterns is independent of the retagging code.
    /// </summary>
    private static BayerPattern ExpectedPattern(
        BayerPattern source, int outWidth, int outHeight, Func<int, int, (int X, int Y)?> map)
    {
        foreach (BayerPattern candidate in Patterns())
        {
            bool matches = true;
            for (int y = 0; y < outHeight && matches; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    if (map(x, y) is not (int sx, int sy))
                        continue;
                    if (ColourAt(candidate, x, y) != ColourAt(source, sx, sy))
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (matches)
                return candidate;
        }

        throw new InvalidOperationException("the transformed tile is not a mosaic of any known pattern");
    }

    /// <summary>Derives the phase from the bytes that were written, not from the source frame.</summary>
    private static BayerPattern DerivePattern(RawSer raw, Func<int, int, (int X, int Y)?> map)
    {
        foreach (BayerPattern candidate in Patterns())
        {
            bool matches = true;
            for (int y = 0; y < raw.Height && matches; y++)
            {
                for (int x = 0; x < raw.Width; x++)
                {
                    if (map(x, y) is null)
                        continue;
                    int value = raw.Samples[(y * raw.Width) + x];
                    if (value != ValueOf(ColourAt(candidate, x, y)))
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (matches)
                return candidate;
        }

        throw new InvalidOperationException("the written tile is not a mosaic of any known pattern");
    }

    private static IEnumerable<BayerPattern> Patterns() =>
        [BayerPattern.RGGB, BayerPattern.GRBG, BayerPattern.GBRG, BayerPattern.BGGR];

    /// <summary>The mapping from the task, spelled out here rather than taken from production code.</summary>
    private static int ExpectedColorId(BayerPattern pattern) => pattern switch
    {
        BayerPattern.None => 0,
        BayerPattern.RGGB => 8,
        BayerPattern.GRBG => 9,
        BayerPattern.GBRG => 10,
        BayerPattern.BGGR => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "unknown Bayer pattern"),
    };

    private static void AssertPixels(RawSer raw, BayerPattern source, Func<int, int, (int X, int Y)?> map)
    {
        for (int y = 0; y < raw.Height; y++)
        {
            for (int x = 0; x < raw.Width; x++)
            {
                if (map(x, y) is not (int sx, int sy))
                    continue;
                Assert.Equal(ValueOf(ColourAt(source, sx, sy)), raw.Samples[(y * raw.Width) + x]);
            }
        }
    }

    private static ushort[] TopLeftTile(RawSer raw) =>
        [raw.Samples[0], raw.Samples[1], raw.Samples[raw.Width], raw.Samples[raw.Width + 1]];

    private static string WriteCapture(
        string dir, string name, int width, int height, BayerPattern pattern, int frames = 1)
    {
        string path = Path.Combine(dir, name);
        using var writer = new SerWriter(path, width, height, 1, 8, frames, pattern);
        for (int f = 0; f < frames; f++)
        {
            writer.Write(Mosaic(width, height, pattern));
        }

        return path;
    }

    private static string SingleSer(string outputRoot) =>
        Directory.GetFiles(outputRoot, "*.ser", SearchOption.AllDirectories)
            .Single(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-ser-cfa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Header fields and samples read straight out of the file, with no SER reader involved.</summary>
    private sealed record RawSer(int ColorId, int Width, int Height, ushort[] Samples);

    private static RawSer ReadRaw(string path, int expectedFrames = 1)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= HeaderSize, $"{path} is shorter than a SER header");

        int colorId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(26));
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(30));
        int bitDepth = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(34));
        int frameCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(38));

        Assert.Equal(expectedFrames, frameCount);
        int channels = colorId is 16 or 17 or 100 or 101 ? 3 : 1;
        int bytesPerSample = bitDepth <= 8 ? 1 : 2;
        Assert.Equal(HeaderSize + (width * height * channels * bytesPerSample * frameCount), bytes.Length);

        var samples = new ushort[width * height * channels * frameCount];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = bytesPerSample == 1
                ? bytes[HeaderSize + i]
                : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(HeaderSize + (i * 2)));
        }

        return new RawSer(colorId, width, height, samples);
    }
}
