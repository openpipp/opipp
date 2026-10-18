using System.Buffers.Binary;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>Options that adjust how a SER file is decoded.</summary>
public sealed class SerReaderOptions
{
    /// <summary>
    /// Forces the bits-per-sample interpretation (8..16) instead of using the header value. This changes
    /// how many bytes are consumed per sample when the header's <c>PixelDepthPerPlane</c> is wrong:
    /// values up to 8 read one byte per sample, values above 8 read two. Null uses the header value.
    /// </summary>
    public int? BitDepthOverride { get; init; }
}

/// <summary>
/// Sequential reader for SER v3 files. Frames are decoded into <see cref="Frame"/> instances at the
/// reader's bit depth: 8-bit sources stay in the 0..255 range, deeper sources use 16-bit samples.
/// Supports forward reads, seeking by frame index, and both little- and big-endian sample data.
/// </summary>
public sealed class SerReader : IFrameSource
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly string? _sourcePath;
    private readonly long _dataStart;
    private readonly int _bytesPerSample;
    private readonly int _frameSize;
    private readonly int _sampleCount;
    private readonly bool _swapRedBlue;
    private readonly byte[] _frameBuffer;

    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens a SER file for reading.</summary>
    /// <param name="path">Path to the SER file.</param>
    /// <param name="options">Optional decode overrides.</param>
    /// <exception cref="InvalidDataException">The header is invalid, or the file is too short for its declared frames.</exception>
    public SerReader(string path, SerReaderOptions? options = null)
        : this(OpenFile(path), ownsStream: true, options, path)
    {
    }

    /// <summary>Reads a SER stream starting at its current position.</summary>
    /// <param name="stream">Seekable or sequential stream positioned at the header.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    /// <param name="options">Optional decode overrides.</param>
    public SerReader(Stream stream, bool leaveOpen = false, SerReaderOptions? options = null)
        : this(stream, ownsStream: !leaveOpen, options, sourcePath: null)
    {
    }

    private SerReader(Stream stream, bool ownsStream, SerReaderOptions? options, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _ownsStream = ownsStream;
        _sourcePath = sourcePath;
        long headerStart = stream.CanSeek ? stream.Position : 0;

        try
        {
            Header = SerHeader.Read(stream);
            _dataStart = headerStart + SerHeader.Size;

            if (Header.Width <= 0 || Header.Height <= 0)
                throw new InvalidDataException($"SER header has invalid dimensions {Header.Width}x{Header.Height}.");
            if (Header.FrameCount < 0)
                throw new InvalidDataException($"SER header has a negative frame count ({Header.FrameCount}).");
            if (!SerHeader.IsSupportedColorId(Header.ColorId))
                throw new InvalidDataException($"Unsupported SER ColorID {Header.ColorId}.");

            int? forcedDepth = options?.BitDepthOverride;
            if (forcedDepth is int forced && (forced < 8 || forced > 16))
                throw new ArgumentOutOfRangeException(nameof(options), forced, "BitDepthOverride must be 8..16");

            int pixelDepth = forcedDepth ?? Header.BitDepth;
            if (pixelDepth is < 8 or > 16)
                throw new InvalidDataException($"Unsupported SER pixel depth {pixelDepth}; expected 8..16.");

            PixelDepth = pixelDepth;
            BitDepth = pixelDepth <= 8 ? 8 : 16;
            _bytesPerSample = pixelDepth <= 8 ? 1 : 2;
            _swapRedBlue = Header.ColorId == (int)SerColorId.Bgr;

            long sampleCount = (long)Header.Width * Header.Height * Header.Channels;
            long frameSize = sampleCount * _bytesPerSample;
            if (frameSize > int.MaxValue)
                throw new InvalidDataException($"SER frame is too large ({frameSize} bytes).");

            _sampleCount = (int)sampleCount;
            _frameSize = (int)frameSize;
            _frameBuffer = new byte[_frameSize];

            if (stream.CanSeek)
            {
                long required = (long)Header.FrameCount * _frameSize;
                long available = stream.Length - _dataStart;
                if (available < required)
                {
                    throw new InvalidDataException(
                        $"SER data truncated: {required} bytes required for {Header.FrameCount} frame(s), " +
                        $"only {Math.Max(0, available)} available after the header.");
                }
            }
        }
        catch
        {
            if (ownsStream)
                stream.Dispose();
            throw;
        }
    }

    /// <summary>The parsed file header.</summary>
    public SerHeader Header { get; }

    /// <inheritdoc />
    public int Width => Header.Width;

    /// <inheritdoc />
    public int Height => Header.Height;

    /// <inheritdoc />
    public int Channels => Header.Channels;

    /// <summary>
    /// Bit depth of the produced <see cref="Frame"/> samples: 8 when the file stores one byte per sample,
    /// otherwise 16 (including 9..15-bit SER files, whose samples occupy 16-bit words).
    /// </summary>
    public int BitDepth { get; }

    /// <summary>Bits per sample actually interpreted: the header value, or <see cref="SerReaderOptions.BitDepthOverride"/>.</summary>
    public int PixelDepth { get; }

    /// <inheritdoc />
    public int FrameCount => Header.FrameCount;

    /// <inheritdoc />
    public BayerPattern Bayer => Header.Bayer;

    /// <summary>Index of the next frame that <see cref="ReadFrame"/> will return.</summary>
    public int Position => _nextIndex;

    /// <summary>Path this reader was opened from, or null for stream construction.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>True when the file's frame data is big-endian.</summary>
    public bool IsBigEndian => !Header.LittleEndian;

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">A frame's bytes are missing (truncated file).</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= Header.FrameCount)
            return null;

        int index = _nextIndex;
        try
        {
            _stream.ReadExactly(_frameBuffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"SER frame {index} is truncated.", ex);
        }

        var frame = new Frame(Header.Width, Header.Height, Header.Channels, BitDepth)
        {
            Bayer = Header.Bayer,
            Index = index,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        if (_bytesPerSample == 1)
        {
            for (int i = 0; i < _sampleCount; i++)
                frame.Samples[i] = _frameBuffer[i];
        }
        else if (Header.LittleEndian)
        {
            for (int i = 0; i < _sampleCount; i++)
                frame.Samples[i] = BinaryPrimitives.ReadUInt16LittleEndian(_frameBuffer.AsSpan(i * 2));
        }
        else
        {
            for (int i = 0; i < _sampleCount; i++)
                frame.Samples[i] = BinaryPrimitives.ReadUInt16BigEndian(_frameBuffer.AsSpan(i * 2));
        }

        if (_swapRedBlue)
            SwapRedAndBlue(frame.Samples, Header.Width * Header.Height);

        _nextIndex++;
        return frame;
    }

    /// <summary>Reads the frame at <paramref name="index"/>, repositioning the reader.</summary>
    /// <param name="index">Zero-based frame index; may equal <see cref="FrameCount"/> to land at end of data.</param>
    /// <returns>The frame, or null when <paramref name="index"/> equals <see cref="FrameCount"/>.</returns>
    public Frame? ReadFrameAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0 || index > Header.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"frame index must be 0..{Header.FrameCount}");
        Seek(index);
        return ReadFrame();
    }

    /// <summary>Moves the next read position to the given zero-based frame index.</summary>
    /// <param name="index">Frame index in the range 0..<see cref="FrameCount"/>.</param>
    /// <exception cref="NotSupportedException">The underlying stream is not seekable.</exception>
    public void Seek(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0 || index > Header.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"frame index must be 0..{Header.FrameCount}");
        if (!_stream.CanSeek)
            throw new NotSupportedException("The underlying SER stream is not seekable.");

        _stream.Position = _dataStart + (long)index * _frameSize;
        _nextIndex = index;
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

    private static void SwapRedAndBlue(ushort[] samples, int pixelCount)
    {
        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 3;
            (samples[i], samples[i + 2]) = (samples[i + 2], samples[i]);
        }
    }
}
