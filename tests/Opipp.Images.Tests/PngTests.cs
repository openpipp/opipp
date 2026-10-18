using System.Buffers.Binary;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Images.Tests;

public sealed class PngTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(3, 8)]
    [InlineData(3, 16)]
    public void RoundTrip(int channels, int bitDepth)
    {
        Frame source = TestFrames.Create(channels, bitDepth);

        using var directory = new TempDir();
        string path = directory.PathFor("frame.png");

        using (var writer = new PngWriter(path, source.Width, source.Height, channels, bitDepth))
        {
            writer.Write(source);
            Assert.True(writer.HasFrame);
        }

        using var reader = new PngReader(path);
        Assert.Equal(source.Width, reader.Width);
        Assert.Equal(source.Height, reader.Height);
        Assert.Equal(channels, reader.Channels);
        Assert.Equal(bitDepth, reader.BitDepth);
        Assert.Equal(channels == 1 ? 0 : 2, reader.ColourType);
        Assert.Equal(channels, reader.EncodedChannels);
        Assert.False(reader.HasAlpha);
        Assert.Equal(1, reader.FrameCount);

        Frame? actual = reader.ReadFrame();
        TestFrames.AssertEqual(source, actual!);
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void Crc32_MatchesKnownVectors()
    {
        Assert.Equal(0xCBF43926u, PngCrc32.Compute("123456789"u8));
        Assert.Equal(0xAE426082u, PngCrc32.Compute("IEND"u8));
        Assert.Equal(PngCrc32.Compute("IEND"u8), PngCrc32.Compute("IE"u8, "ND"u8));
    }

    [Fact]
    public void Writer_EmitsStandardChunkStructure()
    {
        Frame frame = TestFrames.Create(1, 8, width: 5, height: 3);
        using var directory = new TempDir();
        string path = directory.PathFor("structure.png");

        using (var writer = new PngWriter(path, frame.Width, frame.Height, 1, 8))
            writer.Write(frame);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.AsSpan(0, 8).ToArray());
        Assert.Equal(
            new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 0xAE, 0x42, 0x60, 0x82 },
            bytes.AsSpan(bytes.Length - 12, 12).ToArray());

        var chunks = PngTestFile.ReadChunks(bytes);
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(c => c.Type).ToArray());

        byte[] ihdr = chunks[0].Data;
        Assert.Equal(13, ihdr.Length);
        Assert.Equal((uint)frame.Width, BinaryPrimitives.ReadUInt32BigEndian(ihdr));
        Assert.Equal((uint)frame.Height, BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4)));
        Assert.Equal(8, ihdr[8]);
        Assert.Equal(0, ihdr[9]);
        Assert.Equal(0, ihdr[10]);
        Assert.Equal(0, ihdr[11]);
        Assert.Equal(0, ihdr[12]);

        byte[] raw = PngTestFile.InflateIdat(chunks);
        Assert.Equal((frame.Width + 1) * frame.Height, raw.Length);
        for (int y = 0; y < frame.Height; y++)
        {
            Assert.Equal(0, raw[y * (frame.Width + 1)]);
            for (int x = 0; x < frame.Width; x++)
                Assert.Equal((byte)frame.Samples[y * frame.Width + x], raw[y * (frame.Width + 1) + 1 + x]);
        }
    }

    [Fact]
    public void Writer_16BitSamplesAreBigEndian()
    {
        var frame = new Frame(3, 2, 1, 16);
        ushort[] values = { 0x0102, 0x0304, 0x0506, 0x0A0B, 0x0C0D, 0x0E0F };
        values.CopyTo(frame.Samples, 0);

        using var directory = new TempDir();
        string path = directory.PathFor("bigendian.png");

        using (var writer = new PngWriter(path, 3, 2, 1, 16))
            writer.Write(frame);

        byte[] raw = PngTestFile.InflateIdat(PngTestFile.ReadChunks(File.ReadAllBytes(path)));
        byte[] expected =
        {
            0, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06,
            0, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        };

        Assert.Equal(expected, raw);
    }

    [Fact]
    public void Reader_UndoesAllFilterTypes()
    {
        const int width = 5;
        const int height = 5;
        byte[][] rows = new byte[height][];
        for (int y = 0; y < height; y++)
        {
            rows[y] = new byte[width * 3];
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                    rows[y][x * 3 + c] = (byte)(y * 41 + x * 7 + c * 13 + 3);
            }
        }

        byte[] file = PngTestFile.Build(width, height, 8, 2, rows, new byte[] { 0, 1, 2, 3, 4 });
        using var reader = new PngReader(new MemoryStream(file));

        Frame frame = reader.ReadFrame()!;
        var expected = new Frame(width, height, 3, 8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                    expected.Samples[(y * width + x) * 3 + c] = rows[y][x * 3 + c];
            }
        }

        TestFrames.AssertEqual(expected, frame);
    }

    [Fact]
    public void Reader_HandlesSplitIdatChunks()
    {
        const int width = 6;
        const int height = 4;
        byte[][] rows = new byte[height][];
        for (int y = 0; y < height; y++)
        {
            rows[y] = new byte[width * 6];
            for (int i = 0; i < rows[y].Length; i++)
                rows[y][i] = (byte)(y * 53 + i * 11 + 1);
        }

        byte[] file = PngTestFile.Build(width, height, 16, 2, rows, new byte[] { 1, 4, 2, 3 }, idatParts: 3);
        var chunks = PngTestFile.ReadChunks(file);
        Assert.Equal(3, chunks.Count(c => c.Type == "IDAT"));

        using var reader = new PngReader(new MemoryStream(file));
        Frame frame = reader.ReadFrame()!;
        var expected = new Frame(width, height, 3, 16);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int offset = x * 6 + c * 2;
                    expected.Samples[(y * width + x) * 3 + c] =
                        BinaryPrimitives.ReadUInt16BigEndian(rows[y].AsSpan(offset));
                }
            }
        }

        TestFrames.AssertEqual(expected, frame);
    }

    [Fact]
    public void Reader_DropsRgbaAlpha()
    {
        const int width = 4;
        const int height = 3;
        byte[][] rows = new byte[height][];
        for (int y = 0; y < height; y++)
        {
            rows[y] = new byte[width * 4];
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 4; c++)
                    rows[y][x * 4 + c] = (byte)(y * 50 + x * 20 + c * 7 + 1);
            }
        }

        byte[] file = PngTestFile.Build(width, height, 8, 6, rows, new byte[] { 0, 3, 4 });
        using var reader = new PngReader(new MemoryStream(file));

        Assert.Equal(3, reader.Channels);
        Assert.Equal(4, reader.EncodedChannels);
        Assert.True(reader.HasAlpha);
        Assert.Equal(6, reader.ColourType);

        Frame frame = reader.ReadFrame()!;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                    Assert.Equal(rows[y][x * 4 + c], frame.Samples[(y * width + x) * 3 + c]);
            }
        }
    }

    [Fact]
    public void Reader_DropsGreyAlphaAndHonours16BitOrder()
    {
        const int width = 3;
        const int height = 2;
        byte[][] rows = new byte[height][];
        ushort[][] grey = new ushort[height][];

        for (int y = 0; y < height; y++)
        {
            rows[y] = new byte[width * 4];
            grey[y] = new ushort[width];
            for (int x = 0; x < width; x++)
            {
                ushort value = (ushort)(0x2345 + y * 0x0100 + x * 0x0011);
                grey[y][x] = value;
                BinaryPrimitives.WriteUInt16BigEndian(rows[y].AsSpan(x * 4), value);
                BinaryPrimitives.WriteUInt16BigEndian(rows[y].AsSpan(x * 4 + 2), (ushort)(0xF000 + x));
            }
        }

        byte[] file = PngTestFile.Build(width, height, 16, 4, rows, new byte[] { 0, 4 });
        using var reader = new PngReader(new MemoryStream(file));

        Assert.Equal(1, reader.Channels);
        Assert.Equal(2, reader.EncodedChannels);
        Assert.True(reader.HasAlpha);

        Frame frame = reader.ReadFrame()!;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                Assert.Equal(grey[y][x], frame.Samples[y * width + x]);
        }
    }

    [Fact]
    public void Reader_RejectsInterlacedPng()
    {
        byte[] row = new byte[3 * 2];
        byte[] file = PngTestFile.Build(
            2,
            2,
            8,
            2,
            new[] { row, row },
            new byte[] { 0, 0 },
            interlaceOverride: 1);

        using var stream = new MemoryStream(file);
        var error = Assert.Throws<NotSupportedException>(() => new PngReader(stream));
        Assert.Contains("Adam7", error.Message);

        stream.Position = 0;
        Assert.Throws<NotSupportedException>(() => PngReader.Probe(stream));
    }

    [Fact]
    public void Reader_RejectsCorruptChunkCrc()
    {
        Frame frame = TestFrames.Create(1, 8, 4, 2);
        using var directory = new TempDir();
        string path = directory.PathFor("crc.png");

        using (var writer = new PngWriter(path, frame.Width, frame.Height, 1, 8))
            writer.Write(frame);

        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF; // last byte of IEND's CRC

        using var stream = new MemoryStream(bytes);
        var error = Assert.Throws<InvalidDataException>(() => new PngReader(stream));
        Assert.Contains("CRC", error.Message);
    }

    [Fact]
    public void Writer_RejectsMismatchedFrame()
    {
        using var stream = new MemoryStream();
        using var writer = new PngWriter(stream, 3, 2, 1, 16);
        Assert.Throws<ArgumentException>(() => writer.Write(TestFrames.Create(3, 16, 3, 2)));
    }
}
