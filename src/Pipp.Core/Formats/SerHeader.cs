using System.Buffers.Binary;
using System.Text;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// SER <c>ColorID</c> values understood by this implementation.
/// Values 8..11 are single-plane Bayer mosaics; 16 and 17 are three-plane colour.
/// </summary>
public enum SerColorId
{
    /// <summary>Single plane, no mosaic pattern.</summary>
    Mono = 0,

    /// <summary>Single-plane Bayer mosaic with the red sample at the top-left pixel.</summary>
    BayerRggb = 8,

    /// <summary>Single-plane Bayer mosaic with the green sample at the top-left pixel.</summary>
    BayerGrbg = 9,

    /// <summary>Single-plane Bayer mosaic with the green sample at the top-left pixel (GBRG).</summary>
    BayerGbrg = 10,

    /// <summary>Single-plane Bayer mosaic with the blue sample at the top-left pixel.</summary>
    BayerBggr = 11,

    /// <summary>Three interleaved planes in R, G, B order.</summary>
    Rgb = 16,

    /// <summary>Three interleaved planes in B, G, R order (readers convert to RGB).</summary>
    Bgr = 17,
}

/// <summary>
/// The fixed 178-byte SER v3 ("LUCAM-RECORDER") file header. All integers are little-endian and all
/// text fields are fixed-width 40-byte ASCII, NUL padded. Frame data starts at byte 178.
/// </summary>
/// <remarks>
/// Layout (offsets are exact):
/// <code>
///   0  char[14] FileID ("LUCAM-RECORDER")
///  14  int32    LuID
///  18  int32    ColorID
///  22  int32    LittleEndian (1 = little-endian sample data, 0 = big-endian)
///  26  int32    ImageWidth
///  30  int32    ImageHeight
///  34  int32    PixelDepthPerPlane (bits)
///  38  int32    FrameCount
///  42  char[40] Observer
///  82  char[40] Telescope
/// 122  char[40] Instrument
/// 162  int64    LocalTime (DateTime ticks)
/// 170  int64    Utc (DateTime ticks)
/// </code>
/// The published v3 layout has no dedicated UTC-offset field; <see cref="LocalTimeUtcOffsetMinutes"/>
/// is derived from the two timestamps.
/// </remarks>
public sealed class SerHeader
{
    /// <summary>Size of the serialized header, in bytes.</summary>
    public const int Size = 178;

    /// <summary>Length of each fixed-width ASCII text field, in bytes.</summary>
    public const int TextLength = 40;

    /// <summary>The 14-byte file identifier that starts every SER file.</summary>
    public const string FileId = "LUCAM-RECORDER";

    private const int FileIdOffset = 0;
    private const int LuIdOffset = 14;
    private const int ColorIdOffset = 18;
    private const int LittleEndianOffset = 22;
    private const int WidthOffset = 26;
    private const int HeightOffset = 30;
    private const int BitDepthOffset = 34;
    private const int FrameCountOffset = 38;
    private const int ObserverOffset = 42;
    private const int TelescopeOffset = 82;
    private const int InstrumentOffset = 122;
    private const int LocalTimeOffset = 162;
    private const int UtcOffset = 170;

    /// <summary>Camera-specific identifier. Preserved verbatim; not interpreted.</summary>
    public int LuId { get; set; }

    /// <summary>Raw SER ColorID. See <see cref="SerColorId"/> for supported values.</summary>
    public int ColorId { get; set; }

    /// <summary>True when frame samples are stored little-endian; false for big-endian sample data.</summary>
    public bool LittleEndian { get; set; } = true;

    /// <summary>Frame width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Frame height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Bits per sample per plane (normally 8..16).</summary>
    public int BitDepth { get; set; }

    /// <summary>Number of frames stored after the header.</summary>
    public int FrameCount { get; set; }

    /// <summary>Observer name (40-byte fixed-width ASCII field).</summary>
    public string Observer { get; set; } = string.Empty;

    /// <summary>Telescope name (40-byte fixed-width ASCII field).</summary>
    public string Telescope { get; set; } = string.Empty;

