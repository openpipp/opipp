using System.Buffers.Binary;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>
/// Reads classic (TIFF 6.0) files into a single <see cref="Frame"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both byte orders are handled (II/little-endian and MM/big-endian). The reader supports what the
/// original engine's still-image path needs: BitsPerSample 8 or 16, PhotometricInterpretation 1
/// (BlackIsZero) and 2 (RGB), PlanarConfiguration 1, and either one strip or many strips described by
/// StripOffsets/StripByteCounts arrays. Photometric 0 (WhiteIsZero) is inverted to match PIPP's
/// black-is-zero working convention.
/// </para>
/// <para>
/// Compressed files (Compression != 1, e.g. LZW or PackBits) are rejected with
/// <see cref="NotSupportedException"/>; only uncompressed strips are decoded. BigTIFF, tiled TIFF,
/// palette TIFF and planar (separate-plane) data are likewise unsupported.
/// </para>
/// </remarks>
public sealed class TiffReader : IFrameSource
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly string? _sourcePath;
    private readonly long _start;
    private readonly bool _littleEndian;
    private readonly TiffLayout _layout;

    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens a TIFF file for reading.</summary>
    /// <param name="path">Path to the TIFF.</param>
    /// <exception cref="InvalidDataException">The file is not a supported TIFF.</exception>
    /// <exception cref="NotSupportedException">The file uses an unsupported compression or layout.</exception>
    public TiffReader(string path)
        : this(OpenFile(path), ownsStream: true, path)
    {
    }

    /// <summary>Reads a TIFF from a stream positioned at its first byte.</summary>
    /// <param name="stream">Source stream; non-seekable streams are buffered in memory.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    public TiffReader(Stream stream, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, sourcePath: null)
    {
    }

    private TiffReader(Stream stream, bool ownsStream, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _sourcePath = sourcePath;
        _start = stream.CanSeek ? stream.Position : 0;

        if (stream.CanSeek)
        {
            _stream = stream;
            _ownsStream = ownsStream;
        }
        else
        {
            // IFD entries and strips are addressed by absolute offsets, so a sequential source has to
            // be buffered before the directory can be walked.
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (ownsStream)
                stream.Dispose();
            buffer.Position = 0;
            _stream = buffer;
            _ownsStream = true;
        }

        try
        {
            (_littleEndian, TiffTagTable tags) = TiffTagTable.Read(_stream, _start);
            _layout = TiffLayout.FromTags(tags);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("TIFF file is truncated inside its directory or value data.", ex);
        }
    }

    /// <inheritdoc />
    public int Width => _layout.Width;

    /// <inheritdoc />
    public int Height => _layout.Height;

    /// <inheritdoc />
    public int Channels => _layout.Channels;

    /// <inheritdoc />
    public int BitDepth => _layout.BitDepth;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => BayerPattern.None;

    /// <summary>True when the file stores multi-byte values big-endian (MM byte order).</summary>
    public bool IsBigEndian => !_littleEndian;

    /// <summary>Number of strips the image is split across.</summary>
    public int StripCount => _layout.StripOffsets.Length;

    /// <summary>True when PhotometricInterpretation was 0 (WhiteIsZero) and samples are inverted.</summary>
    public bool IsWhiteIsZero => _layout.WhiteIsZero;

    /// <summary>Path this reader was opened from, or null for stream construction.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>
    /// Reads only the TIFF header and image directory to report geometry, depth and decoded channel count.
    /// Strip pixel data is never touched.
    /// </summary>
    /// <param name="info">TIFF file to inspect.</param>
    /// <returns>Metadata for the image.</returns>
    public static ImageInfo Probe(FileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var stream = info.OpenRead();
        return Probe(stream);
    }

    /// <summary>
    /// Reads only the TIFF header and image directory from a stream. The stream is left open; a seekable
    /// stream is restored to its original position.
    /// </summary>
    /// <param name="stream">Stream positioned at the TIFF's first byte.</param>
    /// <returns>Metadata for the image.</returns>
    public static ImageInfo Probe(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long position = stream.CanSeek ? stream.Position : 0;
        Stream working = stream;
        MemoryStream? buffer = null;

        try
        {
            if (!stream.CanSeek)
            {
                buffer = new MemoryStream();
                stream.CopyTo(buffer);
                buffer.Position = 0;
                working = buffer;
                position = 0;
            }

            (bool _, TiffTagTable tags) = TiffTagTable.Read(working, position);
            TiffLayout layout = TiffLayout.FromTags(tags);
            return new ImageInfo(layout.Width, layout.Height, layout.BitDepth, layout.Channels);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("TIFF file is truncated inside its directory.", ex);
        }
        finally
        {
            buffer?.Dispose();
            if (stream.CanSeek)
                stream.Position = position;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">A strip is short or does not cover the image.</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= 1)
            return null;

        int rowBytes = checked(Width * Channels * (_layout.BitDepth / 8));
        int totalBytes = checked(rowBytes * Height);
        byte[] raw = new byte[totalBytes];

        int rowsRemaining = Height;
        int destination = 0;
        int rowsPerStrip = Math.Min(_layout.RowsPerStrip, Height);

        for (int strip = 0; strip < _layout.StripOffsets.Length && rowsRemaining > 0; strip++)
        {
            int rows = Math.Min(rowsPerStrip, rowsRemaining);
            int bytes = checked(rows * rowBytes);
            long declared = _layout.StripByteCounts[strip];
            if (declared < bytes)
            {
                throw new InvalidDataException(
                    $"TIFF strip {strip} holds {declared} byte(s); {bytes} are required for {rows} row(s).");
            }

            _stream.Position = _start + _layout.StripOffsets[strip];
            try
            {
                _stream.ReadExactly(raw.AsSpan(destination, bytes));
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"TIFF strip {strip} is truncated.", ex);
            }

            destination += bytes;
            rowsRemaining -= rows;
        }

        if (rowsRemaining != 0)
            throw new InvalidDataException($"TIFF strips cover {Height - rowsRemaining} of {Height} row(s).");

        var frame = new Frame(Width, Height, Channels, BitDepth)
        {
            Index = 0,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        ushort maximum = BitDepth == 8 ? (ushort)255 : ushort.MaxValue;
        ushort[] samples = frame.Samples;

        if (BitDepth == 8)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                ushort value = raw[i];
                samples[i] = _layout.WhiteIsZero ? (ushort)(maximum - value) : value;
            }
        }
        else if (_littleEndian)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2));
                samples[i] = _layout.WhiteIsZero ? (ushort)(maximum - value) : value;
            }
        }
        else
        {
            for (int i = 0; i < samples.Length; i++)
            {
                ushort value = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(i * 2));
                samples[i] = _layout.WhiteIsZero ? (ushort)(maximum - value) : value;
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
}

