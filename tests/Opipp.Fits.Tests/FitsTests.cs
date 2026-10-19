using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Fits.Tests;

public class FitsTests
{
    // ---------------------------------------------------------------- header formatting

    [Fact]
    public void Header_Serializes_As_2880_Byte_Blocks_Of_80_Char_Cards()
    {
        var header = new FitsHeader();
        header.Add("SIMPLE", true, "conforms to FITS standard");
        header.Add("BITPIX", 16, "bits per sample");
        header.Add("NAXIS", 2);
        header.Add("NAXIS1", 640);
        header.Add("NAXIS2", 480);

        byte[] bytes = header.ToBytes();
        Assert.Equal(0, bytes.Length % 2880);
        Assert.Equal(2880, bytes.Length);

        string text = Encoding.Latin1.GetString(bytes);
        string[] cards = Enumerable.Range(0, text.Length / 80)
            .Select(i => text.Substring(i * 80, 80))
            .ToArray();

        Assert.Equal("SIMPLE  ", cards[0][..8]);
        Assert.Equal("= ", cards[0].Substring(8, 2));
        Assert.Equal("T", cards[0].Substring(29, 1));
        Assert.Equal(" ", cards[0].Substring(30, 1));
        Assert.Equal("/", cards[0].Substring(31, 1));

        Assert.Equal("16", cards[1].Substring(28, 2));
        Assert.Equal("640", cards[3].Substring(27, 3));
        Assert.StartsWith("END", cards[5]);
        Assert.All(cards[6..], card => Assert.Equal(new string(' ', 80), card));
    }

    [Fact]
    public void Card_Formatting_Uses_Fixed_Columns_Comments_And_Quoting()
    {
        var card = new FitsCard("BAYERPAT", "RGGB", "Bayer pattern");
        string text = card.Format();

        Assert.Equal(80, text.Length);
        Assert.Equal("BAYERPAT", text[..8]);
        Assert.Equal("= ", text.Substring(8, 2));
        Assert.Equal("'RGGB'", text.Substring(10, 6));
        Assert.Equal(' ', text[30]);
        Assert.Equal('/', text[31]);

        FitsCard parsed = FitsCard.Parse(text);
        Assert.Equal(FitsValueKind.String, parsed.Kind);
        Assert.Equal("RGGB", parsed.Value);
        Assert.Equal("Bayer pattern", parsed.Comment);

        var logical = new FitsCard("SIMPLE", true, "conforms to FITS standard");
        Assert.Equal('T', logical.Format()[29]);

        var number = new FitsCard("BITPIX", 16);
        Assert.Equal("16", number.Format().Substring(28, 2));
    }

    [Fact]
    public void Card_Parsing_RoundTrips_Types_Comments_And_Escaped_Quotes()
    {
        var apostrophe = new FitsCard("ORIGIN", "O'Brien's", "observer");
        string formatted = apostrophe.Format();
        Assert.Contains("'O''Brien''s'", formatted);

        FitsCard roundTripped = FitsCard.Parse(formatted);
        Assert.Equal("O'Brien's", roundTripped.Value);
        Assert.Equal("observer", roundTripped.Comment);

        var header = new FitsHeader();
        header.Add("BITPIX", -32, "floating point pixels");
        header.Add("BZERO", 32768.0);
        header.Add("BSCALE", 1.0);
        header.Add("EXTEND", true);

        FitsHeader parsed = FitsHeader.Parse(header.ToBytes(), out int consumed);
        Assert.Equal(2880, consumed);
        Assert.Equal(-32L, parsed.GetInteger("BITPIX"));
        Assert.Equal(-32L, parsed.Find("BITPIX")!.Value);
        Assert.Equal(32768.0, parsed.GetReal("BZERO"));
        Assert.Equal(1.0, parsed.GetReal("BSCALE"));
        Assert.True(parsed.GetLogical("EXTEND"));

        FitsCard comment = FitsCard.Parse("COMMENT converted by PIPP".PadRight(80));
        Assert.True(comment.IsCommentary);
        Assert.Equal("converted by PIPP", comment.Text);

        FitsCard history = FitsCard.Parse("HISTORY  frame 1".PadRight(80));
        Assert.True(history.IsCommentary);
        Assert.Equal("frame 1", history.Text!.Trim());
    }

