using System.Buffers.Binary;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>
/// Streaming writer for uncompressed AVI 1.0 files, matching the layout produced by PIPP's built-in
/// muxer: <c>RIFF/AVI </c>, <c>hdrl</c> (<c>avih</c> + one <c>strl</c> with <c>strh</c>/<c>strf</c>),
/// a <c>movi</c> list of <c>00db</c> chunks and a trailing <c>idx1</c> index.
/// </summary>
/// <remarks>
/// <para>
/// Header sizes, frame counts and the RIFF/<c>movi</c> sizes are written as placeholders up front and
/// patched when the writer is disposed, so frames stream straight to disk and writer memory does not grow
/// with the frame count. Because every frame of a file has the same dimensions, index entries are computed
/// arithmetically rather than buffered.
/// </para>
/// <para>
/// Supported layouts are 8bpp <c>BI_RGB</c> mono with a 256-entry grayscale palette, 24bpp <c>BI_RGB</c>
/// colour (BGR bytes, bottom-up, DWORD-aligned rows) and 8bpp <c>Y800</c> mono (top-down, no palette, no
/// row padding, negative <c>biHeight</c>). Samples are 8-bit; callers must convert 16-bit frames first.
/// <c>dwSuggestedBufferSize</c> covers one chunk including its 8-byte header, and <c>dwSampleSize</c> is the
/// frame payload size. AVI 1.0 addresses sizes with 32-bit fields, so a single file is limited to just under
/// 4 GiB.
/// </para>
/// </remarks>
public sealed class AviWriter : IFrameSink
{
    private readonly FileStream _stream;
    private readonly AviWriterOptions _options;
    private readonly string _path;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _isColour;
    private readonly int _rowBytes;
    private readonly int _stride;
    private readonly int _frameBytes;
    private readonly int _chunkStride;
    private readonly byte[] _rowBuffer;
    private readonly long _moviSizePosition;
    private readonly uint _microSecondsPerFrame;
    private readonly uint _maxBytesPerSecond;

    private long _avihFlagsPosition;
    private long _avihTotalFramesPosition;
    private long _strhLengthPosition;
    private int _frameCount;
    private bool _disposed;

    /// <summary>Creates an AVI file and writes its headers.</summary>
    /// <param name="path">Destination file path; an existing file is overwritten.</param>
    /// <param name="width">Frame width in pixels, 1..32767 (the <c>strh</c> rectangle uses 16-bit fields).</param>
    /// <param name="height">Frame height in pixels, 1..32767.</param>
    /// <param name="options">Pixel layout and frame rate. Defaults to 8bpp mono at 25 fps.</param>
    /// <exception cref="ArgumentOutOfRangeException">The dimensions or frame rate are outside the supported range.</exception>
    /// <exception cref="IOException">The file cannot be created.</exception>
    public AviWriter(string path, int width, int height, AviWriterOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, short.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, short.MaxValue);

        _options = options ?? new AviWriterOptions();
        _options.Validate();

        _path = path;
        _width = width;
        _height = height;
        _isColour = _options.PixelFormat == AviPixelFormat.Bgr24;

        int bitCount = _isColour ? 24 : 8;
        _rowBytes = _isColour ? checked(width * 3) : width;
        _stride = _options.PixelFormat == AviPixelFormat.Y800 ? _rowBytes : (_rowBytes + 3) & ~3;
        _frameBytes = checked(_stride * height);
        _chunkStride = checked(8 + _frameBytes + (_frameBytes & 1));
        _rowBuffer = new byte[_stride];

