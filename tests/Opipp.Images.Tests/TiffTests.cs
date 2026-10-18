using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Images.Tests;

public sealed class TiffTests
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
        string path = directory.PathFor("frame.tif");

        using (var writer = new TiffWriter(path, source.Width, source.Height, channels, bitDepth))
        {
            writer.Write(source);
            Assert.True(writer.HasFrame);
        }

        using var reader = new TiffReader(path);
        Assert.Equal(source.Width, reader.Width);
        Assert.Equal(source.Height, reader.Height);
        Assert.Equal(channels, reader.Channels);
        Assert.Equal(bitDepth, reader.BitDepth);
        Assert.Equal(1, reader.FrameCount);
        Assert.Equal(BayerPattern.None, reader.Bayer);
        Assert.False(reader.IsBigEndian);
        Assert.Equal(1, reader.StripCount);

        Frame? actual = reader.ReadFrame();
        TestFrames.AssertEqual(source, actual!);
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void Writer_EmitsLittleEndianClassicTiff()
    {
        Frame frame = TestFrames.Create(3, 16, 3, 2);
        using var directory = new TempDir();
        string path = directory.PathFor("layout.tif");

        using (var writer = new TiffWriter(path, frame.Width, frame.Height, 3, 16))
            writer.Write(frame);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'I', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal(42, bytes[2] | (bytes[3] << 8));
        Assert.Equal(8u, (uint)(bytes[4] | (bytes[5] << 8) | (bytes[6] << 16) | (bytes[7] << 24)));
        Assert.Equal(11, bytes[8] | (bytes[9] << 8)); // entry count

        // 8 header + 2 count + 11*12 entries + 4 next-IFD; RGB BitsPerSample is 6 bytes at 146.
        Assert.Equal(146, FindTagValue(bytes, 258));
        Assert.Equal(152, FindTagValue(bytes, 273));

        using var reader = new TiffReader(new MemoryStream(bytes));
        TestFrames.AssertEqual(frame, reader.ReadFrame()!);
    }

    private static int FindTagValue(byte[] file, ushort wanted)
    {
        int count = file[8] | (file[9] << 8);
        for (int i = 0; i < count; i++)
        {
            int entry = 10 + i * 12;
            int tag = file[entry] | (file[entry + 1] << 8);
            if (tag == wanted)
                return (int)BitConverter.ToUInt32(file, entry + 8);
        }

        throw new InvalidOperationException($"tag {wanted} not found");
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(3, 8)]
    [InlineData(3, 16)]
    public void Reader_ReadsHandBuiltBigEndianFile(int channels, int bitDepth)
    {
        Frame source = TestFrames.Create(channels, bitDepth, width: 3, height: 4);
        byte[] file = TiffTestFile.Build(source, bigEndian: true);

        using var stream = new MemoryStream(file);
        using var reader = new TiffReader(stream);

        Assert.True(reader.IsBigEndian);
        Assert.Equal(1, reader.StripCount);
        TestFrames.AssertEqual(source, reader.ReadFrame()!);
    }

    [Fact]
    public void Reader_ReadsHandBuiltLittleEndianFile()
    {
        Frame source = TestFrames.Create(1, 16, width: 5, height: 3);
        byte[] file = TiffTestFile.Build(source, bigEndian: false);

        using var stream = new MemoryStream(file);
        using var reader = new TiffReader(stream);

        Assert.False(reader.IsBigEndian);
        TestFrames.AssertEqual(source, reader.ReadFrame()!);
    }

    [Fact]
    public void Reader_ReadsMultiStripRgb()
    {
        Frame source = TestFrames.Create(3, 8, width: 5, height: 5);
        byte[] file = TiffTestFile.Build(source, bigEndian: false, rowsPerStrip: 2);
        Assert.True(file.Length > 0);

        using var stream = new MemoryStream(file);
        using var reader = new TiffReader(stream);

        Assert.Equal(3, reader.StripCount);
        Assert.Equal(5, reader.Height);
        TestFrames.AssertEqual(source, reader.ReadFrame()!);
    }

    [Fact]
    public void Reader_RejectsCompressedTiff()
    {
        Frame source = TestFrames.Create(1, 8, width: 3, height: 2);
        byte[] file = TiffTestFile.Build(source, bigEndian: false, compression: 5); // LZW

        using var stream = new MemoryStream(file);
        var error = Assert.Throws<NotSupportedException>(() => new TiffReader(stream));
        Assert.Contains("compression", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reader_InvertsWhiteIsZero()
    {
        Frame source = TestFrames.Create(1, 8, width: 4, height: 3);
        byte[] file = TiffTestFile.Build(source, bigEndian: true, photometric: 0);

        using var stream = new MemoryStream(file);
        using var reader = new TiffReader(stream);
        Assert.True(reader.IsWhiteIsZero);

        Frame actual = reader.ReadFrame()!;
        var expected = new Frame(source.Width, source.Height, 1, 8);
        for (int i = 0; i < source.Samples.Length; i++)
            expected.Samples[i] = (ushort)(255 - source.Samples[i]);

        TestFrames.AssertEqual(expected, actual);
    }

    [Fact]
    public void Reader_RejectsTruncatedStrip()
    {
        Frame source = TestFrames.Create(1, 16, width: 4, height: 4);
        byte[] file = TiffTestFile.Build(source, bigEndian: false);

        // Chop the last few pixel bytes; the strip byte count still claims a full image.
        using var stream = new MemoryStream(file, 0, file.Length - 5);
        using var reader = new TiffReader(stream);
        Assert.Throws<InvalidDataException>(() => reader.ReadFrame());
    }

    [Fact]
    public void Writer_RejectsMismatchedFrame()
    {
        using var stream = new MemoryStream();
        using var writer = new TiffWriter(stream, 3, 2, 3, 8);
        Assert.Throws<ArgumentException>(() => writer.Write(TestFrames.Create(1, 8, 3, 2)));
    }
}
