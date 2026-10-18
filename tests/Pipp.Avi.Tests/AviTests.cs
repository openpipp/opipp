using Pipp.Core.Formats;
using Pipp.Core.Frames;

namespace Pipp.Avi.Tests;

public sealed class AviTests
{
    [Fact]
    public void Mono8_RoundTrips_ThreeFrames_WithExactPixelValues()
    {
        InTempFile(path =>
        {
            const int width = 5;
            const int height = 4;
            Frame[] frames = Enumerable.Range(0, 3).Select(i => AviTestSupport.MonoFrame(width, height, i)).ToArray();

            using (var writer = new AviWriter(path, width, height, new AviWriterOptions { FpsRate = 30, FpsScale = 1 }))
            {
                foreach (Frame frame in frames)
                    writer.Write(frame);
                Assert.Equal(3, writer.FrameCount);
            }

            using var reader = new AviReader(path);
            Assert.Equal(width, reader.Width);
            Assert.Equal(height, reader.Height);
            Assert.Equal(1, reader.Channels);
            Assert.Equal(8, reader.BitDepth);
            Assert.Equal(3, reader.FrameCount);
            Assert.Equal("BI_RGB", reader.FourCC);

            for (int i = 0; i < 3; i++)
            {
                Frame? actual = reader.ReadFrame();
                Assert.NotNull(actual);
                Assert.Equal(i, actual!.Index);
                Assert.Equal(width, actual.Width);
                Assert.Equal(height, actual.Height);
                Assert.True(frames[i].Samples.SequenceEqual(actual.Samples),
                    $"frame {i} samples differ after round-trip");
            }

            Assert.Null(reader.ReadFrame());
            Assert.Equal(3, reader.Position);
        });
    }

    [Fact]
    public void Colour24_RoundTrips_PreservingBgrOrderAndBottomUpRows()
    {
        InTempFile(path =>
        {
            const int width = 4;
            const int height = 3;
            Frame source = AviTestSupport.ColourFrame(width, height, seed: 1);

            using (var writer = new AviWriter(path, width, height, new AviWriterOptions { PixelFormat = AviPixelFormat.Bgr24 }))
            {
                writer.Write(source);
            }

            AviInfo info = AviProbe.GetInfo(path);
            Assert.Equal(width, info.Width);
            Assert.Equal(height, info.Height);
            Assert.Equal(24, info.BitCount);
            Assert.Equal("BI_RGB", info.FourCC);
            Assert.Equal(1, info.FrameCount);

            using var reader = new AviReader(path);
            Assert.Equal(3, reader.Channels);
            Assert.Equal(8, reader.BitDepth);

            Frame? actual = reader.ReadFrame();
            Assert.NotNull(actual);
            Assert.Equal(width, actual!.Width);
            Assert.Equal(height, actual.Height);

            // The pattern differs by row and channel, so a missing bottom-up flip or a BGR/RGB swap fails here.
            Assert.True(source.Samples.SequenceEqual(actual.Samples), "colour samples differ after round-trip");
            Assert.Equal((ushort)60, actual[0, 0, 0]);
            Assert.Equal((ushort)120, actual[0, 0, 1]);
            Assert.Equal((ushort)230, actual[0, 0, 2]);
        });
    }

    [Fact]
    public void OddWidthFrames_ArePaddedToFourByteRows()
    {
        InTempFile(path =>
        {
            using (var writer = new AviWriter(path, 3, 2, new AviWriterOptions { PixelFormat = AviPixelFormat.Bgr24 }))
            {
                writer.Write(AviTestSupport.ColourFrame(3, 2, seed: 0));
            }

            byte[] bytes = File.ReadAllBytes(path);
            var chunks = AviTestSupport.WalkChunks(bytes);
            var data = chunks.Single(c => c.Id == "00db");
            var strf = chunks.Single(c => c.Id == "strf");

            // 3 pixels * 3 bytes = 9 bytes per row, DWORD-aligned to 12; 2 rows = 24 bytes.
            Assert.Equal(24, data.Size);
            Assert.Equal(24u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 20)); // biSizeImage
            Assert.Equal(24, AviTestSupport.ReadUInt16(bytes, strf.DataOffset + 14)); // biBitCount
            Assert.Equal(2, AviTestSupport.ReadInt32(bytes, strf.DataOffset + 8)); // positive = bottom-up
        });