    /// <summary>Instrument or camera name (40-byte fixed-width ASCII field).</summary>
    public string Instrument { get; set; } = string.Empty;

    /// <summary>Local timestamp of the first frame, as .NET <see cref="DateTime"/> ticks. 0 means absent.</summary>
    public long LocalTimeTicks { get; set; }

    /// <summary>UTC timestamp of the first frame, as .NET <see cref="DateTime"/> ticks. 0 means absent.</summary>
    public long UtcTicks { get; set; }

    /// <summary>Parsed <see cref="SerColorId"/> value of <see cref="ColorId"/>.</summary>
    public SerColorId Color => (SerColorId)ColorId;

    /// <summary>Number of interleaved channels implied by <see cref="ColorId"/> (1 or 3).</summary>
    public int Channels => ChannelsForColorId(ColorId);

    /// <summary>True when the colour id describes a three-plane (RGB/BGR) frame.</summary>
    public bool IsColour => Channels == 3;

    /// <summary>Bayer mosaic pattern implied by <see cref="ColorId"/>; <see cref="BayerPattern.None"/> for mono/colour.</summary>
    public BayerPattern Bayer => BayerForColorId(ColorId);

    /// <summary>Bytes occupied by one sample in the file: 1 for depth &lt;= 8, otherwise 2.</summary>
    public int BytesPerSample => BitDepth <= 8 ? 1 : 2;

    /// <summary>Bytes occupied by one frame in the file.</summary>
    /// <exception cref="OverflowException">The dimensions are too large to address.</exception>
    public int FrameSize => Width <= 0 || Height <= 0
        ? 0
        : checked(Width * Height * Channels * BytesPerSample);

    /// <summary>Offset of the first frame's data from the start of the header.</summary>
    public int DataOffset => Size;

    /// <summary>Minimum file size implied by the header (header plus all declared frames).</summary>
    public long ExpectedFileSize
    {
        get
        {
            if (Width <= 0 || Height <= 0 || FrameCount < 0)
                return Size;
            try
            {
                return checked(Size + (long)FrameCount * Width * Height * Channels * BytesPerSample);
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }
    }

    /// <summary>Local timestamp as a <see cref="DateTime"/>, or null when the field is absent.</summary>
    public DateTime? LocalTime => TicksToDateTime(LocalTimeTicks, DateTimeKind.Unspecified);

    /// <summary>UTC timestamp as a <see cref="DateTime"/>, or null when the field is absent.</summary>
    public DateTime? UtcTime => TicksToDateTime(UtcTicks, DateTimeKind.Utc);

    /// <summary>
    /// Offset of <see cref="LocalTime"/> from <see cref="UtcTime"/>, in minutes. Derived from the two
    /// timestamp fields; 0 when either timestamp is absent.
    /// </summary>
    public int LocalTimeUtcOffsetMinutes
    {
        get
        {
            if (LocalTimeTicks == 0 || UtcTicks == 0)
                return 0;
            return (int)Math.Round((LocalTimeTicks - UtcTicks) / (double)TimeSpan.TicksPerMinute);
        }
    }

    /// <summary>True when the dimensions, depth, frame count and colour id are all usable.</summary>
    public bool IsStructurallyValid =>
        Width > 0 && Height > 0 && FrameCount >= 0 && BitDepth is >= 8 and <= 16 && IsSupportedColorId(ColorId);

    /// <summary>Creates a header with the given geometry and a matching ColorID.</summary>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="channels">1 for mono/Bayer, 3 for colour.</param>
    /// <param name="bitDepth">Bits per sample (8..16).</param>
    /// <param name="frameCount">Number of frames, or 0 when unknown and to be patched by the writer.</param>
    /// <param name="bayer">Bayer mosaic pattern for single-channel frames; ignored for colour.</param>
    public static SerHeader Create(
        int width,
        int height,
        int channels,
        int bitDepth,
        int frameCount = 0,
        BayerPattern bayer = BayerPattern.None)
    {
        if (channels is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "channels must be 1 or 3");
        if (bitDepth is < 8 or > 16)
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "bit depth must be 8..16");

        int colorId;
        if (channels == 3)
        {
            if (bayer != BayerPattern.None)
                throw new ArgumentException("Bayer patterns apply only to single-channel frames", nameof(bayer));
            colorId = (int)SerColorId.Rgb;
        }
        else
        {
            colorId = ColorIdForBayer(bayer);
        }

        return new SerHeader
        {
            Width = width,
            Height = height,
            BitDepth = bitDepth,
            FrameCount = frameCount,
            ColorId = colorId,
            LittleEndian = true,
        };
    }

