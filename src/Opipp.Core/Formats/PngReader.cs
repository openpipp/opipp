using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>
/// Reads PNG files into a single <see cref="Frame"/>.
/// </summary>
/// <remarks>
/// <para>
/// Supported inputs are non-interlaced images with bit depth 8 or 16 and colour type 0 (greyscale),
/// 2 (RGB), 4 (greyscale + alpha) or 6 (RGBA). Alpha is dropped because <see cref="Frame"/> stores
/// only grey or RGB. IDAT chunks may be split across the file; they are concatenated before inflation.
/// All five PNG row filters (None, Sub, Up, Average, Paeth) are undone byte-wise, including the
/// <c>bpp</c> back-reference that makes Sub/Average/Paeth operate on bytes rather than samples.
/// </para>
/// <para>
/// Interlaced (Adam7) files are rejected with <see cref="NotSupportedException"/>, as are palette
/// images (colour type 3), non-deflate compression and non-zero filter methods. Chunk CRCs are
/// verified on read. 16-bit samples are big-endian on disk per the PNG specification.
/// </para>
/// </remarks>
public sealed class PngReader : IFrameSource
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly string? _sourcePath;
    private readonly PngHeader _header;
    private readonly byte[] _compressed;

    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens a PNG file for reading.</summary>
    /// <param name="path">Path to the PNG.</param>
    public PngReader(string path)
        : this(OpenFile(path), ownsStream: true, path)
    {
    }

    /// <summary>Reads a PNG from a stream positioned at its signature.</summary>
    /// <param name="stream">Source stream; the whole file is consumed during construction.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    public PngReader(Stream stream, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, sourcePath: null)
    {
    }

    private PngReader(Stream stream, bool ownsStream, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _ownsStream = ownsStream;
        _sourcePath = sourcePath;

        using var idat = new MemoryStream();
        bool sawHeader = false;
        bool sawEnd = false;

        try
        {
            PngHeader? header = null;

            ReadSignature(stream);

            while (!sawEnd)
            {
                (string type, byte[] data) = ReadChunk(stream);

                switch (type)
                {
                    case "IHDR":
                        if (sawHeader)
                            throw new InvalidDataException("PNG contains more than one IHDR chunk.");
                        header = PngHeader.Parse(data);
                        sawHeader = true;
                        break;

                    case "IDAT":
                        if (!sawHeader)
                            throw new InvalidDataException("PNG IDAT chunk appears before IHDR.");
                        idat.Write(data);
                        break;

                    case "IEND":
                        sawEnd = true;
                        break;

                    case "PLTE":
                    case "tRNS":
                        break; // irrelevant for the supported colour types

                    default:
                        if (type.Length > 0 && char.IsUpper(type[0]))
                            throw new NotSupportedException($"PNG critical chunk {type} is not supported.");
                        break; // ancillary chunks are skipped
                }

                if (!sawHeader && !sawEnd)
                    throw new InvalidDataException($"PNG first chunk is {type}; IHDR is required first.");
            }

            if (header is null)
                throw new InvalidDataException("PNG has no IHDR chunk.");
            if (idat.Length == 0)
                throw new InvalidDataException("PNG has no IDAT chunk.");

            _header = header;
            _compressed = idat.ToArray();
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("PNG file is truncated.", ex);
        }
    }

    /// <inheritdoc />
    public int Width => _header.Width;

    /// <inheritdoc />
    public int Height => _header.Height;

    /// <inheritdoc />
    public int Channels => _header.StoredChannels;

    /// <inheritdoc />
    public int BitDepth => _header.BitDepth;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => BayerPattern.None;

    /// <summary>PNG colour type from IHDR (0, 2, 4 or 6).</summary>
    public int ColourType => _header.ColourType;

    /// <summary>Channels stored in the file, including alpha for colour types 4 and 6.</summary>
    public int EncodedChannels => _header.EncodedChannels;

    /// <summary>True when the source carries an alpha channel that <see cref="ReadFrame"/> discards.</summary>
    public bool HasAlpha => EncodedChannels != Channels;

    /// <summary>Path this reader was opened from, or null for stream construction.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>Reads only the PNG signature and IHDR to report geometry, depth and channel count.</summary>
    /// <param name="info">PNG file to inspect.</param>
    /// <returns>Metadata for the image.</returns>
    public static ImageInfo Probe(FileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var stream = info.OpenRead();
        return Probe(stream);
    }

    /// <summary>
    /// Reads only the PNG signature and IHDR from a stream. The stream is left open; a seekable stream
    /// is restored to its original position.
    /// </summary>
    /// <param name="stream">Stream positioned at the PNG signature.</param>
    /// <returns>Metadata for the image.</returns>
    public static ImageInfo Probe(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long position = stream.CanSeek ? stream.Position : 0;
        try
        {
            ReadSignature(stream);
            (string type, byte[] data) = ReadChunk(stream);
            if (type != "IHDR")
                throw new InvalidDataException($"PNG first chunk is {type}; IHDR is required first.");

            PngHeader header = PngHeader.Parse(data);
            return new ImageInfo(header.Width, header.Height, header.BitDepth, header.StoredChannels);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("PNG file is truncated inside its header.", ex);
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = position;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The image data is corrupt, truncated or uses an unknown row filter.</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= 1)
            return null;

        int bytesPerSample = BitDepth / 8;
        int rowBytes = checked(Width * EncodedChannels * bytesPerSample);
        int expected = checked((rowBytes + 1) * Height);

        byte[] raw = new byte[expected];
        using (var input = new MemoryStream(_compressed, writable: false))
        using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
        {
            int total = 0;
            while (total < expected)
            {
                int read = zlib.Read(raw, total, expected - total);
                if (read == 0)
                    break;
                total += read;
            }

            if (total != expected)
            {
                throw new InvalidDataException(
                    $"PNG image data inflated to {total} byte(s); {expected} are required for {Width}x{Height}.");
            }
        }

        Unfilter(raw, rowBytes, EncodedChannels * bytesPerSample);

        var frame = new Frame(Width, Height, Channels, BitDepth)
        {
            Index = 0,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        int stride = rowBytes + 1;
        int pixelIndex = 0;
        ushort[] samples = frame.Samples;

        for (int y = 0; y < Height; y++)
        {
            int rowStart = y * stride + 1;
            for (int x = 0; x < Width; x++)
            {
                int source = rowStart + x * EncodedChannels * bytesPerSample;
                if (bytesPerSample == 1)
                {
                    for (int c = 0; c < Channels; c++)
                        samples[pixelIndex++] = raw[source + c];
                }
                else
                {
                    for (int c = 0; c < Channels; c++)
                        samples[pixelIndex++] = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(source + c * 2));
                }
            }
        }

        _nextIndex++;
        return frame;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsStream)
            _stream.Dispose();
    }

    private static void Unfilter(byte[] raw, int rowBytes, int bytesPerPixel)
    {
        int stride = rowBytes + 1;
        int height = raw.Length / stride;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            int filter = raw[rowStart];
            Span<byte> current = raw.AsSpan(rowStart + 1, rowBytes);
            ReadOnlySpan<byte> previous = y == 0 ? default : raw.AsSpan(rowStart - stride + 1, rowBytes);

            switch (filter)
            {
                case 0: // None
                    break;

                case 1: // Sub: add the byte one pixel to the left
                    for (int i = bytesPerPixel; i < rowBytes; i++)
                        current[i] = (byte)(current[i] + current[i - bytesPerPixel]);
                    break;

                case 2: // Up: add the byte directly above (zero for the first row)
                    if (y > 0)
                    {
                        for (int i = 0; i < rowBytes; i++)
                            current[i] = (byte)(current[i] + previous[i]);
                    }

                    break;

                case 3: // Average of left and above
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        int up = y == 0 ? 0 : previous[i];
                        current[i] = (byte)(current[i] + ((left + up) >> 1));
                    }

                    break;

                case 4: // Paeth predictor
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        int up = y == 0 ? 0 : previous[i];
                        int upLeft = y == 0 || i < bytesPerPixel ? 0 : previous[i - bytesPerPixel];
                        current[i] = (byte)(current[i] + Paeth(left, up, upLeft));
                    }

                    break;

                default:
                    throw new InvalidDataException($"PNG row {y} uses unknown filter type {filter}.");
            }
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left);
        int pb = Math.Abs(p - up);
        int pc = Math.Abs(p - upLeft);
        if (pa <= pb && pa <= pc)
            return left;
        return pb <= pc ? up : upLeft;
    }

    private static void ReadSignature(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file: signature mismatch.");
    }

    private static (string Type, byte[] Data) ReadChunk(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        stream.ReadExactly(head);

        uint length = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (length > int.MaxValue)
            throw new InvalidDataException($"PNG chunk declares an implausible length {length}.");

        byte[] typeBytes = head[4..].ToArray();
        string type = Encoding.ASCII.GetString(typeBytes);
        foreach (char ch in type)
        {
            if (!char.IsAsciiLetter(ch))
                throw new InvalidDataException($"PNG chunk type '{type}' is not four ASCII letters.");
        }

        byte[] data = new byte[length];
        stream.ReadExactly(data);

        Span<byte> crcBytes = stackalloc byte[4];
        stream.ReadExactly(crcBytes);
        uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(crcBytes);
        uint actualCrc = PngCrc32.Compute(typeBytes, data);
        if (actualCrc != expectedCrc)
        {
            throw new InvalidDataException(
                $"PNG chunk {type} CRC mismatch: stored {expectedCrc:X8}, computed {actualCrc:X8}.");
        }

        return (type, data);
    }

    private static FileStream OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}

