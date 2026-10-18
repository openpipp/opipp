using System.Buffers.Binary;
using Opipp.Core.Frames;

namespace Opipp.Core.Formats;

/// <summary>
/// Reads a baseline (uncompressed) DNG file into one single-plane Bayer <see cref="Frame"/>.
/// </summary>
/// <remarks>
/// <para>
/// A DNG is a TIFF whose first directory describes a colour-filter-array (CFA) sensor image. This
/// reader walks that directory, decodes the mosaic at its stored bit depth, applies the DNG black and
/// white levels, and returns the samples normalised to the full 16-bit range with
/// <see cref="Frame.Bayer"/> set to the pattern from CFAPattern (tag 33422). It does not demosaic:
/// the pipeline's debayer stage consumes the mosaic, exactly as it does for a Bayer SER.
/// </para>
/// <para>
/// Supported: both TIFF byte orders, BitsPerSample 8, 10, 12, 14 and 16, uncompressed strips
/// (Compression 1, and the technically invalid 0 that dcraw also accepts), one or more strips, a 2x2
/// CFA tile in any of the four standard arrangements, and scalar or 2x2 black levels. Samples at 12
/// and 14 bits are unpacked MSB-first per the TIFF/DNG bit-packing rules, with each row starting on a
/// byte boundary.
/// </para>
/// <para>
/// Not supported, and rejected with <see cref="NotSupportedException"/>: BigTIFF, tiled images,
/// lossless-JPEG/Deflate/lossy DNG compression, CFA patterns other than 2x2, 4-colour CFAs and
/// X-Trans, FillOrder 2, and bit depths outside the list above. Only the first IFD is examined, so a
/// DNG whose IFD0 is a reduced-resolution preview rather than the raw image is rejected. Crop,
/// active-area, orientation and colour-matrix tags are ignored; the full stored sensor area is
/// returned unrotated.
/// </para>
/// <para>
/// Sample conversion is <c>(raw - black) * 65535 / (white - black)</c>, rounded half away from zero and
/// clamped to 0..65535. That is the same normalisation dcraw applies to a DNG (camera white balance
/// aside), so a 12-bit sensor is presented to the pipeline at 16-bit precision rather than as a dark
/// 0..4095 image.
/// </para>
/// </remarks>
public sealed class RawDngReader : IFrameSource
{
    private const ushort TagNewSubfileType = 254;
    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagPhotometricInterpretation = 262;
    private const ushort TagStripOffsets = 273;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagRowsPerStrip = 278;
    private const ushort TagStripByteCounts = 279;
    private const ushort TagPlanarConfiguration = 284;
    private const ushort TagTileOffsets = 324;
    private const ushort TagTileWidth = 322;
    private const ushort TagFillOrder = 266;
    private const ushort TagCfaRepeatPatternDim = 33421;
    private const ushort TagCfaPattern = 33422;
    private const ushort TagDngVersion = 50706;
    private const ushort TagCfaPlaneColor = 50710;
    private const ushort TagBlackLevelRepeatDim = 50713;
    private const ushort TagBlackLevel = 50714;
    private const ushort TagWhiteLevel = 50717;

    private const ushort PhotometricCfa = 32803;
    private const ushort CompressionNone = 1;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly string? _sourcePath;
    private readonly long _start;
    private readonly DngLayout _layout;

    private int _nextIndex;
    private bool _disposed;

    /// <summary>Opens a DNG file for reading.</summary>
    /// <param name="path">Path to the DNG.</param>
    /// <exception cref="InvalidDataException">The file is not a readable baseline DNG.</exception>
    /// <exception cref="NotSupportedException">The DNG uses a feature this reader does not implement.</exception>
    public RawDngReader(string path)
        : this(OpenFile(path), ownsStream: true, path)
    {
    }

    /// <summary>Reads a DNG from a stream positioned at its first byte.</summary>
    /// <param name="stream">Source stream; a non-seekable stream is buffered in memory.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when this reader is disposed.</param>
    public RawDngReader(Stream stream, bool leaveOpen = false)
        : this(stream, ownsStream: !leaveOpen, sourcePath: null)
    {
    }

