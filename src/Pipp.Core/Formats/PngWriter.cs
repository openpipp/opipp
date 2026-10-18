using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Writes a standards-compliant PNG (signature, IHDR, one IDAT, IEND) for 8- or 16-bit greyscale
/// (colour type 0) and RGB (colour type 2) frames.
/// </summary>
/// <remarks>
/// Scanlines use filter type 0 (None) as PIPP's libpng path does, 16-bit samples are stored big-endian
/// as the PNG specification requires, and the IDAT payload is a zlib stream produced by
/// <see cref="ZLibStream"/>. Each chunk carries a CRC-32 computed by <see cref="PngCrc32"/>. Exactly
/// one frame is buffered and written on <see cref="Dispose"/>.
/// </remarks>
public sealed class PngWriter : IFrameSink
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly int _bitDepth;

    private Frame? _frame;
    private bool _disposed;

    /// <summary>Creates a PNG file writer.</summary>
    /// <param name="path">Destination path; the file is written on <see cref="Dispose"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">1 for greyscale, 3 for RGB.</param>
    /// <param name="bitDepth">Bits per sample: 8 or 16.</param>
    public PngWriter(string path, int width, int height, int channels, int bitDepth)
        : this(CreateFile(path), ownsStream: true, width, height, channels, bitDepth)
    {
    }

    /// <summary>Creates a PNG writer for a stream.</summary>
    /// <param name="stream">Destination stream, written on <see cref="Dispose"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">1 for greyscale, 3 for RGB.</param>
    /// <param name="bitDepth">Bits per sample: 8 or 16.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this writer is disposed.</param>
    public PngWriter(Stream stream, int width, int height, int channels, int bitDepth, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, width, height, channels, bitDepth)
    {
    }

    private PngWriter(Stream stream, bool ownsStream, int width, int height, int channels, int bitDepth)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (channels is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(channels));
        if (bitDepth is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(bitDepth));

        _stream = stream;
        _ownsStream = ownsStream;
        _width = width;
        _height = height;
        _channels = channels;
        _bitDepth = bitDepth;
    }

    /// <summary>True once a frame has been supplied.</summary>
    public bool HasFrame => _frame is not null;

    /// <inheritdoc />
    public void Write(Frame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (_frame is not null)
            throw new InvalidOperationException("A PNG holds a single frame; Write was called twice.");
        if (frame.Width != _width || frame.Height != _height)
            throw new ArgumentException(
                $"frame is {frame.Width}x{frame.Height}, writer expects {_width}x{_height}", nameof(frame));
        if (frame.Channels != _channels)
            throw new ArgumentException(
                $"frame has {frame.Channels} channel(s), writer expects {_channels}", nameof(frame));
        if (frame.BitDepth != _bitDepth)
            throw new ArgumentException(
                $"frame depth is {frame.BitDepth}, writer expects {_bitDepth}", nameof(frame));

        _frame = frame.Clone();
    }

    /// <summary>Serialises the buffered frame to the destination and closes an owned stream. Disposing without a frame writes nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_frame is null)
                return;

            WriteImage(_frame);
            _stream.Flush();
        }
        finally
        {
            if (_ownsStream)
                _stream.Dispose();
        }
    }

    private void WriteImage(Frame frame)
    {
        int bytesPerSample = _bitDepth / 8;
        int rowBytes = checked(_width * _channels * bytesPerSample);
        byte[] raw = new byte[checked((rowBytes + 1) * _height)];

        ushort[] samples = frame.Samples;
        int position = 0;
        for (int y = 0; y < _height; y++)
        {
            raw[position++] = 0; // filter type None
            int source = y * _width * _channels;

            if (bytesPerSample == 1)
            {
                for (int i = 0; i < rowBytes; i++)
                {
                    ushort value = samples[source + i];
                    raw[position++] = value > byte.MaxValue ? byte.MaxValue : (byte)value;
                }
            }
            else
            {
                for (int i = 0; i < rowBytes / 2; i++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(position), samples[source + i]);
                    position += 2;
                }
            }
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            compressed = buffer.ToArray();
        }

        _stream.Write(Signature);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)_width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)_height);
        ihdr[8] = (byte)_bitDepth;
        ihdr[9] = (byte)(_channels == 1 ? 0 : 2); // greyscale or truecolour
        ihdr[10] = 0; // compression = deflate
        ihdr[11] = 0; // filter method 0
        ihdr[12] = 0; // no interlace

        WriteChunk("IHDR", ihdr);
        WriteChunk("IDAT", compressed);
        WriteChunk("IEND", ReadOnlySpan<byte>.Empty);
    }

    private void WriteChunk(string type, ReadOnlySpan<byte> data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        _stream.Write(length);

        _stream.Write(typeBytes);
        _stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, PngCrc32.Compute(typeBytes, data));
        _stream.Write(crc);
    }

    private static FileStream CreateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }
}
