using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Fits.Tests;

/// <summary>
/// FITS stores each image unit as header + data, both padded to 2880-byte boundaries. 8-bit frames
/// carry one byte per sample while 16-bit frames carry two, so the data pad differs. These tests pin
/// the exact byte layout and walk the headers structurally at their computed offsets instead of
/// relying on the tolerant reader.
/// </summary>
public class FitsPaddingTests
{
    private const int Block = 2880;

    [Fact]
    public void Mono8_2x2_Occupies_Exactly_Two_2880_Byte_Blocks()
    {
        var source = new Frame(2, 2, 1, 8, new ushort[] { 10, 20, 30, 40 });
        byte[] bytes = WriteToBytes(w => w.Write(source));

        Assert.Equal(5760, bytes.Length);
        Assert.Equal(Block, HeaderLength(bytes, 0));

        // One byte per sample, rows bottom-up: row 1 (30, 40) then row 0 (10, 20).
        Assert.Equal(new byte[] { 30, 40, 10, 20 }, bytes[Block..(Block + 4)]);
        AssertZero(bytes, Block + 4, bytes.Length);

        List<FitsUnit> units = WalkUnits(bytes);
        FitsUnit unit = Assert.Single(units);
        Assert.Equal(4, unit.DataLength);
        Assert.Equal(2876, unit.PaddingLength);
        Assert.Equal(0, unit.End % Block);
    }

    [Fact]
    public void Rgb8_2x2_Occupies_Exactly_Two_2880_Byte_Blocks()
    {
        var source = new Frame(2, 2, 3, 8);
        for (int i = 0; i < source.SampleCount; i++)
        {
            source.Samples[i] = (ushort)i;
        }

        byte[] bytes = WriteToBytes(w => w.Write(source));

        Assert.Equal(5760, bytes.Length);
        string header = System.Text.Encoding.Latin1.GetString(bytes, 0, Block);
        Assert.Contains("BITPIX  =                    8", header);
        Assert.Contains("NAXIS   =                    3", header);
        Assert.Contains("NAXIS3  =                    3", header);

        // 12 bytes, plane-major (R, G, B) with rows bottom-up: R = 6,9,0,3; G = 7,10,1,4; B = 8,11,2,5.
        Assert.Equal(
            new byte[] { 6, 9, 0, 3, 7, 10, 1, 4, 8, 11, 2, 5 },
            bytes[Block..(Block + 12)]);
        AssertZero(bytes, Block + 12, bytes.Length);

        List<FitsUnit> units = WalkUnits(bytes);
        FitsUnit unit = Assert.Single(units);
        Assert.Equal(12, unit.DataLength);
        Assert.Equal(2868, unit.PaddingLength);
        Assert.Equal(0, unit.End % Block);
    }

    [Fact]
    public void Mono8_TwoFrames_Place_Second_Header_At_5760()
    {
        var first = new Frame(2, 2, 1, 8, new ushort[] { 1, 2, 3, 4 });
        var second = new Frame(2, 2, 1, 8, new ushort[] { 5, 6, 7, 8 });

        byte[] bytes = WriteToBytes(w =>
        {
            w.Write(first);
            w.Write(second);
        });

        Assert.Equal(11520, bytes.Length);

        List<FitsUnit> units = WalkUnits(bytes);
        Assert.Equal(2, units.Count);
        Assert.Equal(0, units[0].HeaderOffset);
        Assert.Equal(5760, units[1].HeaderOffset);
        Assert.Equal(0, units[1].HeaderOffset % Block);
        Assert.All(units, unit => Assert.Equal(Block, unit.HeaderLength));

        // The header at 5760 must be the extension for the second frame, not a shifted view of the first.
        string secondHeader = System.Text.Encoding.Latin1.GetString(bytes, 5760, 480);
        Assert.StartsWith("XTENSION= 'IMAGE", secondHeader);
        Assert.Equal(5.0, units[1].Header.GetReal("DATAMIN"));
        Assert.Equal(8.0, units[1].Header.GetReal("DATAMAX"));

        // Second frame's 4 data bytes start at 5760 + 2880, then padding closes the unit.
        Assert.Equal(new byte[] { 7, 8, 5, 6 }, bytes[8640..8644]);
        AssertZero(bytes, 8644, bytes.Length);
    }

