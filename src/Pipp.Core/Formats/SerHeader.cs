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
    Rgb = 100,

    /// <summary>Three interleaved planes in B, G, R order (readers convert to RGB).</summary>
    Bgr = 101,
}

/// <summary>
/// The fixed 178-byte SER v3 ("LUCAM-RECORDER") file header. All integers are little-endian and all
/// text fields are fixed-width 40-byte ASCII. Producers pad those fields with NULs or spaces; both
/// survive a parse/serialize round-trip byte for byte. Frame data starts at byte 178.
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

    private readonly byte[] _observer = new byte[TextLength];
    private readonly byte[] _telescope = new byte[TextLength];
    private readonly byte[] _instrument = new byte[TextLength];

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

    /// <summary>
    /// Observer name (40-byte fixed-width ASCII field). The property is a view over the raw field
    /// bytes: reading trims the NUL terminator and any trailing spaces, assigning rewrites the field
    /// with NUL padding. Headers parsed from a file therefore re-serialize byte-for-byte even when
    /// the producer padded with spaces.
    /// </summary>
    public string Observer
    {
        get => DecodeText(_observer);
        set => EncodeText(_observer, value);
    }

    /// <summary>Telescope name (40-byte fixed-width ASCII field). See <see cref="Observer"/>.</summary>
    public string Telescope
    {
        get => DecodeText(_telescope);
        set => EncodeText(_telescope, value);
    }

    /// <summary>Instrument or camera name (40-byte fixed-width ASCII field). See <see cref="Observer"/>.</summary>
    public string Instrument
    {
        get => DecodeText(_instrument);
        set => EncodeText(_instrument, value);
    }

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
    /// <remarks>
    /// Depths 9..15 are stored in 16-bit words. The engine rescales such samples by <c>v * 272</c>
    /// rather than left-aligning them (a 0..15 ramp written at depth 12 came back as 0x0000, 0x0110,
    /// 0x0220, ...), so a depth-12..15 input yields a matching header but different sample values.
    /// </remarks>
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
            // 101 = BGR, the dialect the engine writes: frames are stored BGR-interleaved and readers
            // (including this one) swap back to RGB.
            colorId = (int)SerColorId.Bgr;
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

    /// <summary>
    /// Builds the header the engine writes for a processed run: the capture metadata of
    /// <paramref name="source"/> carried onto the output geometry.
    /// </summary>
    /// <remarks>
    /// Measured against <c>bin/pipp_core.exe</c> (saturn30.ser, colour, Bayer and 16-bit fixtures),
    /// the engine writes output headers as follows:
    /// <list type="bullet">
    /// <item><c>LuID</c> is always 0, whatever the input carried.</item>
    /// <item><c>ColorID</c> is 101 for three-plane output; for single-plane output it copies the
    /// input's mono/Bayer id (0, 8, 9, 10, 11) and uses 0 when the input was colour or not SER.
    /// 100 is never written: the engine stores BGR.</item>
    /// <item><c>LittleEndian</c> is always 0, i.e. 16-bit sample data is written big-endian.</item>
    /// <item><c>PixelDepthPerPlane</c> is 8 for one byte per sample and 16 otherwise (a 12-bit input
    /// is re-labelled 16).</item>
    /// <item>Observer/Telescope/Instrument are copied as raw 40-byte fields, padding included.</item>
    /// <item>Both timestamps are 0. The engine only emits non-zero times on the 16/17 input path,
    /// where it stores uninitialised stack data; that is a bug and is not reproduced.</item>
    /// </list>
    /// </remarks>
    /// <param name="width">Output frame width in pixels.</param>
    /// <param name="height">Output frame height in pixels.</param>
    /// <param name="channels">Output channels: 1 for mono/Bayer, 3 for colour.</param>
    /// <param name="bitDepth">Output bits per sample (8 or 16).</param>
    /// <param name="frameCount">Number of frames, or 0 when the writer should patch it.</param>
    /// <param name="source">Header of the input SER file, or null when the input was not SER.</param>
    /// <param name="bayer">Mosaic pattern for single-plane output when <paramref name="source"/> is null.</param>
    public static SerHeader CreateOutput(
        int width,
        int height,
        int channels,
        int bitDepth,
        int frameCount,
        SerHeader? source,
        BayerPattern bayer = BayerPattern.None)
    {
        if (channels is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "channels must be 1 or 3");
        if (bitDepth is < 8 or > 16)
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "bit depth must be 8..16");

        int colorId;
        if (channels == 3)
        {
            colorId = (int)SerColorId.Bgr;
        }
        else if (source is not null && ChannelsForColorId(source.ColorId) == 1)
        {
            // Mono/Bayer ids survive: the engine copies 0/8/9/10/11 from the input header.
            colorId = source.ColorId;
        }
        else
        {
            // Non-SER input, or a colour input split into channels: plain mono. The engine leaves
            // 100 on split files, which contradicts the 1-plane payload - a reader-breaking quirk
            // that is deliberately not reproduced.
            colorId = ColorIdForBayer(bayer);
        }

        var header = new SerHeader
        {
            LuId = 0,
            ColorId = colorId,
            LittleEndian = false,
            Width = width,
            Height = height,
            BitDepth = bitDepth <= 8 ? 8 : 16,
            FrameCount = frameCount,
            LocalTimeTicks = 0,
            UtcTicks = 0,
        };

        if (source is not null)
        {
            source._observer.CopyTo(header._observer, 0);
            source._telescope.CopyTo(header._telescope, 0);
            source._instrument.CopyTo(header._instrument, 0);
        }

        return header;
    }

    /// <summary>
    /// Reads the header of the SER file a frame came from, so its capture metadata can be carried
    /// onto an output file. Returns null for a null/blank path, a non-SER extension, a missing file
    /// or a file that does not parse as SER.
    /// </summary>
    public static SerHeader? TryReadFromSource(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;
        if (!sourcePath.EndsWith(".ser", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            if (!File.Exists(sourcePath))
                return null;
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>True for the ColorID values this implementation can decode.</summary>
    /// <remarks>
    /// 16 and 17 are Bayer CYYM/CYGM in the SER v3 specification, and the engine reads them as
    /// single-plane mosaics (its probe even labels 16 "BAYER_CYYM"). This implementation has always
    /// decoded them as three-plane colour, the dialect early builds of the engine wrote; that legacy
    /// superset is kept because the pipeline and tests depend on it. A file whose header says 16/17
    /// but whose payload is one plane per frame is malformed for this reader - the engine and this
    /// reader therefore disagree on such files, and no output parity is claimed for them.
    /// </remarks>
    public static bool IsSupportedColorId(int colorId) =>
        colorId is (int)SerColorId.Mono
            or (int)SerColorId.BayerRggb
            or (int)SerColorId.BayerGrbg
            or (int)SerColorId.BayerGbrg
            or (int)SerColorId.BayerBggr
            or 16 // Bayer CYYM in the spec; read as legacy RGB here
            or 17 // Bayer CYGM in the spec; read as legacy BGR here
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
        colorId is 16 or 17 or (int)SerColorId.Rgb or (int)SerColorId.Bgr ? 3 : 1;

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

        EncodeText(span.Slice(FileIdOffset, 14), FileId);
        BinaryPrimitives.WriteInt32LittleEndian(span[LuIdOffset..], LuId);
        BinaryPrimitives.WriteInt32LittleEndian(span[ColorIdOffset..], ColorId);
        BinaryPrimitives.WriteInt32LittleEndian(span[LittleEndianOffset..], LittleEndian ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[WidthOffset..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[HeightOffset..], Height);
        BinaryPrimitives.WriteInt32LittleEndian(span[BitDepthOffset..], BitDepth);
        BinaryPrimitives.WriteInt32LittleEndian(span[FrameCountOffset..], FrameCount);
        _observer.CopyTo(span.Slice(ObserverOffset, TextLength));
        _telescope.CopyTo(span.Slice(TelescopeOffset, TextLength));
        _instrument.CopyTo(span.Slice(InstrumentOffset, TextLength));
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

        string fileId = Encoding.ASCII.GetString(buffer.Slice(FileIdOffset, 14)).TrimEnd('\0');
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
            LocalTimeTicks = BinaryPrimitives.ReadInt64LittleEndian(buffer[LocalTimeOffset..]),
            UtcTicks = BinaryPrimitives.ReadInt64LittleEndian(buffer[UtcOffset..]),
        }.WithRawText(
            buffer.Slice(ObserverOffset, TextLength),
            buffer.Slice(TelescopeOffset, TextLength),
            buffer.Slice(InstrumentOffset, TextLength));
    }

    /// <summary>Creates a copy of this header, raw text-field bytes included.</summary>
    public SerHeader Clone() => new SerHeader
    {
        LuId = LuId,
        ColorId = ColorId,
        LittleEndian = LittleEndian,
        Width = Width,
        Height = Height,
        BitDepth = BitDepth,
        FrameCount = FrameCount,
        LocalTimeTicks = LocalTimeTicks,
        UtcTicks = UtcTicks,
    }.WithRawText(_observer, _telescope, _instrument);

    private SerHeader WithRawText(ReadOnlySpan<byte> observer, ReadOnlySpan<byte> telescope, ReadOnlySpan<byte> instrument)
    {
        observer.CopyTo(_observer);
        telescope.CopyTo(_telescope);
        instrument.CopyTo(_instrument);
        return this;
    }

    /// <summary>Formats the header as a one-line summary, primarily for logging.</summary>
    public override string ToString() =>
        $"SER {Width}x{Height} {Channels}ch {BitDepth}bpp frames={FrameCount} color={ColorId} bayer={Bayer}";

    private static DateTime? TicksToDateTime(long ticks, DateTimeKind kind)
    {
        if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
            return null;
        return new DateTime(ticks, kind);
    }

    /// <summary>Reads a fixed-width field up to its first NUL and trims trailing padding spaces.</summary>
    private static string DecodeText(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        if (end >= 0)
            field = field[..end];
        return Encoding.ASCII.GetString(field).TrimEnd(' ');
    }

    /// <summary>Writes ASCII into a fixed-width field, NUL padding the remainder and truncating at the field width.</summary>
    private static void EncodeText(Span<byte> field, string? value)
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
}