    private RawDngReader(Stream stream, bool ownsStream, string? sourcePath)
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
            _layout = DngLayout.Read(_stream, _start);
        }
        catch
        {
            if (_ownsStream)
                _stream.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public int Width => _layout.Width;

    /// <inheritdoc />
    public int Height => _layout.Height;

    /// <inheritdoc />
    public int Channels => 1;

    /// <inheritdoc />
    /// <remarks>Decoded frames are always presented at 16-bit precision after black/white normalisation.</remarks>
    public int BitDepth => 16;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => _layout.Bayer;

    /// <summary>Bit depth stored in the file (8, 10, 12, 14 or 16).</summary>
    public int SourceBitDepth => _layout.BitsPerSample;

    /// <summary>Path this reader was opened from, or null for stream construction.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>
    /// Reads only the TIFF header and image directory to report geometry. Strip pixel data is never
    /// touched.
    /// </summary>
    /// <param name="info">DNG file to inspect.</param>
    /// <returns>Metadata for the mosaic: 16-bit, single channel.</returns>
    public static ImageInfo Probe(FileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        using var stream = info.OpenRead();
        return Probe(stream);
    }

    /// <summary>
    /// Reads only the TIFF header and image directory from a stream. The stream is left open; a
    /// seekable stream is restored to its original position.
    /// </summary>
    /// <param name="stream">Stream positioned at the DNG's first byte.</param>
    /// <returns>Metadata for the mosaic: 16-bit, single channel.</returns>
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

            DngLayout layout = DngLayout.Read(working, position);
            return new ImageInfo(layout.Width, layout.Height, 16, 1);
        }
        finally
        {
            buffer?.Dispose();
            if (stream.CanSeek)
                stream.Position = position;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The strip data is short or does not cover the image.</exception>
    public Frame? ReadFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextIndex >= 1)
            return null;

        int rowBytes = checked((int)(((long)Width * _layout.BitsPerSample + 7) / 8));
        byte[] packed = new byte[checked(rowBytes * Height)];
        ReadStrips(packed, rowBytes);

        var frame = new Frame(Width, Height, 1, 16)
        {
            Bayer = _layout.Bayer,
            Index = 0,
            SourceName = _sourcePath is null ? null : Path.GetFileName(_sourcePath),
        };

        ushort[] samples = frame.Samples;
        int bps = _layout.BitsPerSample;
        bool littleEndian = _layout.LittleEndian;
        double[] black = _layout.BlackLevels;
        int blackRows = _layout.BlackRows;
        int blackCols = _layout.BlackColumns;
        double white = _layout.WhiteLevel;

        for (int y = 0; y < Height; y++)
        {
            int rowStartBit = y * rowBytes * 8;
            for (int x = 0; x < Width; x++)
            {
                int raw;
                if (bps == 8)
                {
                    raw = packed[y * rowBytes + x];
                }
                else if (bps == 16)
                {
                    int offset = y * rowBytes + x * 2;
                    raw = littleEndian
                        ? BinaryPrimitives.ReadUInt16LittleEndian(packed.AsSpan(offset, 2))
                        : BinaryPrimitives.ReadUInt16BigEndian(packed.AsSpan(offset, 2));
                }
                else
                {
                    raw = ReadPackedBits(packed, rowStartBit + x * bps, bps);
                }

                double level = blackRows == 1 && blackCols == 1
                    ? black[0]
                    : black[(y % blackRows) * blackCols + (x % blackCols)];
                double scale = (white - level) <= 0 ? 0 : 65535.0 / (white - level);
                double value = (raw - level) * scale;

                samples[y * Width + x] = (ushort)Math.Clamp(
                    Math.Round(value, MidpointRounding.AwayFromZero), 0.0, 65535.0);
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

    private void ReadStrips(byte[] destination, int rowBytes)
    {
        long[] offsets = _layout.StripOffsets;
        long[] byteCounts = _layout.StripByteCounts;
        int rowsPerStrip = Math.Min(_layout.RowsPerStrip, Height);

        if (offsets.Length == 1)
        {
            // A single strip may cover the whole image even when RowsPerStrip says otherwise; the
            // declared byte count is allowed to include padding beyond the last row.
            int bytes = checked(rowBytes * Height);
            RequireBytes(byteCounts[0], bytes, 0);
            ReadAt(offsets[0], destination.AsSpan(0, bytes));
            return;
        }

        int stripCount = (Height + rowsPerStrip - 1) / rowsPerStrip;
        if (offsets.Length < stripCount || byteCounts.Length < stripCount)
        {
            throw new InvalidDataException(
                $"DNG has {offsets.Length} strip offset(s) for {stripCount} strip(s) of {rowsPerStrip} row(s).");
        }

        int destinationOffset = 0;
        int rowsWritten = 0;
        for (int strip = 0; strip < stripCount; strip++)
        {
            int rows = Math.Min(rowsPerStrip, Height - rowsWritten);
            int bytes = checked(rows * rowBytes);
            RequireBytes(byteCounts[strip], bytes, strip);
            ReadAt(offsets[strip], destination.AsSpan(destinationOffset, bytes));
            destinationOffset += bytes;
            rowsWritten += rows;
        }
    }

    private static void RequireBytes(long declared, int required, int strip)
    {
        if (declared < required)
        {
            throw new InvalidDataException(
                $"DNG strip {strip} holds {declared} byte(s); {required} are required.");
        }
    }

    private void ReadAt(long offset, Span<byte> destination)
    {
        long position = _start + offset;
        if (_stream.CanSeek && (position < _start || position + destination.Length > _stream.Length))
        {
            throw new InvalidDataException(
                $"DNG strip at offset {offset} runs past the end of the file.");
        }

        _stream.Position = position;
        try
        {
            _stream.ReadExactly(destination);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("DNG strip data is truncated.", ex);
        }
    }

    /// <summary>Reads <paramref name="bits"/> bits MSB-first, the TIFF/DNG packed-sample order.</summary>
    private static int ReadPackedBits(byte[] data, int bitOffset, int bits)
    {
        int value = 0;
        int remaining = bits;
        int index = bitOffset >> 3;
        int bitInByte = bitOffset & 7;

        while (remaining > 0)
        {
            int take = Math.Min(8 - bitInByte, remaining);
            int shift = 8 - bitInByte - take;
            int mask = (1 << take) - 1;
            value = (value << take) | ((data[index] >> shift) & mask);
            remaining -= take;
            index++;
            bitInByte = 0;
        }

        return value;
    }

    private static FileStream OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>One TIFF directory entry with its value bytes already read.</summary>
    private readonly record struct TiffEntry(ushort Tag, ushort Type, uint Count, byte[] Data)
    {
        /// <summary>Size in bytes of one value of <paramref name="type"/>, or 0 when unknown.</summary>
        public static int SizeOf(ushort type) => type switch
        {
            1 => 1,  // BYTE
            2 => 1,  // ASCII
            3 => 2,  // SHORT
            4 => 4,  // LONG
            5 => 8,  // RATIONAL
            6 => 1,  // SBYTE
            7 => 1,  // UNDEFINED
            8 => 2,  // SSHORT
            9 => 4,  // SLONG
            10 => 8, // SRATIONAL
            11 => 4, // FLOAT
            12 => 8, // DOUBLE
            _ => 0,
        };
    }

    /// <summary>Header and directory values a baseline DNG needs, validated once at construction.</summary>
    private sealed class DngLayout
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int BitsPerSample { get; init; }
        public required bool LittleEndian { get; init; }
        public required BayerPattern Bayer { get; init; }
        public required double[] BlackLevels { get; init; }
        public required int BlackRows { get; init; }
        public required int BlackColumns { get; init; }
        public required double WhiteLevel { get; init; }
        public required int RowsPerStrip { get; init; }
        public required long[] StripOffsets { get; init; }
        public required long[] StripByteCounts { get; init; }

        public static DngLayout Read(Stream stream, long start)
        {
            Span<byte> header = stackalloc byte[8];
            ReadExactlyAt(stream, start, header, "TIFF header");

            bool littleEndian;
            if (header[0] == 0x49 && header[1] == 0x49)
                littleEndian = true;
            else if (header[0] == 0x4D && header[1] == 0x4D)
                littleEndian = false;
            else
                throw new InvalidDataException("Not a TIFF-based DNG: the byte-order mark is missing.");

            ushort magic = ReadUInt16(header[2..4], littleEndian);
            if (magic == 43)
                throw new NotSupportedException("BigTIFF (magic 43) is not supported.");
            if (magic != 42)
                throw new InvalidDataException($"Not a TIFF-based DNG: unexpected magic {magic}.");

            uint ifdOffset = ReadUInt32(header[4..8], littleEndian);
            if (ifdOffset < 8)
                throw new InvalidDataException($"DNG directory offset {ifdOffset} overlaps the header.");

            Dictionary<ushort, TiffEntry> tags = ReadDirectory(stream, start, ifdOffset, littleEndian);

            if (tags.TryGetValue(TagNewSubfileType, out TiffEntry subfile) &&
                (ReadUnsignedFirst(subfile, littleEndian) & 1) != 0)
            {
                throw new NotSupportedException(
                    "IFD0 is a reduced-resolution preview; this reader does not follow SubIFDs.");
            }

            if (!tags.TryGetValue(TagDngVersion, out TiffEntry dngVersion) ||
                dngVersion.Type != 1 || dngVersion.Count < 4)
            {
                throw new InvalidDataException("Not a DNG: the DNGVersion tag (50706) is missing or malformed.");
            }

            if (tags.TryGetValue(TagTileOffsets, out _) || tags.TryGetValue(TagTileWidth, out _))
                throw new NotSupportedException("Tiled DNGs are not supported; only strips are read.");

            if (tags.TryGetValue(TagFillOrder, out TiffEntry fillOrder) &&
                ReadUnsignedFirst(fillOrder, littleEndian) != 1)
            {
                throw new NotSupportedException("FillOrder 2 (LSB-first bit packing) is not supported.");
            }

            int width = ReadPositiveInt(tags, TagImageWidth, "ImageWidth", littleEndian);
            int height = ReadPositiveInt(tags, TagImageLength, "ImageLength", littleEndian);

            int samplesPerPixel = tags.TryGetValue(TagSamplesPerPixel, out TiffEntry spp)
                ? (int)ReadUnsignedFirst(spp, littleEndian)
                : 1;
            if (samplesPerPixel != 1)
                throw new NotSupportedException($"SamplesPerPixel {samplesPerPixel} is not supported; a CFA DNG has one plane.");

            if (tags.TryGetValue(TagPlanarConfiguration, out TiffEntry planar) &&
                ReadUnsignedFirst(planar, littleEndian) != 1)
            {
                throw new NotSupportedException("PlanarConfiguration 2 is not supported for single-plane CFA data.");
            }

            int bitsPerSample = ReadBitsPerSample(tags, littleEndian);
            if (bitsPerSample is not (8 or 10 or 12 or 14 or 16))
            {
                throw new NotSupportedException(
                    $"BitsPerSample {bitsPerSample} is not supported; expected 8, 10, 12, 14 or 16.");
            }

            int compression = ReadUnsignedTag(tags, TagCompression, "Compression", littleEndian);
            if (compression is not (0 or CompressionNone))
            {
                throw new NotSupportedException(
                    $"DNG compression {compression} is not supported; only uncompressed strips are read.");
            }

            int photometric = ReadUnsignedTag(tags, TagPhotometricInterpretation, "PhotometricInterpretation", littleEndian);
            if (photometric != PhotometricCfa)
            {
                throw new NotSupportedException(
                    $"PhotometricInterpretation {photometric} is not a CFA image; expected 32803.");
            }

            BayerPattern bayer = ReadCfaPattern(tags, littleEndian);
            (double[] blackLevels, int blackRows, int blackColumns) = ReadBlackLevels(tags, littleEndian, bitsPerSample);
            double whiteLevel = ReadWhiteLevel(tags, littleEndian, bitsPerSample);
            foreach (double level in blackLevels)
            {
                if (level < 0 || level >= whiteLevel)
                    throw new InvalidDataException($"DNG black level {level} is outside 0..{whiteLevel}.");
            }

            int rowsPerStrip = tags.TryGetValue(TagRowsPerStrip, out TiffEntry rowsTag)
                ? (int)Math.Min(ReadUnsignedFirst(rowsTag, littleEndian), int.MaxValue)
                : height;
            if (rowsPerStrip <= 0)
                rowsPerStrip = height;

            (long[] stripOffsets, long[] stripByteCounts) = ReadStripLayout(tags, littleEndian);

            return new DngLayout
            {
                Width = width,
                Height = height,
                BitsPerSample = bitsPerSample,
                LittleEndian = littleEndian,
                Bayer = bayer,
                BlackLevels = blackLevels,
                BlackRows = blackRows,
                BlackColumns = blackColumns,
                WhiteLevel = whiteLevel,
                RowsPerStrip = rowsPerStrip,
                StripOffsets = stripOffsets,
                StripByteCounts = stripByteCounts,
            };
        }

        private static Dictionary<ushort, TiffEntry> ReadDirectory(
            Stream stream, long start, long directoryOffset, bool littleEndian)
        {
            long position = start + directoryOffset;
            byte[] countBytes = new byte[2];
            ReadExactlyAt(stream, position, countBytes, "TIFF directory");
            int entryCount = ReadUInt16(countBytes, littleEndian);
            if (entryCount == 0)
                throw new InvalidDataException("DNG has an empty first directory.");

            byte[] entries = new byte[entryCount * 12];
            ReadExactlyAt(stream, position + 2, entries, "TIFF directory entries");

            var tags = new Dictionary<ushort, TiffEntry>(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                ReadOnlySpan<byte> entry = entries.AsSpan(i * 12, 12);
                ushort tag = ReadUInt16(entry[..2], littleEndian);
                ushort type = ReadUInt16(entry[2..4], littleEndian);
                uint count = ReadUInt32(entry[4..8], littleEndian);
                int size = TiffEntry.SizeOf(type);
                if (size == 0)
                    throw new InvalidDataException($"DNG tag {tag} has unknown TIFF type {type}.");

                ulong total = (ulong)count * (uint)size;
                if (total > 16 * 1024 * 1024)
                    throw new InvalidDataException($"DNG tag {tag} declares {total} bytes of value data.");

                byte[] data = new byte[(int)total];
                if (total <= 4)
                {
                    entry.Slice(8, (int)total).CopyTo(data);
                }
                else
                {
                    uint valueOffset = ReadUInt32(entry[8..12], littleEndian);
                    ReadExactlyAt(stream, start + valueOffset, data, $"DNG tag {tag} value");
                }

                tags[tag] = new TiffEntry(tag, type, count, data);
            }

            return tags;
        }

        private static BayerPattern ReadCfaPattern(Dictionary<ushort, TiffEntry> tags, bool littleEndian)
        {
            if (!tags.TryGetValue(TagCfaRepeatPatternDim, out TiffEntry repeat))
                throw new InvalidDataException("DNG is missing CFARepeatPatternDim (tag 33421).");
            if (repeat.Type != 3 || repeat.Count < 2)
                throw new InvalidDataException("CFARepeatPatternDim must be two SHORT values.");
            ushort rows = ReadUInt16(repeat.Data.AsSpan(0, 2), littleEndian);
            ushort columns = ReadUInt16(repeat.Data.AsSpan(2, 2), littleEndian);
            if (rows != 2 || columns != 2)
                throw new NotSupportedException($"CFARepeatPatternDim {rows}x{columns} is not supported; only 2x2 Bayer tiles are.");

            if (tags.TryGetValue(TagCfaPlaneColor, out TiffEntry planes))
            {
                if (planes.Type != 1 || planes.Count < 3 ||
                    planes.Data[0] != 0 || planes.Data[1] != 1 || planes.Data[2] != 2)
                {
                    throw new NotSupportedException("CFAPlaneColor must be the standard R,G,B plane order.");
                }
            }

            if (!tags.TryGetValue(TagCfaPattern, out TiffEntry pattern) ||
                pattern.Type != 1 || pattern.Count != 4)
            {
                throw new InvalidDataException("DNG CFAPattern (tag 33422) must be four BYTE values.");
            }

            return (pattern.Data[0], pattern.Data[1], pattern.Data[2], pattern.Data[3]) switch
            {
                (0, 1, 1, 2) => BayerPattern.RGGB,
                (2, 1, 1, 0) => BayerPattern.BGGR,
                (1, 0, 2, 1) => BayerPattern.GRBG,
                (1, 2, 0, 1) => BayerPattern.GBRG,
                _ => throw new NotSupportedException(
                    $"CFAPattern [{pattern.Data[0]},{pattern.Data[1]},{pattern.Data[2]},{pattern.Data[3]}] " +
                    "is not one of the four 2x2 Bayer arrangements."),
            };
        }

        private static (double[] Levels, int Rows, int Columns) ReadBlackLevels(
            Dictionary<ushort, TiffEntry> tags, bool littleEndian, int bitsPerSample)
        {
            int rows = 1;
            int columns = 1;
            if (tags.TryGetValue(TagBlackLevelRepeatDim, out TiffEntry repeat))
            {
                if (repeat.Type != 3 || repeat.Count < 2)
                    throw new InvalidDataException("BlackLevelRepeatDim must be two SHORT values.");
                rows = ReadUInt16(repeat.Data.AsSpan(0, 2), littleEndian);
                columns = ReadUInt16(repeat.Data.AsSpan(2, 2), littleEndian);
                if (rows is not (1 or 2) || columns is not (1 or 2))
                {
                    throw new NotSupportedException(
                        $"BlackLevelRepeatDim {rows}x{columns} is not supported; only 1x1 and 2x2 are.");
                }
            }

            if (!tags.TryGetValue(TagBlackLevel, out TiffEntry black))
                return (new double[] { 0 }, 1, 1);

            double[] values = ReadNumbers(black, littleEndian);
            int sites = rows * columns;
            if (values.Length == 1)
            {
                var replicated = new double[sites];
                Array.Fill(replicated, values[0]);
                return (replicated, rows, columns);
            }

            if (values.Length < sites)
            {
                throw new InvalidDataException(
                    $"BlackLevel has {values.Length} value(s) for a {rows}x{columns} repeat pattern.");
            }

            return (values[..sites], rows, columns);
        }

        private static double ReadWhiteLevel(
            Dictionary<ushort, TiffEntry> tags, bool littleEndian, int bitsPerSample)
        {
            if (!tags.TryGetValue(TagWhiteLevel, out TiffEntry white))
                return (1 << bitsPerSample) - 1;

            double[] values = ReadNumbers(white, littleEndian);
            if (values.Length == 0 || values[0] <= 0)
                throw new InvalidDataException("WhiteLevel must be positive.");
            return values[0];
        }

        private static (long[] Offsets, long[] Counts) ReadStripLayout(
            Dictionary<ushort, TiffEntry> tags, bool littleEndian)
        {
            if (!tags.TryGetValue(TagStripOffsets, out TiffEntry offsets))
                throw new NotSupportedException("DNG has no StripOffsets; only stripped images are read.");
            if (!tags.TryGetValue(TagStripByteCounts, out TiffEntry counts))
                throw new InvalidDataException("DNG is missing StripByteCounts (tag 279).");

            uint[] offsetValues = ReadUnsignedValues(offsets, littleEndian);
            uint[] countValues = ReadUnsignedValues(counts, littleEndian);
            if (offsetValues.Length == 0 || countValues.Length == 0)
                throw new InvalidDataException("DNG strip arrays are empty.");

            return (Array.ConvertAll(offsetValues, v => (long)v), Array.ConvertAll(countValues, v => (long)v));
        }

        private static int ReadBitsPerSample(Dictionary<ushort, TiffEntry> tags, bool littleEndian)
        {
            if (!tags.TryGetValue(TagBitsPerSample, out TiffEntry bits))
                throw new InvalidDataException("DNG is missing BitsPerSample (tag 258).");
            return (int)ReadUnsignedFirst(bits, littleEndian);
        }

        private static int ReadPositiveInt(
            Dictionary<ushort, TiffEntry> tags, ushort tag, string name, bool littleEndian)
        {
            if (!tags.TryGetValue(tag, out TiffEntry entry))
                throw new InvalidDataException($"DNG is missing {name} (tag {tag}).");
            uint value = ReadUnsignedFirst(entry, littleEndian);
            if (value == 0 || value > int.MaxValue)
                throw new InvalidDataException($"DNG {name} value {value} is out of range.");
            return (int)value;
        }

        private static int ReadUnsignedTag(
            Dictionary<ushort, TiffEntry> tags, ushort tag, string name, bool littleEndian)
        {
            if (!tags.TryGetValue(tag, out TiffEntry entry))
                throw new InvalidDataException($"DNG is missing {name} (tag {tag}).");
            return (int)ReadUnsignedFirst(entry, littleEndian);
        }

        private static uint ReadUnsignedFirst(TiffEntry entry, bool littleEndian)
        {
            uint[] values = ReadUnsignedValues(entry, littleEndian);
            if (values.Length == 0)
                throw new InvalidDataException($"DNG tag {entry.Tag} has no values.");
            return values[0];
        }

        private static uint[] ReadUnsignedValues(TiffEntry entry, bool littleEndian)
        {
            int size = entry.Type switch
            {
                1 => 1,
                3 => 2,
                4 => 4,
                _ => 0,
            };
            if (size == 0)
                throw new InvalidDataException($"DNG tag {entry.Tag} has non-integer type {entry.Type}.");

            var values = new uint[entry.Count];
            for (int i = 0; i < values.Length; i++)
            {
                ReadOnlySpan<byte> span = entry.Data.AsSpan(i * size, size);
                values[i] = size switch
                {
                    1 => span[0],
                    2 => ReadUInt16(span, littleEndian),
                    _ => ReadUInt32(span, littleEndian),
                };
            }

            return values;
        }

        private static double[] ReadNumbers(TiffEntry entry, bool littleEndian)
        {
            if (entry.Type is 3 or 4)
                return Array.ConvertAll(ReadUnsignedValues(entry, littleEndian), v => (double)v);

            if (entry.Type != 5)
                throw new InvalidDataException($"DNG tag {entry.Tag} has non-numeric type {entry.Type}.");

            var values = new double[entry.Count];
            for (int i = 0; i < values.Length; i++)
            {
                ReadOnlySpan<byte> span = entry.Data.AsSpan(i * 8, 8);
                uint numerator = ReadUInt32(span[..4], littleEndian);
                uint denominator = ReadUInt32(span[4..], littleEndian);
                if (denominator == 0)
                    throw new InvalidDataException($"DNG tag {entry.Tag} has a zero rational denominator.");
                values[i] = (double)numerator / denominator;
            }

            return values;
        }

        private static void ReadExactlyAt(Stream stream, long position, Span<byte> destination, string what)
        {
            if (position < 0)
                throw new InvalidDataException($"{what} is at a negative file offset.");
            stream.Position = position;
            try
            {
                stream.ReadExactly(destination);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"{what} is truncated.", ex);
            }
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> span, bool littleEndian) =>
            littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span);

        private static uint ReadUInt32(ReadOnlySpan<byte> span, bool littleEndian) =>
            littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(span)
                : BinaryPrimitives.ReadUInt32BigEndian(span);
    }
}