    // ---------------------------------------------------------------- round trips

    [Fact]
    public void Mono16_RoundTrips_Samples_Dimensions_And_Date()
    {
        using var file = new TempFile();
        var source = new Frame(4, 3, 1, 16);
        for (int i = 0; i < source.SampleCount; i++)
        {
            source.Samples[i] = (ushort)((i * 7919) % 65536);
        }

        source.Samples[0] = 0;
        source.Samples[1] = 65535;

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        string headerText = Encoding.Latin1.GetString(File.ReadAllBytes(file.Path), 0, 2880);
        Assert.Contains("SIMPLE  =                    T", headerText);
        Assert.Contains("BITPIX  =                   16", headerText);
        Assert.Contains("NAXIS   =                    2", headerText);
        Assert.Contains("BZERO   =              32768.0", headerText);
        Assert.Contains("BSCALE  =                  1.0", headerText);
        Assert.Contains("DATE    = '", headerText);

        using var reader = new FitsReader(file.Path);
        Assert.Equal(4, reader.Width);
        Assert.Equal(3, reader.Height);
        Assert.Equal(1, reader.Channels);
        Assert.Equal(16, reader.BitDepth);
        Assert.Equal(1, reader.FrameCount);
        Assert.Equal(BayerPattern.None, reader.Bayer);
        Assert.False(reader.IsTruncated);
        Assert.True(reader.Header.GetLogical("EXTEND"));

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(source.Samples, frame.Samples);
        Assert.Equal(0, frame.Samples[0]);
        Assert.Equal(65535, frame.Samples[1]);
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void Writer_PreservesEightBitDepth()
    {
        // The original writes BITPIX=8 for 8-bit frames instead of promoting them to 16.
        using var file = new TempFile();
        var source = new Frame(2, 2, 1, 8);
        source.Samples[0] = 10;
        source.Samples[1] = 20;
        source.Samples[2] = 30;
        source.Samples[3] = 40;

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        byte[] bytes = File.ReadAllBytes(file.Path);
        // 2880 header + 4 data bytes (one per sample) + 2872 pad: the writer must not charge 8-bit
        // samples twice when sizing the pad.
        Assert.Equal(5760, bytes.Length);
        string header = System.Text.Encoding.ASCII.GetString(bytes, 0, 2880);
        Assert.Contains("BITPIX  =                    8", header);
        Assert.Contains("BZERO   =                  0.0", header);

        using var reader = new FitsReader(file.Path);
        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(8, frame.BitDepth);
        Assert.Equal(source.Samples, frame.Samples);
    }

    [Fact]
    public void Colour16_RoundTrips_And_Writes_Planes_In_Order()
    {
        using var file = new TempFile();
        var source = new Frame(2, 2, 3, 16);
        for (int i = 0; i < source.SampleCount; i++)
        {
            source.Samples[i] = (ushort)(100 + (i * 101));
        }

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        byte[] bytes = File.ReadAllBytes(file.Path);
        int dataLength = source.SampleCount * 2;
        int dataOffset = bytes.Length - PaddedLength(dataLength);
        int pixels = source.Width * source.Height;

        // Plane-major on disk (R, G, B) with rows written bottom-up, like the original engine.
        for (int channel = 0; channel < 3; channel++)
        {
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                int row = pixel / source.Width;
                int flipped = ((source.Height - 1) - row) * source.Width + (pixel % source.Width);
                ushort expected = source.Samples[(flipped * 3) + channel];
                int offset = dataOffset + (((channel * pixels) + pixel) * 2);
                Assert.Equal((ushort)(expected - 32768), ReadStoredUInt16(bytes, offset));
            }
        }

        using var reader = new FitsReader(file.Path);
        Assert.Equal(3, reader.Channels);
        Assert.Equal(3, reader.Naxis);

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(source.Samples, frame.Samples);
    }

