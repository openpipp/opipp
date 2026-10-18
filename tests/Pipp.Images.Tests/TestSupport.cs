using System.Buffers.Binary;
using System.IO.Compression;
using Pipp.Core.Formats;
using Pipp.Core.Frames;

namespace Pipp.Images.Tests;

/// <summary>Temporary directory that removes itself at the end of a test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pipp-images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathFor(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked file should not fail the test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Deterministic frames whose samples exercise full byte ranges and odd row widths.</summary>
internal static class TestFrames
{
    public static Frame Create(int channels, int bitDepth, int width = 7, int height = 5)
    {
        var frame = new Frame(width, height, channels, bitDepth);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int index = (y * width + x) * channels + c;
                    frame.Samples[index] = bitDepth == 8
                        ? (ushort)((index * 37 + 11) & 0xFF)
                        : (ushort)(0x1200 + (index * 977 % 0x0E00));
                }
            }
        }

        return frame;
    }

    /// <summary>Models the 8-bit reduction <see cref="BmpWriter"/> applies to 16-bit frames.</summary>
    public static Frame DownshiftToEightBits(Frame source)
    {
        var reduced = new Frame(source.Width, source.Height, source.Channels, 8);
        for (int i = 0; i < source.Samples.Length; i++)
            reduced.Samples[i] = (ushort)(source.Samples[i] >> 8);
        return reduced;
    }

    public static void AssertEqual(Frame expected, Frame actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Channels, actual.Channels);
        Assert.Equal(expected.BitDepth, actual.BitDepth);

        int firstDifference = -1;
        for (int i = 0; i < expected.Samples.Length; i++)
        {
            if (expected.Samples[i] != actual.Samples[i])
            {
                firstDifference = i;
                break;
            }
        }

        if (firstDifference >= 0)
        {
            Assert.Fail(
                $"sample {firstDifference} differs: expected {expected.Samples[firstDifference]}, " +
                $"got {actual.Samples[firstDifference]}");
        }
    }
}

/// <summary>Independent CRC-32 implementation (bitwise) used to validate the production table.</summary>
internal static class TestCrc32
{
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }

    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Fold(crc, first);
        crc = Fold(crc, second);
        return ~crc;
    }

    private static uint Fold(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}

/// <summary>Small hand-rolled BMP encoder used to exercise variants the writer never emits.</summary>
internal static class BmpTestFile
{
    /// <summary>
    /// Builds a BI_RGB bitmap. <paramref name="rows"/> are logical top-down rows containing raw pixel
    /// bytes (index, BGR or BGRA according to <paramref name="bitCount"/>); padding is added here.
    /// </summary>
    public static byte[] Build(
        int width,
        int height,
        ushort bitCount,
        IReadOnlyList<byte[]> rows,
        byte[]? palette = null,
        bool topDown = false,
        uint compression = 0,
        int infoHeaderSize = 40)
    {
        int channels = bitCount == 8 ? 1 : bitCount == 24 ? 3 : 4;
        int stride = (width * channels + 3) & ~3;
        byte[] paletteBytes = palette ?? (bitCount == 8 ? GreyPalette() : Array.Empty<byte>());
        int offset = 14 + infoHeaderSize + paletteBytes.Length;
        int imageBytes = stride * height;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write((uint)(offset + imageBytes));
        writer.Write(0u);
        writer.Write((uint)offset);

        writer.Write((uint)infoHeaderSize);
        writer.Write(width);
        writer.Write(topDown ? -height : height);
        writer.Write((ushort)1);
        writer.Write(bitCount);
        writer.Write(compression);
        writer.Write((uint)imageBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write((uint)(paletteBytes.Length / 4));
        writer.Write((uint)(paletteBytes.Length / 4));

        for (int i = infoHeaderSize - 40; i > 0; i--)
            writer.Write((byte)0);

        writer.Write(paletteBytes);

        byte[] rowBuffer = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = topDown ? y : height - 1 - y;
            Array.Clear(rowBuffer);
            rows[sourceRow].CopyTo(rowBuffer, 0);
            writer.Write(rowBuffer);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] GreyPalette()
    {
        byte[] palette = new byte[256 * 4];
        for (int i = 0; i < 256; i++)
        {
            palette[i * 4] = (byte)i;
            palette[i * 4 + 1] = (byte)i;
            palette[i * 4 + 2] = (byte)i;
        }

        return palette;
    }
}

/// <summary>Hand-rolled classic TIFF encoder for big-endian and multi-strip cases.</summary>
internal static class TiffTestFile
{
    private const int EntryCount = 10;