        _microSecondsPerFrame = (uint)Math.Clamp(
            (long)Math.Round(1_000_000.0 * _options.FpsScale / _options.FpsRate), 1, uint.MaxValue);
        _maxBytesPerSecond = (uint)Math.Min(
            uint.MaxValue,
            (long)Math.Ceiling((double)_frameBytes * _options.FpsRate / _options.FpsScale));

        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024);
        try
        {
            WriteFourCC(AviIds.Riff);
            WriteUInt32(0); // RIFF size, patched on Dispose
            WriteFourCC(AviIds.Avi);

            long hdrlSizePosition = BeginList(AviIds.Hdrl);
            WriteMainHeader();
            long strlSizePosition = BeginList(AviIds.Strl);
            WriteStreamHeader();
            WriteBitmapHeader(bitCount);
            EndList(strlSizePosition);
            EndList(hdrlSizePosition);

            if (_options.LegacyLayout)
            {
                WriteLegacyPadding();
            }

            WriteFourCC(AviIds.List);
            _moviSizePosition = _stream.Position;
            WriteUInt32(0); // movi size, patched on Dispose
            WriteFourCC(AviIds.Movi);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    /// <summary>Destination path of the file being written.</summary>
    public string Path => _path;

    /// <summary>Number of frames written so far.</summary>
    public int FrameCount => _frameCount;

    /// <summary>Options this writer was created with.</summary>
    public AviWriterOptions Options => _options;

    /// <summary>True once <see cref="Dispose"/> has patched the headers and closed the file.</summary>
    public bool IsComplete => _disposed;

    /// <summary>Writes one frame. Dimensions, channel count and 8-bit depth must match the writer.</summary>
    /// <param name="frame">Frame to append; samples above 255 are clamped.</param>
    /// <exception cref="ArgumentException">The frame geometry or channel count differs from the writer's.</exception>
    /// <exception cref="NotSupportedException">The frame is not 8-bit.</exception>
    /// <exception cref="InvalidOperationException">Adding the frame would push the AVI past the 4 GiB limit.</exception>
    public void Write(Frame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Width != _width || frame.Height != _height)
            throw new ArgumentException(
                $"AVI writer is {_width}x{_height}, frame is {frame.Width}x{frame.Height}.", nameof(frame));
        if (frame.Channels != (_isColour ? 3 : 1))
            throw new ArgumentException(
                $"AVI writer expects {(_isColour ? 3 : 1)} channel(s), frame has {frame.Channels}.", nameof(frame));
        if (frame.BitDepth != 8)
            throw new NotSupportedException(
                $"AviWriter stores 8-bit samples; frame bit depth is {frame.BitDepth}. Convert 16-bit frames first.");

        EnsureRoomForOneMoreFrame();

        WriteFourCC(AviIds.Db);
        WriteUInt32((uint)_frameBytes);
        WriteFrameRows(frame);
        if ((_frameBytes & 1) != 0)
            _stream.WriteByte(0); // RIFF chunks are padded to even sizes

        _frameCount++;
    }

    /// <summary>
    /// Finishes the file: writes the <c>idx1</c> index, patches the RIFF, <c>movi</c>, <c>avih</c> and
    /// <c>strh</c> sizes/counts, flushes and closes the stream.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            long moviContentEnd = _stream.Position;
            uint moviSize = checked((uint)(moviContentEnd - (_moviSizePosition + 4)));
            uint indexSize = checked((uint)(16L * _frameCount));
            uint flags = _frameCount > 0 ? AviIds.AviFlagHasIndex : 0;

            WriteFourCC(AviIds.Idx1);
            WriteUInt32(indexSize);
            for (int i = 0; i < _frameCount; i++)
            {
                WriteFourCC(AviIds.Db);
                WriteUInt32(AviIds.AviKeyFrame);
                // Offsets are relative to the 'movi' FourCC; the first chunk starts four bytes later.
                WriteUInt32(checked((uint)(4L + (long)i * _chunkStride)));
                WriteUInt32((uint)_frameBytes);
            }

            long end = _stream.Position;
            if (end - 8 > uint.MaxValue)
                throw new InvalidOperationException(
                    $"AVI 1.0 cannot address files larger than 4 GiB (file would be {end} bytes).");

            PatchUInt32(_moviSizePosition, moviSize);
            PatchUInt32(_avihTotalFramesPosition, (uint)_frameCount);
            PatchUInt32(_strhLengthPosition, (uint)_frameCount);
            PatchUInt32(_avihFlagsPosition, flags);
            PatchUInt32(4, (uint)(end - 8));
            _stream.Flush();
        }
        finally
        {
            _stream.Dispose();
        }
    }

    private void WriteMainHeader()
    {
        long dataPosition = BeginChunk(AviIds.Avih, AviMainHeader.Size);
        _avihFlagsPosition = dataPosition + 12;
        _avihTotalFramesPosition = dataPosition + 16;

        var header = new AviMainHeader
        {
            MicroSecPerFrame = _microSecondsPerFrame,
            MaxBytesPerSec = _maxBytesPerSecond,
            PaddingGranularity = 0,
            Flags = AviIds.AviFlagHasIndex,
            TotalFrames = 0,
            InitialFrames = 0,
            Streams = 1,
            SuggestedBufferSize = (uint)_chunkStride,
            Width = (uint)_width,
            Height = (uint)_height,
        };

        Span<byte> buffer = stackalloc byte[AviMainHeader.Size];
        header.WriteTo(buffer);
        _stream.Write(buffer);
    }

    private void WriteStreamHeader()
    {
        long dataPosition = BeginChunk(AviIds.Strh, AviStreamHeader.Size);
        _strhLengthPosition = dataPosition + 32;

        var header = new AviStreamHeader
        {
            Type = AviIds.Vids,
            Handler = _options.PixelFormat == AviPixelFormat.Y800 ? AviIds.Y800 : AviIds.Dib,
            Flags = 0,
            Priority = 0,
            Language = 0,
            InitialFrames = 0,
            Scale = (uint)_options.FpsScale,
            Rate = (uint)_options.FpsRate,
            Start = 0,
            Length = 0,
            SuggestedBufferSize = (uint)_chunkStride,
            Quality = uint.MaxValue,
            SampleSize = (uint)_frameBytes,
            Left = 0,
            Top = 0,
            Right = (short)_width,
            Bottom = (short)_height,
        };

        Span<byte> buffer = stackalloc byte[AviStreamHeader.Size];
        header.WriteTo(buffer);
        _stream.Write(buffer);
    }

    private void WriteBitmapHeader(int bitCount)
    {
        bool y800 = _options.PixelFormat == AviPixelFormat.Y800;
        bool palette = bitCount == 8 && !y800;
        BeginChunk(AviIds.Strf, (uint)(BitmapInfoHeader.Size + (palette ? 256 * 4 : 0)));

        var header = new BitmapInfoHeader
        {
            HeaderSize = BitmapInfoHeader.Size,
            Width = _width,
            Height = y800 ? -_height : _height,
            Planes = 1,
            BitCount = (ushort)bitCount,
            Compression = y800 ? AviIds.Y800 : BitmapInfoHeader.BiRgb,
            SizeImage = (uint)_frameBytes,
            XPelsPerMeter = 0,
            YPelsPerMeter = 0,
            ClrUsed = palette ? 256u : 0u,
            ClrImportant = 0,
        };

        Span<byte> buffer = stackalloc byte[BitmapInfoHeader.Size];
        header.WriteTo(buffer);
        _stream.Write(buffer);

        if (palette)
        {
            Span<byte> entry = stackalloc byte[4];
            for (int i = 0; i < 256; i++)
            {
                entry[0] = (byte)i; // blue
                entry[1] = (byte)i; // green
                entry[2] = (byte)i; // red
                entry[3] = 0;
                _stream.Write(entry);
            }
        }
    }

    private void WriteFrameRows(Frame frame)
    {
        int width = _width;
        int height = _height;
        bool topDown = _options.PixelFormat == AviPixelFormat.Y800;

        for (int row = 0; row < height; row++)
        {
            int y = topDown ? row : height - 1 - row;
            if (_isColour)
            {
                int source = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    // DIB colour pixels are stored blue, green, red.
                    _rowBuffer[x * 3] = Clamp(frame.Samples[source + 2]);
                    _rowBuffer[x * 3 + 1] = Clamp(frame.Samples[source + 1]);
                    _rowBuffer[x * 3 + 2] = Clamp(frame.Samples[source]);
                    source += 3;
                }
            }
            else
            {
                int source = y * width;
                for (int x = 0; x < width; x++)
                    _rowBuffer[x] = Clamp(frame.Samples[source + x]);
            }

            _stream.Write(_rowBuffer.AsSpan(0, _stride));
        }
    }

    private void EnsureRoomForOneMoreFrame()
    {
        long count = _frameCount + 1L;
        long predictedEnd = (_moviSizePosition - 4) + 8 + 4 + count * _chunkStride + 8 + 16 * count;
        if (predictedEnd - 8 > uint.MaxValue)
            throw new InvalidOperationException(
                "AVI 1.0 cannot address files larger than 4 GiB; split the sequence across multiple AVI files.");
    }

    private void WriteLegacyPadding()
    {
        // The old muxer reserved a fixed 8 KiB header area so it could rewrite the (then
        // indeterminate) stream headers in place; everything after hdrl up to that mark is a
        // zero-filled JUNK chunk. pipp_core writes 6948 bytes here for a mono 8bpp stream, which
        // puts the movi list at offset 8192 exactly.
        const long legacyHeaderEnd = 8192;
        long pad = legacyHeaderEnd - _stream.Position - 8;
        if (pad <= 0)
            return;

        WriteFourCC(AviIds.Junk);
        WriteUInt32((uint)pad);
        Span<byte> zeros = stackalloc byte[256];
        zeros.Clear();
        long remaining = pad;
        while (remaining > 0)
        {
            int count = (int)Math.Min(remaining, zeros.Length);
            _stream.Write(zeros[..count]);
            remaining -= count;
        }
    }

    private long BeginList(uint listType)
    {
        WriteFourCC(AviIds.List);
        long sizePosition = _stream.Position;
        WriteUInt32(0);
        WriteFourCC(listType);
        return sizePosition;
    }

    private void EndList(long sizePosition)
    {
        long end = _stream.Position;
        PatchUInt32(sizePosition, checked((uint)(end - (sizePosition + 4))));
    }

    private long BeginChunk(uint id, uint size)
    {
        WriteFourCC(id);
        WriteUInt32(size);
        return _stream.Position;
    }

    private void PatchUInt32(long position, uint value)
    {
        long saved = _stream.Position;
        _stream.Position = position;
        WriteUInt32(value);
        _stream.Position = saved;
    }

    private void WriteFourCC(uint value) => WriteUInt32(value);

    private void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    private static byte Clamp(ushort sample) => sample > 255 ? (byte)255 : (byte)sample;
}
