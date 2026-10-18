using System.Buffers.Binary;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Writes SER v3 files: a 178-byte header followed by packed frames. Sample data is always written
/// little-endian. When the header's frame count is 0 the writer back-patches the real count on dispose
/// (requires a seekable stream). Supports mono, Bayer and RGB/BGR frames at 8..16 bits per sample.
/// </summary>
public sealed class SerWriter : IFrameSink
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly SerHeader _header;
    private readonly long _headerStart;
    private readonly int _bytesPerSample;
    private readonly int _expectedFrameBitDepth;
    private readonly int _pixelCount;
    private readonly bool _writeBgr;
    private readonly byte[] _frameBuffer;

    private bool _disposed;

    /// <summary>Creates (or truncates) a SER file and writes its header.</summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="header">Header describing the frames to write. It is copied; the original is not modified.</param>
    public SerWriter(string path, SerHeader header)
        : this(CreateFile(path), ownsStream: true, header)
    {
    }

    /// <summary>Writes a SER header at the stream's current position.</summary>
    /// <param name="stream">Destination stream, positioned where the header should start.</param>
    /// <param name="header">Header describing the frames to write. It is copied; the original is not modified.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this writer is disposed.</param>
    public SerWriter(Stream stream, SerHeader header, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, header)
    {
    }

    private SerWriter(Stream stream, bool ownsStream, SerHeader header)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);

        _stream = stream;
        _ownsStream = ownsStream;
        _header = header.Clone();
        _header.LittleEndian = true;

        try
        {
            ValidateHeader(_header);

            if (_header.FrameCount == 0 && !stream.CanSeek)
                throw new ArgumentException("A non-seekable stream needs a known frame count in the header.", nameof(header));

            _bytesPerSample = _header.BitDepth <= 8 ? 1 : 2;
            _expectedFrameBitDepth = _bytesPerSample == 1 ? 8 : 16;
            _writeBgr = _header.ColorId == (int)SerColorId.Bgr;
            _pixelCount = checked(_header.Width * _header.Height);
            _frameBuffer = new byte[checked(_pixelCount * _header.Channels * _bytesPerSample)];

            _headerStart = stream.CanSeek ? stream.Position : 0;
            stream.Write(_header.ToBytes());
        }
        catch
        {
            if (ownsStream)
                stream.Dispose();
            throw;
        }
    }

    /// <summary>Convenience constructor that builds a header from geometry.</summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="channels">1 for mono/Bayer, 3 for RGB.</param>
    /// <param name="bitDepth">Bits per sample (8..16).</param>
    /// <param name="frameCount">Number of frames, or 0 when it should be derived from the frames written.</param>
    /// <param name="bayer">Bayer pattern for single-channel frames.</param>
    public SerWriter(
        string path,
        int width,
        int height,
        int channels,
        int bitDepth,
        int frameCount = 0,
        BayerPattern bayer = BayerPattern.None)
        : this(path, SerHeader.Create(width, height, channels, bitDepth, frameCount, bayer))
    {
    }

    /// <summary>The writer's copy of the header. <see cref="SerHeader.FrameCount"/> reflects frames written after disposal.</summary>
    public SerHeader Header => _header;

    /// <summary>Number of frames successfully written so far.</summary>
    public int FramesWritten { get; private set; }

    /// <summary>Appends one frame.</summary>
    /// <param name="frame">Frame matching the header's width, height, channel count and storage depth.</param>
    /// <exception cref="ArgumentException">The frame geometry or depth does not match the header.</exception>
    /// <exception cref="InvalidOperationException">More frames were written than the header declared.</exception>
    public void Write(Frame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Width != _header.Width || frame.Height != _header.Height)
            throw new ArgumentException(
                $"frame is {frame.Width}x{frame.Height}, header expects {_header.Width}x{_header.Height}", nameof(frame));
        if (frame.Channels != _header.Channels)
            throw new ArgumentException(
                $"frame has {frame.Channels} channel(s), header expects {_header.Channels}", nameof(frame));
        if (frame.BitDepth != _expectedFrameBitDepth)
            throw new ArgumentException(
                $"frame depth is {frame.BitDepth}, header depth {_header.BitDepth} requires {_expectedFrameBitDepth}", nameof(frame));
        if (_header.FrameCount != 0 && FramesWritten >= _header.FrameCount)
            throw new InvalidOperationException(
                $"header declares {_header.FrameCount} frame(s); no more can be written");

        Pack(frame);

        _stream.Write(_frameBuffer);
        FramesWritten++;
    }

    /// <summary>
    /// Flushes pending frames and, on a seekable stream, rewrites the header's frame count to the number
    /// actually written. For non-seekable streams the count must already have been exact.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_stream.CanSeek)
            {
                long end = _stream.Position;
                _header.FrameCount = FramesWritten;
                _stream.Position = _headerStart;
                _stream.Write(_header.ToBytes());
                _stream.Position = end;
                _stream.Flush();
            }
            else if (FramesWritten != _header.FrameCount)
            {
                throw new InvalidOperationException(
                    $"header declared {_header.FrameCount} frame(s) but {FramesWritten} were written to a non-seekable stream");
            }
        }
        finally
        {
            if (_ownsStream)
                _stream.Dispose();
        }
    }

    private void Pack(Frame frame)
    {
        ushort[] samples = frame.Samples;

        if (_bytesPerSample == 1)
        {
            if (_writeBgr)
            {
                for (int p = 0; p < _pixelCount; p++)
                {
                    int s = p * 3;
                    _frameBuffer[s] = ClampToByte(samples[s + 2]);
                    _frameBuffer[s + 1] = ClampToByte(samples[s + 1]);
                    _frameBuffer[s + 2] = ClampToByte(samples[s]);
                }
            }
            else
            {
                for (int i = 0; i < _frameBuffer.Length; i++)
                    _frameBuffer[i] = ClampToByte(samples[i]);
            }
        }
        else
        {
            Span<byte> destination = _frameBuffer;
            if (_writeBgr)
            {
                for (int p = 0; p < _pixelCount; p++)
                {
                    int s = p * 3;
                    int b = p * 6;
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[b..], samples[s + 2]);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[(b + 2)..], samples[s + 1]);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[(b + 4)..], samples[s]);
                }
            }
            else
            {
                for (int i = 0; i < samples.Length; i++)
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], samples[i]);
            }
        }
    }

    private static byte ClampToByte(ushort value) => value > byte.MaxValue ? byte.MaxValue : (byte)value;

    private static void ValidateHeader(SerHeader header)
    {
        if (header.Width <= 0 || header.Height <= 0)
            throw new ArgumentException($"invalid SER dimensions {header.Width}x{header.Height}", nameof(header));
        if (header.BitDepth is < 8 or > 16)
            throw new ArgumentException($"SER bit depth must be 8..16, got {header.BitDepth}", nameof(header));
        if (!SerHeader.IsSupportedColorId(header.ColorId))
            throw new ArgumentException($"unsupported SER ColorID {header.ColorId}", nameof(header));
        if (header.FrameCount < 0)
            throw new ArgumentException($"SER frame count cannot be negative ({header.FrameCount})", nameof(header));
    }

    private static FileStream CreateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        return new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    }
}
