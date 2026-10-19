using System.Globalization;
using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;

namespace Opipp.Fits.Tests;

/// <summary>
/// The reader's later-HDU contract: an IMAGE header it cannot decode must fail the open naming the
/// HDU, never stop the scan and leave the frames after it unread. Fixtures are raw FITS bytes built
/// card by card, and every assertion about the file layout comes from the independent walker below,
/// not from <see cref="FitsReader"/>.
/// </summary>
public class FitsImageConstraintTests
{
    private const int Block = 2880;

    // ------------------------------------------------------------------ control

    [Fact]
    public void ThreeImageHdus_Control_ReadsEveryFrameAndTheRawLayoutAgrees()
    {
        byte[] bytes = ThreeHdus(
            [1, 2, 3, 4, 5, 6],
            [11, 12, 13, 14, 15, 16],
            [21, 22, 23, 24, 25, 26]);

        List<WalkedHdu> units = Walk(bytes);
        Assert.Equal(3, units.Count);
        Assert.All(units, unit => Assert.Equal(6, unit.DataLength));
        Assert.Equal(2880, units[0].DataOffset);
        Assert.Equal(5760, units[1].HeaderOffset);
        Assert.Equal(8640, units[1].DataOffset);
        Assert.Equal(11520, units[2].HeaderOffset);
        Assert.Equal(14400, units[2].DataOffset);
        Assert.Equal(3 * 5760, bytes.Length);

        // The walker proves the third payload is really on disk, byte for byte.
        Assert.Equal(new byte[] { 21, 22, 23, 24, 25, 26 }, bytes[units[2].DataOffset..(units[2].DataOffset + 6)]);

        using var reader = new FitsReader(new MemoryStream(bytes, writable: false));
        Assert.Equal(3, reader.FrameCount);
        Assert.Equal(3, reader.Width);
        Assert.Equal(2, reader.Height);
        Assert.Equal(1, reader.Channels);
        Assert.False(reader.IsTruncated);

        // Expected samples come from the raw data the walker located, with FITS rows bottom-up:
        // the reader flips them, so row 1 of the file becomes output row 0.
        for (int i = 0; i < 3; i++)
        {
            byte[] raw = bytes[units[i].DataOffset..(units[i].DataOffset + 6)];
            Assert.Equal(BottomUp(raw, width: 3, height: 2), reader.ReadFrame()!.Samples);
        }

        Assert.Null(reader.ReadFrame());
    }

    // ------------------------------------------------------------------ later IMAGE header faults

    [Fact]
    public void LaterImageHeader_WithZeroBScale_IsRejectedNamingHduTwo()
    {
        byte[] bytes = ThreeHdus([1, 2, 3, 4, 5, 6], [11, 12, 13, 14, 15, 16], [21, 22, 23, 24, 25, 26],
            secondBScale: "0.0");

        AssertRejected(bytes, "HDU 2", "BSCALE");
        AssertAgreesWithRawWalk(bytes, rejectedHdu: 2);
    }

    [Fact]
    public void LaterImageHeader_WithPCountAndSevenBytePayload_IsRejectedNamingHduTwo()
    {
        // A 3x2 8-bit image needs 6 bytes; PCOUNT=1 declares 7 and the file stores 7.
        byte[] bytes = ThreeHdus([1, 2, 3, 4, 5, 6], [11, 12, 13, 14, 15, 16], [21, 22, 23, 24, 25, 26],
            secondPCount: 1,
            secondPayload: [11, 12, 13, 14, 15, 16, 17]);

        AssertRejected(bytes, "HDU 2", "PCOUNT");
        AssertAgreesWithRawWalk(bytes, rejectedHdu: 2);
    }

    [Fact]
    public void LaterImageHeader_WithGCountAndTwelveBytePayload_IsRejectedNamingHduTwo()
    {
        // GCOUNT=2 doubles the declared data section to 12 bytes, which the fixture really stores.
        byte[] bytes = ThreeHdus([1, 2, 3, 4, 5, 6], [11, 12, 13, 14, 15, 16], [21, 22, 23, 24, 25, 26],
            secondGCount: 2,
            secondPayload: [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22]);

        AssertRejected(bytes, "HDU 2", "GCOUNT");
        AssertAgreesWithRawWalk(bytes, rejectedHdu: 2);
    }

    // ------------------------------------------------------------------ the same faults in HDU 1

    [Fact]
    public void FirstImageHeader_WithZeroBScale_IsRejectedNamingHduOne()
    {
        byte[] bytes = Build(
            Image([1, 2, 3, 4, 5, 6], primary: true, bScale: "0.0"),
            Image([11, 12, 13, 14, 15, 16]));

        AssertRejected(bytes, "HDU 1", "BSCALE");
        AssertAgreesWithRawWalk(bytes, rejectedHdu: 1);
    }

