using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Sequential reader for uncompressed AVI 1.0 files: 8bpp and 24bpp <c>BI_RGB</c> DIB video plus
/// <c>Y800</c> 8bpp mono. The first video stream in the file is used. Compressed codecs (ULRG, FMP4,
/// MJPEG, YUY2, ...) are rejected with <see cref="NotSupportedException"/> rather than misdecoded.
/// </summary>
/// <remarks>
/// The RIFF walk tolerates the usual real-world damage: both <c>00db</c> and <c>00dc</c> video chunk ids,
/// files without <c>idx1</c>, broken RIFF sizes, and both <c>idx1</c> offset conventions. DIB rows are
/// interpreted with the sign of <c>biHeight</c> (positive = bottom-up, negative = top-down); Y800 scanlines
/// are always interpreted top-down (some muxers, including ffmpeg, leave <c>biHeight</c> positive while storing
/// top-down), matching PIPP's own Y800 output, and its row padding is ignored.
/// </remarks>
public sealed class AviReader : IFrameSource
{
    private enum DecodeMode
    {
        Mono8,
        Palette8,
        Bgr24,
        Y800,
        Jpeg,
    }

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly string? _sourcePath;
    private readonly AviStructure _structure;
    private readonly DecodeMode _mode;
    private readonly byte[] _palette;
    private readonly int _stride;
    private readonly int _rowBytes;
    private readonly byte[] _frameBuffer;
    private readonly bool _topDown;

    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens an AVI file for reading.</summary>
    /// <param name="path">Path to the AVI file.</param>
    /// <exception cref="InvalidDataException">The file is not a usable RIFF/AVI file.</exception>
    /// <exception cref="NotSupportedException">The video stream uses a compressed codec.</exception>
    public AviReader(string path)
        : this(OpenFile(path), ownsStream: true, path)
    {
    }

    /// <summary>Reads an AVI stream positioned at its first byte.</summary>
    /// <param name="stream">Readable, seekable stream containing the AVI file.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    public AviReader(Stream stream, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, sourcePath: null)
    {
    }

    private AviReader(Stream stream, bool ownsStream, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _ownsStream = ownsStream;
        _sourcePath = sourcePath;

        try
        {
            _structure = AviStructureReader.Read(stream);
            if (!_structure.HasBitmap)
                throw new InvalidDataException("AVI file has no video stream with a BITMAPINFOHEADER ('strf') chunk.");

            Info = AviStructureReader.CreateInfo(_structure);
            BitmapInfoHeader bitmap = _structure.BitmapHeader;
            _topDown = bitmap.Height < 0;

            _mode = SelectMode(bitmap.Compression, bitmap.BitCount, Info.FourCC);
            Channels = _mode switch
            {
                DecodeMode.Bgr24 => 3,
                DecodeMode.Jpeg => 3,
                DecodeMode.Palette8 => PaletteIsGrayscale(bitmap, _structure.Palette) ? 1 : 3,
                _ => 1,
            };

            _palette = BuildPalette(bitmap, _structure.Palette);

            if (_mode == DecodeMode.Jpeg)
            {
                // JPEG frames are self-describing: no row stride or fixed frame buffer applies.
                _rowBytes = 0;
                _stride = 0;
                _frameBuffer = Array.Empty<byte>();
            }
            else
            {
                long rowBytes = _mode == DecodeMode.Bgr24 ? (long)Info.Width * 3 : Info.Width;
                long stride = _mode == DecodeMode.Y800 ? rowBytes : (rowBytes + 3) & ~3L;
                long frameBytes = stride * Info.Height;
                if (rowBytes > int.MaxValue || frameBytes > int.MaxValue)
                    throw new InvalidDataException($"AVI frame {Info.Width}x{Info.Height} is too large to decode in memory.");

                _rowBytes = (int)rowBytes;
                _stride = (int)stride;
                _frameBuffer = new byte[(int)frameBytes];

                if (_mode == DecodeMode.Y800)
                    _topDown = true;
            }
        }
        catch
        {
            if (_ownsStream)
                stream.Dispose();
            throw;
        }
    }

    /// <summary>Metadata of the first video stream.</summary>
    public AviInfo Info { get; }

    /// <summary>True when the file contains an <c>avih</c> main header.</summary>
    public bool HasMainHeader => _structure.HasMainHeader;

    /// <inheritdoc />
    public int Width => Info.Width;

    /// <inheritdoc />
    public int Height => Info.Height;

    /// <inheritdoc />
    public int Channels { get; }

    /// <summary>Bit depth of the produced <see cref="Frame"/> samples; always 8 because AVI 1.0 DIB/Y800 here is 8bpp/24bpp.</summary>
    public int BitDepth => 8;

    /// <inheritdoc />
    public int FrameCount => Info.FrameCount;

    /// <inheritdoc />
    public BayerPattern Bayer => BayerPattern.None;

    /// <summary>Index of the next frame that <see cref="ReadFrame"/> will return.</summary>
    public int Position => _nextIndex;

    /// <summary>Path this reader was opened from, or null for stream construction.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>Compression FourCC of the stream as text (<c>BI_RGB</c> for uncompressed DIB).</summary>
    public string FourCC => Info.FourCC;

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">A frame chunk is missing or truncated.</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= FrameCount)
            return null;