    /// <summary>True for the ColorID values this implementation can decode.</summary>
    public static bool IsSupportedColorId(int colorId) =>
        colorId is (int)SerColorId.Mono
            or (int)SerColorId.BayerRggb
            or (int)SerColorId.BayerGrbg
            or (int)SerColorId.BayerGbrg
            or (int)SerColorId.BayerBggr
            or (int)SerColorId.Rgb
            or (int)SerColorId.Bgr;

    /// <summary>Maps a raw SER ColorID to its <see cref="BayerPattern"/> (None when not Bayer).</summary>
    public static BayerPattern BayerForColorId(int colorId) => colorId switch
    {
        (int)SerColorId.BayerRggb => BayerPattern.RGGB,
        (int)SerColorId.BayerGrbg => BayerPattern.GRBG,
        (int)SerColorId.BayerGbrg => BayerPattern.GBRG,
        (int)SerColorId.BayerBggr => BayerPattern.BGGR,
        _ => BayerPattern.None,
    };

    /// <summary>Maps a <see cref="BayerPattern"/> to its raw SER ColorID (0 for <see cref="BayerPattern.None"/>).</summary>
    public static int ColorIdForBayer(BayerPattern bayer) => bayer switch
    {
        BayerPattern.None => (int)SerColorId.Mono,
        BayerPattern.RGGB => (int)SerColorId.BayerRggb,
        BayerPattern.GRBG => (int)SerColorId.BayerGrbg,
        BayerPattern.GBRG => (int)SerColorId.BayerGbrg,
        BayerPattern.BGGR => (int)SerColorId.BayerBggr,
        _ => throw new ArgumentOutOfRangeException(nameof(bayer), bayer, "unknown Bayer pattern"),
    };

    /// <summary>Number of interleaved channels implied by a raw SER ColorID.</summary>
    public static int ChannelsForColorId(int colorId) =>
        colorId is (int)SerColorId.Rgb or (int)SerColorId.Bgr ? 3 : 1;

    /// <summary>Serializes this header into a new 178-byte array.</summary>
    public byte[] ToBytes()
    {
        var buffer = new byte[Size];
        WriteTo(buffer);
        return buffer;
    }

    /// <summary>Writes the 178-byte header to the start of <paramref name="destination"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 178 bytes.</exception>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"SER headers need {Size} bytes, got {destination.Length}.", nameof(destination));

        Span<byte> span = destination[..Size];
        span.Clear();

        WriteFixedString(span.Slice(FileIdOffset, 14), FileId);
        BinaryPrimitives.WriteInt32LittleEndian(span[LuIdOffset..], LuId);
        BinaryPrimitives.WriteInt32LittleEndian(span[ColorIdOffset..], ColorId);
        BinaryPrimitives.WriteInt32LittleEndian(span[LittleEndianOffset..], LittleEndian ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[WidthOffset..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[HeightOffset..], Height);
        BinaryPrimitives.WriteInt32LittleEndian(span[BitDepthOffset..], BitDepth);
        BinaryPrimitives.WriteInt32LittleEndian(span[FrameCountOffset..], FrameCount);
        WriteFixedString(span.Slice(ObserverOffset, TextLength), Observer);
        WriteFixedString(span.Slice(TelescopeOffset, TextLength), Telescope);
        WriteFixedString(span.Slice(InstrumentOffset, TextLength), Instrument);
        BinaryPrimitives.WriteInt64LittleEndian(span[LocalTimeOffset..], LocalTimeTicks);
        BinaryPrimitives.WriteInt64LittleEndian(span[UtcOffset..], UtcTicks);
    }

