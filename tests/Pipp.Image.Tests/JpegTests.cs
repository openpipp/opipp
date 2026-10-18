namespace Pipp.Image.Tests;

using System.Numerics;
using System.Text;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Xunit;

/// <summary>
/// End-to-end tests for <see cref="JpegBaselineDecoder"/>. The helper encoder below emits real
/// baseline JPEG bitstreams (marker segments, canonical Huffman codes, byte-stuffed entropy data,
/// optional restart markers and optional 16-bit quantisation tables) so the decoder is exercised
/// against complete files rather than hand-built structures.
/// </summary>
public class JpegTests
{
    [Fact]
    public void Probe_ReadsSof0Geometry_AndSkipsMetadataSegments()
    {
        byte[] rgb = Solid(24, 16, 90, 120, 150);
        byte[] jpeg = JpegWriter.Encode(24, 16, rgb, new JpegWriter.Options { ExtraMarkers = true });

        ImageInfo info = JpegBaselineDecoder.Probe(jpeg);
        Assert.Equal(24, info.Width);
        Assert.Equal(16, info.Height);
        Assert.Equal(8, info.BitDepth);
        Assert.Equal(3, info.Channels);

        using var decoder = new JpegBaselineDecoder(new MemoryStream(jpeg));
        Assert.Equal(24, decoder.Width);
        Assert.Equal(16, decoder.Height);
        Assert.Equal(3, decoder.Channels);
        Assert.Equal(1, decoder.FrameCount);
        Assert.Equal(BayerPattern.None, decoder.Bayer);

        Frame frame = Assert.IsType<Frame>(decoder.ReadFrame());
        Assert.Equal(24, frame.Width);
        Assert.Equal(16, frame.Height);
        Assert.Equal(3, frame.Channels);
        Assert.Equal(8, frame.BitDepth);
        Assert.Equal(24 * 16 * 3, frame.SampleCount);
        Assert.Null(decoder.ReadFrame());
    }