    /// <summary>Encodes a frame as an uncompressed classic TIFF.</summary>
    public static byte[] Build(
        Frame frame,
        bool bigEndian,
        ushort? photometric = null,
        ushort compression = 1,
        int rowsPerStrip = 0)
    {
        ushort photometricValue = photometric ?? (frame.Channels == 3 ? (ushort)2 : (ushort)1);
        int bytesPerSample = frame.BitDepth / 8;
        int rowBytes = frame.Width * frame.Channels * bytesPerSample;
        int rowsPerStripActual = rowsPerStrip <= 0 ? frame.Height : rowsPerStrip;
        int stripCount = (frame.Height + rowsPerStripActual - 1) / rowsPerStripActual;

        int extraBase = 8 + 2 + EntryCount * 12 + 4;
        int bitsBytes = frame.Channels == 3 ? 6 : 0;
        int arrayBytes = stripCount > 1 ? stripCount * 4 : 0;
        int dataStart = extraBase + bitsBytes + arrayBytes * 2;

        long[] stripOffsets = new long[stripCount];
        long[] stripCounts = new long[stripCount];
        long running = dataStart;
        for (int strip = 0; strip < stripCount; strip++)
        {
            int rowStart = strip * rowsPerStripActual;
            int rows = Math.Min(rowsPerStripActual, frame.Height - rowStart);
            stripOffsets[strip] = running;
            stripCounts[strip] = (long)rows * rowBytes;
            running += stripCounts[strip];
        }

        using var pixels = new MemoryStream();
        byte[] pair = new byte[2];
        for (int y = 0; y < frame.Height; y++)
        {
            int source = y * frame.Width * frame.Channels;
            for (int i = 0; i < frame.Width * frame.Channels; i++)
            {
                ushort value = frame.Samples[source + i];
                if (bytesPerSample == 1)
                {
                    pixels.WriteByte((byte)value);
                }
                else
                {
                    if (bigEndian)
                        BinaryPrimitives.WriteUInt16BigEndian(pair, value);
                    else
                        BinaryPrimitives.WriteUInt16LittleEndian(pair, value);
                    pixels.Write(pair);
                }
            }
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(bigEndian ? (byte)'M' : (byte)'I');
        writer.Write(bigEndian ? (byte)'M' : (byte)'I');
        WriteU16(writer, 42, bigEndian);
        WriteU32(writer, 8, bigEndian);

        WriteU16(writer, EntryCount, bigEndian);
        WriteLongEntry(writer, 256, (uint)frame.Width, bigEndian);
        WriteLongEntry(writer, 257, (uint)frame.Height, bigEndian);
        if (frame.Channels == 1)
            WriteShortEntry(writer, 258, (ushort)frame.BitDepth, bigEndian);
        else
            WriteShortArrayEntry(writer, 258, 3, (uint)(extraBase), bigEndian);

        WriteShortEntry(writer, 259, compression, bigEndian);
        WriteShortEntry(writer, 262, photometricValue, bigEndian);
        if (stripCount == 1)
            WriteLongEntry(writer, 273, (uint)stripOffsets[0], bigEndian);
        else
            WriteLongArrayEntry(writer, 273, (uint)stripCount, (uint)(extraBase + bitsBytes), bigEndian);

        WriteShortEntry(writer, 277, (ushort)frame.Channels, bigEndian);
        WriteLongEntry(writer, 278, (uint)rowsPerStripActual, bigEndian);
        if (stripCount == 1)
            WriteLongEntry(writer, 279, (uint)stripCounts[0], bigEndian);
        else
            WriteLongArrayEntry(writer, 279, (uint)stripCount, (uint)(extraBase + bitsBytes + arrayBytes), bigEndian);

        WriteShortEntry(writer, 284, 1, bigEndian);
        WriteU32(writer, 0, bigEndian); // next IFD

        if (frame.Channels == 3)
        {
            for (int c = 0; c < 3; c++)
                WriteU16(writer, (ushort)frame.BitDepth, bigEndian);
        }

        if (stripCount > 1)
        {
            foreach (long offset in stripOffsets)
                WriteU32(writer, (uint)offset, bigEndian);
            foreach (long count in stripCounts)
                WriteU32(writer, (uint)count, bigEndian);
        }

        if (stream.Position != dataStart)
            throw new InvalidOperationException($"TIFF test layout drift: {stream.Position} != {dataStart}");

        pixels.Position = 0;
        pixels.CopyTo(stream);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteShortEntry(BinaryWriter writer, ushort tag, ushort value, bool bigEndian)
    {
        WriteU16(writer, tag, bigEndian);
        WriteU16(writer, 3, bigEndian);
        WriteU32(writer, 1, bigEndian);
        WriteU16(writer, value, bigEndian);
        WriteU16(writer, 0, bigEndian);
    }

    private static void WriteLongEntry(BinaryWriter writer, ushort tag, uint value, bool bigEndian)
    {
        WriteU16(writer, tag, bigEndian);
        WriteU16(writer, 4, bigEndian);
        WriteU32(writer, 1, bigEndian);
        WriteU32(writer, value, bigEndian);
    }

    private static void WriteShortArrayEntry(BinaryWriter writer, ushort tag, uint count, uint offset, bool bigEndian)
    {
        WriteU16(writer, tag, bigEndian);
        WriteU16(writer, 3, bigEndian);
        WriteU32(writer, count, bigEndian);
        WriteU32(writer, offset, bigEndian);
    }

    private static void WriteLongArrayEntry(BinaryWriter writer, ushort tag, uint count, uint offset, bool bigEndian)
    {
        WriteU16(writer, tag, bigEndian);
        WriteU16(writer, 4, bigEndian);
        WriteU32(writer, count, bigEndian);
        WriteU32(writer, offset, bigEndian);
    }

    private static void WriteU16(BinaryWriter writer, ushort value, bool bigEndian)
        => writer.Write(bigEndian ? BinaryPrimitives.ReverseEndianness(value) : value);

    private static void WriteU32(BinaryWriter writer, uint value, bool bigEndian)
        => writer.Write(bigEndian ? BinaryPrimitives.ReverseEndianness(value) : value);
}

/// <summary>Hand-rolled PNG encoder supporting arbitrary row filters and split IDAT chunks.</summary>
internal static class PngTestFile
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static int ChannelsFor(int colourType) => colourType switch
    {
        0 => 1,
        2 => 3,
        4 => 2,
        6 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(colourType)),
    };

