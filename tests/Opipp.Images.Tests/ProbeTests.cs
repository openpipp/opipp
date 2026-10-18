using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Images.Tests;

public sealed class ProbeTests
{
    [Theory]
    [InlineData(1, 8, 1, 8)]
    [InlineData(3, 8, 3, 8)]
    [InlineData(1, 16, 1, 8)] // BMP cannot store 16-bit greyscale
    [InlineData(3, 16, 3, 8)]
    public void Bmp_ProbeReportsGeometryAndDepth(int channels, int bitDepth, int expectedChannels, int expectedDepth)
    {
        Frame frame = TestFrames.Create(channels, bitDepth, width: 9, height: 4);
        using var directory = new TempDir();
        string path = directory.PathFor("probe.bmp");

        using (var writer = new BmpWriter(path, frame.Width, frame.Height, channels, bitDepth))
            writer.Write(frame);

        ImageInfo info = BmpReader.Probe(new FileInfo(path));
        Assert.Equal(9, info.Width);
        Assert.Equal(4, info.Height);
        Assert.Equal(expectedChannels, info.Channels);
        Assert.Equal(expectedDepth, info.BitDepth);
        Assert.Equal(expectedChannels == 3, info.IsColour);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(3, 8)]
    [InlineData(3, 16)]
    public void Tiff_ProbeReportsGeometryAndDepth(int channels, int bitDepth)
    {
        Frame frame = TestFrames.Create(channels, bitDepth, width: 9, height: 4);
        using var directory = new TempDir();
        string path = directory.PathFor("probe.tif");

        using (var writer = new TiffWriter(path, frame.Width, frame.Height, channels, bitDepth))
            writer.Write(frame);

        ImageInfo info = TiffReader.Probe(new FileInfo(path));
        Assert.Equal(9, info.Width);
        Assert.Equal(4, info.Height);
        Assert.Equal(channels, info.Channels);
        Assert.Equal(bitDepth, info.BitDepth);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(3, 8)]
    [InlineData(3, 16)]
    public void Png_ProbeReportsGeometryAndDepth(int channels, int bitDepth)
    {
        Frame frame = TestFrames.Create(channels, bitDepth, width: 9, height: 4);
        using var directory = new TempDir();
        string path = directory.PathFor("probe.png");

        using (var writer = new PngWriter(path, frame.Width, frame.Height, channels, bitDepth))
            writer.Write(frame);

        ImageInfo info = PngReader.Probe(new FileInfo(path));
        Assert.Equal(9, info.Width);
        Assert.Equal(4, info.Height);
        Assert.Equal(channels, info.Channels);
        Assert.Equal(bitDepth, info.BitDepth);
    }

    [Fact]
    public void Probes_DoNotRequirePixelData()
    {
        Frame frame = TestFrames.Create(1, 8, width: 6, height: 3);
        using var directory = new TempDir();

        string bmpPath = directory.PathFor("header-only.bmp");
        using (var writer = new BmpWriter(bmpPath, frame.Width, frame.Height, 1, 8))
            writer.Write(frame);
        byte[] bmp = File.ReadAllBytes(bmpPath);
        int bmpOffset = (int)BitConverter.ToUInt32(bmp, 10);
        File.WriteAllBytes(bmpPath, bmp.AsSpan(0, bmpOffset).ToArray());
        ImageInfo bmpInfo = BmpReader.Probe(new FileInfo(bmpPath));
        Assert.Equal((6, 3, 8, 1), (bmpInfo.Width, bmpInfo.Height, bmpInfo.BitDepth, bmpInfo.Channels));

        string tiffPath = directory.PathFor("header-only.tif");
        using (var writer = new TiffWriter(tiffPath, frame.Width, frame.Height, 1, 8))
            writer.Write(frame);
        byte[] tiff = File.ReadAllBytes(tiffPath);
        int pixelOffset = FindTiffTagValue(tiff, 273);
        File.WriteAllBytes(tiffPath, tiff.AsSpan(0, pixelOffset).ToArray());
        ImageInfo tiffInfo = TiffReader.Probe(new FileInfo(tiffPath));
        Assert.Equal((6, 3, 8, 1), (tiffInfo.Width, tiffInfo.Height, tiffInfo.BitDepth, tiffInfo.Channels));

        string pngPath = directory.PathFor("header-only.png");
        using (var writer = new PngWriter(pngPath, frame.Width, frame.Height, 1, 8))
            writer.Write(frame);
        byte[] png = File.ReadAllBytes(pngPath);
        // Signature (8) + IHDR chunk header (8) + IHDR payload (13) + CRC (4).
        File.WriteAllBytes(pngPath, png.AsSpan(0, 8 + 8 + 13 + 4).ToArray());
        ImageInfo pngInfo = PngReader.Probe(new FileInfo(pngPath));
        Assert.Equal((6, 3, 8, 1), (pngInfo.Width, pngInfo.Height, pngInfo.BitDepth, pngInfo.Channels));
    }

    [Fact]
    public void Probe_RestoresSeekableStreamPosition()
    {
        Frame frame = TestFrames.Create(3, 8, 4, 2);
        using var directory = new TempDir();
        string path = directory.PathFor("position.png");
        using (var writer = new PngWriter(path, frame.Width, frame.Height, 3, 8))
            writer.Write(frame);

        using var stream = File.OpenRead(path);
        stream.Position = 3;
        Assert.Throws<InvalidDataException>(() => PngReader.Probe(stream));
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Bmp_ProbeReportsExpandedPaletteChannels()
    {
        byte[] palette = BmpTestFile.GreyPalette();
        palette[0] = 255; // make entry 0 non-grey
        byte[] file = BmpTestFile.Build(2, 2, 8, new[] { new byte[] { 0, 1 }, new byte[] { 1, 0 } }, palette);

        using var stream = new MemoryStream(file);
        ImageInfo info = BmpReader.Probe(stream);
        Assert.Equal(3, info.Channels);
        Assert.Equal(0, stream.Position);
    }

    private static int FindTiffTagValue(byte[] file, ushort wanted)
    {
        int count = file[8] | (file[9] << 8);
        for (int i = 0; i < count; i++)
        {
            int entry = 10 + i * 12;
            int tag = file[entry] | (file[entry + 1] << 8);
            if (tag == wanted)
            {
                return (int)BitConverter.ToUInt32(file, entry + 8);
            }
        }

        throw new InvalidOperationException($"tag {wanted} not found");
    }
}