        if (_mode == DecodeMode.Jpeg)
        {
            AviFrameLocation jpegLocation = _structure.Frames[_nextIndex];
            var payload = new byte[jpegLocation.Size];
            _stream.Position = jpegLocation.Offset;
            _stream.ReadExactly(payload);

            Frame decoded = JpegBaselineDecoder.Decode(payload);
            decoded.Index = _nextIndex;
            decoded.SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath);
            _nextIndex++;
            return decoded;
        }

        AviFrameLocation location = _structure.Frames[_nextIndex];
        int stride = _stride;
        long required = (long)stride * Height;

        if (location.Size < required)
        {
            // Tolerate writers that did not DWORD-align DIB rows or that used tight Y800 rows.
            long tight = (long)_rowBytes * Height;
            if (location.Size >= tight)
            {
                stride = _rowBytes;
                required = tight;
            }
            else
            {
                throw new InvalidDataException(
                    $"AVI frame {_nextIndex} is truncated: {location.Size} bytes available, {required} required.");
            }
        }

        int index = _nextIndex;
        try
        {
            _stream.Position = location.Offset;
            _stream.ReadExactly(_frameBuffer.AsSpan(0, (int)required));
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"AVI frame {index} is truncated.", ex);
        }

        var frame = new Frame(Width, Height, Channels, 8)
        {
            Index = index,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        switch (_mode)
        {
            case DecodeMode.Bgr24:
                DecodeBgr24(frame, stride);
                break;
            case DecodeMode.Y800:
                DecodeMono8(frame, stride);
                break;
            default:
                if (Channels == 3)
                    DecodePalette(frame, stride);
                else
                    DecodeMono8(frame, stride);
                break;
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

    private static DecodeMode SelectMode(uint compression, int bitCount, string fourCC)
    {
        if (compression == AviIds.Mjpg || string.Equals(fourCC, "MJPG", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeMode.Jpeg;
        }

        if (compression == AviIds.Y800)
        {
            if (bitCount != 8)
                throw new NotSupportedException(
                    $"AVI Y800 stream has biBitCount={bitCount}; only 8bpp Y800 is supported.");
            return DecodeMode.Y800;
        }

        if (compression == BitmapInfoHeader.BiRgb)
        {
            return bitCount switch
            {
                8 => DecodeMode.Palette8,
                24 => DecodeMode.Bgr24,
                _ => throw new NotSupportedException(
                    $"AVI video stream uses uncompressed BI_RGB at {bitCount} bpp, which is not supported. " +
                    "This reader decodes BI_RGB 8bpp and 24bpp only; decode other depths with the FFmpeg path."),
            };
        }

        throw new NotSupportedException(
            $"AVI video stream compression '{fourCC}' (biBitCount={bitCount}) is not supported. " +
            $"Decoding '{fourCC}' requires the FFmpeg path; this reader handles only uncompressed BI_RGB " +
            "(8bpp DIB, 24bpp DIB) and Y800.");
    }

    private static byte[] BuildPalette(BitmapInfoHeader bitmap, byte[] stored)
    {
        var palette = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            palette[i * 3] = (byte)i;
            palette[i * 3 + 1] = (byte)i;
            palette[i * 3 + 2] = (byte)i;
        }

        int entries = Math.Min(256, stored.Length / 3);
        if (bitmap.ClrUsed is > 0 and <= 256)
            entries = Math.Min(entries, (int)bitmap.ClrUsed);

        Array.Copy(stored, palette, entries * 3);
        return palette;
    }

    private static bool PaletteIsGrayscale(BitmapInfoHeader bitmap, byte[] stored)
    {
        int entries = Math.Min(256, stored.Length / 3);
        if (bitmap.ClrUsed is > 0 and <= 256)
            entries = Math.Min(entries, (int)bitmap.ClrUsed);
        return entries == 0 || IsGrayscale(stored, entries);
    }

    private static bool IsGrayscale(byte[] palette, int entries)
    {
        for (int i = 0; i < entries; i++)
        {
            if (palette[i * 3] != palette[i * 3 + 1] || palette[i * 3] != palette[i * 3 + 2])
                return false;
        }

        return true;
    }

    private void DecodeBgr24(Frame frame, int stride)
    {
        int width = Width;
        int height = Height;
        ushort[] samples = frame.Samples;

        for (int y = 0; y < height; y++)
        {
            int sourceRow = _topDown ? y : height - 1 - y;
            int source = sourceRow * stride;
            int destination = y * width * 3;
            for (int x = 0; x < width; x++)
            {
                byte b = _frameBuffer[source];
                byte g = _frameBuffer[source + 1];
                byte r = _frameBuffer[source + 2];
                source += 3;
                samples[destination] = r;
                samples[destination + 1] = g;
                samples[destination + 2] = b;
                destination += 3;
            }
        }
    }

    private void DecodeMono8(Frame frame, int stride)
    {
        int width = Width;
        int height = Height;
        ushort[] samples = frame.Samples;

        for (int y = 0; y < height; y++)
        {
            int sourceRow = _topDown ? y : height - 1 - y;
            int source = sourceRow * stride;
            int destination = y * width;
            for (int x = 0; x < width; x++)
                samples[destination + x] = _palette[_frameBuffer[source + x] * 3];
        }
    }

    private void DecodePalette(Frame frame, int stride)
    {
        int width = Width;
        int height = Height;
        ushort[] samples = frame.Samples;

        for (int y = 0; y < height; y++)
        {
            int sourceRow = _topDown ? y : height - 1 - y;
            int source = sourceRow * stride;
            int destination = y * width * 3;
            for (int x = 0; x < width; x++)
            {
                int entry = _frameBuffer[source + x] * 3;
                samples[destination] = _palette[entry];
                samples[destination + 1] = _palette[entry + 1];
                samples[destination + 2] = _palette[entry + 2];
                destination += 3;
            }
        }
    }

    private static FileStream OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
