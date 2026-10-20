using System.Globalization;
using System.Text;
using Opipp.Core.Formats;

namespace Opipp.Fits.Tests;

public class FitsScanBoundaryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(79)]
    [InlineData(80)]
    public void NonzeroTrailingPartialHeader_IsRejectedNamingTheNextHdu(int length)
    {
        byte[] tail = new byte[length];
        tail[0] = (byte)'X';
        byte[] bytes = [.. Image(primary: true), .. tail];

        FitsFormatException error = Assert.Throws<FitsFormatException>(
            () => new FitsReader(new MemoryStream(bytes)));

        Assert.Contains("HDU 2", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IMAGE", 2, 640)]
    [InlineData("BINTABLE", 0, 2879)]
    public void PartialHeaderContainingEnd_IsRejectedBeforeReadingAnyFrames(
        string extension, int axes, int length)
    {
        byte[] header = Header(primary: false, extension: extension);
        Encoding.ASCII.GetBytes(Card("NAXIS", axes.ToString(CultureInfo.InvariantCulture)))
            .CopyTo(header, 2 * 80);
        byte[] bytes = [.. Image(primary: true), .. header[..length]];

        FitsFormatException error = Assert.Throws<FitsFormatException>(
            () => new FitsReader(new MemoryStream(bytes)));

        Assert.Contains("HDU 2", error.Message, StringComparison.Ordinal);
        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(79)]
    [InlineData(80)]
    [InlineData(2880)]
    public void ZeroTrailingPadding_StillReadsTheCompleteImage(int length)
    {
        byte[] bytes = [.. Image(primary: true), .. new byte[length]];

        using var reader = new FitsReader(new MemoryStream(bytes));

        Assert.Equal(new ushort[] { 4, 5, 6, 1, 2, 3 }, reader.ReadFrame()!.Samples);
        Assert.Null(reader.ReadFrame());
        Assert.False(reader.IsTruncated);
    }

    [Theory]
    [InlineData("IMAGE", 4)]
    [InlineData("BINTABLE", 3)]
    public void TruncatedSkippedHdu_IsRejectedNamingItsHdu(string extension, int width)
    {
        byte[] bytes = [
            .. Image(primary: true),
            .. Header(primary: false, extension: extension, width: width),
            11, 12, 13,
        ];

        FitsFormatException error = Assert.Throws<FitsFormatException>(
            () => new FitsReader(new MemoryStream(bytes)));

        Assert.Contains("HDU 2", error.Message, StringComparison.Ordinal);
        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("IMAGE", 4)]
    [InlineData("BINTABLE", 3)]
    public void CompleteSkippedHdu_DoesNotHideTheFollowingMatchingImage(string extension, int width)
    {
        byte[] bytes = [
            .. Image(primary: true),
            .. Header(primary: false, extension: extension, width: width),
            .. new byte[2880],
            .. Image(primary: false),
        ];

        using var reader = new FitsReader(new MemoryStream(bytes));

        Assert.Equal(2, reader.FrameCount);
        Assert.NotNull(reader.ReadFrame());
        Assert.NotNull(reader.ReadFrame());
        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public void FinalPayloadWithoutBlockPadding_StillReadsTheCompleteImage()
    {
        byte[] bytes = [.. Header(primary: true), 1, 2, 3, 4, 5, 6];

        using var reader = new FitsReader(new MemoryStream(bytes));

        Assert.Equal(new ushort[] { 4, 5, 6, 1, 2, 3 }, reader.ReadFrame()!.Samples);
        Assert.Null(reader.ReadFrame());
        Assert.False(reader.IsTruncated);
    }

    [Theory]
    [InlineData(16, 32768)]
    [InlineData(32, 16384)]
    [InlineData(64, 8192)]
    [InlineData(-32, 16384)]
    [InlineData(-64, 8192)]
    public void PayloadExceedingOneByteArray_IsRejectedBeforePixelAllocation(int bitPix, int height)
    {
        // Each geometry has 2,147,483,648 payload bytes but fewer than int.MaxValue samples.
        // Validate the header only: no large image or allocation is needed to exercise this limit.
        FitsHeader header = FitsHeader.Parse(
            Header(primary: true, width: 32768, height: height, bitPix: bitPix), out _);

        IReadOnlyList<string> errors = FitsImageConstraints.Validate(header, 2147483648L);

        Assert.Contains(errors, error => error.Contains("buffer", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<FitsFormatException>(() => FitsImageConstraints.Require(header, 2147483648L));
    }

    private static byte[] Image(bool primary) =>
        [.. Header(primary), 1, 2, 3, 4, 5, 6, .. new byte[2874]];

    private static byte[] Header(
        bool primary,
        string extension = "IMAGE",
        int width = 3,
        int height = 2,
        int bitPix = 8)
    {
        string[] cards = [
            primary ? Card("SIMPLE", "T") : Card("XTENSION", $"'{extension}'"),
            Card("BITPIX", bitPix.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS", "2"),
            Card("NAXIS1", width.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS2", height.ToString(CultureInfo.InvariantCulture)),
            Card("PCOUNT", "0"),
            Card("GCOUNT", "1"),
            "END".PadRight(80),
        ];
        return Encoding.ASCII.GetBytes(string.Concat(cards).PadRight(2880));
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + "= " + value.PadLeft(20)).PadRight(80);
}