    [Fact]
    public void BZero_Makes_Unsigned_16Bit_Values_Round_Trip_Including_65535()
    {
        using var file = new TempFile();
        var source = new Frame(3, 1, 1, 16);
        source.Samples[0] = 0;
        source.Samples[1] = 65535;
        source.Samples[2] = 32768;

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        byte[] bytes = File.ReadAllBytes(file.Path);
        int dataOffset = bytes.Length - PaddedLength(6);
        Assert.Equal(0x8000, ReadStoredUInt16(bytes, dataOffset));
        Assert.Equal(0x7FFF, ReadStoredUInt16(bytes, dataOffset + 2));
        Assert.Equal(0x0000, ReadStoredUInt16(bytes, dataOffset + 4));

        using var reader = new FitsReader(file.Path);
        Assert.Equal(32768.0, reader.BZero);
        Assert.Equal(1.0, reader.BScale);

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(0, frame.Samples[0]);
        Assert.Equal(65535, frame.Samples[1]);
        Assert.Equal(32768, frame.Samples[2]);
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    [InlineData(BayerPattern.BGGR)]
    public void BayerPattern_RoundTrips(BayerPattern pattern)
    {
        using var file = new TempFile();
        var source = new Frame(4, 4, 1, 16) { Bayer = pattern };
        for (int i = 0; i < source.SampleCount; i++)
        {
            source.Samples[i] = (ushort)i;
        }

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        string headerText = Encoding.Latin1.GetString(File.ReadAllBytes(file.Path), 0, 2880);
        Assert.Contains($"BAYERPAT= '{pattern}'", headerText);

        using var reader = new FitsReader(file.Path);
        Assert.Equal(pattern, reader.Bayer);
        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(pattern, frame.Bayer);

        FitsProbeResult probe = FitsProbe.Probe(file.Path);
        Assert.Equal(pattern, probe.Bayer);
    }

    // ---------------------------------------------------------------- HDU handling

    [Fact]
    public void Reader_Skips_Leading_NonImage_Hdu()
    {
        using var file = new TempFile();

        var table = new FitsHeader();
        table.Add("XTENSION", "BINTABLE", "binary table extension");
        table.Add("BITPIX", 8);
        table.Add("NAXIS", 2);
        table.Add("NAXIS1", 16);
        table.Add("NAXIS2", 2);
        table.Add("PCOUNT", 0);
        table.Add("GCOUNT", 1);

        var image = new FitsHeader();
        image.Add("SIMPLE", true);
        image.Add("BITPIX", 16);
        image.Add("NAXIS", 2);
        image.Add("NAXIS1", 2);
        image.Add("NAXIS2", 2);
        image.Add("BZERO", 32768.0);
        image.Add("BSCALE", 1.0);
        image.Add("BAYERPAT", "GRBG");
        image.Add("BAYOFFX", 1);
        image.Add("BAYOFFY", 0);

        var pixels = new ushort[] { 10, 20, 30, 40 };
        byte[] payload = EncodeUnsigned16(pixels);

        using (var stream = File.Create(file.Path))
        {
            AppendHdu(stream, table, new byte[32]);
            AppendHdu(stream, image, payload);
        }

        using var reader = new FitsReader(file.Path);
        Assert.Equal(2, reader.Width);
        Assert.Equal(2, reader.Height);
        Assert.Equal(BayerPattern.GRBG, reader.Bayer);
        Assert.Equal(1, reader.BayerOffsetX);
        Assert.Equal(0, reader.BayerOffsetY);

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        // PIPP stores FITS bottom-up and flips on read, so a hand-built top-down file comes back
        // with its rows reversed (this is the engine's actual behaviour, verified against it).
        Assert.Equal(new ushort[] { 30, 40, 10, 20 }, frame.Samples);
    }

    [Fact]
    public void Reader_Handles_MultiBlock_Headers()
    {
        using var file = new TempFile();
        var header = new FitsHeader();
        header.Add("SIMPLE", true);
        header.Add("BITPIX", 16);
        header.Add("NAXIS", 2);
        header.Add("NAXIS1", 2);
        header.Add("NAXIS2", 1);
        header.Add("BZERO", 32768.0);
        header.Add("BSCALE", 1.0);
        for (int i = 0; i < 40; i++)
        {
            header.AddComment($"pipeline note {i}");
        }

        Assert.True(header.ToBytes().Length > 2880);

        byte[] payload = EncodeUnsigned16([1234, 5678]);
        using (var stream = File.Create(file.Path))
        {
            AppendHdu(stream, header, payload);
        }

        using var reader = new FitsReader(file.Path);
        Assert.Equal(2, reader.Width);
        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(new ushort[] { 1234, 5678 }, frame.Samples);
    }

    [Fact]
    public void Reader_Detects_Truncation_And_Can_Zero_Fill()
    {
        using var file = new TempFile();
        var source = new Frame(8, 8, 1, 16);
        for (int i = 0; i < source.SampleCount; i++)
        {
            source.Samples[i] = (ushort)(i + 1);
        }

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        byte[] complete = File.ReadAllBytes(file.Path);
        int dataLength = source.SampleCount * 2;
        int headerLength = complete.Length - PaddedLength(dataLength);
        File.WriteAllBytes(file.Path, complete[..(headerLength + (dataLength / 2))]);

        using (var reader = new FitsReader(file.Path))
        {
            Assert.True(reader.IsTruncated);
            Assert.NotNull(reader.TruncationMessage);
            Assert.Contains("truncated", reader.TruncationMessage!);
            Assert.Throws<FitsTruncationException>(() => { reader.ReadFrame(); });
        }

        using (var reader = new FitsReader(file.Path) { TruncationMode = FitsTruncationMode.ZeroFill })
        {
            Frame? frame = reader.ReadFrame();
            Assert.NotNull(frame);
            // Rows are written bottom-up, so truncating the second half of the file loses the TOP
            // rows (source rows 0..3) and keeps the bottom ones.
            Assert.Equal(0, frame.Samples[0]);
            Assert.Equal(0, frame.Samples[31]);
            Assert.Equal(source.Samples[32], frame.Samples[32]);
            Assert.Equal(source.Samples[^1], frame.Samples[^1]);
        }
    }

    [Fact]
    public void Reader_Tolerates_Missing_Trailing_Data_Padding()
    {
        using var file = new TempFile();
        var source = new Frame(5, 5, 1, 16);
        for (int i = 0; i < source.SampleCount; i++)
        {
            source.Samples[i] = (ushort)(1000 + i);
        }

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        byte[] complete = File.ReadAllBytes(file.Path);
        int dataLength = source.SampleCount * 2;
        int headerLength = complete.Length - PaddedLength(dataLength);
        Assert.True(complete.Length > headerLength + dataLength);
        File.WriteAllBytes(file.Path, complete[..(headerLength + dataLength)]);

        using var reader = new FitsReader(file.Path);
        Assert.False(reader.IsTruncated);
        Assert.Null(reader.TruncationMessage);
        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(source.Samples, frame.Samples);
    }

    [Fact]
    public void MultiFrame_Writer_Produces_Readable_Image_Extensions()
    {
        using var file = new TempFile();
        var first = new Frame(3, 2, 1, 16);
        var second = new Frame(3, 2, 1, 16);
        for (int i = 0; i < first.SampleCount; i++)
        {
            first.Samples[i] = (ushort)(i + 100);
            second.Samples[i] = (ushort)(i + 500);
        }

        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(first);
            writer.Write(second);
            Assert.Equal(2, writer.FramesWritten);
        }

        using var reader = new FitsReader(file.Path);
        Assert.Equal(2, reader.FrameCount);
        Assert.Equal(first.Samples, reader.ReadFrame()!.Samples);
        Assert.Equal(second.Samples, reader.ReadFrame()!.Samples);
        Assert.Null(reader.ReadFrame());

        Assert.Equal(2, FitsProbe.Probe(file.Path).FrameCount);
    }

