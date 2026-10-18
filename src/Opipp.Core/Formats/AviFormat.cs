namespace Opipp.Core.Formats;

/// <summary>Pixel layout written by <see cref="AviWriter"/>.</summary>
public enum AviPixelFormat
{
    /// <summary>
    /// One 8-bit channel, stored as a bottom-up 8bpp <c>BI_RGB</c> DIB with a 256-entry grayscale
    /// palette, matching PIPP's default uncompressed AVI output.
    /// </summary>
    Mono8 = 0,

    /// <summary>
    /// Three 8-bit channels, stored as a bottom-up 24bpp <c>BI_RGB</c> DIB with BGR byte order
    /// (as required by the DIB format) and DWORD-aligned rows.
    /// </summary>
    Bgr24 = 1,

    /// <summary>
    /// One 8-bit channel, stored top-down as an 8bpp <c>Y800</c> stream with no palette and no
    /// row padding. The bitmap header's height is negative to advertise the top-down layout.
    /// </summary>
    Y800 = 2,
}

/// <summary>Compression codec requested for AVI output (<c>-avi_codec</c>).</summary>
public enum AviCodec
{
    /// <summary>Uncompressed DIB/Y800, the only layout <see cref="AviWriter"/> can produce (0).</summary>
    Dib = 0,

    /// <summary>Ut Video lossless compression (1) — requires the FFmpeg Ut Video encoder.</summary>
    Ulrg = 1,

    /// <summary>MPEG-4 part 2 lossy compression (2) — requires the FFmpeg MPEG-4 encoder.</summary>
    Fmp4 = 2,
}

/// <summary>Options for <see cref="AviWriter"/>.</summary>
public sealed class AviWriterOptions
{
    /// <summary>Pixel layout of the written stream. Defaults to <see cref="AviPixelFormat.Mono8"/>.</summary>
    public AviPixelFormat PixelFormat { get; init; } = AviPixelFormat.Mono8;

    /// <summary>
    /// Compression codec (<c>-avi_codec</c>). Only <see cref="AviCodec.Dib"/> is implementable: the
    /// writer stores uncompressed samples and this build contains no Ut Video or MPEG-4 encoder.
    /// </summary>
    public AviCodec Codec { get; init; } = AviCodec.Dib;

    /// <summary>
    /// Codec quality, 1..10 (<c>-avi_quality</c>). The engine accepts exactly this range and feeds it
    /// to the FMP4 encoder; the uncompressed DIB path has no quality parameter, so the value is
    /// validated and otherwise unused here.
    /// </summary>
    public int Quality { get; init; } = 10;

    /// <summary>Frame rate numerator (frames per second when <see cref="FpsScale"/> is 1). Defaults to 25.</summary>
    public int FpsRate { get; init; } = 25;

    /// <summary>Frame rate denominator. Defaults to 1.</summary>
    public int FpsScale { get; init; } = 1;

    /// <summary>
    /// Write the legacy layout selected by <c>-avi_old</c> / <c>-avi_old4</c>: the same header and
    /// <c>idx1</c> with no OpenDML chunks (<c>indx</c>, <c>odml</c>/<c>dmlh</c>, <c>ix00</c>), followed
    /// by a zero-filled <c>JUNK</c> chunk that pushes the <c>movi</c> list to absolute offset 8192.
    /// The engine's two switches produce identical files; they differ only in the >2 GiB arithmetic.
    /// </summary>
    public bool LegacyLayout { get; init; }

    internal void Validate()
    {
        if (FpsRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(FpsRate), FpsRate, "FPS rate must be positive.");
        if (FpsScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(FpsScale), FpsScale, "FPS scale must be positive.");
        if (!Enum.IsDefined(PixelFormat))
            throw new ArgumentOutOfRangeException(nameof(PixelFormat), PixelFormat, "Unknown AVI pixel format.");
        if (!Enum.IsDefined(Codec))
            throw new ArgumentOutOfRangeException(nameof(Codec), Codec, "Unknown AVI codec.");
        if (Quality is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(Quality), Quality, "AVI codec quality must be between 1 and 10.");
        if (Codec != AviCodec.Dib)
        {
            throw new NotSupportedException(
                $"AVI codec {Codec} is not supported: this writer stores uncompressed DIB/Y800 frames only " +
                "and contains no Ut Video or MPEG-4 encoder. Use -avi_codec=0 (DIB) instead.");
        }
    }
}

