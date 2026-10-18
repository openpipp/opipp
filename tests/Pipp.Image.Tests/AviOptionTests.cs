namespace Pipp.Image.Tests;

using System.Security.Cryptography;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Xunit;

/// <summary>
/// AVI codec and quality option tests. The engine's <c>-avi_codec</c> accepts 0..10 where 0 is DIB,
/// 1 is ULRG (Ut Video) and everything else is FMP4; <c>-avi_quality</c> only accepts 1..10 and only
/// changes the lossy FMP4 stream (verified: quality 1..10 produced ten distinct files of identical
/// size, while the DIB path ignored it and always writes <c>dwQuality = 0xFFFFFFFF</c>).
/// </summary>
public class AviOptionTests
{
    private static Frame MonoFrame(int width, int height, byte value)
    {
        var samples = new ushort[width * height];
        Array.Fill(samples, (ushort)value);
        return new Frame(width, height, 1, 8, samples);
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "pipp-avi-" + Guid.NewGuid().ToString("N") + ".avi");

    [Theory]
    [InlineData(AviCodec.Ulrg)]
    [InlineData(AviCodec.Fmp4)]
    public void NonDibCodec_IsRejectedWithAClearMessage(AviCodec codec)
    {
        string path = TempFile();
        try
        {
            var error = Assert.Throws<NotSupportedException>(() =>
                new AviWriter(path, 8, 8, new AviWriterOptions { Codec = codec }));
            Assert.Contains("not supported", error.Message);
            Assert.Contains("DIB", error.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DibCodec_WritesUncompressedFrames()
    {
        string path = TempFile();
        try
        {
            using (var writer = new AviWriter(path, 8, 6, new AviWriterOptions { Codec = AviCodec.Dib, Quality = 1 }))
            {
                writer.Write(MonoFrame(8, 6, 77));
                Assert.Equal(1, writer.FrameCount);
            }

            AviInfo info = AviProbe.GetInfo(path);
            Assert.Equal(8, info.Width);
            Assert.Equal(6, info.Height);
            Assert.Equal(8, info.BitCount);
            Assert.Equal("BI_RGB", info.FourCC);
            Assert.Equal(1, info.FrameCount);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(100)]
    public void QualityOutsideTheEngineRange_IsRejected(int quality)
    {
        string path = TempFile();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AviWriter(path, 8, 8, new AviWriterOptions { Quality = quality }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Quality_DoesNotChangeTheUncompressedStream()
    {
        var hashes = new List<string>();
        var paths = new List<string>();
        try
        {
            foreach (int quality in new[] { 1, 10 })
            {
                string path = TempFile();
                paths.Add(path);
                using (var writer = new AviWriter(path, 16, 12, new AviWriterOptions { Quality = quality }))
                {
                    writer.Write(MonoFrame(16, 12, 42));
                }

                hashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            }

            // DIB has no quality parameter; the engine also writes dwQuality = 0xFFFFFFFF for it.
            Assert.Equal(hashes[0], hashes[1]);
        }
        finally
        {
            foreach (string path in paths)
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