        InTempFile(path =>
        {
            using (var writer = new AviWriter(path, 5, 4))
            {
                writer.Write(AviTestSupport.MonoFrame(5, 4, seed: 0));
            }

            byte[] bytes = File.ReadAllBytes(path);
            var chunks = AviTestSupport.WalkChunks(bytes);
            var data = chunks.Single(c => c.Id == "00db");
            var strf = chunks.Single(c => c.Id == "strf");

            // 5 bytes per row DWORD-aligns to 8; 4 rows = 32 bytes, and the chunk stays even.
            Assert.Equal(32, data.Size);
            Assert.Equal(32u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 20));
            Assert.Equal(1064, strf.Size); // 40-byte header + 256 BGR0 palette entries
            Assert.Equal(256u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 32)); // biClrUsed
        });
    }

    [Fact]
    public void WrittenFile_ContainsChunksInOrder_AndIndexPointsAtEveryDataChunk()
    {
        InTempFile(path =>
        {
            const int width = 5;
            const int height = 4;
            using (var writer = new AviWriter(path, width, height))
            {
                for (int i = 0; i < 3; i++)
                    writer.Write(AviTestSupport.MonoFrame(width, height, i));
            }

            byte[] bytes = File.ReadAllBytes(path);
            var chunks = AviTestSupport.WalkChunks(bytes);
            string[] ids = chunks.Select(c => c.Id).ToArray();

            Assert.Equal(
                new[] { "RIFF", "AVI ", "hdrl", "avih", "strl", "strh", "strf", "movi", "00db", "00db", "00db", "idx1" },
                ids);

            Assert.Equal((uint)(bytes.Length - 8), AviTestSupport.ReadUInt32(bytes, 4));

            var avih = chunks.Single(c => c.Id == "avih");
            Assert.Equal(56, avih.Size);
            Assert.Equal(40000u, AviTestSupport.ReadUInt32(bytes, avih.DataOffset)); // 25 fps
            Assert.NotEqual(0u, AviTestSupport.ReadUInt32(bytes, avih.DataOffset + 4)); // max bytes/sec
            Assert.Equal(0x10u, AviTestSupport.ReadUInt32(bytes, avih.DataOffset + 12) & 0x10u); // AVIF_HASINDEX
            Assert.Equal(3u, AviTestSupport.ReadUInt32(bytes, avih.DataOffset + 16)); // total frames
            Assert.Equal(1u, AviTestSupport.ReadUInt32(bytes, avih.DataOffset + 24)); // streams
            Assert.Equal((uint)width, AviTestSupport.ReadUInt32(bytes, avih.DataOffset + 32));
            Assert.Equal((uint)height, AviTestSupport.ReadUInt32(bytes, avih.DataOffset + 36));

            var strh = chunks.Single(c => c.Id == "strh");
            Assert.Equal(56, strh.Size);
            Assert.Equal("vids", AviTestSupport.FccText(AviTestSupport.ReadUInt32(bytes, strh.DataOffset)));
            Assert.Equal("DIB ", AviTestSupport.FccText(AviTestSupport.ReadUInt32(bytes, strh.DataOffset + 4)));
            Assert.Equal(1u, AviTestSupport.ReadUInt32(bytes, strh.DataOffset + 20)); // scale
            Assert.Equal(25u, AviTestSupport.ReadUInt32(bytes, strh.DataOffset + 24)); // rate
            Assert.Equal(3u, AviTestSupport.ReadUInt32(bytes, strh.DataOffset + 32)); // length
            Assert.Equal(40u, AviTestSupport.ReadUInt32(bytes, strh.DataOffset + 36)); // suggested buffer = 8 + 32
            Assert.Equal((ushort)0, AviTestSupport.ReadUInt16(bytes, strh.DataOffset + 48)); // rcFrame.left
            Assert.Equal((ushort)width, AviTestSupport.ReadUInt16(bytes, strh.DataOffset + 52)); // rcFrame.right
            Assert.Equal((ushort)height, AviTestSupport.ReadUInt16(bytes, strh.DataOffset + 54)); // rcFrame.bottom

            var strf = chunks.Single(c => c.Id == "strf");
            Assert.Equal(40u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset)); // biSize
            Assert.Equal(width, AviTestSupport.ReadInt32(bytes, strf.DataOffset + 4));
            Assert.Equal(height, AviTestSupport.ReadInt32(bytes, strf.DataOffset + 8));
            Assert.Equal((ushort)1, AviTestSupport.ReadUInt16(bytes, strf.DataOffset + 12));
            Assert.Equal((ushort)8, AviTestSupport.ReadUInt16(bytes, strf.DataOffset + 14));
            Assert.Equal(0u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 16)); // BI_RGB
            Assert.Equal(32u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 20)); // biSizeImage

            // Palette is 256 grayscale entries stored B,G,R,0.
            Assert.Equal(0, bytes[strf.DataOffset + 40]);
            Assert.Equal(0, bytes[strf.DataOffset + 41]);
            Assert.Equal(0, bytes[strf.DataOffset + 42]);
            Assert.Equal(255, bytes[strf.DataOffset + 40 + 255 * 4]);
            Assert.Equal(255, bytes[strf.DataOffset + 40 + 255 * 4 + 1]);
            Assert.Equal(255, bytes[strf.DataOffset + 40 + 255 * 4 + 2]);

            var movi = chunks.Single(c => c.Id == "movi");
            var idx1 = chunks.Single(c => c.Id == "idx1");
            Assert.Equal(idx1.Offset, movi.Offset + 8 + movi.Size);
            Assert.Equal(3 * 16, idx1.Size);

            var dataChunks = chunks.Where(c => c.Id == "00db").ToArray();
            for (int i = 0; i < 3; i++)
            {
                int entry = idx1.DataOffset + i * 16;
                Assert.Equal("00db", AviTestSupport.FccText(AviTestSupport.ReadUInt32(bytes, entry)));
                Assert.Equal(0x10u, AviTestSupport.ReadUInt32(bytes, entry + 4)); // AVIIF_KEYFRAME
                // idx1 offsets are relative to the 'movi' FourCC; first chunk sits at +4.
                Assert.Equal((uint)(dataChunks[i].Offset - movi.DataOffset), AviTestSupport.ReadUInt32(bytes, entry + 8));
                Assert.Equal((uint)dataChunks[i].Size, AviTestSupport.ReadUInt32(bytes, entry + 12));
                Assert.Equal(32, dataChunks[i].Size);
            }
        });
    }

    [Fact]
    public void Reader_RejectsCompressedFourCC_WithFfmpegGuidance()
    {
        InTempFile(path =>
        {
            byte[] bytes = AviTestSupport.BuildAvi(
                2, 1, 24, AviTestSupport.Fcc("FMP4"), biHeight: 1, frames: new[] { new byte[8] });
            File.WriteAllBytes(path, bytes);

            var exception = Assert.Throws<NotSupportedException>(() =>
            {
                using var reader = new AviReader(path);
            });

            Assert.Contains("FMP4", exception.Message);
            Assert.Contains("FFmpeg", exception.Message);

            // Probing must still report the metadata without decoding.
            AviInfo info = AviProbe.GetInfo(path);
            Assert.Equal(2, info.Width);
            Assert.Equal(1, info.Height);
            Assert.Equal(24, info.BitCount);
            Assert.Equal("FMP4", info.FourCC);
            Assert.Equal(1, info.FrameCount);
        });
    }

    [Fact]
    public void Probe_ReturnsExpectedMetadata()
    {
        InTempFile(path =>
        {
            const int width = 7;
            const int height = 3;
            using (var writer = new AviWriter(path, width, height, new AviWriterOptions { FpsRate = 60, FpsScale = 2 }))
            {
                writer.Write(AviTestSupport.MonoFrame(width, height, 0));
                writer.Write(AviTestSupport.MonoFrame(width, height, 1));
            }

            AviInfo info = AviProbe.GetInfo(path);
            Assert.Equal(width, info.Width);
            Assert.Equal(height, info.Height);
            Assert.Equal(8, info.BitCount);
            Assert.Equal("BI_RGB", info.FourCC);
            Assert.Equal(60, info.FpsRate);
            Assert.Equal(2, info.FpsScale);
            Assert.Equal(30.0, info.Fps, 3);
            Assert.Equal(2, info.FrameCount);
            Assert.Contains("7x3", info.ToString());
        });
    }

    [Fact]
    public void Y800_RoundTrips_TopDownWithoutPalette()
    {
        InTempFile(path =>
        {
            const int width = 5;
            const int height = 3;
            Frame[] frames = Enumerable.Range(0, 3).Select(i => AviTestSupport.MonoFrame(width, height, i)).ToArray();

            using (var writer = new AviWriter(
                       path, width, height, new AviWriterOptions { PixelFormat = AviPixelFormat.Y800, FpsRate = 15 }))
            {
                foreach (Frame frame in frames)
                    writer.Write(frame);
            }

            AviInfo info = AviProbe.GetInfo(path);
            Assert.Equal(8, info.BitCount);
            Assert.Equal("Y800", info.FourCC);
            Assert.Equal(3, info.FrameCount);
            Assert.Equal(15, info.FpsRate);

            using (var reader = new AviReader(path))
            {
                Assert.Equal(1, reader.Channels);
                Assert.Equal(3, reader.FrameCount);
                for (int i = 0; i < frames.Length; i++)
                {
                    Frame? actual = reader.ReadFrame();
                    Assert.NotNull(actual);
                    Assert.True(frames[i].Samples.SequenceEqual(actual!.Samples));
                }
            }

            byte[] bytes = File.ReadAllBytes(path);
            var chunks = AviTestSupport.WalkChunks(bytes);
            var strf = chunks.Single(c => c.Id == "strf");
            Assert.Equal(40, strf.Size); // no palette
            Assert.Equal((ushort)8, AviTestSupport.ReadUInt16(bytes, strf.DataOffset + 14));
            Assert.Equal("Y800", AviTestSupport.FccText(AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 16)));
            Assert.Equal(-height, AviTestSupport.ReadInt32(bytes, strf.DataOffset + 8)); // top-down marker
            Assert.Equal((uint)(width * height), AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 20));
            Assert.Equal(0u, AviTestSupport.ReadUInt32(bytes, strf.DataOffset + 32)); // biClrUsed

            var dataChunks = chunks.Where(c => c.Id == "00db").ToArray();
            Assert.Equal(3, dataChunks.Length);
            Assert.Equal(width * height, dataChunks[0].Size); // 15 bytes, odd
            // Top-down rows: the first chunk bytes are row 0 of the first frame.
            for (int x = 0; x < width; x++)
                Assert.Equal((byte)frames[0].Samples[x], bytes[dataChunks[0].DataOffset + x]);

            // Odd chunk sizes are followed by one pad byte before the next chunk header.
            Assert.Equal(dataChunks[0].Offset + 8 + width * height + 1, dataChunks[1].Offset);
        });
    }

    [Fact]
    public void Reader_AppliesColourPaletteFromStrf()
    {
        InTempFile(path =>
        {
            var palette = new byte[256 * 4];
            palette[0] = 0; // blue
            palette[1] = 0; // green
            palette[2] = 255; // red
            palette[4] = 255; // blue
            palette[5] = 0;
            palette[6] = 0;

            // 2x1 8bpp DIB: one row of 2 pixels plus 2 bytes of DWORD padding.
            byte[] payload = { 0, 1, 0, 0 };
            byte[] bytes = AviTestSupport.BuildAvi(2, 1, 8, 0, biHeight: 1, frames: new[] { payload }, paletteBgra: palette);
            File.WriteAllBytes(path, bytes);

            using var reader = new AviReader(path);
            Assert.Equal(3, reader.Channels);

            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal((ushort)255, frame![0, 0, 0]);
            Assert.Equal((ushort)0, frame[0, 0, 1]);
            Assert.Equal((ushort)0, frame[0, 0, 2]);
            Assert.Equal((ushort)0, frame[1, 0, 0]);
            Assert.Equal((ushort)0, frame[1, 0, 1]);
            Assert.Equal((ushort)255, frame[1, 0, 2]);
        });
    }

    [Fact]
    public void Reader_HandlesDcChunksAndMissingIndex()
    {
        InTempFile(path =>
        {
            // 2x1 8bpp, 4 bytes per row; '00dc' id and no idx1 at all.
            byte[] bytes = AviTestSupport.BuildAvi(
                2, 1, 8, 0, biHeight: 1, frames: new[] { new byte[] { 5, 6, 0, 0 } },
                chunkId: "00dc", includeIndex: false);
            File.WriteAllBytes(path, bytes);

            AviInfo info = AviProbe.GetInfo(path);
            Assert.Equal(1, info.FrameCount);

            using var reader = new AviReader(path);
            Assert.Equal(1, reader.FrameCount);
            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal((ushort)5, frame![0, 0]);
            Assert.Equal((ushort)6, frame[1, 0]);
            Assert.Null(reader.ReadFrame());
        });
    }

    [Fact]
    public void Reader_TreatsY800AsTopDown_EvenWithPositiveBiHeight()
    {
        InTempFile(path =>
        {
            // 5x2 Y800 as ffmpeg writes it: positive biHeight but top-down scanlines.
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            byte[] bytes = AviTestSupport.BuildAvi(
                5, 2, 8, AviTestSupport.Fcc("Y800"), biHeight: 2, frames: new[] { payload });
            File.WriteAllBytes(path, bytes);

            using var reader = new AviReader(path);
            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal((ushort)1, frame![0, 0]);
            Assert.Equal((ushort)5, frame[4, 0]);
            Assert.Equal((ushort)6, frame[0, 1]);
            Assert.Equal((ushort)10, frame[4, 1]);
        });
    }

    [Fact]
    public void Reader_HonoursNegativeDibHeightAsTopDown()
    {
        InTempFile(path =>
        {
            // 2x2 24bpp DIB with negative biHeight; rows are stored top-down, each padded to 8 bytes.
            byte[] payload =
            {
                1, 2, 3, 4, 5, 6, 0, 0,
                7, 8, 9, 10, 11, 12, 0, 0,
            };
            byte[] bytes = AviTestSupport.BuildAvi(2, 2, 24, 0, biHeight: -2, frames: new[] { payload });
            File.WriteAllBytes(path, bytes);

            using var reader = new AviReader(path);
            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal((ushort)3, frame![0, 0, 0]);
            Assert.Equal((ushort)2, frame[0, 0, 1]);
            Assert.Equal((ushort)1, frame[0, 0, 2]);
            Assert.Equal((ushort)9, frame[0, 1, 0]);
            Assert.Equal((ushort)8, frame[0, 1, 1]);
            Assert.Equal((ushort)7, frame[0, 1, 2]);
        });
    }

    [Fact]
    public void Writer_RejectsMismatchedFramesAnd16BitSamples()
    {
        InTempFile(path =>
        {
            using var writer = new AviWriter(path, 4, 2);

            var wrongSize = new Frame(4, 3, 1, 8);
            Assert.Throws<ArgumentException>(() => writer.Write(wrongSize));

            var wrongChannels = new Frame(4, 2, 3, 8);
            Assert.Throws<ArgumentException>(() => writer.Write(wrongChannels));

            var deep = new Frame(4, 2, 1, 16);
            Assert.Throws<NotSupportedException>(() => writer.Write(deep));
        });
    }

    private static void InTempFile(Action<string> test)
    {
        string path = AviTestSupport.TempPath();
        try
        {
            test(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