/// <summary>One 12-byte IFD entry, with its 4-byte value field kept verbatim.</summary>
internal readonly struct TiffEntry
{
    public TiffEntry(ushort tag, ushort type, uint count, byte[] valueField)
    {
        Tag = tag;
        Type = type;
        Count = count;
        ValueField = valueField;
    }

    public ushort Tag { get; }

    public ushort Type { get; }

    public uint Count { get; }

    /// <summary>The four value/offset bytes exactly as stored in the file.</summary>
    public byte[] ValueField { get; }
}

/// <summary>Tag lookup for the first IFD, plus endian-aware value decoding.</summary>
internal sealed class TiffTagTable
{
    private readonly Dictionary<ushort, TiffEntry> _entries;
    private readonly Stream _stream;
    private readonly long _start;
    private readonly bool _littleEndian;

    private TiffTagTable(Stream stream, long start, bool littleEndian, Dictionary<ushort, TiffEntry> entries)
    {
        _stream = stream;
        _start = start;
        _littleEndian = littleEndian;
        _entries = entries;
    }

    public static (bool LittleEndian, TiffTagTable Table) Read(Stream stream, long start)
    {
        Span<byte> header = stackalloc byte[8];
        stream.Position = start;
        stream.ReadExactly(header);

        bool littleEndian;
        if (header[0] == (byte)'I' && header[1] == (byte)'I')
            littleEndian = true;
        else if (header[0] == (byte)'M' && header[1] == (byte)'M')
            littleEndian = false;
        else
            throw new InvalidDataException("Not a TIFF file: byte-order mark is neither 'II' nor 'MM'.");

        ushort magic = ReadU16(header[2..], littleEndian);
        if (magic != 42)
            throw new InvalidDataException($"Unsupported TIFF version {magic}; only classic TIFF (42) is supported.");

        uint ifdOffset = ReadU32(header[4..], littleEndian);
        if (ifdOffset < 8)
            throw new InvalidDataException($"TIFF first IFD offset {ifdOffset} overlaps the file header.");
        if (stream.CanSeek && ifdOffset > stream.Length - start)
            throw new InvalidDataException($"TIFF first IFD offset {ifdOffset} lies past the end of the file.");

        stream.Position = start + ifdOffset;
        Span<byte> countBytes = stackalloc byte[2];
        stream.ReadExactly(countBytes);
        int count = ReadU16(countBytes, littleEndian);
        if (count == 0)
            throw new InvalidDataException("TIFF first IFD has no entries.");
        if (count > 4096)
            throw new InvalidDataException($"TIFF first IFD declares {count} entries; refusing to parse.");

        byte[] block = new byte[count * 12 + 4];
        stream.ReadExactly(block);

        var entries = new Dictionary<ushort, TiffEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int offset = i * 12;
            ushort tag = ReadU16(block.AsSpan(offset), littleEndian);
            ushort type = ReadU16(block.AsSpan(offset + 2), littleEndian);
            uint valueCount = ReadU32(block.AsSpan(offset + 4), littleEndian);
            byte[] valueField = block.AsSpan(offset + 8, 4).ToArray();
            entries[tag] = new TiffEntry(tag, type, valueCount, valueField);
        }