    /// <summary>Writes this header at the stream's current position.</summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> buffer = stackalloc byte[Size];
        WriteTo(buffer);
        stream.Write(buffer);
    }

    /// <summary>Reads a header from the stream's current position, leaving the stream at the frame data.</summary>
    /// <exception cref="InvalidDataException">The stream is too short or does not start with the SER file id.</exception>
    public static SerHeader Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> buffer = stackalloc byte[Size];
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"SER header truncated: {Size} bytes required.", ex);
        }

        return Parse(buffer);
    }

    /// <summary>Parses a header from the first 178 bytes of <paramref name="buffer"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is too short.</exception>
    /// <exception cref="InvalidDataException">The buffer does not start with the SER file id.</exception>
    public static SerHeader Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < Size)
            throw new ArgumentException($"SER headers need {Size} bytes, got {buffer.Length}.", nameof(buffer));

        string fileId = ReadFixedString(buffer.Slice(FileIdOffset, 14)).TrimEnd('\0');
        if (!string.Equals(fileId, FileId, StringComparison.Ordinal))
            throw new InvalidDataException($"Not a SER file: expected FileID \"{FileId}\", got \"{fileId}\".");

        return new SerHeader
        {
            LuId = BinaryPrimitives.ReadInt32LittleEndian(buffer[LuIdOffset..]),
            ColorId = BinaryPrimitives.ReadInt32LittleEndian(buffer[ColorIdOffset..]),
            LittleEndian = BinaryPrimitives.ReadInt32LittleEndian(buffer[LittleEndianOffset..]) != 0,
            Width = BinaryPrimitives.ReadInt32LittleEndian(buffer[WidthOffset..]),
            Height = BinaryPrimitives.ReadInt32LittleEndian(buffer[HeightOffset..]),
            BitDepth = BinaryPrimitives.ReadInt32LittleEndian(buffer[BitDepthOffset..]),
            FrameCount = BinaryPrimitives.ReadInt32LittleEndian(buffer[FrameCountOffset..]),
            Observer = ReadFixedString(buffer.Slice(ObserverOffset, TextLength)),
            Telescope = ReadFixedString(buffer.Slice(TelescopeOffset, TextLength)),
            Instrument = ReadFixedString(buffer.Slice(InstrumentOffset, TextLength)),
            LocalTimeTicks = BinaryPrimitives.ReadInt64LittleEndian(buffer[LocalTimeOffset..]),
            UtcTicks = BinaryPrimitives.ReadInt64LittleEndian(buffer[UtcOffset..]),
        };
    }

    /// <summary>Creates a copy of this header.</summary>
    public SerHeader Clone() => new()
    {
        LuId = LuId,
        ColorId = ColorId,
        LittleEndian = LittleEndian,
        Width = Width,
        Height = Height,
        BitDepth = BitDepth,
        FrameCount = FrameCount,
        Observer = Observer,
        Telescope = Telescope,
        Instrument = Instrument,
        LocalTimeTicks = LocalTimeTicks,
        UtcTicks = UtcTicks,
    };

    /// <summary>Formats the header as a one-line summary, primarily for logging.</summary>
    public override string ToString() =>
        $"SER {Width}x{Height} {Channels}ch {BitDepth}bpp frames={FrameCount} color={ColorId} bayer={Bayer}";

    private static DateTime? TicksToDateTime(long ticks, DateTimeKind kind)
    {
        if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
            return null;
        return new DateTime(ticks, kind);
    }

    /// <summary>Writes ASCII into a fixed-width field, NUL padding the remainder and truncating at the field width.</summary>
    private static void WriteFixedString(Span<byte> field, string? value)
    {
        field.Clear();
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Contains('\0'))
            throw new ArgumentException("SER text fields cannot contain NUL characters.", nameof(value));

        int count = Math.Min(value.Length, field.Length);
        for (int i = 0; i < count; i++)
        {
            char c = value[i];
            field[i] = c <= 0x7F ? (byte)c : (byte)'?';
        }
    }

    /// <summary>Reads a fixed-width ASCII field up to its first NUL and trims trailing padding spaces.</summary>
    private static string ReadFixedString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        if (end >= 0)
            field = field[..end];
        return Encoding.ASCII.GetString(field).TrimEnd(' ');
    }
}
