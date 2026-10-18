using System.Buffers.Binary;
using Pipp.Core.Formats;
using Pipp.Core.Frames;

namespace Pipp.Images.Tests;

public sealed class BmpTests
{
    [Theory]
    [InlineData(1, 8, false)]
    [InlineData(3, 8, false)]
    [InlineData(1, 16, true)]
    [InlineData(3, 16, true)]
    public void RoundTrip(int channels, int bitDepth, bool downshift)
    {
        Frame source = TestFrames.Create(channels, bitDepth);
        Frame expected = downshift ? TestFrames.DownshiftToEightBits(source) : source;

        using var directory = new TempDir();
        string path = directory.PathFor("frame.bmp");

        using (var writer = new BmpWriter(path, source.Width, source.Height, channels, bitDepth))
        {
            writer.Write(source);
            Assert.True(writer.HasFrame);
        }

        using var reader = new BmpReader(path);
        Assert.Equal(expected.Width, reader.Width);
        Assert.Equal(expected.Height, reader.Height);
        Assert.Equal(expected.Channels, reader.Channels);
        Assert.Equal(8, reader.BitDepth);
        Assert.Equal(1, reader.FrameCount);
        Assert.Equal(BayerPattern.None, reader.Bayer);
        Assert.False(reader.IsTopDown);

        Frame? actual = reader.ReadFrame();
        TestFrames.AssertEqual(expected, actual!);
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void Writer_Emits8bppMonoWithIdentityPalette()
    {
        Frame frame = TestFrames.Create(1, 8);
        using var directory = new TempDir();
        string path = directory.PathFor("mono.bmp");

        using (var writer = new BmpWriter(path, frame.Width, frame.Height, 1, 8))
            writer.Write(frame);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.Equal((uint)bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(2)));
        Assert.Equal(14u + 40u + 1024u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10)));
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(14)));
        Assert.Equal(frame.Width, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)));
        Assert.Equal(frame.Height, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30)));
        Assert.Equal(256u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(46)));

        for (int i = 0; i < 256; i++)
        {
            int p = 54 + i * 4;
            Assert.Equal((byte)i, bytes[p]);
            Assert.Equal((byte)i, bytes[p + 1]);
            Assert.Equal((byte)i, bytes[p + 2]);
            Assert.Equal(0, bytes[p + 3]);
        }

        int stride = (frame.Width + 3) & ~3;
        Assert.Equal(54 + 1024 + stride * frame.Height, bytes.Length);
    }

    [Fact]
    public void Writer_Emits24bppForColour()
    {
        Frame frame = TestFrames.Create(3, 8);
        using var directory = new TempDir();
        string path = directory.PathFor("colour.bmp");

        using (var writer = new BmpWriter(path, frame.Width, frame.Height, 3, 8))
            writer.Write(frame);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(46)));
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10)));

        int stride = (frame.Width * 3 + 3) & ~3;
        Assert.Equal(54 + stride * frame.Height, bytes.Length);

        // The first row in the file is the image's bottom row (BMP is bottom-up) and is stored B,G,R.
        int rowStart = 54;
        int source = (frame.Height - 1) * frame.Width * 3;
        Assert.Equal((byte)frame.Samples[source + 2], bytes[rowStart]);
        Assert.Equal((byte)frame.Samples[source + 1], bytes[rowStart + 1]);
        Assert.Equal((byte)frame.Samples[source], bytes[rowStart + 2]);
    }

    [Fact]
    public void Reader_HandlesTopDown32bppAndDropsAlpha()
    {
        byte[][] rows =
        {
            new byte[] { 30, 20, 10, 255, 60, 50, 40, 128, 90, 80, 70, 0 },
            new byte[] { 3, 2, 1, 4, 7, 6, 5, 8, 11, 10, 9, 12 },
        };

        byte[] file = BmpTestFile.Build(3, 2, 32, rows, topDown: true);
        using var stream = new MemoryStream(file);
        using var reader = new BmpReader(stream);

        Assert.True(reader.IsTopDown);
        Assert.Equal(3, reader.Channels);
        Assert.Equal(8, reader.BitDepth);
        Assert.Equal(12, reader.RowStride);

        Frame frame = reader.ReadFrame()!;
        int[] expected =
        {
            10, 20, 30, 40, 50, 60, 70, 80, 90,
            1, 2, 3, 5, 6, 7, 9, 10, 11,
        };

        Assert.Equal(expected.Length, frame.Samples.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], frame.Samples[i]);
    }

    [Fact]
    public void Reader_ExpandsNonGreyPaletteToRgb()
    {
        byte[] palette = BmpTestFile.GreyPalette();
        palette[0] = 30;  // B
        palette[1] = 20;  // G
        palette[2] = 10;  // R
        palette[4] = 200; // B
        palette[5] = 100; // G
        palette[6] = 50;  // R

        byte[][] rows =
        {
            new byte[] { 0, 1 },
            new byte[] { 1, 0 },
        };

        byte[] file = BmpTestFile.Build(2, 2, 8, rows, palette);
        using var stream = new MemoryStream(file);
        using var reader = new BmpReader(stream);

        Assert.Equal(3, reader.Channels);
        Assert.True(reader.HasColourPalette);

        Frame frame = reader.ReadFrame()!;
        Assert.Equal(new ushort[] { 10, 20, 30, 50, 100, 200, 50, 100, 200, 10, 20, 30 }, frame.Samples);
    }

    [Theory]
    [InlineData(108)]
    [InlineData(124)]
    public void Reader_AcceptsV4AndV5InfoHeaderSizes(int infoHeaderSize)
    {
        byte[][] rows =
        {
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 },
        };

        byte[] file = BmpTestFile.Build(3, 2, 8, rows, infoHeaderSize: infoHeaderSize);
        using var stream = new MemoryStream(file);
        using var reader = new BmpReader(stream);

        Assert.Equal(3, reader.Width);
        Assert.Equal(2, reader.Height);
        Assert.Equal(1, reader.Channels);
        Frame frame = reader.ReadFrame()!;
        Assert.Equal(new ushort[] { 4, 5, 6, 7, 8, 9 }, frame.Samples);
    }

    [Fact]
    public void Reader_Rejects16bppWithEngineMessage()
    {
        byte[][] rows = { new byte[4], new byte[4] };
        byte[] file = BmpTestFile.Build(1, 2, 16, rows);

        using var stream = new MemoryStream(file);
        var error = Assert.Throws<InvalidDataException>(() => new BmpReader(stream));
        Assert.Contains("Only 8bpp, 24bpp and 32bpp supported by PIPP", error.Message);

        stream.Position = 0;
        var probeError = Assert.Throws<InvalidDataException>(() => BmpReader.Probe(stream));
        Assert.Contains("Only 8bpp, 24bpp and 32bpp supported by PIPP", probeError.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Reader_RejectsOtherDepths(ushort bitCount)
    {
        byte[] file = BmpTestFile.Build(2, 2, bitCount, new[] { new byte[8], new byte[8] });
        using var stream = new MemoryStream(file);
        Assert.Throws<InvalidDataException>(() => new BmpReader(stream));
    }

    [Fact]
    public void Reader_RejectsCompressedBmp()
    {
        byte[][] rows = { new byte[] { 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 } };
        byte[] file = BmpTestFile.Build(2, 2, 8, rows, compression: 1); // BI_RLE8

        using var stream = new MemoryStream(file);
        Assert.Throws<NotSupportedException>(() => new BmpReader(stream));
    }

    [Fact]
    public void Writer_RejectsMismatchedFrame()
    {
        using var stream = new MemoryStream();
        using var writer = new BmpWriter(stream, 3, 2, 1, 8);
        Assert.Throws<ArgumentException>(() => writer.Write(TestFrames.Create(1, 8, 4, 2)));
    }
}
