using System.Buffers.Binary;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Reads uncompressed Windows bitmap files into <see cref="Frame"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Supported encodings match the PIPP engine: 8bpp palette, 24bpp BGR and 32bpp BGRA with BI_RGB
/// compression. Every other depth is rejected with <see cref="InvalidDataException"/> carrying the
/// engine's "Only 8bpp, 24bpp and 32bpp supported by PIPP" wording. 1bpp, 4bpp and 16bpp files are
/// therefore never silently misinterpreted.
/// </para>
/// <para>
/// Both <c>BITMAPINFOHEADER</c> (biSize 40) and its larger V4/V5 forms (108/124) are accepted; the
/// common 40-byte prefix carries every field this reader needs. Rows may be stored bottom-up (positive
/// height, the normal case) or top-down (negative height), and the 4-byte row padding is skipped.
/// </para>
/// <para>
/// An 8bpp palette whose entries are the identity ramp (B=G=R=index) is treated as greyscale and
/// decoded to a one-channel frame. Any other palette is expanded to interleaved RGB, because the
/// <see cref="Frame"/> model has no palette channel. 32bpp alpha is discarded.
/// </para>
/// </remarks>
public sealed class BmpReader : IFrameSource
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly string? _sourcePath;
    private readonly long _start;
    private readonly BmpHeader _header;

    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens a BMP file for reading.</summary>
    /// <param name="path">Path to the bitmap.</param>
    /// <exception cref="InvalidDataException">The file is not a supported BMP.</exception>
    public BmpReader(string path)
        : this(OpenFile(path), ownsStream: true, path)
    {
    }

    /// <summary>Reads a BMP from a stream positioned at the bitmap's first byte.</summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    /// <exception cref="InvalidDataException">The data is not a supported BMP.</exception>
    public BmpReader(Stream stream, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, sourcePath: null)
    {
    }

    private BmpReader(Stream stream, bool ownsStream, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _ownsStream = ownsStream;
        _sourcePath = sourcePath;
        _start = stream.CanSeek ? stream.Position : 0;

        try
        {
            _header = BmpHeader.Read(stream);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("BMP file is truncated inside its header.", ex);
        }

        // Position the stream at the pixel array. Non-seekable streams are advanced by discarding the
        // gap (palette padding or an over-sized bfOffBits).
        long position = 14L + _header.InfoHeaderSize + (long)_header.PaletteBytes;
        if (stream.CanSeek)
        {
            stream.Position = _start + _header.DataOffset;
        }
        else
        {
            long gap = _header.DataOffset - position;
            if (gap < 0)
                throw new InvalidDataException($"BMP pixel offset {_header.DataOffset} overlaps its header/palette.");
            Skip(stream, gap);
        }
    }

    /// <inheritdoc />
    public int Width => _header.Width;

    /// <inheritdoc />
    public int Height => _header.Height;

    /// <inheritdoc />
    public int Channels => _header.Channels;

    /// <summary>BMP stores at most 8 bits per channel, so decoded frames are always 8-bit.</summary>
    public int BitDepth => 8;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => BayerPattern.None;

    /// <summary>True when the bitmap's rows are stored top-down (negative biHeight).</summary>
    public bool IsTopDown => _header.TopDown;

    /// <summary>True when an 8bpp palette was expanded to RGB because it is not the identity ramp.</summary>
    public bool HasColourPalette => _header.Channels == 3;

    /// <summary>Size in bytes of one padded on-disk row.</summary>
    public int RowStride => _header.RowStride;

    /// <summary>Path this reader was opened from, or null for stream construction.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>
    /// Reads only the BMP headers to report geometry, depth and decoded channel count.
    /// </summary>
    /// <param name="info">Bitmap file to inspect.</param>
    /// <returns>Metadata for the image.</returns>
    /// <exception cref="InvalidDataException">The file is not a supported BMP.</exception>
    public static ImageInfo Probe(FileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var stream = info.OpenRead();
        return Probe(stream);
    }

    /// <summary>
    /// Reads only the BMP headers from a stream to report geometry, depth and decoded channel count.
    /// The stream is left open; a seekable stream is restored to its original position.
    /// </summary>
    /// <param name="stream">Stream positioned at the bitmap's first byte.</param>
    /// <returns>Metadata for the image.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported BMP.</exception>
    public static ImageInfo Probe(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long position = stream.CanSeek ? stream.Position : 0;
        try
        {
            BmpHeader header = BmpHeader.Read(stream);
            return new ImageInfo(header.Width, header.Height, 8, header.Channels);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("BMP file is truncated inside its header.", ex);
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = position;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The pixel array is truncated.</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= 1)
            return null;

        if (_stream.CanSeek)
            _stream.Position = _start + _header.DataOffset;

        var frame = new Frame(Width, Height, Channels, 8)
        {
            Index = 0,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        byte[] row = new byte[_header.RowStride];
        int pixelBytes = Width * Channels;

        for (int y = 0; y < Height; y++)
        {
            try
            {
                _stream.ReadExactly(row);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"BMP pixel row {y} is truncated.", ex);
            }

            int destY = _header.TopDown ? y : Height - 1 - y;
            int dest = destY * Width * Channels;

            if (_header.PaletteRgb is { } palette)
            {
                for (int x = 0; x < Width; x++)
                {
                    int p = x * 3;
                    int entry = row[x] * 3;
                    frame.Samples[dest + p] = palette[entry];
                    frame.Samples[dest + p + 1] = palette[entry + 1];
                    frame.Samples[dest + p + 2] = palette[entry + 2];
                }
            }
            else if (Channels == 1)
            {
                for (int x = 0; x < Width; x++)
                    frame.Samples[dest + x] = row[x];
            }
            else if (_header.BitCount == 24)
            {
                for (int x = 0; x < Width; x++)
                {
                    int src = x * 3;
                    int p = dest + x * 3;
                    frame.Samples[p] = row[src + 2];
                    frame.Samples[p + 1] = row[src + 1];
                    frame.Samples[p + 2] = row[src];
                }
            }
            else // 32bpp BGRA, alpha discarded
            {
                for (int x = 0; x < Width; x++)
                {
                    int src = x * 4;
                    int p = dest + x * 3;
                    frame.Samples[p] = row[src + 2];
                    frame.Samples[p + 1] = row[src + 1];
                    frame.Samples[p + 2] = row[src];
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

    private static FileStream OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void Skip(Stream stream, long count)
    {
        if (count <= 0)
            return;
        byte[] scratch = new byte[(int)Math.Min(count, 4096)];
        while (count > 0)
        {
            int n = stream.Read(scratch, 0, (int)Math.Min(scratch.Length, count));
            if (n <= 0)
                throw new InvalidDataException("BMP file is truncated before its pixel array.");
            count -= n;
        }
    }
}

/// <summary>Parsed fields of a BMP file and info header.</summary>
internal sealed class BmpHeader
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Channels { get; init; }

    public required int BitCount { get; init; }

    public required bool TopDown { get; init; }

    public required int RowStride { get; init; }

    /// <summary>Offset of the pixel array from the bitmap's start.</summary>
    public required long DataOffset { get; init; }

    /// <summary>Size of the info header (40, 108 or 124).</summary>
    public required int InfoHeaderSize { get; init; }

    /// <summary>Bytes of palette data following the info header.</summary>
    public required int PaletteBytes { get; init; }

    /// <summary>When non-null, an 8bpp palette expanded to packed RGB triples.</summary>
    public ushort[]? PaletteRgb { get; init; }

    public static BmpHeader Read(Stream stream)
    {
        Span<byte> fileHeader = stackalloc byte[14];
        stream.ReadExactly(fileHeader);

        if (fileHeader[0] != (byte)'B' || fileHeader[1] != (byte)'M')
            throw new InvalidDataException("Not a BMP file: missing the 'BM' signature.");

        uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(fileHeader[10..]);

        Span<byte> sizeBytes = stackalloc byte[4];
        stream.ReadExactly(sizeBytes);
        uint infoSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBytes);
        if (infoSize < 40)
            throw new InvalidDataException($"Unsupported BMP info header size {infoSize}; BITMAPCOREHEADER is not supported.");
        if (infoSize > 1024)
            throw new InvalidDataException($"Implausible BMP info header size {infoSize}.");

        byte[] info = new byte[infoSize];
        BinaryPrimitives.WriteUInt32LittleEndian(info, infoSize);
        stream.ReadExactly(info.AsSpan(4));

        int width = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(4));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(8));
        ushort planes = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(12));
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(14));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(16));
        uint clrUsed = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(32));

        if (width <= 0)
            throw new InvalidDataException($"BMP has invalid width {width}.");
        if (rawHeight == 0)
            throw new InvalidDataException("BMP has zero height.");
        if (planes != 1)
            throw new InvalidDataException($"BMP declares {planes} planes; only 1 is supported.");

        if (bitCount is not (8 or 24 or 32))
            throw new InvalidDataException($"Only 8bpp, 24bpp and 32bpp supported by PIPP (file uses {bitCount}bpp).");

        if (compression != 0)
            throw new NotSupportedException($"BMP compression method {compression} is not supported; only BI_RGB (0).");

        bool topDown = rawHeight < 0;
        int height = topDown ? -rawHeight : rawHeight;
        if (height <= 0)
            throw new InvalidDataException($"BMP has invalid height {rawHeight}.");

        int channels = bitCount == 8 ? 1 : 3;
        ushort[]? paletteRgb = null;
        int paletteBytes = 0;

        if (bitCount == 8)
        {
            long entries = clrUsed == 0 ? 256 : clrUsed;
            if (entries > 256)
                throw new InvalidDataException($"BMP declares {entries} palette entries; 8bpp allows at most 256.");
            paletteBytes = checked((int)entries * 4);
            byte[] palette = new byte[paletteBytes];
            stream.ReadExactly(palette);

            bool identity = entries == 256;
            for (int i = 0; identity && i < 256; i++)
            {
                int p = i * 4;
                if (palette[p] != i || palette[p + 1] != i || palette[p + 2] != i)
                    identity = false;
            }

            if (!identity)
            {
                channels = 3;
                paletteRgb = new ushort[256 * 3];
                for (int i = 0; i < 256; i++)
                {
                    int src = Math.Min(i, (int)entries - 1) * 4;
                    paletteRgb[i * 3] = palette[src + 2];     // R
                    paletteRgb[i * 3 + 1] = palette[src + 1]; // G
                    paletteRgb[i * 3 + 2] = palette[src];     // B
                }
            }
        }

        long minimumOffset = 14L + infoSize + paletteBytes;
        if (dataOffset < minimumOffset)
            throw new InvalidDataException(
                $"BMP pixel offset {dataOffset} overlaps its header/palette ({minimumOffset} bytes).");

        int rowBytes = checked(width * (bitCount == 8 ? 1 : bitCount == 24 ? 3 : 4));
        int rowStride = (rowBytes + 3) & ~3;

        return new BmpHeader
        {
            Width = width,
            Height = height,
            Channels = channels,
            BitCount = bitCount,
            TopDown = topDown,
            RowStride = rowStride,
            DataOffset = dataOffset,
            InfoHeaderSize = (int)infoSize,
            PaletteBytes = paletteBytes,
            PaletteRgb = paletteRgb,
        };
    }
}