    [Fact]
    public void Probe_ThrowsInvalidData_WhenDataIsNotAJpeg()
    {
        Assert.Throws<InvalidDataException>(() => JpegBaselineDecoder.Probe(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(
            () => JpegBaselineDecoder.Probe(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Fact]
    public void Decode_DcOnlyBlocks_ProduceAUniformLevel()
    {
        // Every 8x8 block of a constant image carries only a DC coefficient, which exercises the
        // zero-AC shortcut in the inverse DCT and the +128 level shift.
        byte[] jpeg = JpegWriter.Encode(
            16,
            16,
            Solid(16, 16, 176, 176, 176),
            new JpegWriter.Options { Greyscale = true });

        Frame frame = JpegBaselineDecoder.Decode(jpeg);

        Assert.Equal(16, frame.Width);
        Assert.Equal(16, frame.Height);
        Assert.Equal(3, frame.Channels);
        Assert.All(frame.Samples, sample => Assert.Equal(176, sample));
    }

    [Fact]
    public void Decode_ConstructsCanonicalHuffmanTables_FromNonStandardSpecs()
    {
        // The custom tables force symbols that the default tables encode with different code words:
        // DC category 4 is assigned the two-bit code "10" and end-of-block is the one-bit code "0".
        var dc = new JpegWriter.HuffmanSpec(
            new byte[] { 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new byte[] { 0, 4 });
        var ac = new JpegWriter.HuffmanSpec(
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new byte[] { 0 });

        byte[] jpeg = JpegWriter.Encode(
            8,
            8,
            Solid(8, 8, 129, 129, 129),
            new JpegWriter.Options { Greyscale = true, DcLuma = dc, AcLuma = ac });

        Frame frame = JpegBaselineDecoder.Decode(jpeg);
        Assert.All(frame.Samples, sample => Assert.Equal(129, sample));
    }

    [Fact]
    public void Decode_FullBaselineColourJpeg_MatchesSourceWithinRounding()
    {
        const int width = 24;
        const int height = 16;
        byte[] rgb = Gradient(width, height);
        byte[] jpeg = JpegWriter.Encode(width, height, rgb, new JpegWriter.Options());

        Frame frame = JpegBaselineDecoder.Decode(jpeg);

        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(3, frame.Channels);
        Assert.Equal(8, frame.BitDepth);

        int maximumError = 0;
        for (int i = 0; i < rgb.Length; i++)
            maximumError = Math.Max(maximumError, Math.Abs(frame.Samples[i] - rgb[i]));

        Assert.True(maximumError <= 3, $"maximum per-sample error was {maximumError}");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void Decode_SupportsTheThreeSamplingLayouts(int horizontal, int vertical)
    {
        const int width = 24;
        const int height = 16;
        byte[] rgb = Gradient(width, height);
        byte[] jpeg = JpegWriter.Encode(
            width,
            height,
            rgb,
            new JpegWriter.Options { LumaH = horizontal, LumaV = vertical });

        using var decoder = new JpegBaselineDecoder(new MemoryStream(jpeg));
        Assert.Equal((horizontal, vertical), decoder.SamplingFactors[0]);
        Assert.Equal((1, 1), decoder.SamplingFactors[1]);
        Assert.Equal((1, 1), decoder.SamplingFactors[2]);

        Frame frame = Assert.IsType<Frame>(decoder.ReadFrame());
        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);

        long error = 0;
        for (int i = 0; i < rgb.Length; i++)
            error += Math.Abs(frame.Samples[i] - rgb[i]);

        double meanError = (double)error / rgb.Length;
        Assert.True(meanError <= 10.0, $"mean absolute error was {meanError:F2}");
    }

    [Fact]
    public void Decode_FourTwoZero_UpsamplesChromaBilinearly()
    {
        const int size = 32;
        byte[] rgb = Quadrants(size);
        byte[] jpeg = JpegWriter.Encode(size, size, rgb, new JpegWriter.Options { LumaH = 2, LumaV = 2 });

        Frame frame = JpegBaselineDecoder.Decode(jpeg);

        Assert.Equal(size, frame.Width);
        Assert.Equal(size, frame.Height);
        AssertPixelNear(frame, 8, 8, (200, 40, 40), 4);
        AssertPixelNear(frame, 24, 8, (40, 170, 70), 4);
        AssertPixelNear(frame, 8, 24, (60, 70, 200), 4);
        AssertPixelNear(frame, 24, 24, (140, 140, 140), 4);
    }

    [Fact]
    public void Decode_RestartMarkers_ResetDcPredictionAtEveryInterval()
    {
        const int width = 32;
        const int height = 24;
        byte[] rgb = Gradient(width, height);

        byte[] plain = JpegWriter.Encode(
            width,
            height,
            rgb,
            new JpegWriter.Options { LumaH = 2, LumaV = 2 });
        byte[] restarted = JpegWriter.Encode(
            width,
            height,
            rgb,
            new JpegWriter.Options { LumaH = 2, LumaV = 2, RestartInterval = 3 });

        int restartMarkers = 0;
        for (int i = 0; i < restarted.Length - 1; i++)
        {
            if (restarted[i] == 0xFF && restarted[i + 1] is >= 0xD0 and <= 0xD7)
                restartMarkers++;
        }

        Assert.True(restartMarkers >= 1, "the fixture must contain at least one RSTn marker");

        Frame expected = JpegBaselineDecoder.Decode(plain);
        Frame actual = JpegBaselineDecoder.Decode(restarted);

        Assert.Equal(expected.Samples, actual.Samples);
    }

    [Fact]
    public void Decode_AcceptsSixteenBitQuantisationTables()
    {
        const int width = 16;
        const int height = 16;
        byte[] rgb = Solid(width, height, 120, 140, 160);
        byte[] jpeg = JpegWriter.Encode(
            width,
            height,
            rgb,
            new JpegWriter.Options { WideQuantisation = true });

        Frame frame = JpegBaselineDecoder.Decode(jpeg);

        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        AssertPixelNear(frame, 0, 0, (120, 140, 160), 2);
        AssertPixelNear(frame, width - 1, height - 1, (120, 140, 160), 2);
    }

    [Fact]
    public void Decode_ThrowsNotSupported_ForProgressiveJpeg()
    {
        byte[] jpeg = JpegWriter.Encode(16, 16, Solid(16, 16, 80, 90, 100), new JpegWriter.Options());
        int sof = JpegWriter.FindMarker(jpeg, 0xC0);
        Assert.True(sof >= 0, "the fixture must contain a SOF0 marker");

        jpeg[sof + 1] = 0xC2; // SOF2: progressive DCT

        Assert.Throws<NotSupportedException>(() => JpegBaselineDecoder.Decode(jpeg));
    }

    [Fact]
    public void Decode_ThrowsInvalidData_ForTruncatedFiles()
    {
        byte[] jpeg = JpegWriter.Encode(24, 16, Gradient(24, 16), new JpegWriter.Options());

        Assert.Throws<InvalidDataException>(
            () => JpegBaselineDecoder.Decode(jpeg.AsSpan(0, jpeg.Length - 20).ToArray()));
        Assert.Throws<InvalidDataException>(
            () => JpegBaselineDecoder.Decode(jpeg.AsSpan(0, 10).ToArray()));
        Assert.Throws<InvalidDataException>(() => JpegBaselineDecoder.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void Decode_ThrowsInvalidData_ForAnOverSubscribedHuffmanTable()
    {
        byte[] jpeg = JpegWriter.Encode(8, 8, Solid(8, 8, 100, 100, 100), new JpegWriter.Options());

        // Seventeen one-bit codes cannot exist: the canonical table overflows after two.
        var payload = new List<byte> { 0x00, 17 };
        for (int i = 0; i < 15; i++)
            payload.Add(0);
        for (int i = 0; i < 17; i++)
            payload.Add(0);

        byte[] segment = JpegWriter.Segment(0xC4, payload.ToArray());
        var patched = new byte[jpeg.Length + segment.Length];
        jpeg.AsSpan(0, 2).CopyTo(patched);
        segment.CopyTo(patched, 2);
        jpeg.AsSpan(2).CopyTo(patched.AsSpan(2 + segment.Length));

        Assert.Throws<InvalidDataException>(() => JpegBaselineDecoder.Probe(patched));
    }

    [Fact]
    public void Decode_PillowEncodedFixture_MatchesTheSourceQuadrants()
    {
        // 32x32, 4:2:0, quality 90, written by Pillow (libjpeg-turbo). This checks the decoder
        // against a bitstream produced by an independent encoder rather than the helper below.
        byte[] jpeg = Convert.FromBase64String(PillowQuadrants);

        Frame frame = JpegBaselineDecoder.Decode(jpeg);

        Assert.Equal(32, frame.Width);
        Assert.Equal(32, frame.Height);
        Assert.Equal(3, frame.Channels);
        AssertPixelNear(frame, 8, 8, (200, 40, 40), 12);
        AssertPixelNear(frame, 24, 8, (40, 170, 70), 12);
        AssertPixelNear(frame, 8, 24, (60, 70, 200), 12);
        AssertPixelNear(frame, 24, 24, (140, 140, 140), 12);
    }

    private const string PillowQuadrants =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAgACADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDxSiiivzc/tU9WoooqD/JI8Zooor+/j9tPpuiiiv5MPqz/2Q==";

    private static void AssertPixelNear(Frame frame, int x, int y, (int R, int G, int B) expected, int tolerance)
    {
        int index = ((y * frame.Width) + x) * frame.Channels;
        Assert.InRange((int)frame.Samples[index], expected.R - tolerance, expected.R + tolerance);
        Assert.InRange((int)frame.Samples[index + 1], expected.G - tolerance, expected.G + tolerance);
        Assert.InRange((int)frame.Samples[index + 2], expected.B - tolerance, expected.B + tolerance);
    }

    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            rgb[(i * 3) + 0] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }

        return rgb;
    }

    private static byte[] Gradient(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ((y * width) + x) * 3;
                rgb[index + 0] = (byte)((x * 255) / Math.Max(1, width - 1));
                rgb[index + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                rgb[index + 2] = (byte)(((x + y) * 255) / Math.Max(1, width + height - 2));
            }
        }

        return rgb;
    }

    private static byte[] Quadrants(int size)
    {
        var rgb = new byte[size * size * 3];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                (byte r, byte g, byte b) = (x < size / 2, y < size / 2) switch
                {
                    (true, true) => ((byte)200, (byte)40, (byte)40),
                    (false, true) => ((byte)40, (byte)170, (byte)70),
                    (true, false) => ((byte)60, (byte)70, (byte)200),
                    _ => ((byte)140, (byte)140, (byte)140),
                };

                int index = ((y * size) + x) * 3;
                rgb[index + 0] = r;
                rgb[index + 1] = g;
                rgb[index + 2] = b;
            }
        }

        return rgb;
    }

    /// <summary>
    /// Minimal baseline JPEG writer used only by these tests. Quantisation tables are all ones, so
    /// the only loss is the rounding of the forward DCT coefficients.
    /// </summary>
    private static class JpegWriter
    {
        private static readonly byte[] ZigZag =
        {
            0, 1, 8, 16, 9, 2, 3, 10,
            17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        };

        private static readonly float[] Cosine = BuildCosineTable();

        private static readonly HuffmanSpec DcLumaTable = new(
            new byte[] { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 },
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 });

        private static readonly HuffmanSpec DcChromaTable = new(
            new byte[] { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 },
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 });

        private static readonly HuffmanSpec AcLumaTable = new(
            new byte[] { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D },
            new byte[]
            {
                0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
                0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
                0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
                0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
                0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
                0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
                0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
                0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
                0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
                0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
                0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
                0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
                0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
                0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
                0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
                0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
                0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
                0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
                0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
                0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
                0xF9, 0xFA,
            });

        private static readonly HuffmanSpec AcChromaTable = new(
            new byte[] { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 },
            new byte[]
            {
                0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
                0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
                0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
                0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
                0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34,
                0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
                0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38,
                0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
                0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
                0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
                0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96,
                0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
                0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4,
                0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
                0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2,
                0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
                0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9,
                0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
                0xF9, 0xFA,
            });

        public sealed class Options
        {
            public bool Greyscale { get; set; }

            public int LumaH { get; set; } = 1;

            public int LumaV { get; set; } = 1;

            public int RestartInterval { get; set; }

            public bool WideQuantisation { get; set; }

            public bool ExtraMarkers { get; set; }

            public HuffmanSpec? DcLuma { get; set; }

            public HuffmanSpec? AcLuma { get; set; }

            public HuffmanSpec? DcChroma { get; set; }

            public HuffmanSpec? AcChroma { get; set; }
        }

        public sealed record HuffmanSpec(byte[] Bits, byte[] Values);

        public static int FindMarker(byte[] jpeg, byte marker)
        {
            for (int i = 0; i + 1 < jpeg.Length; i++)
            {
                if (jpeg[i] == 0xFF && jpeg[i + 1] == marker)
                    return i;
            }

            return -1;
        }

        public static byte[] Segment(byte marker, byte[] payload)
        {
            using var segment = new MemoryStream();
            WriteMarker(segment, marker);
            segment.WriteByte((byte)((payload.Length + 2) >> 8));
            segment.WriteByte((byte)(payload.Length + 2));
            segment.Write(payload, 0, payload.Length);
            return segment.ToArray();
        }

        public static byte[] Encode(int width, int height, byte[] rgb, Options options)
        {
            ArgumentNullException.ThrowIfNull(rgb);
            if (rgb.Length != width * height * 3)
                throw new ArgumentException("RGB buffer size does not match the dimensions.", nameof(rgb));

            int componentCount = options.Greyscale ? 1 : 3;
            int lumaH = options.Greyscale ? 1 : options.LumaH;
            int lumaV = options.Greyscale ? 1 : options.LumaV;
            if (lumaH is < 1 or > 2 || lumaV is < 1 or > 2)
                throw new ArgumentOutOfRangeException(nameof(options), "luma sampling must be 1 or 2");

            int maxH = lumaH;
            int maxV = lumaV;
            int mcuColumns = (width + (8 * maxH) - 1) / (8 * maxH);
            int mcuRows = (height + (8 * maxV) - 1) / (8 * maxV);

            var horizontal = new int[componentCount];
            var vertical = new int[componentCount];
            for (int i = 0; i < componentCount; i++)
            {
                horizontal[i] = i == 0 ? lumaH : 1;
                vertical[i] = i == 0 ? lumaV : 1;
            }

            (byte[] y, byte[] cb, byte[] cr) = ToYCbCr(rgb, width, height);
            var planes = new byte[componentCount][];
            var planeWidths = new int[componentCount];

            for (int i = 0; i < componentCount; i++)
            {
                byte[] source = i switch
                {
                    0 => y,
                    1 => cb,
                    _ => cr,
                };

                planeWidths[i] = mcuColumns * horizontal[i] * 8;
                int planeHeight = mcuRows * vertical[i] * 8;
                planes[i] = BuildPlane(
                    source,
                    width,
                    height,
                    horizontal[i],
                    vertical[i],
                    maxH,
                    maxV,
                    planeWidths[i],
                    planeHeight);
            }

            HuffmanSpec dcLuma = options.DcLuma ?? DcLumaTable;
            HuffmanSpec acLuma = options.AcLuma ?? AcLumaTable;
            HuffmanSpec dcChroma = options.DcChroma ?? DcChromaTable;
            HuffmanSpec acChroma = options.AcChroma ?? AcChromaTable;

            var dcEncoders = new HuffmanEncoder[componentCount];
            var acEncoders = new HuffmanEncoder[componentCount];
            for (int i = 0; i < componentCount; i++)
            {
                dcEncoders[i] = new HuffmanEncoder(i == 0 ? dcLuma : dcChroma);
                acEncoders[i] = new HuffmanEncoder(i == 0 ? acLuma : acChroma);
            }

            using var output = new MemoryStream();
            WriteMarker(output, 0xD8);
            WriteSegment(output, 0xE0, JfifPayload());

            if (options.ExtraMarkers)
            {
                WriteSegment(output, 0xE2, Encoding.ASCII.GetBytes("PIPP test metadata"));
                WriteSegment(output, 0xFE, Encoding.ASCII.GetBytes("jpeg helper comment"));
            }

            WriteSegment(output, 0xDB, BuildDqt(options.WideQuantisation, componentCount == 1 ? 1 : 2));
            WriteSegment(output, 0xC0, BuildSof(width, height, horizontal, vertical));
            WriteSegment(output, 0xC4, BuildDht(componentCount, dcLuma, acLuma, dcChroma, acChroma));

            if (options.RestartInterval > 0)
            {
                int interval = options.RestartInterval;
                WriteSegment(output, 0xDD, new[] { (byte)(interval >> 8), (byte)interval });
            }

            WriteSegment(output, 0xDA, BuildSos(componentCount));
            WriteEntropy(
                output,
                planes,
                planeWidths,
                horizontal,
                vertical,
                mcuColumns,
                mcuRows,
                dcEncoders,
                acEncoders,
                options.RestartInterval);
            WriteMarker(output, 0xD9);

            return output.ToArray();
        }

        private static void WriteEntropy(
            Stream output,
            byte[][] planes,
            int[] planeWidths,
            int[] horizontal,
            int[] vertical,
            int mcuColumns,
            int mcuRows,
            HuffmanEncoder[] dcEncoders,
            HuffmanEncoder[] acEncoders,
            int restartInterval)
        {
            var writer = new BitWriter(output);
            var previousDc = new int[planes.Length];
            int mcuIndex = 0;
            int restartIndex = 0;

            for (int mcuRow = 0; mcuRow < mcuRows; mcuRow++)
            {
                for (int mcuColumn = 0; mcuColumn < mcuColumns; mcuColumn++)
                {
                    if (restartInterval > 0 && mcuIndex > 0 && mcuIndex % restartInterval == 0)
                    {
                        writer.Flush();
                        WriteMarker(output, (byte)(0xD0 + (restartIndex++ & 0x07)));
                        Array.Clear(previousDc);
                    }

                    for (int component = 0; component < planes.Length; component++)
                    {
                        for (int v = 0; v < vertical[component]; v++)
                        {
                            for (int h = 0; h < horizontal[component]; h++)
                            {
                                int blockX = (mcuColumn * horizontal[component]) + h;
                                int blockY = (mcuRow * vertical[component]) + v;
                                int[] quantised = QuantiseBlock(planes[component], planeWidths[component], blockX, blockY);
                                EncodeBlock(
                                    writer,
                                    quantised,
                                    dcEncoders[component],
                                    acEncoders[component],
                                    ref previousDc[component]);
                            }
                        }
                    }

                    mcuIndex++;
                }
            }

            writer.Flush();
        }

        private static int[] QuantiseBlock(byte[] plane, int planeWidth, int blockX, int blockY)
        {
            var samples = new float[64];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                    samples[(y * 8) + x] = plane[((blockY * 8) + y) * planeWidth + (blockX * 8) + x] - 128f;
            }

            var coefficients = new float[64];
            for (int v = 0; v < 8; v++)
            {
                for (int u = 0; u < 8; u++)
                {
                    float sum = 0f;
                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 8; x++)
                            sum += samples[(y * 8) + x] * Cosine[(x * 8) + u] * Cosine[(y * 8) + v];
                    }

                    coefficients[(v * 8) + u] = 0.25f * sum;
                }
            }

            var quantised = new int[64];
            for (int i = 0; i < 64; i++)
                quantised[i] = (int)MathF.Round(coefficients[i]);

            return quantised;
        }

        private static void EncodeBlock(
            BitWriter writer,
            int[] coefficients,
            HuffmanEncoder dc,
            HuffmanEncoder ac,
            ref int previousDc)
        {
            int difference = coefficients[0] - previousDc;
            previousDc = coefficients[0];

            int dcCategory = Category(difference);
            dc.Write(writer, (byte)dcCategory);
            WriteAmplitude(writer, difference, dcCategory);

            int last = 63;
            while (last > 0 && coefficients[ZigZag[last]] == 0)
                last--;

            int run = 0;
            for (int k = 1; k <= last; k++)
            {
                int value = coefficients[ZigZag[k]];
                if (value == 0)
                {
                    run++;
                    continue;
                }

                while (run > 15)
                {
                    ac.Write(writer, 0xF0);
                    run -= 16;
                }

                int category = Category(value);
                ac.Write(writer, (byte)((run << 4) | category));
                WriteAmplitude(writer, value, category);
                run = 0;
            }

            if (last < 63)
                ac.Write(writer, 0x00);
        }

        private static void WriteAmplitude(BitWriter writer, int value, int category)
        {
            if (category == 0)
                return;

            int encoded = value >= 0 ? value : value + (1 << category) - 1;
            writer.Write(encoded, category);
        }

        private static int Category(int value)
        {
            int magnitude = Math.Abs(value);
            return magnitude == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)magnitude);
        }

        private static byte[] BuildPlane(
            byte[] source,
            int width,
            int height,
            int h,
            int v,
            int maxH,
            int maxV,
            int planeWidth,
            int planeHeight)
        {
            int scaleX = maxH / h;
            int scaleY = maxV / v;
            var plane = new byte[planeWidth * planeHeight];

            for (int y = 0; y < planeHeight; y++)
            {
                for (int x = 0; x < planeWidth; x++)
                {
                    int sum = 0;
                    int count = 0;
                    for (int sy = 0; sy < scaleY; sy++)
                    {
                        int sourceY = Math.Min(height - 1, (y * scaleY) + sy);
                        for (int sx = 0; sx < scaleX; sx++)
                        {
                            int sourceX = Math.Min(width - 1, (x * scaleX) + sx);
                            sum += source[(sourceY * width) + sourceX];
                            count++;
                        }
                    }

                    plane[(y * planeWidth) + x] = (byte)((sum + (count / 2)) / count);
                }
            }

            return plane;
        }

        private static (byte[] Y, byte[] Cb, byte[] Cr) ToYCbCr(byte[] rgb, int width, int height)
        {
            int pixels = width * height;
            var y = new byte[pixels];
            var cb = new byte[pixels];
            var cr = new byte[pixels];

            for (int i = 0; i < pixels; i++)
            {
                int r = rgb[(i * 3) + 0];
                int g = rgb[(i * 3) + 1];
                int b = rgb[(i * 3) + 2];

                y[i] = ClampByte((0.299 * r) + (0.587 * g) + (0.114 * b));
                cb[i] = ClampByte(128.0 - (0.168736 * r) - (0.331264 * g) + (0.5 * b));
                cr[i] = ClampByte(128.0 + (0.5 * r) - (0.418688 * g) - (0.081312 * b));
            }

            return (y, cb, cr);
        }

        private static byte ClampByte(double value)
        {
            int rounded = (int)Math.Round(value);
            if (rounded < 0)
                return 0;
            return rounded > 255 ? (byte)255 : (byte)rounded;
        }

        private static byte[] JfifPayload() => new byte[]
        {
            0x4A, 0x46, 0x49, 0x46, 0x00,
            0x01, 0x01,
            0x00,
            0x00, 0x01,
            0x00, 0x01,
            0x00, 0x00,
        };

        private static byte[] BuildDqt(bool wide, int tableCount)
        {
            using var payload = new MemoryStream();
            for (int identifier = 0; identifier < tableCount; identifier++)
            {
                payload.WriteByte((byte)((wide ? 1 << 4 : 0) | identifier));
                for (int i = 0; i < 64; i++)
                {
                    if (wide)
                    {
                        payload.WriteByte(0x00);
                        payload.WriteByte(0x01);
                    }
                    else
                    {
                        payload.WriteByte(0x01);
                    }
                }
            }

            return payload.ToArray();
        }

        private static byte[] BuildSof(int width, int height, int[] horizontal, int[] vertical)
        {
            using var payload = new MemoryStream();
            payload.WriteByte(8);
            payload.WriteByte((byte)(height >> 8));
            payload.WriteByte((byte)height);
            payload.WriteByte((byte)(width >> 8));
            payload.WriteByte((byte)width);
            payload.WriteByte((byte)horizontal.Length);

            for (int i = 0; i < horizontal.Length; i++)
            {
                payload.WriteByte((byte)(i + 1));
                payload.WriteByte((byte)((horizontal[i] << 4) | vertical[i]));
                payload.WriteByte((byte)(i == 0 ? 0 : 1));
            }

            return payload.ToArray();
        }

        private static byte[] BuildDht(
            int componentCount,
            HuffmanSpec dcLuma,
            HuffmanSpec acLuma,
            HuffmanSpec dcChroma,
            HuffmanSpec acChroma)
        {
            using var payload = new MemoryStream();
            WriteHuffmanTable(payload, 0, 0, dcLuma);
            WriteHuffmanTable(payload, 1, 0, acLuma);
            if (componentCount > 1)
            {
                WriteHuffmanTable(payload, 0, 1, dcChroma);
                WriteHuffmanTable(payload, 1, 1, acChroma);
            }

            return payload.ToArray();
        }

        private static void WriteHuffmanTable(Stream payload, int tableClass, int identifier, HuffmanSpec spec)
        {
            payload.WriteByte((byte)((tableClass << 4) | identifier));
            payload.Write(spec.Bits, 0, 16);
            payload.Write(spec.Values, 0, spec.Values.Length);
        }

        private static byte[] BuildSos(int componentCount)
        {
            using var payload = new MemoryStream();
            payload.WriteByte((byte)componentCount);
            for (int i = 0; i < componentCount; i++)
            {
                payload.WriteByte((byte)(i + 1));
                int dc = i == 0 ? 0 : 1;
                int ac = i == 0 ? 0 : 1;
                payload.WriteByte((byte)((dc << 4) | ac));
            }

            payload.WriteByte(0);
            payload.WriteByte(63);
            payload.WriteByte(0);

            return payload.ToArray();
        }

        private static void WriteMarker(Stream stream, byte marker)
        {
            stream.WriteByte(0xFF);
            stream.WriteByte(marker);
        }

        private static void WriteSegment(Stream stream, byte marker, byte[] payload)
        {
            if (payload.Length + 2 > 0xFFFF)
                throw new ArgumentException("segment is too large", nameof(payload));

            WriteMarker(stream, marker);
            stream.WriteByte((byte)((payload.Length + 2) >> 8));
            stream.WriteByte((byte)(payload.Length + 2));
            stream.Write(payload, 0, payload.Length);
        }

        private static float[] BuildCosineTable()
        {
            var table = new float[64];
            for (int x = 0; x < 8; x++)
            {
                for (int u = 0; u < 8; u++)
                {
                    double value = Math.Cos(((2 * x) + 1) * u * Math.PI / 16.0);
                    if (u == 0)
                        value /= Math.Sqrt(2.0);
                    table[(x * 8) + u] = (float)value;
                }
            }

            return table;
        }

        private sealed class HuffmanEncoder
        {
            private readonly Dictionary<int, (int Code, int Length)> _codes = new();

            public HuffmanEncoder(HuffmanSpec spec)
            {
                int code = 0;
                int index = 0;
                for (int length = 1; length <= 16; length++)
                {
                    int count = spec.Bits[length - 1];
                    for (int i = 0; i < count; i++)
                        _codes[spec.Values[index++]] = (code + i, length);
                    code = (code + count) << 1;
                }

                if (index != spec.Values.Length)
                    throw new InvalidOperationException("Huffman specification is inconsistent.");
            }

            public void Write(BitWriter writer, int symbol)
            {
                if (!_codes.TryGetValue(symbol, out (int Code, int Length) entry))
                    throw new InvalidOperationException($"No Huffman code for symbol 0x{symbol:X2}.");
                writer.Write(entry.Code, entry.Length);
            }
        }

        private sealed class BitWriter
        {
            private readonly Stream _stream;
            private int _buffer;
            private int _count;

            public BitWriter(Stream stream)
            {
                _stream = stream;
            }

            public void Write(int value, int bits)
            {
                for (int i = bits - 1; i >= 0; i--)
                {
                    _buffer = (_buffer << 1) | ((value >> i) & 1);
                    _count++;
                    if (_count == 8)
                        Emit();
                }
            }

            public void Flush()
            {
                while ((_count & 7) != 0)
                    Write(1, 1);
            }

            private void Emit()
            {
                byte value = (byte)_buffer;
                _stream.WriteByte(value);
                if (value == 0xFF)
                    _stream.WriteByte(0x00);

                _buffer = 0;
                _count = 0;
            }
        }
    }
}
