using System.Buffers.Binary;
using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;

namespace Opipp.Ser.Tests;

public class SerTests
{
    private const int HeaderSize = 178;

    // ---------- header ----------

    [Fact]
    public void Header_RoundTrips_AtDocumentedByteOffsets()
    {
        long utcTicks = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).Ticks;
        long localTicks = utcTicks + TimeSpan.FromMinutes(120).Ticks;

        var header = new SerHeader
        {
            LuId = 0x0A0B0C0D,
            ColorId = (int)SerColorId.Rgb,
            LittleEndian = true,
            Width = 641,
            Height = 481,
            BitDepth = 16,
            FrameCount = 7,
            Observer = "Observer 1",
            Telescope = "Scope 1",
            Instrument = "Camera 1",
            LocalTimeTicks = localTicks,
            UtcTicks = utcTicks,
        };

        byte[] bytes = header.ToBytes();

        Assert.Equal(178, bytes.Length);
        Assert.Equal("LUCAM-RECORDER", Encoding.ASCII.GetString(bytes, 0, 14));
        Assert.Equal(0x0A0B0C0D, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14)));
        Assert.Equal((int)SerColorId.Rgb, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(641, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(26)));
        Assert.Equal(481, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(30)));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(34)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(38)));

        Assert.Equal("Observer 1", ReadAscii(bytes, 42, 40));
        Assert.Equal((byte)0, bytes[42 + "Observer 1".Length]);
        Assert.Equal("Scope 1", ReadAscii(bytes, 82, 40));
        Assert.Equal("Camera 1", ReadAscii(bytes, 122, 40));
        Assert.Equal(localTicks, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(162)));
        Assert.Equal(utcTicks, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(170)));

        var parsed = SerHeader.Parse(bytes);

        Assert.Equal(header.LuId, parsed.LuId);
        Assert.Equal(header.ColorId, parsed.ColorId);
        Assert.True(parsed.LittleEndian);
        Assert.Equal(header.Width, parsed.Width);
        Assert.Equal(header.Height, parsed.Height);
        Assert.Equal(header.BitDepth, parsed.BitDepth);
        Assert.Equal(header.FrameCount, parsed.FrameCount);
        Assert.Equal(header.Observer, parsed.Observer);
        Assert.Equal(header.Telescope, parsed.Telescope);
        Assert.Equal(header.Instrument, parsed.Instrument);
        Assert.Equal(localTicks, parsed.LocalTimeTicks);
        Assert.Equal(utcTicks, parsed.UtcTicks);
        Assert.Equal(new DateTime(localTicks, DateTimeKind.Unspecified), parsed.LocalTime!.Value);
        Assert.Equal(new DateTime(utcTicks, DateTimeKind.Utc), parsed.UtcTime!.Value);
        Assert.Equal(120, parsed.LocalTimeUtcOffsetMinutes);
        Assert.Equal(bytes, parsed.ToBytes());
    }

    [Fact]
    public void Header_FixedWidthStrings_AreNulPadded_AndTruncatedAt40()
    {
        var header = new SerHeader
        {
            Width = 2,
            Height = 2,
            BitDepth = 8,
            FrameCount = 1,
            Observer = new string('A', 50),
            Telescope = "Scope",
            Instrument = "Cam",
        };

        byte[] bytes = header.ToBytes();

        Assert.Equal(new string('A', 40), Encoding.ASCII.GetString(bytes, 42, 40));
        Assert.Equal((byte)0, bytes[82 + "Scope".Length]); // the rest of the Telescope field is NUL padded
        Assert.Equal("Scope", SerHeader.Parse(bytes).Telescope);

        var parsed = SerHeader.Parse(bytes);
        Assert.Equal(new string('A', 40), parsed.Observer);
        Assert.Equal("Scope", parsed.Telescope);
        Assert.Equal("Cam", parsed.Instrument);

        Assert.Throws<ArgumentException>(() => header.WriteTo(new byte[177]));
    }

    [Fact]
    public void Header_Parse_RejectsBadFileId()
    {
        var bytes = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("NOT-A-SER-FILE", bytes);

        var ex = Assert.Throws<InvalidDataException>(() => SerHeader.Parse(bytes));
        Assert.Contains("LUCAM-RECORDER", ex.Message);
    }

    [Fact]
    public void Header_Parse_RejectsShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => SerHeader.Parse(new byte[HeaderSize - 1]));
    }

    [Theory]
    [InlineData(0, BayerPattern.None, 1)]
    [InlineData(8, BayerPattern.RGGB, 1)]
    [InlineData(9, BayerPattern.GRBG, 1)]
    [InlineData(10, BayerPattern.GBRG, 1)]
    [InlineData(11, BayerPattern.BGGR, 1)]
    [InlineData(16, BayerPattern.None, 3)]
    [InlineData(17, BayerPattern.None, 3)]
    public void ColorId_MapsToBayerPatternAndChannels(int colorId, BayerPattern expected, int channels)
    {
        var header = new SerHeader { Width = 2, Height = 2, BitDepth = 8, FrameCount = 1, ColorId = colorId };

        Assert.Equal(expected, header.Bayer);
        Assert.Equal(channels, header.Channels);
        Assert.Equal(channels == 3, header.IsColour);
        Assert.Equal((SerColorId)colorId, header.Color);
        Assert.True(SerHeader.IsSupportedColorId(colorId));

        if (expected != BayerPattern.None)
            Assert.Equal(colorId, SerHeader.ColorIdForBayer(expected));
    }

    // ---------- frame round-trips ----------

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(3, 8)]
    [InlineData(3, 16)]
    public void Frames_RoundTrip_ThroughFile(int channels, int bitDepth)
    {
        using var temp = new TempSer();
        const int width = 5, height = 3, count = 3;

        var header = SerHeader.Create(width, height, channels, bitDepth, count);
        Frame[] frames = Enumerable.Range(0, count)
            .Select(i => MakeFrame(width, height, channels, bitDepth, i + 1))
            .ToArray();

        WriteFrames(temp.Path, header, frames);

        using var reader = new SerReader(temp.Path);
        Assert.Equal(width, reader.Width);
        Assert.Equal(height, reader.Height);
        Assert.Equal(channels, reader.Channels);
        Assert.Equal(bitDepth, reader.BitDepth);
        Assert.Equal(count, reader.FrameCount);
        Assert.Equal(BayerPattern.None, reader.Bayer);

        for (int i = 0; i < count; i++)
        {
            Frame? got = reader.ReadFrame();
            Assert.NotNull(got);
            Assert.Equal(i, got!.Index);
            Assert.Equal(bitDepth, got.BitDepth);
            Assert.Equal(frames[i].Samples, got.Samples);
        }

        Assert.Null(reader.ReadFrame());
        Assert.Null(reader.ReadFrame());
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    [InlineData(BayerPattern.BGGR)]
    public void BayerFile_RoundTripsPatternAndSingleChannel(BayerPattern pattern)
    {
        using var temp = new TempSer();
        var header = SerHeader.Create(4, 2, 1, 16, 2, pattern);
        Frame first = MakeFrame(4, 2, 1, 16, 7);
        Frame second = MakeFrame(4, 2, 1, 16, 8);

        WriteFrames(temp.Path, header, first, second);

        using var reader = new SerReader(temp.Path);
        Assert.Equal(pattern, reader.Bayer);
        Assert.Equal(1, reader.Channels);
        Assert.Equal(SerHeader.ColorIdForBayer(pattern), reader.Header.ColorId);

        Frame? got = reader.ReadFrame();
        Assert.NotNull(got);
        Assert.Equal(pattern, got!.Bayer);
        Assert.Equal(first.Samples, got.Samples);
    }

    [Fact]
    public void Reader_InterpretsDeepBitDepthAs16BitSamples()
    {
        using var temp = new TempSer();
        var header = SerHeader.Create(2, 2, 1, 12, 1);
        var frame = new Frame(2, 2, 1, 16, new ushort[] { 0, 4095, 2048, 1 });

        WriteFrames(temp.Path, header, frame);

        using var reader = new SerReader(temp.Path);
        Assert.Equal(12, reader.PixelDepth);
        Assert.Equal(16, reader.BitDepth);

        Frame? got = reader.ReadFrame();
        Assert.NotNull(got);
        Assert.Equal(16, got!.BitDepth);
        Assert.Equal(frame.Samples, got.Samples);
    }

    [Fact]
    public void Reader_AllowsTrailingTimestampTrailer()
    {
        using var temp = new TempSer();
        var header = SerHeader.Create(2, 2, 1, 8, 1);
        byte[] file = header.ToBytes()
            .Concat(new Frame(2, 2, 1, 8).Samples.Select(s => (byte)s))
            .Concat(new byte[8]) // SER v4 style per-frame timestamp trailer
            .ToArray();
        File.WriteAllBytes(temp.Path, file);

        using (var reader = new SerReader(temp.Path))
        {
            Assert.NotNull(reader.ReadFrame());
            Assert.Null(reader.ReadFrame());
        }

        SerInfo info = SerProbe.Probe(temp.Path);
        Assert.False(info.IsTruncated);
        Assert.Equal(8L, info.ExtraBytes);
    }

    [Fact]
    public void Writer_StoresBgr_AndReaderRestoresRgb()
    {
        using var temp = new TempSer();
        var header = new SerHeader
        {
            Width = 2,
            Height = 1,
            BitDepth = 16,
            FrameCount = 1,
            ColorId = (int)SerColorId.Bgr,
        };
        var frame = new Frame(2, 1, 3, 16);
        ushort[] rgb = { 0x1111, 0x2222, 0x3333, 0x4444, 0x5555, 0x6666 };
        rgb.CopyTo(frame.Samples, 0);

        WriteFrames(temp.Path, header, frame);

        byte[] all = File.ReadAllBytes(temp.Path);
        byte[] expectedPayload =
        {
            0x33, 0x33, 0x22, 0x22, 0x11, 0x11, // pixel 0: B, G, R
            0x66, 0x66, 0x55, 0x55, 0x44, 0x44, // pixel 1: B, G, R
        };
        Assert.Equal(expectedPayload, all[HeaderSize..]);

        using var reader = new SerReader(temp.Path);
        Frame? got = reader.ReadFrame();
        Assert.NotNull(got);
        Assert.Equal(3, got!.Channels);
        Assert.Equal(rgb, got.Samples);
    }

    [Fact]
    public void Reader_DecodesBigEndianSamples()
    {
        using var temp = new TempSer();
        var header = new SerHeader
        {
            Width = 2,
            Height = 1,
            BitDepth = 16,
            FrameCount = 1,
            ColorId = (int)SerColorId.Mono,
            LittleEndian = false,
        };
        byte[] payload = { 0x12, 0x34, 0xAB, 0xCD };
        File.WriteAllBytes(temp.Path, header.ToBytes().Concat(payload).ToArray());

        using var reader = new SerReader(temp.Path);
        Assert.True(reader.IsBigEndian);

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(new ushort[] { 0x1234, 0xABCD }, frame!.Samples);
    }

    // ---------- seeking ----------

    [Fact]
    public void Reader_SeekAndReadFrameAt_AddressRequestedFrame()
    {
        using var temp = new TempSer();
        const int width = 3, height = 2, count = 5;
        Frame[] frames = Enumerable.Range(0, count)
            .Select(i => MakeFrame(width, height, 1, 16, i + 1))
            .ToArray();
        WriteFrames(temp.Path, SerHeader.Create(width, height, 1, 16, count), frames);

        using var reader = new SerReader(temp.Path);

        Frame? first = reader.ReadFrame();
        Frame? second = reader.ReadFrame();
        Assert.Equal(0, first!.Index);
        Assert.Equal(1, second!.Index);
        Assert.Equal(2, reader.Position);

        Frame? fourth = reader.ReadFrameAt(3);
        Assert.Equal(3, fourth!.Index);
        Assert.Equal(frames[3].Samples, fourth.Samples);
        Assert.Equal(4, reader.Position);

        reader.Seek(1);
        Frame? again = reader.ReadFrame();
        Assert.Equal(1, again!.Index);
        Assert.Equal(frames[1].Samples, again.Samples);

        Assert.Null(reader.ReadFrameAt(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Seek(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFrameAt(count + 1));
    }

    // ---------- truncation / overrides ----------

    [Fact]
    public void Reader_RejectsFileTruncatedMidFrame()
    {
        using var temp = new TempSer();
        const int width = 4, height = 4;
        WriteFrames(temp.Path, SerHeader.Create(width, height, 1, 8, 2),
            MakeFrame(width, height, 1, 8, 1), MakeFrame(width, height, 1, 8, 2));

        using (var stream = new FileStream(temp.Path, FileMode.Open, FileAccess.Write))
            stream.SetLength(HeaderSize + width * height + 5);

        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = new SerReader(temp.Path);
        });
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);

        SerInfo info = SerProbe.Probe(temp.Path);
        Assert.True(info.IsTruncated);
        Assert.Equal(2, info.FrameCount);
    }

    [Fact]
    public void Reader_RejectsFileShorterThanHeader()
    {
        using var temp = new TempSer();
        File.WriteAllBytes(temp.Path, new byte[100]);

        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = new SerReader(temp.Path);
        });
        Assert.Contains("header", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reader_BitDepthOverride_RepairsWrongHeaderDepth()
    {
        using var temp = new TempSer();
        const int width = 4, height = 2, count = 2;

        // Header claims 16 bpp but the payload is really one byte per sample.
        var header = new SerHeader
        {
            Width = width,
            Height = height,
            BitDepth = 16,
            FrameCount = count,
            ColorId = (int)SerColorId.Mono,
            LittleEndian = true,
        };
        byte[] payload = Enumerable.Range(10, width * height * count).Select(v => (byte)v).ToArray();
        File.WriteAllBytes(temp.Path, header.ToBytes().Concat(payload).ToArray());

        // Without the override the file is (correctly) too short for its declared frames.
        Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = new SerReader(temp.Path);
        });

        using var fixedReader = new SerReader(temp.Path, new SerReaderOptions { BitDepthOverride = 8 });
        Assert.Equal(8, fixedReader.BitDepth);
        Assert.Equal(8, fixedReader.PixelDepth);
        Assert.Equal(count, fixedReader.FrameCount);

        Frame? first = fixedReader.ReadFrame();
        Frame? second = fixedReader.ReadFrame();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(payload[..(width * height)], first!.Samples.Select(s => (byte)s).ToArray());
        Assert.Equal(payload[(width * height)..], second!.Samples.Select(s => (byte)s).ToArray());
        Assert.Null(fixedReader.ReadFrame());
    }

    [Fact]
    public void Reader_BitDepthOverride_OutsideRangeThrows()
    {
        using var temp = new TempSer();
        WriteFrames(temp.Path, SerHeader.Create(2, 2, 1, 8, 1), MakeFrame(2, 2, 1, 8, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var reader = new SerReader(temp.Path, new SerReaderOptions { BitDepthOverride = 7 });
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var reader = new SerReader(temp.Path, new SerReaderOptions { BitDepthOverride = 17 });
        });
    }

    // ---------- writer behaviour ----------

    [Fact]
    public void Writer_PatchesUnknownFrameCount_OnDispose()
    {
        using var temp = new TempSer();
        var header = SerHeader.Create(2, 2, 1, 8); // frame count = 0 / unknown
        Assert.Equal(0, header.FrameCount);

        using (var writer = new SerWriter(temp.Path, header))
        {
            writer.Write(MakeFrame(2, 2, 1, 8, 1));
            writer.Write(MakeFrame(2, 2, 1, 8, 2));
            Assert.Equal(2, writer.FramesWritten);
            Assert.Equal(0, header.FrameCount); // the caller's header is not mutated
        }

        SerInfo info = SerProbe.Probe(temp.Path);
        Assert.Equal(2, info.FrameCount);
        Assert.False(info.IsTruncated);
        Assert.Equal(HeaderSize + 2L * 2 * 2, info.FileSizeBytes!.Value);
    }

    [Fact]
    public void Writer_RejectsMoreFramesThanDeclared()
    {
        using var temp = new TempSer();
        using var writer = new SerWriter(temp.Path, SerHeader.Create(2, 2, 1, 8, frameCount: 1));

        writer.Write(MakeFrame(2, 2, 1, 8, 1));
        Assert.Throws<InvalidOperationException>(() => writer.Write(MakeFrame(2, 2, 1, 8, 2)));
    }

    [Fact]
    public void Writer_RejectsFramesThatDoNotMatchHeader()
    {
        using var temp = new TempSer();
        using var writer = new SerWriter(temp.Path, SerHeader.Create(2, 2, 1, 8, frameCount: 1));

        Assert.Throws<ArgumentException>(() => writer.Write(new Frame(3, 2, 1, 8)));
        Assert.Throws<ArgumentException>(() => writer.Write(new Frame(2, 2, 3, 8)));
        Assert.Throws<ArgumentException>(() => writer.Write(new Frame(2, 2, 1, 16)));
    }

    // ---------- byte-exact colour 16-bit file ----------

    [Fact]
    public void ColourRgb_ThreeFrames_MatchesHandComputedBytes()
    {
        using var temp = new TempSer();
        var header = new SerHeader
        {
            LuId = 0x0A0B0C0D,
            Width = 2,
            Height = 1,
            BitDepth = 16,
            FrameCount = 3,
            ColorId = (int)SerColorId.Rgb,
            LittleEndian = true,
        };

        var frames = new[]
        {
            new Frame(2, 1, 3, 16, new ushort[] { 0x0001, 0x0002, 0x0003, 0x0004, 0x0005, 0x0006 }),
            new Frame(2, 1, 3, 16, new ushort[] { 0x0102, 0x0103, 0x0104, 0x0105, 0x0106, 0x0107 }),
            new Frame(2, 1, 3, 16, new ushort[] { 0xFFFF, 0x0000, 0x1234, 0xABCD, 0x00FF, 0xFF00 }),
        };

        WriteFrames(temp.Path, header, frames);

        byte[] all = File.ReadAllBytes(temp.Path);
        Assert.Equal(HeaderSize + 3 * 12, all.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(26)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(30)));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(34)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(38)));
        Assert.Equal((int)SerColorId.Rgb, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(18)));
        Assert.Equal(0x0A0B0C0D, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(14)));

        // Hand computed little-endian samples, R G B per pixel:
        // frame 0: 1,2,3,4,5,6
        // frame 1: 258,259,260,261,262,263
        // frame 2: 65535,0,4660,43981,255,65280
        byte[] expectedPayload =
        {
            0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06, 0x00,
            0x02, 0x01, 0x03, 0x01, 0x04, 0x01, 0x05, 0x01, 0x06, 0x01, 0x07, 0x01,
            0xFF, 0xFF, 0x00, 0x00, 0x34, 0x12, 0xCD, 0xAB, 0xFF, 0x00, 0x00, 0xFF,
        };
        Assert.Equal(expectedPayload, all[HeaderSize..]);

        using var reader = new SerReader(temp.Path);
        for (int i = 0; i < frames.Length; i++)
        {
            Frame? got = reader.ReadFrame();
            Assert.NotNull(got);
            Assert.Equal(frames[i].Samples, got!.Samples);
        }

        Assert.Null(reader.ReadFrame());
    }

    // ---------- probe ----------

    [Fact]
    public void Probe_ReportsDimensionsColourDepthFramesBayerAndTimestamps()
    {
        using var temp = new TempSer();
        var local = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var utc = local - TimeSpan.FromHours(2);

        var header = SerHeader.Create(8, 6, 1, 12, 4, BayerPattern.GBRG);
        header.LocalTimeTicks = local.Ticks;
        header.UtcTicks = utc.Ticks;
        header.Observer = "Obs";
        header.Telescope = "Tel";
        header.Instrument = "Cam";
        header.LuId = 42;

        Frame[] frames = Enumerable.Range(0, 4)
            .Select(i => MakeFrame(8, 6, 1, 16, i + 1)) // 12-bit SER uses 16-bit sample words
            .ToArray();
        WriteFrames(temp.Path, header, frames);

        SerInfo info = SerProbe.Probe(temp.Path);

        Assert.Equal(temp.Path, info.SourcePath);
        Assert.Equal(8, info.Width);
        Assert.Equal(6, info.Height);
        Assert.Equal(1, info.Channels);
        Assert.Equal(12, info.BitDepth);
        Assert.Equal(4, info.FrameCount);
        Assert.Equal((int)SerColorId.BayerGbrg, info.ColorId);
        Assert.Equal(SerColorId.BayerGbrg, info.Color);
        Assert.Equal(BayerPattern.GBRG, info.BayerPattern);
        Assert.False(info.IsColour);
        Assert.True(info.IsSupported);
        Assert.False(info.IsTruncated);
        Assert.Equal(0L, info.ExtraBytes);
        Assert.Equal(HeaderSize + 4L * 8 * 6 * 2, info.FileSizeBytes!.Value);
        Assert.Equal(HeaderSize + 4L * 8 * 6 * 2, info.ExpectedFileSizeBytes);

        Assert.True(info.HasTimestamps);
        Assert.Equal(local, info.LocalTime!.Value);
        Assert.Equal(utc, info.UtcTime!.Value);
        Assert.Equal(120, info.LocalTimeUtcOffsetMinutes);
        Assert.Equal("Obs", info.Observer);
        Assert.Equal("Tel", info.Telescope);
        Assert.Equal("Cam", info.Instrument);
        Assert.Equal(42, info.LuId);
        Assert.True(info.LittleEndian);
    }

    [Fact]
    public void Probe_RestoresStreamPosition()
    {
        using var temp = new TempSer();
        WriteFrames(temp.Path, SerHeader.Create(2, 2, 1, 8, 1), MakeFrame(2, 2, 1, 8, 1));

        byte[] file = File.ReadAllBytes(temp.Path);
        using var stream = new MemoryStream();
        stream.Write(new byte[5]);
        stream.Write(file);
        stream.Position = 5;

        SerInfo info = SerProbe.Probe(stream, "x");

        Assert.Equal(5L, stream.Position);
        Assert.Equal("x", info.SourcePath);
        Assert.NotNull(info.FileSizeBytes);
        Assert.Equal((long)file.Length, info.FileSizeBytes!.Value);
    }

    // ---------- engine-compatible output headers ----------

    [Fact]
    public void Parse_ToBytes_PreservesSpacePaddedTextFieldsByteForByte()
    {
        // SharpCap-style headers pad the 40-byte text fields with spaces rather than NULs. Reading
        // trims them for display, but re-serializing must reproduce the original bytes exactly.
        var bytes = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("LUCAM-RECORDER", bytes);
        WriteInt32(bytes, 14, 0x1234);
        WriteInt32(bytes, 18, 8);
        WriteInt32(bytes, 22, 0);
        WriteInt32(bytes, 26, 640);
        WriteInt32(bytes, 30, 480);
        WriteInt32(bytes, 34, 8);
        WriteInt32(bytes, 38, 3);
        FillWithSpacePadding(bytes, 42, "Observer");
        FillWithSpacePadding(bytes, 82, "ZWO ASI183MC");
        FillWithSpacePadding(bytes, 122, "telescope");
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(162), 638022417177322949);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(170), 638022561177322949);

        SerHeader header = SerHeader.Parse(bytes);

        Assert.Equal("Observer", header.Observer);
        Assert.Equal("ZWO ASI183MC", header.Telescope);
        Assert.Equal("telescope", header.Instrument);
        Assert.Equal(640, header.Width);
        Assert.Equal(bytes, header.ToBytes());
        Assert.Equal(bytes, header.Clone().ToBytes());
    }

    [Fact]
    public void CreateOutput_CarriesCaptureMetadata_AndResetsEngineIgnoredFields()
    {
        SerHeader source = MakeSpacePaddedSource();

        SerHeader output = SerHeader.CreateOutput(320, 240, 1, 8, 5, source);

        Assert.Equal(0, output.LuId);                 // the engine always writes LuID 0
        Assert.Equal((int)SerColorId.BayerRggb, output.ColorId);
        Assert.False(output.LittleEndian);            // the engine always writes big-endian samples
        Assert.Equal(320, output.Width);
        Assert.Equal(240, output.Height);
        Assert.Equal(8, output.BitDepth);
        Assert.Equal(5, output.FrameCount);
        Assert.Equal(0, output.LocalTimeTicks);       // the engine zeroes both timestamps
        Assert.Equal(0, output.UtcTicks);
        Assert.Equal(0, output.LocalTimeUtcOffsetMinutes);

        // Text fields are copied as raw bytes, so the source's space padding survives.
        byte[] outputBytes = output.ToBytes();
        Assert.Equal(source.ToBytes().AsSpan(42, 40).ToArray(), outputBytes.AsSpan(42, 40).ToArray());
        Assert.Equal(source.ToBytes().AsSpan(82, 40).ToArray(), outputBytes.AsSpan(82, 40).ToArray());
        Assert.Equal(source.ToBytes().AsSpan(122, 40).ToArray(), outputBytes.AsSpan(122, 40).ToArray());
        Assert.Equal("Observer", output.Observer);
        Assert.Equal("ZWO ASI183MC", output.Telescope);
        Assert.Equal("telescope", output.Instrument);
    }

    [Theory]
    [InlineData(1, 0)]    // non-SER mono input falls back to ColorID 0
    [InlineData(3, 101)]  // colour output is BGR 101, never RGB 100
    public void CreateOutput_WithoutSource_UsesEngineFallbacks(int channels, int expectedColorId)
    {
        SerHeader output = SerHeader.CreateOutput(64, 48, channels, 8, 1, source: null);

        Assert.Equal(expectedColorId, output.ColorId);
        Assert.Equal(0, output.LuId);
        Assert.False(output.LittleEndian);
        Assert.Equal(string.Empty, output.Observer);
        Assert.Equal(string.Empty, output.Telescope);
        Assert.Equal(string.Empty, output.Instrument);
        Assert.Equal(0, output.LocalTimeTicks);
        Assert.Equal(0, output.UtcTicks);
    }

    [Theory]
    [InlineData(100, 1, 0)]     // colour source split into channels -> plain mono (engine writes 100; not reproduced)
    [InlineData(101, 1, 0)]
    [InlineData(8, 1, 8)]       // Bayer id carries through a mono output
    [InlineData(8, 3, 101)]     // debayered output is BGR
    [InlineData(0, 3, 101)]
    public void CreateOutput_ColorIdFollowsOutputChannelsAndSource(int sourceColorId, int channels, int expected)
    {
        var source = new SerHeader { ColorId = sourceColorId, Width = 32, Height = 24, BitDepth = 8, FrameCount = 4 };

        Assert.Equal(expected, SerHeader.CreateOutput(32, 24, channels, 8, 4, source).ColorId);
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(12, 16)]  // the engine re-labels every two-byte sample depth as 16
    [InlineData(16, 16)]
    public void CreateOutput_NormalisesBitDepthTo8Or16(int sourceDepth, int expected)
    {
        var source = new SerHeader { ColorId = 0, Width = 32, Height = 24, BitDepth = sourceDepth, FrameCount = 1 };

        SerHeader output = SerHeader.CreateOutput(32, 24, 1, sourceDepth, 1, source);

        Assert.Equal(expected, output.BitDepth);
        Assert.Equal(expected <= 8 ? 1 : 2, output.BytesPerSample);
    }

    [Fact]
    public void Writer_WritesBigEndianSamples_WhenHeaderIsBigEndian()
    {
        using var temp = new TempSer();
        var header = new SerHeader
        {
            Width = 2,
            Height = 1,
            BitDepth = 16,
            FrameCount = 1,
            ColorId = (int)SerColorId.Mono,
            LittleEndian = false,
        };
        var frame = new Frame(2, 1, 1, 16, new ushort[] { 0x1234, 0x00FF });

        WriteFrames(temp.Path, header, frame);

        byte[] all = File.ReadAllBytes(temp.Path);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(22))); // header says big-endian
        Assert.Equal(new byte[] { 0x12, 0x34, 0x00, 0xFF }, all[HeaderSize..]);

        using var reader = new SerReader(temp.Path);
        Assert.True(reader.IsBigEndian);
        Assert.Equal(frame.Samples, reader.ReadFrame()!.Samples);
    }

    [Fact]
    public void Writer_WritesBigEndianBgrSamples_WhenHeaderIsBigEndian()
    {
        using var temp = new TempSer();
        var header = new SerHeader
        {
            Width = 1,
            Height = 1,
            BitDepth = 16,
            FrameCount = 1,
            ColorId = (int)SerColorId.Bgr,
            LittleEndian = false,
        };
        var frame = new Frame(1, 1, 3, 16, new ushort[] { 0x0102, 0x0304, 0x0506 }); // R, G, B

        WriteFrames(temp.Path, header, frame);

        byte[] all = File.ReadAllBytes(temp.Path);
        Assert.Equal(new byte[] { 0x05, 0x06, 0x03, 0x04, 0x01, 0x02 }, all[HeaderSize..]);
        Assert.Equal(frame.Samples, new SerReader(temp.Path).ReadFrame()!.Samples);
    }

    [Fact]
    public void FrameSinkFactory_CarriesSourceHeaderMetadata_IntoSerOutput()
    {
        using var source = new TempSer();
        SerHeader sourceHeader = MakeSpacePaddedSource();
        sourceHeader.Width = 4;
        sourceHeader.Height = 3;
        sourceHeader.FrameCount = 2;
        WriteFrames(source.Path, sourceHeader, MakeFrame(4, 3, 1, 8, 1), MakeFrame(4, 3, 1, 8, 2));

        using var output = new TempSer();
        var template = new Frame(4, 3, 1, 8) { SourceName = source.Path };
        using (IFrameSink sink = FrameSinkFactory.Create(output.Path, template, frameCountHint: 1))
            sink.Write(template);

        SerHeader written = SerProbe.Probe(output.Path).Header;
        Assert.Equal(0, written.LuId);
        Assert.Equal((int)SerColorId.BayerRggb, written.ColorId);
        Assert.False(written.LittleEndian);
        Assert.Equal(1, written.FrameCount);
        Assert.Equal("Observer", written.Observer);
        Assert.Equal("ZWO ASI183MC", written.Telescope);
        Assert.Equal("telescope", written.Instrument);
        Assert.Equal(0, written.LocalTimeTicks);
        Assert.Equal(0, written.UtcTicks);
    }

    [Fact]
    public void FrameSinkFactory_UsesFallbacks_WhenTemplateHasNoSerSource()
    {
        using var output = new TempSer();
        var template = new Frame(4, 3, 3, 8) { SourceName = null };

        using (IFrameSink sink = FrameSinkFactory.Create(output.Path, template, frameCountHint: 1))
            sink.Write(template);

        SerHeader written = SerProbe.Probe(output.Path).Header;
        Assert.Equal((int)SerColorId.Bgr, written.ColorId);
        Assert.False(written.LittleEndian);
        Assert.Equal(0, written.LuId);
        Assert.Equal(string.Empty, written.Observer);
        Assert.Equal(0, written.LocalTimeTicks);
    }

    // ---------- helpers ----------

    /// <summary>A header whose text fields are padded with spaces, like SharpCap writes them.</summary>
    private static SerHeader MakeSpacePaddedSource()
    {
        var bytes = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("LUCAM-RECORDER", bytes);
        WriteInt32(bytes, 14, 0x1234);   // LuID the engine discards
        WriteInt32(bytes, 18, (int)SerColorId.BayerRggb);
        WriteInt32(bytes, 22, 1);        // little-endian in the source
        WriteInt32(bytes, 26, 640);
        WriteInt32(bytes, 30, 480);
        WriteInt32(bytes, 34, 8);
        WriteInt32(bytes, 38, 30);
        FillWithSpacePadding(bytes, 42, "Observer");
        FillWithSpacePadding(bytes, 82, "ZWO ASI183MC");
        FillWithSpacePadding(bytes, 122, "telescope");
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(162), 638022417177322949);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(170), 638022561177322949);
        return SerHeader.Parse(bytes);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), value);

    private static void FillWithSpacePadding(byte[] buffer, int offset, string value)
    {
        for (int i = 0; i < SerHeader.TextLength; i++)
            buffer[offset + i] = (byte)' ';
        Encoding.ASCII.GetBytes(value, buffer.AsSpan(offset, value.Length));
    }

    private static Frame MakeFrame(int width, int height, int channels, int bitDepth, int seed)
    {
        var frame = new Frame(width, height, channels, bitDepth);
        int mask = bitDepth == 8 ? 0xFF : 0xFFFF;
        for (int i = 0; i < frame.SampleCount; i++)
            frame.Samples[i] = (ushort)((seed * 4099 + i * 131 + (i % 7) * 17) & mask);
        return frame;
    }

    private static void WriteFrames(string path, SerHeader header, params Frame[] frames)
    {
        using var writer = new SerWriter(path, header);
        foreach (Frame frame in frames)
            writer.Write(frame);
    }

    private static string ReadAscii(byte[] buffer, int offset, int length)
    {
        int nul = Array.IndexOf(buffer, (byte)0, offset, length);
        int end = nul < 0 ? offset + length : nul;
        return Encoding.ASCII.GetString(buffer, offset, end - offset);
    }

    private sealed class TempSer : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pipp-ser-{Guid.NewGuid():N}.ser");

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }
}