/// <summary>
/// Metadata of an AVI file, as reported by <see cref="AviProbe"/>. The values describe the first video
/// stream and are read from the file's <c>avih</c>/<c>strh</c>/<c>strf</c> chunks without decoding frames.
/// </summary>
public sealed record AviInfo
{
    /// <summary>Frame width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Frame height in pixels (always positive; the stored DIB may be bottom-up or top-down).</summary>
    public required int Height { get; init; }

    /// <summary>Bits per pixel of the stored frames: 8 or 24.</summary>
    public required int BitCount { get; init; }

    /// <summary>
    /// Compression four-character code of the video stream, rendered as text. Uncompressed DIB streams
    /// with <c>biCompression == 0</c> are reported as <c>BI_RGB</c>; Y800 is reported as <c>Y800</c>.
    /// </summary>
    public required string FourCC { get; init; }

    /// <summary>Frame rate numerator from the stream header.</summary>
    public required int FpsRate { get; init; }

    /// <summary>Frame rate denominator from the stream header.</summary>
    public required int FpsScale { get; init; }

    /// <summary>Number of frames the stream header declares, clamped to the frames actually present.</summary>
    public required int FrameCount { get; init; }

    /// <summary>Frames per second (<see cref="FpsRate"/> / <see cref="FpsScale"/>), or 0 when the header lacks a rate.</summary>
    public double Fps => FpsScale > 0 ? (double)FpsRate / FpsScale : 0;

    /// <summary>Formats the metadata as a one-line summary, primarily for logging.</summary>
    public override string ToString() =>
        $"AVI {Width}x{Height} {BitCount}bpp {FourCC} {Fps:0.###}fps frames={FrameCount}";
}

/// <summary>FourCC constants and helpers shared by the AVI reader and writer.</summary>
internal static class AviFourCC
{
    /// <summary>Encodes four ASCII characters into a little-endian FourCC value.</summary>
    public static uint Make(string text)
    {
        if (text is null || text.Length != 4)
            throw new ArgumentException("A FourCC must be exactly four characters.", nameof(text));

        return (uint)(text[0] | (text[1] << 8) | (text[2] << 16) | (text[3] << 24));
    }

    /// <summary>
    /// Renders a FourCC as text. Values without four printable ASCII bytes (for example
    /// <c>BI_RGB</c>'s numeric 0) are rendered in hexadecimal instead.
    /// </summary>
    public static string ToText(uint value)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte b = (byte)(value >> (8 * i));
            if (b is < 0x20 or > 0x7E)
                return $"0x{value:X8}";
            chars[i] = (char)b;
        }

        return new string(chars);
    }
}

/// <summary>RIFF/AVI chunk identifiers used by this implementation.</summary>
internal static class AviIds
{
    public static readonly uint Riff = AviFourCC.Make("RIFF");
    public static readonly uint List = AviFourCC.Make("LIST");
    public static readonly uint Avi = AviFourCC.Make("AVI ");
    public static readonly uint Hdrl = AviFourCC.Make("hdrl");
    public static readonly uint Strl = AviFourCC.Make("strl");
    public static readonly uint Movi = AviFourCC.Make("movi");
    public static readonly uint Avih = AviFourCC.Make("avih");
    public static readonly uint Strh = AviFourCC.Make("strh");
    public static readonly uint Strf = AviFourCC.Make("strf");
    public static readonly uint Idx1 = AviFourCC.Make("idx1");
    public static readonly uint Vids = AviFourCC.Make("vids");

    /// <summary>Motion JPEG video stream.</summary>
    public static readonly uint Mjpg = AviFourCC.Make("MJPG");
    public static readonly uint Dib = AviFourCC.Make("DIB ");
    public static readonly uint Y800 = AviFourCC.Make("Y800");
    public static readonly uint Db = AviFourCC.Make("00db");
    public static readonly uint Dc = AviFourCC.Make("00dc");

    /// <summary><c>AVIF_HASINDEX</c>: the file contains an <c>idx1</c> chunk.</summary>
    public const uint AviFlagHasIndex = 0x00000010;

    /// <summary>Zero-filled filler chunk (<c>JUNK</c>) used by the legacy header layout.</summary>
    public static readonly uint Junk = AviFourCC.Make("JUNK");

    /// <summary><c>AVIIF_KEYFRAME</c>: every index entry points at a key frame (all DIB frames are).</summary>
    public const uint AviKeyFrame = 0x00000010;
}