    /// <summary>Builds a PNG from unfiltered row byte arrays, applying the requested filter per row.</summary>
    public static byte[] Build(
        int width,
        int height,
        int bitDepth,
        int colourType,
        IReadOnlyList<byte[]> rows,
        IReadOnlyList<byte> filters,
        int idatParts = 1,
        int? interlaceOverride = null)
    {
        int channels = ChannelsFor(colourType);
        int bpp = channels * (bitDepth / 8);

        using var payload = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            byte filter = filters[y];
            payload.WriteByte(filter);
            payload.Write(FilterRow(rows[y], filter, y == 0 ? null : rows[y - 1], bpp));
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(payload.ToArray(), 0, (int)payload.Length);
            compressed = buffer.ToArray();
        }

        using var png = new MemoryStream();
        png.Write(Signature);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colourType;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = (byte)(interlaceOverride ?? 0);
        WriteChunk(png, "IHDR", ihdr);

        int parts = Math.Max(1, idatParts);
        int chunkSize = (compressed.Length + parts - 1) / parts;
        if (chunkSize == 0)
            chunkSize = 1;

        for (int offset = 0; offset < compressed.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, compressed.Length - offset);
            WriteChunk(png, "IDAT", compressed.AsSpan(offset, length).ToArray());
        }

        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    /// <summary>Parses chunk type/payload pairs and verifies each CRC with the independent test CRC.</summary>
    public static List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(Signature), "PNG signature is wrong");

        var chunks = new List<(string, byte[])>();
        int position = 8;
        while (position < png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            byte[] data = png.AsSpan(position + 8, (int)length).ToArray();
            uint stored = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + (int)length));
            uint computed = TestCrc32.Compute(png.AsSpan(position + 4, 4 + (int)length));
            Assert.Equal(computed, stored);
            chunks.Add((type, data));
            position += 12 + (int)length;
        }

        return chunks;
    }

    public static byte[] InflateIdat(IEnumerable<(string Type, byte[] Data)> chunks)
    {
        using var compressed = new MemoryStream();
        foreach ((string type, byte[] data) in chunks)
        {
            if (type == "IDAT")
                compressed.Write(data);
        }

        compressed.Position = 0;
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] FilterRow(byte[] raw, byte filter, byte[]? above, int bpp)
    {
        byte[] encoded = new byte[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            int left = i >= bpp ? raw[i - bpp] : 0;
            int up = above is null ? 0 : above[i];
            int upLeft = above is not null && i >= bpp ? above[i - bpp] : 0;
            int predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) >> 1,
                4 => Paeth(left, up, upLeft),
                _ => throw new ArgumentOutOfRangeException(nameof(filter)),
            };
            encoded[i] = (byte)(raw[i] - predictor);
        }

        return encoded;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left);
        int pb = Math.Abs(p - up);
        int pc = Math.Abs(p - upLeft);
        if (pa <= pb && pa <= pc)
            return left;
        return pb <= pc ? up : upLeft;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, TestCrc32.Compute(typeBytes, data));
        stream.Write(crc);
    }
}
