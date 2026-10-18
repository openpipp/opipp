namespace Opipp.Image.Tests;

using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;

/// <summary>
/// Contract tests for <see cref="MasterTmpFile"/>, the engine's private <c>pipp_master_*.tmp</c>
/// container. The golden fixtures are byte-for-byte output of the shipped
/// <c>pipp_dll!stack_frames</c> (driven through its export table with two identical 4x4 TIFF inputs),
/// so the header fields, the bottom-up row order and the B,G,R sample order are all oracle data, not
/// restatements of the implementation.
/// </summary>
public class MasterTmpFileTests
{
    // stack_frames(2 identical 4x4 TIFFs), output named .tmp:
    //   mono 8-bit, samples 1..16
    private const string EngineMono8 =
        "04000000040000000000000001000000ff0000000000000000000000000000000000000000000000" +
        "0d0e0f10090a0b0c0506070801020304";

    //   mono 16-bit, sample(x,y) = 1000 + 100y + x
    private const string EngineMono16 =
        "04000000040000000000000002000000ffff00000000000000000000000000000000000000000000" +
        "1405150516051705b004b104b204b3044c044d044e044f04e803e903ea03eb03";

    //   colour 8-bit, sample(x,y) = (R 10+x, G 40+y, B 70+x+y)
    private const string EngineColour8 =
        "04000000040000000100000001000000ff0000000000000000000000000000000000000000000000" +
        "492b0a4a2b0b4b2b0c4c2b0d482a0a492a0b4a2a0c4b2a0d47290a48290b49290c4a290d46280a47280b48280c49280d";

    private static Frame Mono(int width, int height, int bitDepth, Func<int, int, ushort> sample)
    {
        var frame = new Frame(width, height, 1, bitDepth);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                frame[x, y] = sample(x, y);
        return frame;
    }

    private static Frame ReadHex(string hex) => MasterTmpFile.Read(Convert.FromHexString(hex));

    // ------------------------------------------------------------ engine output

    [Fact]
    public void Read_EngineMono8_IsTopDownAndKeepsValues()
    {
        Frame frame = ReadHex(EngineMono8);

        Assert.Equal(4, frame.Width);
        Assert.Equal(4, frame.Height);
        Assert.Equal(1, frame.Channels);
        Assert.Equal(8, frame.BitDepth);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(1 + y * 4 + x, frame[x, y]);
    }

    [Fact]
    public void Read_EngineMono16_IsLittleEndianTopDown()
    {
        Frame frame = ReadHex(EngineMono16);

        Assert.Equal(1, frame.Channels);
        Assert.Equal(16, frame.BitDepth);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(1000 + 100 * y + x, frame[x, y]);
    }

