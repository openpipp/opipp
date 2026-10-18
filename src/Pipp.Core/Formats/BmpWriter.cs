using System.Buffers.Binary;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Writes a single uncompressed Windows bitmap: 8bpp greyscale with an identity palette for mono frames,
/// 24bpp BGR for colour frames. Rows are bottom-up and padded to a 4-byte boundary, matching PIPP's
/// <c>save_mono_file</c>/<c>save_colour_file</c> output.
/// </summary>
/// <remarks>
/// BMP has no portable 16-bit-per-channel form (16bpp bitmaps are RGB555/565, which PIPP rejects on
/// read), so 16-bit frames are reduced to 8 bits by taking the high byte (<c>value &gt;&gt; 8</c>).
/// The frame is buffered and the file is written on <see cref="Dispose"/>; exactly one frame may be
/// written per bitmap.
/// </remarks>
public sealed class BmpWriter : IFrameSink
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly int _sourceBitDepth;

    private Frame? _frame;
    private bool _disposed;

    /// <summary>Creates a BMP file writer.</summary>
    /// <param name="path">Destination path; the file is written on <see cref="Dispose"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">1 for mono, 3 for colour.</param>
    /// <param name="bitDepth">Bit depth of the frames that will be written: 8 or 16.</param>
    /// <exception cref="ArgumentOutOfRangeException">A geometry or depth value is invalid.</exception>
    public BmpWriter(string path, int width, int height, int channels, int bitDepth)
        : this(CreateFile(path), ownsStream: true, width, height, channels, bitDepth)
    {
    }

    /// <summary>Creates a BMP writer for a stream.</summary>
    /// <param name="stream">Destination stream, written on <see cref="Dispose"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">1 for mono, 3 for colour.</param>
    /// <param name="bitDepth">Bit depth of the frames that will be written: 8 or 16.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this writer is disposed.</param>
    public BmpWriter(Stream stream, int width, int height, int channels, int bitDepth, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, width, height, channels, bitDepth)
    {
    }

    private BmpWriter(Stream stream, bool ownsStream, int width, int height, int channels, int bitDepth)
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
        _sourceBitDepth = bitDepth;
    }

    /// <summary>True once a frame has been supplied.</summary>
    public bool HasFrame => _frame is not null;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentException">The frame geometry, channel count or depth differs from the writer's.</exception>
    /// <exception cref="InvalidOperationException">A frame was already written.</exception>
    public void Write(Frame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (_frame is not null)
            throw new InvalidOperationException("A BMP holds a single frame; Write was called twice.");
        if (frame.Width != _width || frame.Height != _height)
            throw new ArgumentException(
                $"frame is {frame.Width}x{frame.Height}, writer expects {_width}x{_height}", nameof(frame));
        if (frame.Channels != _channels)
            throw new ArgumentException(
                $"frame has {frame.Channels} channel(s), writer expects {_channels}", nameof(frame));
        if (frame.BitDepth != _sourceBitDepth)
            throw new ArgumentException(
                $"frame depth is {frame.BitDepth}, writer expects {_sourceBitDepth}", nameof(frame));

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

            WriteBitmap(_frame);
            _stream.Flush();
        }
        finally
        {
            if (_ownsStream)
                _stream.Dispose();
        }
    }

    private void WriteBitmap(Frame frame)
    {
        bool mono = _channels == 1;
        int rowBytes = checked(_width * _channels);
        int stride = (rowBytes + 3) & ~3;
        int paletteBytes = mono ? 256 * 4 : 0;
        int imageBytes = checked(stride * _height);
        int offset = 14 + 40 + paletteBytes;
        int fileSize = checked(offset + imageBytes);

        Span<byte> header = stackalloc byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)fileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], (uint)offset);
        BinaryPrimitives.WriteUInt32LittleEndian(header[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], _width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], _height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], (ushort)(mono ? 8 : 24));
        BinaryPrimitives.WriteUInt32LittleEndian(header[30..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[34..], (uint)imageBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header[46..], mono ? 256u : 0u); // biClrUsed
        BinaryPrimitives.WriteUInt32LittleEndian(header[50..], mono ? 256u : 0u); // biClrImportant
        _stream.Write(header);

        if (mono)
        {
            Span<byte> palette = stackalloc byte[4];
            for (int i = 0; i < 256; i++)
            {
                palette[0] = (byte)i;
                palette[1] = (byte)i;
                palette[2] = (byte)i;
                palette[3] = 0;
                _stream.Write(palette);
            }
        }

        byte[] row = new byte[stride];
        ushort[] samples = frame.Samples;

        for (int y = _height - 1; y >= 0; y--) // BMP is bottom-up
        {
            Array.Clear(row);
            int src = y * _width * _channels;

            if (mono)
            {
                for (int x = 0; x < _width; x++)
                    row[x] = ToByte(samples[src + x], _sourceBitDepth);
            }
            else
            {
                for (int x = 0; x < _width; x++)
                {
                    int s = src + x * 3;
                    int p = x * 3;
                    row[p] = ToByte(samples[s + 2], _sourceBitDepth);     // B
                    row[p + 1] = ToByte(samples[s + 1], _sourceBitDepth); // G
                    row[p + 2] = ToByte(samples[s], _sourceBitDepth);     // R
                }
            }

            _stream.Write(row);
        }
    }

    private static byte ToByte(ushort value, int bitDepth)
    {
        if (bitDepth == 16)
            return (byte)(value >> 8);
        return value > byte.MaxValue ? byte.MaxValue : (byte)value;
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