/// <summary>Validated IHDR fields.</summary>
internal sealed class PngHeader
{
    private PngHeader(int width, int height, int bitDepth, int colourType, int encodedChannels, int storedChannels)
    {
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColourType = colourType;
        EncodedChannels = encodedChannels;
        StoredChannels = storedChannels;
    }

    public int Width { get; }

    public int Height { get; }

    public int BitDepth { get; }

    public int ColourType { get; }

    public int EncodedChannels { get; }

    public int StoredChannels { get; }

    public static PngHeader Parse(byte[] data)
    {
        if (data.Length != 13)
            throw new InvalidDataException($"PNG IHDR is {data.Length} bytes; expected 13.");

        uint width = BinaryPrimitives.ReadUInt32BigEndian(data);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        int bitDepth = data[8];
        int colourType = data[9];
        int compression = data[10];
        int filter = data[11];
        int interlace = data[12];

        if (width == 0 || height == 0)
            throw new InvalidDataException($"PNG has invalid dimensions {width}x{height}.");
        if (width > int.MaxValue || height > int.MaxValue)
            throw new InvalidDataException($"PNG dimensions {width}x{height} are too large.");

        if (compression != 0)
            throw new NotSupportedException($"PNG compression method {compression} is not supported; only deflate (0).");
        if (filter != 0)
            throw new NotSupportedException($"PNG filter method {filter} is not supported; only 0.");
        if (interlace == 1)
            throw new NotSupportedException("Interlaced (Adam7) PNG is not supported.");
        if (interlace != 0)
            throw new InvalidDataException($"PNG declares unknown interlace method {interlace}.");

        int encodedChannels;
        int storedChannels;
        switch (colourType)
        {
            case 0: // greyscale
                encodedChannels = 1;
                storedChannels = 1;
                break;
            case 2: // truecolour RGB
                encodedChannels = 3;
                storedChannels = 3;
                break;
            case 4: // greyscale + alpha
                encodedChannels = 2;
                storedChannels = 1;
                break;
            case 6: // RGBA
                encodedChannels = 4;
                storedChannels = 3;
                break;
            case 3:
                throw new NotSupportedException("Palette PNG (colour type 3) is not supported; use greyscale or RGB.");
            default:
                throw new InvalidDataException($"PNG colour type {colourType} is invalid.");
        }

        if (bitDepth is not (8 or 16))
        {
            throw new NotSupportedException(
                $"PNG {bitDepth}-bit samples are not supported; only 8 and 16 bits per sample.");
        }

        return new PngHeader((int)width, (int)height, bitDepth, colourType, encodedChannels, storedChannels);
    }
}