    [Fact]
    public void Read_EngineColour8_ReordersBgrToRgb()
    {
        Frame frame = ReadHex(EngineColour8);

        Assert.Equal(3, frame.Channels);
        Assert.Equal(8, frame.BitDepth);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(10 + x, frame[x, y, 0]);
                Assert.Equal(40 + y, frame[x, y, 1]);
                Assert.Equal(70 + x + y, frame[x, y, 2]);
            }
        }
    }

    [Fact]
    public void Read_EngineHeaders_HaveNoMagicAndRecordGeometryDepthAndFlag()
    {
        foreach (var (hex, colour, bytesPerSample, maxValue) in
                 new[] { (EngineMono8, 0u, 1u, 255u), (EngineMono16, 0u, 2u, 65535u), (EngineColour8, 1u, 1u, 255u) })
        {
            byte[] bytes = Convert.FromHexString(hex);
            Assert.Equal(4u, BitConverter.ToUInt32(bytes, 0));
            Assert.Equal(4u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(colour, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(bytesPerSample, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(maxValue, BitConverter.ToUInt32(bytes, 16));
            Assert.All(bytes[20..MasterTmpFile.HeaderSize], b => Assert.Equal(0, b));
            Assert.Equal(MasterTmpFile.HeaderSize, bytes.Length - 16 * (int)bytesPerSample * (colour == 1 ? 3 : 1));
        }
    }

    // ------------------------------------------------------------ round trips

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(3, 8)]
    [InlineData(3, 16)]
    public void Write_ThenRead_RoundTripsEveryDepthAndChannelCount(int channels, int bitDepth)
    {
        int width = 5, height = 3;
        var original = new Frame(width, height, channels, bitDepth);
        ushort max = bitDepth == 8 ? (ushort)255 : (ushort)65535;
        for (int i = 0; i < original.Samples.Length; i++)
            original.Samples[i] = (ushort)((i * 37 + 11) % (max + 1));

        using var stream = new MemoryStream();
        MasterTmpFile.Write(original, stream, leaveOpen: true);

        Frame restored = MasterTmpFile.Read(stream.ToArray());
        Assert.Equal(width, restored.Width);
        Assert.Equal(height, restored.Height);
        Assert.Equal(channels, restored.Channels);
        Assert.Equal(bitDepth, restored.BitDepth);
        Assert.Equal(original.Samples, restored.Samples);
    }

    [Fact]
    public void Write_StoresRowsBottomUpAndColourAsBgr()
    {
        var mono = new Frame(2, 2, 1, 8, [1, 2, 3, 4]);
        using (var stream = new MemoryStream())
        {
            MasterTmpFile.Write(mono, stream, leaveOpen: true);
            byte[] bytes = stream.ToArray();
            Assert.Equal(new byte[] { 3, 4, 1, 2 }, bytes[MasterTmpFile.HeaderSize..]);
        }

        var colour = new Frame(1, 1, 3, 8, [10, 20, 30]); // R, G, B
        using (var stream = new MemoryStream())
        {
            MasterTmpFile.Write(colour, stream, leaveOpen: true);
            byte[] bytes = stream.ToArray();
            Assert.Equal(new byte[] { 30, 20, 10 }, bytes[MasterTmpFile.HeaderSize..]);
        }
    }

    [Fact]
    public void Write_UsesTheSameHeaderAsTheEngine()
    {
        var frame = new Frame(4, 4, 3, 16);
        using var stream = new MemoryStream();
        MasterTmpFile.Write(frame, stream, leaveOpen: true);

        byte[] header = stream.ToArray()[..MasterTmpFile.HeaderSize];
        Assert.Equal(BitConverter.GetBytes(4u), header[..4]);
        Assert.Equal(BitConverter.GetBytes(4u), header[4..8]);
        Assert.Equal(BitConverter.GetBytes(1u), header[8..12]);
        Assert.Equal(BitConverter.GetBytes(2u), header[12..16]);
        Assert.Equal(BitConverter.GetBytes(65535u), header[16..20]);
        Assert.All(header[20..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Write_ReproducesEngineBytesExactly()
    {
        // Same three images the DLL produced the fixtures from: if the writer is right, the files are
        // byte-identical, header padding included.
        Frame mono8 = Mono(4, 4, 8, (x, y) => (ushort)(1 + y * 4 + x));
        Assert.Equal(Convert.FromHexString(EngineMono8), WriteToBytes(mono8));

        Frame mono16 = Mono(4, 4, 16, (x, y) => (ushort)(1000 + 100 * y + x));
        Assert.Equal(Convert.FromHexString(EngineMono16), WriteToBytes(mono16));

        var colour = new Frame(4, 4, 3, 8);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                colour[x, y, 0] = (ushort)(10 + x);
                colour[x, y, 1] = (ushort)(40 + y);
                colour[x, y, 2] = (ushort)(70 + x + y);
            }
        }
        Assert.Equal(Convert.FromHexString(EngineColour8), WriteToBytes(colour));
    }

    private static byte[] WriteToBytes(Frame frame)
    {
        using var stream = new MemoryStream();
        MasterTmpFile.Write(frame, stream, leaveOpen: true);
        return stream.ToArray();
    }

    // ------------------------------------------------------------ rejections

    [Fact]
    public void Read_RejectsATiffHandoff()
    {
        // What the managed builder used to write into -flat_tmp: a TIFF. The engine's loader would
        // interpret "II*\0" as a width, so the reader must refuse it instead of allocating 1e9 pixels.
        var tiff = new Frame(8, 8, 1, 8);
        using var stream = new MemoryStream();
        using (var writer = new TiffWriter(stream, 8, 8, 1, 8, leaveOpen: true)) writer.Write(tiff);

        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(stream.ToArray()));
    }

    [Fact]
    public void Read_RejectsTruncatedRasterAndShortHeader()
    {
        byte[] full = Convert.FromHexString(EngineMono8);
        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(full[..^1]));
        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(full[..MasterTmpFile.HeaderSize]));
        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Read_RejectsUnknownDepthAndColourFlag()
    {
        byte[] bytes = Convert.FromHexString(EngineMono8);
        BitConverter.GetBytes(3u).CopyTo(bytes, 12);
        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(bytes));

        bytes = Convert.FromHexString(EngineMono8);
        BitConverter.GetBytes(2u).CopyTo(bytes, 8);
        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(bytes));

        bytes = Convert.FromHexString(EngineMono8);
        BitConverter.GetBytes(0u).CopyTo(bytes, 0);
        Assert.Throws<InvalidDataException>(() => MasterTmpFile.Read(bytes));
    }

    // ------------------------------------------------------------ handoff wiring

    [Fact]
    public void Build_WritesTheEngineMasterContainerNotATiff()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pipp-mastertmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                string path = Path.Combine(dir, $"cal{i}.ser");
                using var writer = new SerWriter(path, 8, 8, 1, 16);
                var frame = new Frame(8, 8, 1, 16);
                for (int s = 0; s < frame.Samples.Length; s++) frame.Samples[s] = (ushort)(10 * (i + 1));
                writer.Write(frame);
                files.Add(path);
            }

            string master = Path.Combine(dir, "pipp_master_flat.tmp");
            string pgm = Path.Combine(dir, "pipp_master_flat.pgm");

            int stacked = MasterFrameBuilder.Build(files, null, master, pgm, 3);

            Assert.Equal(3, stacked);
            byte[] bytes = File.ReadAllBytes(master);
            Assert.Equal(MasterTmpFile.HeaderSize + 8 * 8 * 2, bytes.Length);
            Assert.NotEqual((byte)'I', bytes[0]); // not "II*\0"

            Frame loaded = MasterTmpFile.Read(master);
            Assert.Equal(8, loaded.Width);
            Assert.Equal(8, loaded.Height);
            Assert.Equal(1, loaded.Channels);
            Assert.Equal(16, loaded.BitDepth);
            Assert.All(loaded.Samples, v => Assert.Equal(20, v));

            // the PGM handoff still carries the same master for -dark_pgm
            Assert.All(PgmFile.Read(pgm).Samples, v => Assert.Equal(20, v));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MasterPaths_AreReadableByTheEngineLoaderContract()
    {
        // The loader (pipp_core FUN_1400b3370) reads 40 bytes, then exactly
        // width*height*(colour ? 3 : 1)*bytesPerSample bytes; nothing else in the file matters.
        var frame = new Frame(16, 16, 3, 8);
        using var stream = new MemoryStream();
        MasterTmpFile.Write(frame, stream, leaveOpen: true);

        byte[] bytes = stream.ToArray();
        uint width = BitConverter.ToUInt32(bytes, 0);
        uint height = BitConverter.ToUInt32(bytes, 4);
        uint colour = BitConverter.ToUInt32(bytes, 8);
        uint depth = BitConverter.ToUInt32(bytes, 12);
        Assert.Equal(bytes.Length, MasterTmpFile.HeaderSize + width * height * (colour == 1 ? 3 : 1) * depth);
    }
}