    // ---------------------------------------------------------------- non-16-bit sources

    [Fact]
    public void BitPix8_Maps_To_Eight_Bit_Frame_Depth()
    {
        using var file = new TempFile();
        var header = new FitsHeader();
        header.Add("SIMPLE", true);
        header.Add("BITPIX", 8);
        header.Add("NAXIS", 2);
        header.Add("NAXIS1", 4);
        header.Add("NAXIS2", 1);
        byte[] payload = [0, 1, 127, 255];

        using (var stream = File.Create(file.Path))
        {
            AppendHdu(stream, header, payload);
        }

        using var reader = new FitsReader(file.Path);
        Assert.Equal(8, reader.BitDepth);
        Assert.Equal(8, reader.BitPix);

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(8, frame.BitDepth);
        Assert.Equal(new ushort[] { 0, 1, 127, 255 }, frame.Samples);
    }

    [Fact]
    public void BitPix32_Clamps_To_The_Sixteen_Bit_Frame_Range()
    {
        using var file = new TempFile();
        var header = new FitsHeader();
        header.Add("SIMPLE", true);
        header.Add("BITPIX", 32);
        header.Add("NAXIS", 2);
        header.Add("NAXIS1", 4);
        header.Add("NAXIS2", 1);

        int[] values = [0, 1000, 70000, -5];
        var payload = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            payload[(i * 4) + 0] = (byte)(values[i] >> 24);
            payload[(i * 4) + 1] = (byte)(values[i] >> 16);
            payload[(i * 4) + 2] = (byte)(values[i] >> 8);
            payload[(i * 4) + 3] = (byte)values[i];
        }