    [Fact]
    public void FirstImageHeader_WithPCount_IsRejectedNamingHduOne()
    {
        byte[] bytes = Build(
            Image([1, 2, 3, 4, 5, 6, 7], primary: true, pcount: 1),
            Image([11, 12, 13, 14, 15, 16]));

        AssertRejected(bytes, "HDU 1", "PCOUNT");
        AssertAgreesWithRawWalk(bytes, rejectedHdu: 1);
    }

    [Fact]
    public void FirstImageHeader_WithGCount_IsRejectedNamingHduOne()
    {
        byte[] bytes = Build(
            Image([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], primary: true, gcount: 2),
            Image([11, 12, 13, 14, 15, 16]));

        AssertRejected(bytes, "HDU 1", "GCOUNT");
        AssertAgreesWithRawWalk(bytes, rejectedHdu: 1);
    }

    // ------------------------------------------------------------------ truncation stays controlled

    [Fact]
    public void TruncatedSecondDataBlock_StaysAControlledTruncation()
    {
        byte[] complete = ThreeHdus(
            [1, 2, 3, 4, 5, 6],
            [11, 12, 13, 14, 15, 16],
            [21, 22, 23, 24, 25, 26]);

        List<WalkedHdu> units = Walk(complete);
        byte[] truncated = complete[..(units[1].DataOffset + 3)];

        using var reader = new FitsReader(new MemoryStream(truncated, writable: false));
        // The second header is complete, so it is a readable image with a short payload; the third
        // HDU cannot be located from the declared offsets and is not guessed at.
        Assert.Equal(2, reader.FrameCount);
        Assert.True(reader.IsTruncated);
        Assert.Contains("truncated", reader.TruncationMessage!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(new ushort[] { 4, 5, 6, 1, 2, 3 }, reader.ReadFrame()!.Samples);
        FitsTruncationException ex = Assert.Throws<FitsTruncationException>(() => reader.ReadFrame());
        Assert.Equal(6, ex.ExpectedBytes);
        Assert.Equal(3, ex.AvailableBytes);
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertRejected(byte[] bytes, string hdu, string keyword)
    {
        FitsFormatException ex = Assert.Throws<FitsFormatException>(
            () => new FitsReader(new MemoryStream(bytes, writable: false)));
        Assert.Contains(hdu, ex.Message, StringComparison.Ordinal);
        Assert.Contains(keyword, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared constraint decision must reject exactly the HDU the reader rejected, which is
    /// what lets the CLI audit report the same fault instead of calling the file valid.
    /// </summary>
    private static void AssertAgreesWithRawWalk(byte[] bytes, int rejectedHdu)
    {
        List<WalkedHdu> units = Walk(bytes);
        FitsHeader header = ParseHeader(bytes, units[rejectedHdu - 1].HeaderOffset);

        IReadOnlyList<string> problems = FitsImageConstraints.Validate(
            header,
            FitsImageConstraints.DeclaredDataBytes(header));

        Assert.NotEmpty(problems);

        // Every earlier image HDU is acceptable; the fault is exactly at the HDU the reader named.
        for (int i = 0; i < rejectedHdu - 1; i++)
        {
            FitsHeader other = ParseHeader(bytes, units[i].HeaderOffset);
            Assert.Empty(FitsImageConstraints.Validate(
                other,
                FitsImageConstraints.DeclaredDataBytes(other)));
        }
    }

    private static FitsHeader ParseHeader(byte[] file, int offset)
    {
        using var stream = new MemoryStream(file, offset, file.Length - offset, writable: false);
        return FitsHeader.Read(stream, out _);
    }

    private static byte[] ThreeHdus(
        byte[] first,
        byte[] second,
        byte[] third,
        string? secondBScale = null,
        int? secondPCount = null,
        int? secondGCount = null,
        byte[]? secondPayload = null) =>
        Build(
            Image(first, primary: true),
            Image(secondPayload ?? second, bScale: secondBScale, pcount: secondPCount, gcount: secondGCount),
            Image(third));

    private static byte[] Image(
        byte[] payload,
        bool primary = false,
        int width = 3,
        int height = 2,
        int bitPix = 8,
        string? bScale = null,
        int? pcount = null,
        int? gcount = null)
    {
        var cards = new List<string>
        {
            primary ? Card("SIMPLE", "T") : Card("XTENSION", "'IMAGE'", quoted: true),
            Card("BITPIX", bitPix.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS", "2"),
            Card("NAXIS1", width.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS2", height.ToString(CultureInfo.InvariantCulture)),
        };

        if (pcount is not null) cards.Add(Card("PCOUNT", pcount.Value.ToString(CultureInfo.InvariantCulture)));
        if (gcount is not null) cards.Add(Card("GCOUNT", gcount.Value.ToString(CultureInfo.InvariantCulture)));
        if (bScale is not null) cards.Add(Card("BSCALE", bScale));
        cards.Add("END".PadRight(80));

        return [.. Encoding.ASCII.GetBytes(string.Concat(cards)), .. payload];
    }

    private static string Card(string keyword, string value, bool quoted = false)
    {
        string field = quoted ? value.PadRight(20) : value.PadLeft(20);
        string card = keyword.PadRight(8) + "= " + field;
        if (card.Length > 80) throw new InvalidOperationException($"card for {keyword} is too long");
        return card.PadRight(80);
    }

    /// <summary>Concatenates HDUs, padding every header and data block to 2880 bytes.</summary>
    private static byte[] Build(params byte[][] hdus)
    {
        using var stream = new MemoryStream();
        foreach (byte[] hdu in hdus)
        {
            int headerEnd = EndOfHeader(hdu);
            int headerLength = RoundUp(headerEnd);
            stream.Write(hdu, 0, headerEnd);
            stream.Write(new byte[headerLength - headerEnd], 0, headerLength - headerEnd);

            int dataLength = hdu.Length - headerEnd;
            stream.Write(hdu, headerEnd, dataLength);
            int dataPad = RoundUp(dataLength) - dataLength;
            stream.Write(new byte[dataPad], 0, dataPad);
        }

        return stream.ToArray();
    }

    /// <summary>Offset just past the END card, measured from the raw cards.</summary>
    private static int EndOfHeader(byte[] hdu)
    {
        string firstBlock = Encoding.ASCII.GetString(hdu, 0, Math.Min(hdu.Length, Block));
        int index = firstBlock.IndexOf("END".PadRight(80), StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException("fixture header has no END card");
        return index + 80;
    }

    private sealed record WalkedHdu(
        int HeaderOffset,
        int HeaderLength,
        int DataOffset,
        long DataLength,
        Dictionary<string, string> Cards);

    /// <summary>
    /// Walks the file from the raw 80-byte cards: finds END, rounds the header, reads NAXISn,
    /// PCOUNT and GCOUNT from the cards themselves, and computes the next offset. It shares no code
    /// with the production reader or header parser.
    /// </summary>
    private static List<WalkedHdu> Walk(byte[] file)
    {
        var units = new List<WalkedHdu>();
        int position = 0;
        while (position + Block <= file.Length)
        {
            var cards = new Dictionary<string, string>(StringComparer.Ordinal);
            int cursor = position;
            bool ended = false;
            while (cursor + 80 <= file.Length)
            {
                string card = Encoding.ASCII.GetString(file, cursor, 80);
                cursor += 80;
                if (card[..8].Trim() == "END")
                {
                    ended = true;
                    break;
                }

                string keyword = card[..8].Trim();
                if (keyword.Length == 0 || card[8] != '=') continue;

                string value = card[10..].Trim();
                int comment = value.IndexOf('/');
                if (comment >= 0) value = value[..comment].Trim();
                cards[keyword] = value.Trim('\'');
            }

            Assert.True(ended, $"no END card in the header at offset {position}");

            int headerLength = RoundUp(cursor - position);
            int dataOffset = position + headerLength;
            int naxis = int.Parse(cards["NAXIS"], CultureInfo.InvariantCulture);
            long count = 1;
            for (int axis = 1; axis <= naxis; axis++)
            {
                count *= long.Parse(cards["NAXIS" + axis.ToString(CultureInfo.InvariantCulture)], CultureInfo.InvariantCulture);
            }

            long pcount = cards.TryGetValue("PCOUNT", out string? p) ? long.Parse(p, CultureInfo.InvariantCulture) : 0;
            long gcount = cards.TryGetValue("GCOUNT", out string? g) ? long.Parse(g, CultureInfo.InvariantCulture) : 1;
            int bitPix = int.Parse(cards["BITPIX"], CultureInfo.InvariantCulture);
            long dataLength = (count + pcount) * gcount * (Math.Abs(bitPix) / 8);

            units.Add(new WalkedHdu(position, headerLength, dataOffset, dataLength, cards));
            position = dataOffset + (int)RoundUp(dataLength);
        }

        return units;
    }

    private static int RoundUp(long value) => checked((int)(((value + Block - 1) / Block) * Block));

    /// <summary>Flips the stored rows (FITS is bottom-up) and widens to 8-bit frame samples.</summary>
    private static ushort[] BottomUp(byte[] raw, int width, int height)
    {
        var samples = new ushort[raw.Length];
        for (int row = 0; row < height; row++)
        {
            int targetRow = height - 1 - row;
            for (int column = 0; column < width; column++)
            {
                samples[(targetRow * width) + column] = raw[(row * width) + column];
            }
        }

        return samples;
    }
}
