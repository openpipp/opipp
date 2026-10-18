using System.Buffers.Binary;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>
/// Writes a classic little-endian TIFF with one uncompressed strip: 8- or 16-bit greyscale
/// (PhotometricInterpretation 1) for mono frames, 8- or 16-bit RGB (PhotometricInterpretation 2)
/// for colour frames.
/// </summary>
/// <remarks>
/// The directory holds the tags PIPP's TIFF path emits: ImageWidth, ImageLength, BitsPerSample,
/// Compression, PhotometricInterpretation, StripOffsets, SamplesPerPixel, RowsPerStrip,
/// StripByteCounts, PlanarConfiguration and SampleFormat. Samples are interleaved R,G,B for colour
/// and stored little-endian at 16 bits. Exactly one frame is buffered and written on
/// <see cref="Dispose"/>.
/// </remarks>
public sealed class TiffWriter : IFrameSink
{
    private const int EntryCount = 11;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly int _bitDepth;

    private Frame? _frame;
    private bool _disposed;

    /// <summary>Creates a TIFF file writer.</summary>
    /// <param name="path">Destination path; the file is written on <see cref="Dispose"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">1 for mono, 3 for RGB.</param>
    /// <param name="bitDepth">Samples per channel: 8 or 16.</param>
    public TiffWriter(string path, int width, int height, int channels, int bitDepth)
        : this(CreateFile(path), ownsStream: true, width, height, channels, bitDepth)
    {
    }

    /// <summary>Creates a TIFF writer for a stream.</summary>
    /// <param name="stream">Destination stream, written on <see cref="Dispose"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="channels">1 for mono, 3 for RGB.</param>
    /// <param name="bitDepth">Samples per channel: 8 or 16.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this writer is disposed.</param>
    public TiffWriter(Stream stream, int width, int height, int channels, int bitDepth, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, width, height, channels, bitDepth)
    {
    }

    private TiffWriter(Stream stream, bool ownsStream, int width, int height, int channels, int bitDepth)
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
            throw new InvalidOperationException("A TIFF holds a single frame; Write was called twice.");
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
        int dataBytes = checked(rowBytes * _height);

        const int ifdOffset = 8;
        int extraOffset = ifdOffset + 2 + EntryCount * 12 + 4; // = 146
        int bitsOffset = extraOffset;
        int pixelOffset = _channels == 3 ? extraOffset + 6 : extraOffset;

        Span<byte> header = stackalloc byte[8];
        header[0] = (byte)'I';
        header[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], ifdOffset);
        _stream.Write(header);

        Span<byte> count = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(count, EntryCount);
        _stream.Write(count);

        WriteLongEntry(256, (uint)_width);                 // ImageWidth
        WriteLongEntry(257, (uint)_height);                // ImageLength
        if (_channels == 1)
        {
            WriteShortEntry(258, (ushort)_bitDepth);       // BitsPerSample
        }
        else
        {
            WriteOffsetEntry(258, 3, (uint)bitsOffset);    // BitsPerSample, one per channel
        }

        WriteShortEntry(259, 1);                           // Compression = none
        WriteShortEntry(262, (ushort)(_channels == 1 ? 1 : 2)); // BlackIsZero / RGB
        WriteLongEntry(273, (uint)pixelOffset);            // StripOffsets
        WriteShortEntry(277, (ushort)_channels);           // SamplesPerPixel
        WriteLongEntry(278, (uint)_height);                // RowsPerStrip
        WriteLongEntry(279, (uint)dataBytes);              // StripByteCounts
        WriteShortEntry(284, 1);                           // PlanarConfiguration = chunky
        WriteShortEntry(339, 1);                           // SampleFormat = unsigned

        Span<byte> next = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(next, 0);
        _stream.Write(next);

        if (_stream.Position != ifdOffset + 2 + EntryCount * 12 + 4)
            throw new InvalidOperationException("TIFF directory size accounting is inconsistent.");

        if (_channels == 3)
        {
            Span<byte> bits = stackalloc byte[6];
            for (int c = 0; c < 3; c++)
                BinaryPrimitives.WriteUInt16LittleEndian(bits[(c * 2)..], (ushort)_bitDepth);
            _stream.Write(bits);
        }

        if (_stream.Position != pixelOffset)
            throw new InvalidOperationException("TIFF pixel offset accounting is inconsistent.");

        byte[] row = new byte[rowBytes];
        ushort[] samples = frame.Samples;

        for (int y = 0; y < _height; y++)
        {
            int source = y * _width * _channels;
            if (bytesPerSample == 1)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    ushort value = samples[source + i];
                    row[i] = value > byte.MaxValue ? byte.MaxValue : (byte)value;
                }
            }
            else
            {
                for (int i = 0; i < row.Length / 2; i++)
                    BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(i * 2), samples[source + i]);
            }

            _stream.Write(row);
        }
    }

    private void WriteShortEntry(ushort tag, ushort value)
    {
        Span<byte> entry = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], value);
        _stream.Write(entry);
    }

    private void WriteLongEntry(ushort tag, uint value)
    {
        Span<byte> entry = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], value);
        _stream.Write(entry);
    }

    private void WriteOffsetEntry(ushort tag, uint count, uint offset)
    {
        Span<byte> entry = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], offset);
        _stream.Write(entry);
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