        return (littleEndian, new TiffTagTable(stream, start, littleEndian, entries));
    }

    public bool Contains(ushort tag) => _entries.ContainsKey(tag);

    public TiffEntry Required(ushort tag, string name)
    {
        return _entries.TryGetValue(tag, out TiffEntry entry)
            ? entry
            : throw new InvalidDataException($"TIFF is missing the required {name} tag ({tag}).");
    }

    public uint? Scalar(ushort tag, string name)
    {
        if (!_entries.TryGetValue(tag, out TiffEntry entry))
            return null;
        uint[] values = Values(entry, name);
        return values.Length == 0 ? null : values[0];
    }

    /// <summary>Decodes a SHORT/LONG/BYTE tag array, following out-of-line values when needed.</summary>
    public uint[] Values(TiffEntry entry, string name)
    {
        int elementSize = entry.Type switch
        {
            1 => 1, // BYTE
            3 => 2, // SHORT
            4 => 4, // LONG
            _ => throw new NotSupportedException(
                $"TIFF tag {name} ({entry.Tag}) uses field type {entry.Type}; only BYTE, SHORT and LONG are supported."),
        };

        long total = (long)elementSize * entry.Count;
        if (total > int.MaxValue)
            throw new InvalidDataException($"TIFF tag {name} ({entry.Tag}) declares an implausible value count {entry.Count}.");

        byte[] raw;
        if (total <= 4)
        {
            raw = entry.ValueField;
        }
        else
        {
            uint offset = ReadU32(entry.ValueField, _littleEndian);
            if (offset < 8)
                throw new InvalidDataException($"TIFF tag {name} ({entry.Tag}) points at invalid offset {offset}.");
            if (_stream.CanSeek && offset > _stream.Length - _start)
                throw new InvalidDataException($"TIFF tag {name} ({entry.Tag}) points past the end of the file.");

            _stream.Position = _start + offset;
            raw = new byte[total];
            _stream.ReadExactly(raw);
        }

        uint[] values = new uint[entry.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = elementSize switch
            {
                1 => raw[i],
                2 => ReadU16(raw.AsSpan(i * 2), _littleEndian),
                _ => ReadU32(raw.AsSpan(i * 4), _littleEndian),
            };
        }

        return values;
    }

    public static ushort ReadU16(ReadOnlySpan<byte> source, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(source)
        : BinaryPrimitives.ReadUInt16BigEndian(source);

    public static uint ReadU32(ReadOnlySpan<byte> source, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(source)
        : BinaryPrimitives.ReadUInt32BigEndian(source);
}

/// <summary>Validated image geometry and strip table for one TIFF IFD.</summary>
internal sealed class TiffLayout
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Channels { get; init; }

    public required int BitDepth { get; init; }

    public required bool WhiteIsZero { get; init; }

    public required int RowsPerStrip { get; init; }

    public required long[] StripOffsets { get; init; }

    public required long[] StripByteCounts { get; init; }

    public static TiffLayout FromTags(TiffTagTable tags)
    {
        uint width = tags.Scalar(256, "ImageWidth") ?? throw new InvalidDataException("TIFF is missing ImageWidth (256).");
        uint height = tags.Scalar(257, "ImageLength") ?? throw new InvalidDataException("TIFF is missing ImageLength (257).");
        if (width == 0 || height == 0)
            throw new InvalidDataException($"TIFF has invalid dimensions {width}x{height}.");
        if (width > int.MaxValue || height > int.MaxValue)
            throw new InvalidDataException($"TIFF dimensions {width}x{height} are too large.");

        uint compression = tags.Scalar(259, "Compression") ?? 1;
        if (compression != 1)
        {
            throw new NotSupportedException(
                $"TIFF compression {compression} is not supported; only uncompressed strips (Compression = 1) are decoded.");
        }

        uint photometric = tags.Scalar(262, "PhotometricInterpretation") ?? 1;
        uint samplesPerPixel = tags.Scalar(277, "SamplesPerPixel") ?? 1;
        uint planar = tags.Scalar(284, "PlanarConfiguration") ?? 1;
        if (planar != 1)
        {
            throw new NotSupportedException(
                $"TIFF PlanarConfiguration {planar} is not supported; only chunky data (1) is decoded.");
        }

        int channels;
        bool whiteIsZero = false;
        switch (photometric)
        {
            case 0: // WhiteIsZero
                channels = 1;
                whiteIsZero = true;
                break;
            case 1: // BlackIsZero
                channels = 1;
                break;
            case 2: // RGB
                if (samplesPerPixel != 3)
                {
                    throw new NotSupportedException(
                        $"TIFF RGB image declares {samplesPerPixel} samples per pixel; only 3 (no alpha) is supported.");
                }

                channels = 3;
                break;
            default:
                throw new NotSupportedException(
                    $"TIFF photometric interpretation {photometric} is not supported; only BlackIsZero (1) and RGB (2).");
        }

        if (channels == 1 && samplesPerPixel != 1)
        {
            throw new NotSupportedException(
                $"TIFF greyscale image declares {samplesPerPixel} samples per pixel; only 1 is supported.");
        }

        TiffEntry bitsEntry = tags.Required(258, "BitsPerSample");
        uint[] bits = tags.Values(bitsEntry, "BitsPerSample");
        if (bits.Length == 0)
            throw new InvalidDataException("TIFF BitsPerSample (258) is empty.");
        if (bits.Length != 1 && bits.Length != channels)
        {
            throw new InvalidDataException(
                $"TIFF BitsPerSample has {bits.Length} value(s); expected 1 or {channels}.");
        }

        int bitDepth = (int)bits[0];
        for (int i = 1; i < bits.Length; i++)
        {
            if (bits[i] != bits[0])
                throw new InvalidDataException("TIFF BitsPerSample values differ between channels.");
        }

        if (bitDepth is not (8 or 16))
            throw new NotSupportedException($"TIFF {bitDepth} bits per sample is not supported; only 8 and 16.");

        long sampleCount = (long)width * height * channels;
        if (sampleCount > int.MaxValue)
            throw new InvalidDataException($"TIFF image is too large ({sampleCount} samples).");

        long[] offsets = ToLongs(tags.Values(tags.Required(273, "StripOffsets"), "StripOffsets"));
        long[] counts = ToLongs(tags.Values(tags.Required(279, "StripByteCounts"), "StripByteCounts"));
        if (offsets.Length == 0 || counts.Length == 0)
            throw new InvalidDataException("TIFF has no strips.");
        if (offsets.Length != counts.Length)
        {
            throw new InvalidDataException(
                $"TIFF has {offsets.Length} strip offset(s) but {counts.Length} strip byte count(s).");
        }

        uint rowsPerStripTag = tags.Scalar(278, "RowsPerStrip") ?? height;
        int rowsPerStrip = rowsPerStripTag == 0 ? (int)height : (int)Math.Min(rowsPerStripTag, int.MaxValue);

        return new TiffLayout
        {
            Width = (int)width,
            Height = (int)height,
            Channels = channels,
            BitDepth = bitDepth,
            WhiteIsZero = whiteIsZero,
            RowsPerStrip = rowsPerStrip,
            StripOffsets = offsets,
            StripByteCounts = counts,
        };
    }

    private static long[] ToLongs(uint[] values)
    {
        long[] result = new long[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = values[i];
        return result;
    }
}
