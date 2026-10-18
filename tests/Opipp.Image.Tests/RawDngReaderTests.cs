using System.Buffers.Binary;
using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Image.Tests;

/// <summary>
/// Tests for <see cref="RawDngReader"/> against hand-built baseline DNGs. The fixtures are written
/// here rather than checked in: a DNG is a TIFF with a CFA tag, so the builder below produces known
/// sample values whose expected normalised output can be stated exactly.
/// </summary>
public class RawDngReaderTests
{
    // ---------------------------------------------------------------- fixtures

    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;

    /// <summary>
    /// Builds a minimal single-IFD DNG. Samples come from <paramref name="sample"/>; strips are laid
    /// out contiguously after the 8-byte TIFF header.
    /// </summary>
    private static byte[] BuildDng(
        int width,
        int height,
        int bps = 16,
        bool bigEndian = false,
        int[]? blackLevels = null,
        int? whiteLevel = null,
        bool includeWhiteLevel = true,
        byte[]? cfa = null,
        ushort cfaRows = 2,
        ushort cfaColumns = 2,
        Func<int, int, int>? sample = null,
        int stripCount = 1,
        bool includeDngVersion = true,
        ushort compression = 1,
        ushort photometric = 32803,
        uint newSubfileType = 0,
        bool includePlaneColor = true,
        bool includeBlackRepeatDim = true)
    {
        sample ??= (x, y) => 0;
        cfa ??= [0, 1, 1, 2];
        int resolvedWhite = whiteLevel ?? ((1 << bps) - 1);

        int rowBytes = (width * bps + 7) / 8;
        int rowsPerStrip = Math.Max(1, (height + stripCount - 1) / stripCount);
        int actualStrips = (height + rowsPerStrip - 1) / rowsPerStrip;

        byte[] PackRow(int y)
        {
            var row = new byte[rowBytes];
            if (bps == 8)
            {
                for (int x = 0; x < width; x++)
                    row[x] = (byte)sample(x, y);
                return row;
            }

            if (bps == 16)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort v = (ushort)sample(x, y);
                    if (bigEndian)
                        BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(x * 2, 2), v);
                    else
                        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(x * 2, 2), v);
                }

                return row;
            }

            int bit = 0;
            for (int x = 0; x < width; x++)
            {
                int v = sample(x, y);
                for (int i = bps - 1; i >= 0; i--)
                {
                    if (((v >> i) & 1) != 0)
                        row[bit >> 3] |= (byte)(0x80 >> (bit & 7));
                    bit++;
                }
            }

            return row;
        }

        var rows = new byte[height][];
        for (int y = 0; y < height; y++)
            rows[y] = PackRow(y);

        byte[] pixelData = rows.SelectMany(r => r).ToArray();
        long[] stripOffsets = new long[actualStrips];
        long[] stripBytes = new long[actualStrips];
        long cursor = 8;
        for (int s = 0; s < actualStrips; s++)
        {
            int firstRow = s * rowsPerStrip;
            int rowsHere = Math.Min(rowsPerStrip, height - firstRow);
            stripOffsets[s] = cursor;
            stripBytes[s] = (long)rowsHere * rowBytes;
            cursor += stripBytes[s];
        }

        var entries = new List<(ushort Tag, ushort Type, uint Count, byte[] Payload)>();

        byte[] U16(ushort v) => bigEndian
            ? [(byte)(v >> 8), (byte)v]
            : [(byte)v, (byte)(v >> 8)];

        byte[] U32(uint v) => bigEndian
            ? [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]
            : [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)];

        void Add(ushort tag, ushort type, uint count, byte[] payload) =>
            entries.Add((tag, type, count, payload));

        Add(254, TypeLong, 1, U32(newSubfileType));
        Add(256, TypeLong, 1, U32((uint)width));
        Add(257, TypeLong, 1, U32((uint)height));
        Add(258, TypeShort, 1, U16((ushort)bps));
        Add(259, TypeShort, 1, U16(compression));
        Add(262, TypeShort, 1, U16(photometric));
        Add(271, TypeAscii, 8, Encoding.ASCII.GetBytes("Fixture\0"));
        Add(272, TypeAscii, 4, Encoding.ASCII.GetBytes("DNG\0"));
        Add(273, TypeLong, (uint)actualStrips, stripOffsets.SelectMany(o => U32((uint)o)).ToArray());
        Add(274, TypeShort, 1, U16(1));
        Add(277, TypeShort, 1, U16(1));
        Add(278, TypeLong, 1, U32((uint)rowsPerStrip));
        Add(279, TypeLong, (uint)actualStrips, stripBytes.SelectMany(b => U32((uint)b)).ToArray());
        Add(284, TypeShort, 1, U16(1));
        Add(33421, TypeShort, 2, U16(cfaRows).Concat(U16(cfaColumns)).ToArray());
        Add(33422, TypeByte, 4, cfa);
        if (includeDngVersion)
        {
            Add(50706, TypeByte, 4, [1, 4, 0, 0]);
            Add(50707, TypeByte, 4, [1, 1, 0, 0]);
            Add(50708, TypeAscii, 9, Encoding.ASCII.GetBytes("Test DNG\0"));
        }

        if (includePlaneColor)
            Add(50710, TypeByte, 3, [0, 1, 2]);
        if (blackLevels is not null)
        {
            if (includeBlackRepeatDim)
            {
                ushort rowsDim = blackLevels.Length == 4 ? (ushort)2 : (ushort)1;
                Add(50713, TypeShort, 2, U16(rowsDim).Concat(U16(rowsDim)).ToArray());
            }

            Add(50714, TypeShort, (uint)blackLevels.Length,
                blackLevels.SelectMany(b => U16((ushort)b)).ToArray());
        }

        if (includeWhiteLevel)
            Add(50717, TypeLong, 1, U32((uint)resolvedWhite));

        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        var blob = new List<byte>();
        long blobStart = 8 + pixelData.Length;
        var valueFields = new List<(ushort Tag, ushort Type, uint Count, byte[] Field)>();
        foreach ((ushort tag, ushort type, uint count, byte[] payload) in entries)
        {
            if (payload.Length <= 4)
            {
                valueFields.Add((tag, type, count, payload.Concat(new byte[4 - payload.Length]).ToArray()));
            }
            else
            {
                long offset = blobStart + blob.Count;
                valueFields.Add((tag, type, count, U32((uint)offset)));
                blob.AddRange(payload);
                if ((blob.Count & 1) != 0)
                    blob.Add(0);
            }
        }

        long ifdOffset = blobStart + blob.Count;
        var ifd = new List<byte>();
        ifd.AddRange(U16((ushort)valueFields.Count));
        foreach ((ushort tag, ushort type, uint count, byte[] field) in valueFields)
        {
            ifd.AddRange(U16(tag));
            ifd.AddRange(U16(type));
            ifd.AddRange(U32(count));
            ifd.AddRange(field);
        }

        ifd.AddRange(U32(0));

        var header = new byte[8];
        header[0] = header[1] = (byte)(bigEndian ? 'M' : 'I');
        header[2] = bigEndian ? (byte)0 : (byte)42;
        header[3] = bigEndian ? (byte)42 : (byte)0;
        if (bigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)ifdOffset);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)ifdOffset);

        return [.. header, .. pixelData, .. blob, .. ifd];
    }

    private static Frame Read(byte[] dng)
    {
        using var stream = new MemoryStream(dng, writable: false);
        using var reader = new RawDngReader(stream);
        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        return frame;
    }

    private static int ReadSourceBitDepth(byte[] dng)
    {
        using var stream = new MemoryStream(dng, writable: false);
        using var reader = new RawDngReader(stream);
        return reader.SourceBitDepth;
    }

    private static BayerPattern ReadBayer(byte[] dng)
    {
        using var stream = new MemoryStream(dng, writable: false);
        using var reader = new RawDngReader(stream);
        return reader.Bayer;
    }

    /// <summary>Independent restatement of the documented black/white normalisation.</summary>
    private static int Normalise(int raw, int black, int white) =>
        (int)Math.Clamp(Math.Round((raw - black) * 65535.0 / (white - black), MidpointRounding.AwayFromZero), 0, 65535);

    private static int[] Values(Frame frame) => Array.ConvertAll(frame.Samples, s => (int)s);

    // ---------------------------------------------------------------- 16-bit

    [Fact]
    public void ReadsSixteenBitRggb_AppliesBlackAndWhiteLevels()
    {
        const int black = 64;
        const int white = 4095;
        byte[] dng = BuildDng(4, 4, 16, blackLevels: [black], whiteLevel: white,
            sample: (x, y) => 64 + (y * 4 + x) * 250);

        Frame frame = Read(dng);

        Assert.Equal(4, frame.Width);
        Assert.Equal(4, frame.Height);
        Assert.Equal(1, frame.Channels);
        Assert.Equal(16, frame.BitDepth);
        Assert.Equal(BayerPattern.RGGB, frame.Bayer);
        Assert.Equal(
            [0, 4064, 8129, 12193, 16258, 20322, 24387, 28451,
             32516, 36580, 40644, 44709, 48773, 52838, 56902, 60967],
            Values(frame));
    }

    [Fact]
    public void BlackLevelMapsToZeroAndWhiteLevelToFullScale()
    {
        byte[] dng = BuildDng(2, 2, 16, blackLevels: [100], whiteLevel: 4000,
            sample: (x, y) => (x, y) switch
            {
                (0, 0) => 0,     // below black: clamps to 0
                (1, 0) => 100,   // exactly black
                (0, 1) => 4000,  // exactly white
                _ => 5000,       // above white: clamps to 65535
            });

        Frame frame = Read(dng);

        Assert.Equal([0, 0, 65535, 65535], Values(frame));
    }

    [Fact]
    public void SecondReadFrameReturnsNull()
    {
        byte[] dng = BuildDng(2, 2, 16);
        using var stream = new MemoryStream(dng, writable: false);
        using var reader = new RawDngReader(stream);

        Assert.NotNull(reader.ReadFrame());
        Assert.Null(reader.ReadFrame());
    }

    // ---------------------------------------------------------------- packed depths

    [Fact]
    public void ReadsTwelveBitPackedSamples_MsbFirstRows()
    {
        byte[] dng = BuildDng(4, 4, 12, blackLevels: [0], whiteLevel: 4095,
            sample: (x, y) => (y * 4 + x) * 257);

        Frame frame = Read(dng);

        Assert.Equal(12, ReadSourceBitDepth(dng));
        Assert.Equal(16, frame.BitDepth);
        Assert.Equal(
            [0, 4113, 8226, 12339, 16452, 20565, 24678, 28791,
             32904, 37016, 41129, 45242, 49355, 53468, 57581, 61694],
            Values(frame));
    }

    [Fact]
    public void ReadsFourteenBitPackedSamples_MsbFirstRows()
    {
        byte[] dng = BuildDng(4, 4, 14, blackLevels: [0], whiteLevel: 16383,
            sample: (x, y) => (y * 4 + x) * 1000);

        Frame frame = Read(dng);

        Assert.Equal(
            [0, 4000, 8000, 12001, 16001, 20001, 24001, 28001,
             32001, 36002, 40002, 44002, 48002, 52002, 56003, 60003],
            Values(frame));
    }

    [Fact]
    public void PackedRowsDoNotBleedAcrossRowBoundaries()
    {
        // 3 pixels at 12 bits = 36 bits, so every row has four padding bits; a decoder that reads
        // continuously would shift the next row by those bits.
        byte[] dng = BuildDng(3, 3, 12, blackLevels: [0], whiteLevel: 4095,
            sample: (x, _) => x * 1000 + 1);

        Frame frame = Read(dng);

        for (int y = 0; y < 3; y++)
        {
            Assert.Equal(Normalise(1, 0, 4095), frame.Samples[y * 3 + 0]);
            Assert.Equal(Normalise(1001, 0, 4095), frame.Samples[y * 3 + 1]);
            Assert.Equal(Normalise(2001, 0, 4095), frame.Samples[y * 3 + 2]);
        }
    }

    [Fact]
    public void ReadsEightBitMosaic()
    {
        byte[] dng = BuildDng(2, 2, 8, blackLevels: [0], whiteLevel: 255,
            sample: (x, y) => (x + y * 2) * 60);

        Frame frame = Read(dng);

        Assert.Equal([0, 15420, 30840, 46260], Values(frame));
    }

    [Fact]
    public void ReadsTenBitPackedSamples()
    {
        // 5 pixels at 10 bits = 50 bits, so each row carries six padding bits.
        byte[] dng = BuildDng(5, 3, 10, blackLevels: [0], whiteLevel: 1023,
            sample: (x, y) => (y * 5 + x) * 17);

        Frame frame = Read(dng);

        for (int i = 0; i < 15; i++)
            Assert.Equal(Normalise(i * 17, 0, 1023), frame.Samples[i]);
    }

    // ---------------------------------------------------------------- CFA patterns

    [Theory]
    [InlineData(new byte[] { 0, 1, 1, 2 }, BayerPattern.RGGB)]
    [InlineData(new byte[] { 2, 1, 1, 0 }, BayerPattern.BGGR)]
    [InlineData(new byte[] { 1, 0, 2, 1 }, BayerPattern.GRBG)]
    [InlineData(new byte[] { 1, 2, 0, 1 }, BayerPattern.GBRG)]
    public void MapsEveryTwoByTwoCfaPattern(byte[] cfa, BayerPattern expected)
    {
        byte[] dng = BuildDng(2, 2, 16, cfa: cfa,
            sample: (x, y) => 1000 * (y * 2 + x + 1));

        Frame frame = Read(dng);

        Assert.Equal(expected, frame.Bayer);
        Assert.Equal(expected, ReadBayer(dng));
        Assert.Equal([1000, 2000, 3000, 4000], Values(frame));
    }

    [Fact]
    public void RejectsXTransCfaDimensions()
    {
        byte[] dng = BuildDng(2, 2, 16, cfaRows: 6, cfaColumns: 6);

        var ex = Assert.Throws<NotSupportedException>(() => Read(dng));
        Assert.Contains("2x2", ex.Message);
    }

    [Fact]
    public void RejectsFourColourCfaPlaneOrder()
    {
        byte[] dng = BuildDng(2, 2, 16, cfa: [0, 1, 2, 3]);

        Assert.Throws<NotSupportedException>(() => Read(dng));
    }

    // ---------------------------------------------------------------- geometry and container

    [Fact]
    public void AppliesPerSiteTwoByTwoBlackLevels()
    {
        byte[] dng = BuildDng(4, 4, 16, blackLevels: [10, 20, 30, 40], whiteLevel: 4095,
            sample: (_, _) => 1000);

        Frame frame = Read(dng);

        int[] values = Values(frame);
        int[] levels = [10, 20, 30, 40];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int expected = Normalise(1000, levels[(y % 2) * 2 + (x % 2)], 4095);
                Assert.Equal(expected, values[y * 4 + x]);
            }
        }
    }

    [Fact]
    public void ReadsMultiStripFile()
    {
        Func<int, int, int> sample = (x, y) => 100 + x * 10 + y * 37;
        byte[] single = BuildDng(5, 7, 12, blackLevels: [8], whiteLevel: 4095, sample: sample, stripCount: 1);
        byte[] multi = BuildDng(5, 7, 12, blackLevels: [8], whiteLevel: 4095, sample: sample, stripCount: 3);

        Assert.Equal(Values(Read(single)), Values(Read(multi)));
    }

    [Fact]
    public void ReadsBigEndianSixteenBitFile()
    {
        Func<int, int, int> sample = (x, y) => 200 + x * 333 + y * 717;
        byte[] little = BuildDng(3, 4, 16, bigEndian: false, sample: sample);
        byte[] big = BuildDng(3, 4, 16, bigEndian: true, sample: sample);

        Assert.Equal(Values(Read(little)), Values(Read(big)));
    }

    [Fact]
    public void DefaultsWhiteLevelToBitDepthMaximum()
    {
        byte[] dng = BuildDng(2, 2, 12, blackLevels: [0], whiteLevel: null, includeWhiteLevel: false,
            sample: (x, y) => (x == 0 && y == 0) ? 0 : 4095);

        Frame frame = Read(dng);

        Assert.Equal(0, frame.Samples[0]);
        Assert.Equal(65535, frame.Samples[1]);
        Assert.Equal(65535, frame.Samples[3]);
    }

    [Fact]
    public void ProbeReportsGeometryWithoutReadingStripData()
    {
        byte[] dng = BuildDng(6, 9, 14, blackLevels: [100], whiteLevel: 16383);

        ImageInfo info = RawDngReader.Probe(new MemoryStream(dng, writable: false));

        Assert.Equal(6, info.Width);
        Assert.Equal(9, info.Height);
        Assert.Equal(16, info.BitDepth);
        Assert.Equal(1, info.Channels);
    }

    // ---------------------------------------------------------------- rejections

    [Fact]
    public void RejectsTiffWithoutDngVersion()
    {
        byte[] dng = BuildDng(4, 4, 16, includeDngVersion: false);

        var ex = Assert.Throws<InvalidDataException>(() => Read(dng));
        Assert.Contains("DNGVersion", ex.Message);
    }

    [Fact]
    public void RejectsLosslessJpegCompression()
    {
        byte[] dng = BuildDng(4, 4, 16, compression: 7);

        var ex = Assert.Throws<NotSupportedException>(() => Read(dng));
        Assert.Contains("compression", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNonCfaPhotometricInterpretation()
    {
        byte[] dng = BuildDng(4, 4, 16, photometric: 1);

        Assert.Throws<NotSupportedException>(() => Read(dng));
    }

    [Fact]
    public void RejectsReducedResolutionFirstDirectory()
    {
        byte[] dng = BuildDng(4, 4, 16, newSubfileType: 1);

        var ex = Assert.Throws<NotSupportedException>(() => Read(dng));
        Assert.Contains("preview", ex.Message);
    }

    [Fact]
    public void RejectsBigTiffHeader()
    {
        byte[] dng = BuildDng(4, 4, 16);
        dng[2] = 43;
        dng[3] = 0;

        Assert.Throws<NotSupportedException>(() => Read(dng));
    }

    [Fact]
    public void RejectsNonTiffBytes()
    {
        byte[] junk = Encoding.ASCII.GetBytes("this is not a raw file");
        Assert.Throws<InvalidDataException>(() => Read(junk));
    }

    [Fact]
    public void RejectsStripOffsetPastEndOfFile()
    {
        byte[] dng = BuildDng(4, 4, 16);
        PatchInlineLong(dng, tag: 273, value: 0x00FF_FFFF);

        var ex = Assert.Throws<InvalidDataException>(() => Read(dng));
        Assert.Contains("past the end", ex.Message);
    }

    [Fact]
    public void RejectsTruncatedDirectory()
    {
        byte[] dng = BuildDng(4, 4, 16);
        byte[] truncated = dng[..(dng.Length / 2)];

        Assert.Throws<InvalidDataException>(() => Read(truncated));
    }

    /// <summary>Overwrites the inline LONG value of one little-endian IFD entry.</summary>
    private static void PatchInlineLong(byte[] dng, ushort tag, uint value)
    {
        int ifdOffset = BinaryPrimitives.ReadInt32LittleEndian(dng.AsSpan(4, 4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(dng.AsSpan(ifdOffset, 2));
        for (int i = 0; i < count; i++)
        {
            int entry = ifdOffset + 2 + i * 12;
            if (BinaryPrimitives.ReadUInt16LittleEndian(dng.AsSpan(entry, 2)) == tag)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(dng.AsSpan(entry + 8, 4), value);
                return;
            }
        }

        throw new InvalidOperationException($"tag {tag} not found");
    }
}