        using (var stream = File.Create(file.Path))
        {
            AppendHdu(stream, header, payload);
        }

        using var reader = new FitsReader(file.Path);
        Assert.Equal(16, reader.BitDepth);
        Assert.Equal(32, reader.BitPix);

        Frame? frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(new ushort[] { 0, 1000, 65535, 0 }, frame.Samples);
    }

    // ---------------------------------------------------------------- probe

    [Fact]
    public void Probe_Returns_Metadata_Without_Decoding_Pixels()
    {
        using var file = new TempFile();
        var source = new Frame(6, 5, 1, 16) { Bayer = BayerPattern.GBRG };
        using (var writer = new FitsWriter(file.Path))
        {
            writer.Write(source);
        }

        FitsProbeResult probe = FitsProbe.Probe(file.Path);

        Assert.Equal(6, probe.Width);
        Assert.Equal(5, probe.Height);
        Assert.Equal(1, probe.Channels);
        Assert.Equal(16, probe.BitDepth);
        Assert.Equal(16, probe.BitPix);
        Assert.Equal(2, probe.Naxis);
        Assert.Equal(1, probe.FrameCount);
        Assert.Equal(BayerPattern.GBRG, probe.Bayer);
        Assert.False(probe.IsTruncated);
        Assert.Null(probe.TruncationMessage);
        Assert.NotNull(probe.Date);
        Assert.StartsWith("20", probe.Date);
        Assert.Equal(32768.0, probe.BZero);
        Assert.Equal(1.0, probe.BScale);
        Assert.True(probe.Header.Contains("SIMPLE"));
    }

    // ---------------------------------------------------------------- helpers

    private static int PaddedLength(int length) => ((length + 2879) / 2880) * 2880;

    private static ushort ReadStoredUInt16(byte[] bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static byte[] EncodeUnsigned16(ushort[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            ushort stored = (ushort)(samples[i] - 32768);
            bytes[i * 2] = (byte)(stored >> 8);
            bytes[(i * 2) + 1] = (byte)stored;
        }

        return bytes;
    }

    private static void AppendHdu(Stream stream, FitsHeader header, byte[] data)
    {
        byte[] headerBytes = header.ToBytes();
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(data, 0, data.Length);
        int padding = (2880 - (data.Length % 2880)) % 2880;
        if (padding > 0)
        {
            stream.Write(new byte[padding], 0, padding);
        }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"pipp-fits-{Guid.NewGuid():N}.fits");

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
        }
    }
}