    [Fact]
    public void Rgb8_TwoFrames_Place_Second_Header_At_5760()
    {
        var first = new Frame(2, 2, 3, 8);
        var second = new Frame(2, 2, 3, 8);
        for (int i = 0; i < first.SampleCount; i++)
        {
            first.Samples[i] = (ushort)i;
            second.Samples[i] = (ushort)(100 + i);
        }

        byte[] bytes = WriteToBytes(w =>
        {
            w.Write(first);
            w.Write(second);
        });

        Assert.Equal(11520, bytes.Length);

        List<FitsUnit> units = WalkUnits(bytes);
        Assert.Equal(2, units.Count);
        Assert.Equal(5760, units[1].HeaderOffset);
        Assert.Equal(12, units[1].DataLength);
        Assert.Equal(100.0, units[1].Header.GetReal("DATAMIN"));
        Assert.Equal(111.0, units[1].Header.GetReal("DATAMAX"));
    }

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(2, 2, 3)]
    [InlineData(100, 9, 1)]   // 900-byte payload: data stays inside one block
    [InlineData(60, 60, 1)]   // 3600-byte payload: data itself crosses a block boundary
    [InlineData(64, 48, 3)]   // 9216-byte payload: exactly three blocks of data
    public void EightBit_ThreeFrames_Produce_Aligned_Units_And_Correct_Header_Offsets(int width, int height, int channels)
    {
        var frames = new List<Frame>();
        for (int frameIndex = 0; frameIndex < 3; frameIndex++)
        {
            var frame = new Frame(width, height, channels, 8);
            for (int i = 0; i < frame.SampleCount; i++)
            {
                frame.Samples[i] = (ushort)((frameIndex * 50) + (i % 200));
            }

            frames.Add(frame);
        }

        byte[] bytes = WriteToBytes(w =>
        {
            foreach (Frame frame in frames)
            {
                w.Write(frame);
            }
        });

        Assert.Equal(0, bytes.Length % Block);

        List<FitsUnit> units = WalkUnits(bytes);
        Assert.Equal(frames.Count, units.Count);

        long expectedData = (long)width * height * channels;
        for (int i = 0; i < units.Count; i++)
        {
            FitsUnit unit = units[i];
            Assert.Equal(0, unit.HeaderOffset % Block);
            Assert.Equal(Block, unit.HeaderLength);
            Assert.Equal(8L, unit.Header.GetInteger("BITPIX"));
            Assert.Equal(channels == 3 ? 3 : 2, unit.Header.GetInt32("NAXIS", 0));
            Assert.Equal(width, unit.Header.GetInt32("NAXIS1", 0));
            Assert.Equal(height, unit.Header.GetInt32("NAXIS2", 0));
            Assert.Equal(expectedData, unit.DataLength);
            Assert.Equal(RoundUp(expectedData) - expectedData, unit.PaddingLength);
            Assert.Equal(0, unit.End % Block);

            // DATAMIN proves the header found at this offset is the one belonging to frame i.
            Assert.Equal((double)frames[i].Samples.Min(), unit.Header.GetReal("DATAMIN"));
            AssertZero(bytes, unit.DataOffset + unit.DataLength, unit.End);
        }

        Assert.Equal(bytes.Length, units[^1].End);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EightBit_Mono_Samples_RoundTrip_Through_The_Reader(int frameCount)
    {
        var frames = new List<Frame>();
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new Frame(5, 4, 1, 8);
            for (int i = 0; i < frame.SampleCount; i++)
            {
                frame.Samples[i] = (ushort)((frameIndex * 60) + i);
            }

            frames.Add(frame);
        }

        byte[] bytes = WriteToBytes(w =>
        {
            foreach (Frame frame in frames)
            {
                w.Write(frame);
            }
        });

        Assert.Equal(frameCount * 5760, bytes.Length);

        using var reader = new FitsReader(new MemoryStream(bytes, writable: false), leaveOpen: false);
        Assert.Equal(frameCount, reader.FrameCount);
        Assert.Equal(8, reader.BitDepth);
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(8, frame.BitDepth);
            Assert.Equal(frames[frameIndex].Samples, frame.Samples);
        }

        Assert.Null(reader.ReadFrame());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EightBit_Rgb_Samples_RoundTrip_Through_The_Reader(int frameCount)
    {
        var frames = new List<Frame>();
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new Frame(4, 3, 3, 8);
            for (int i = 0; i < frame.SampleCount; i++)
            {
                frame.Samples[i] = (ushort)((frameIndex * 30) + i);
            }

            frames.Add(frame);
        }

        byte[] bytes = WriteToBytes(w =>
        {
            foreach (Frame frame in frames)
            {
                w.Write(frame);
            }
        });

        using var reader = new FitsReader(new MemoryStream(bytes, writable: false), leaveOpen: false);
        Assert.Equal(frameCount, reader.FrameCount);
        Assert.Equal(3, reader.Channels);
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(8, frame.BitDepth);
            Assert.Equal(frames[frameIndex].Samples, frame.Samples);
        }

        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void SixteenBit_Units_Remain_2880_Aligned_After_The_Eight_Bit_Fix()
    {
        var first = new Frame(2, 2, 1, 16);
        var second = new Frame(2, 2, 1, 16);
        for (int i = 0; i < first.SampleCount; i++)
        {
            first.Samples[i] = (ushort)(1000 + i);
            second.Samples[i] = (ushort)(2000 + i);
        }

        byte[] bytes = WriteToBytes(w =>
        {
            w.Write(first);
            w.Write(second);
        });

        Assert.Equal(11520, bytes.Length);

        List<FitsUnit> units = WalkUnits(bytes);
        Assert.Equal(2, units.Count);
        Assert.Equal(5760, units[1].HeaderOffset);
        Assert.All(units, unit =>
        {
            Assert.Equal(Block, unit.HeaderLength);
            Assert.Equal(8, unit.DataLength);
            Assert.Equal(2872, unit.PaddingLength);
            Assert.Equal(0, unit.End % Block);
        });
        AssertZero(bytes, 2888, 5760);
    }

    // ---------------------------------------------------------------- pinned layout

    [Fact]
    public void EightBit_File_Layout_Is_Hash_Pinned()
    {
        // Byte-exact anchor for the corrected 8-bit layout: 2880-byte header, one byte per sample,
        // and a pad that closes the unit at 5760. DATE is fixed so the header is deterministic.
        var mono = new Frame(2, 2, 1, 8, new ushort[] { 10, 20, 30, 40 });
        var rgb = new Frame(2, 2, 3, 8, new ushort[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 });

        Assert.Equal(
            "04C18724EAFDBA94E7979F9E939669826E567553157EB74DB43FA0D21BB47ABA",
            HashOf(mono));
        Assert.Equal(
            "ED3C0117859305AB827941B9319DD073D9AAF8011FBC18CE0EB37F92C07266A6",
            HashOf(rgb));
    }

    // ---------------------------------------------------------------- helpers

    private sealed record FitsUnit(int HeaderOffset, int HeaderLength, int DataLength, int PaddingLength, FitsHeader Header)
    {
        public int DataOffset => HeaderOffset + HeaderLength;

        public int End => DataOffset + DataLength + PaddingLength;
    }

    /// <summary>
    /// Walks the file unit by unit using only the bytes each header declares, so a header written at
    /// the wrong offset shows up as a parse failure or a mismatched DATAMIN rather than being hidden
    /// by the reader's tolerance.
    /// </summary>
    private static List<FitsUnit> WalkUnits(byte[] bytes)
    {
        var units = new List<FitsUnit>();
        int position = 0;
        while (position < bytes.Length)
        {
            using var stream = new MemoryStream(bytes, position, bytes.Length - position, writable: false);
            FitsHeader header = FitsHeader.Read(stream, out int headerLength);

            int naxis = header.GetInt32("NAXIS", 0);
            int bitPix = (int)(header.GetInteger("BITPIX") ?? 0);
            long samples = 1;
            for (int axis = 1; axis <= naxis; axis++)
            {
                samples *= header.GetInteger($"NAXIS{axis}") ?? 0;
            }

            long dataLength = samples * (Math.Abs(bitPix) / 8);
            int paddingLength = checked((int)(RoundUp(dataLength) - dataLength));
            units.Add(new FitsUnit(position, headerLength, checked((int)dataLength), paddingLength, header));

            position = checked(position + headerLength + (int)dataLength + paddingLength);
        }

        return units;
    }

    private static int HeaderLength(byte[] bytes, int offset)
    {
        using var stream = new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
        FitsHeader.Read(stream, out int consumed);
        return consumed;
    }

    private static byte[] WriteToBytes(Action<FitsWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new FitsWriter(stream, leaveOpen: true))
        {
            write(writer);
        }

        return stream.ToArray();
    }

    private static long RoundUp(long value) => ((value + Block - 1) / Block) * Block;

    private static string HashOf(Frame frame)
    {
        byte[] bytes = WriteToBytes(w =>
        {
            w.Date = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            w.Write(frame);
        });

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static void AssertZero(byte[] bytes, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            Assert.True(bytes[i] == 0, $"Expected zero FITS padding at offset {i}, found 0x{bytes[i]:X2}.");
        }
    }
}
