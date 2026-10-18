using System.Buffers.Binary;

namespace Pipp.Core.Formats;

/// <summary>Location of one uncompressed video chunk inside an AVI file.</summary>
internal readonly record struct AviFrameLocation(long Offset, int Size, uint ChunkId);

/// <summary>Parsed header and frame index of an AVI file.</summary>
internal sealed class AviStructure
{
    public AviMainHeader MainHeader;
    public bool HasMainHeader;
    public AviStreamHeader VideoStreamHeader;
    public bool HasVideoStream;
    public BitmapInfoHeader BitmapHeader;
    public bool HasBitmap;
    public byte[] Palette = Array.Empty<byte>();
    public List<AviFrameLocation> Frames { get; } = new();

    /// <summary>Absolute position of the <c>movi</c> FourCC (the LIST type), or -1 when absent.</summary>
    public long MoviTypePosition = -1;

    /// <summary>Absolute position just past the last byte of the <c>movi</c> LIST payload.</summary>
    public long MoviEnd = -1;

    public long Idx1Position = -1;
    public int Idx1Size;

    public long RiffEnd;
}

/// <summary>
/// Tolerant RIFF/AVI 1.0 walker. It extracts the first video stream's <c>avih</c>/<c>strh</c>/<c>strf</c>
/// headers plus the byte range of every uncompressed video chunk. Codec support is left to the caller so
/// that probing a compressed AVI can report its FourCC without decoding it.
/// </summary>
internal static class AviStructureReader
{
    /// <summary>Maximum nesting depth followed inside <c>movi</c> (rec lists are allowed, loops are not).</summary>
    private const int MaxListDepth = 4;

    /// <summary>Reads the AVI structure from the stream's current position.</summary>
    /// <exception cref="InvalidDataException">The stream is not a RIFF/AVI file or its headers are broken.</exception>
    public static AviStructure Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("AVI parsing requires a readable, seekable stream.", nameof(stream));

        long baseOffset = stream.Position;
        Span<byte> header = stackalloc byte[12];
        if (!TryReadExactly(stream, header))
            throw new InvalidDataException("File is too short to contain a RIFF/AVI header.");

        uint riffId = BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        uint formType = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);

        if (riffId != AviIds.Riff)
            throw new InvalidDataException($"Not a RIFF file: expected 'RIFF', got '{AviFourCC.ToText(riffId)}'.");
        if (formType != AviIds.Avi)
            throw new InvalidDataException($"RIFF form is '{AviFourCC.ToText(formType)}', not 'AVI '.");

        long fileLength = stream.Length;
        long riffEnd = baseOffset + 8L + riffSize;
        // Broken RIFF sizes are common in the wild (interrupted captures); the real file length wins.
        if (riffSize < 4 || riffEnd > fileLength || riffEnd < baseOffset + 12)
            riffEnd = fileLength;

        var structure = new AviStructure { RiffEnd = riffEnd };

        long pos = baseOffset + 12;
        while (pos + 8 <= riffEnd)
        {
            if (!TryReadChunkHeader(stream, pos, out uint id, out uint size))
                break;

            long dataPos = pos + 8;
            long available = riffEnd - dataPos;
            if (size > available)
                size = (uint)Math.Max(0, available);
            long chunkEnd = dataPos + size;

            if (id == AviIds.List)
            {
                if (size >= 4 && TryReadUInt32(stream, dataPos, out uint listType))
                {
                    if (listType == AviIds.Hdrl)
                    {
                        ParseHeaderList(stream, dataPos + 4, chunkEnd, structure);
                    }
                    else if (listType == AviIds.Movi && structure.MoviTypePosition < 0)
                    {
                        structure.MoviTypePosition = dataPos;
                        structure.MoviEnd = chunkEnd;
                    }
                }
            }
            else if (id == AviIds.Idx1 && structure.Idx1Position < 0)
            {
                structure.Idx1Position = dataPos;
                structure.Idx1Size = (int)Math.Min(size, int.MaxValue);
            }

            long next = chunkEnd + (size & 1);
            if (next <= pos)
                break;
            pos = next;
        }

        if (structure.MoviTypePosition >= 0)
            ScanMovi(stream, structure.MoviTypePosition + 4, structure.MoviEnd, structure.Frames, 0);

        // The chunk walk is authoritative; idx1 is only needed when the movi walk found nothing
        // (for example a file whose movi chunk sizes are unusable).
        if (structure.Frames.Count == 0 && structure.Idx1Position >= 0)
            ResolveFramesFromIndex(stream, structure);

        return structure;
    }

    /// <summary>Frame count reported to callers: the stream header's length when present, clamped to real chunks.</summary>
    public static int ResolveFrameCount(AviStructure structure)
    {
        int available = structure.Frames.Count;
        if (structure.HasVideoStream && structure.VideoStreamHeader.Length > 0)
        {
            long declared = structure.VideoStreamHeader.Length;
            if (declared < available)
                return (int)declared;
        }

        return available;
    }

    /// <summary>Builds the public metadata view; works for unsupported codecs because nothing is decoded.</summary>
    public static AviInfo CreateInfo(AviStructure structure)
    {
        if (!structure.HasBitmap)
            throw new InvalidDataException("AVI file has no video stream with a BITMAPINFOHEADER ('strf') chunk.");

        BitmapInfoHeader bitmap = structure.BitmapHeader;
        if (bitmap.Width <= 0)
            throw new InvalidDataException($"AVI video stream has an invalid width ({bitmap.Width}).");
        if (bitmap.Height == 0 || bitmap.Height == int.MinValue)
            throw new InvalidDataException($"AVI video stream has an invalid height ({bitmap.Height}).");

        uint rate = structure.HasVideoStream ? structure.VideoStreamHeader.Rate : 0;
        uint scale = structure.HasVideoStream ? structure.VideoStreamHeader.Scale : 0;

        return new AviInfo
        {
            Width = bitmap.Width,
            Height = Math.Abs(bitmap.Height),
            BitCount = bitmap.BitCount,
            FourCC = bitmap.Compression == BitmapInfoHeader.BiRgb
                ? "BI_RGB"
                : AviFourCC.ToText(bitmap.Compression),
            FpsRate = ToInt(rate),
            FpsScale = ToInt(scale),
            FrameCount = ResolveFrameCount(structure),
        };
    }

    private static void ParseHeaderList(Stream stream, long start, long end, AviStructure structure)
    {
        Span<byte> buffer = stackalloc byte[AviMainHeader.Size];
        long pos = start;
        while (pos + 8 <= end)
        {
            if (!TryReadChunkHeader(stream, pos, out uint id, out uint size))
                break;

            long dataPos = pos + 8;
            long available = end - dataPos;
            if (size > available)
                size = (uint)Math.Max(0, available);
            long chunkEnd = dataPos + size;

            if (id == AviIds.Avih && size >= AviMainHeader.Size && !structure.HasMainHeader)
            {
                if (TryReadExactlyAt(stream, dataPos, buffer))
                {
                    structure.MainHeader = AviMainHeader.Parse(buffer);
                    structure.HasMainHeader = true;
                }
            }
            else if (id == AviIds.List && size >= 4 && TryReadUInt32(stream, dataPos, out uint listType)
                     && listType == AviIds.Strl && !structure.HasVideoStream)
            {
                AviStream? video = ParseStreamList(stream, dataPos + 4, chunkEnd);
                if (video is { HasHeader: true, HasBitmap: true } && video.Header.Type == AviIds.Vids)
                {
                    structure.VideoStreamHeader = video.Header;
                    structure.HasVideoStream = true;
                    structure.BitmapHeader = video.Bitmap;
                    structure.HasBitmap = true;
                    structure.Palette = video.Palette;
                }
            }

            long next = chunkEnd + (size & 1);
            if (next <= pos)
                break;
            pos = next;
        }
    }

    private static AviStream? ParseStreamList(Stream stream, long start, long end)
    {
        var result = new AviStream();
        Span<byte> buffer = stackalloc byte[AviStreamHeader.Size];
        long pos = start;
        while (pos + 8 <= end)
        {
            if (!TryReadChunkHeader(stream, pos, out uint id, out uint size))
                break;

            long dataPos = pos + 8;
            long available = end - dataPos;
            if (size > available)
                size = (uint)Math.Max(0, available);
            long chunkEnd = dataPos + size;

            if (id == AviIds.Strh && size >= AviStreamHeader.Size && !result.HasHeader)
            {
                if (TryReadExactlyAt(stream, dataPos, buffer))
                {
                    result.Header = AviStreamHeader.Parse(buffer);
                    result.HasHeader = true;
                }
            }
            else if (id == AviIds.Strf && size >= BitmapInfoHeader.Size && !result.HasBitmap)
            {
                Span<byte> bitmapBuffer = buffer[..BitmapInfoHeader.Size];
                if (TryReadExactlyAt(stream, dataPos, bitmapBuffer))
                {
                    result.Bitmap = BitmapInfoHeader.Parse(bitmapBuffer);
                    result.HasBitmap = true;
                    result.Palette = ReadPalette(stream, dataPos + BitmapInfoHeader.Size, size - BitmapInfoHeader.Size, result.Bitmap);
                }
            }

            long next = chunkEnd + (size & 1);
            if (next <= pos)
                break;
            pos = next;
        }

        return result.HasHeader ? result : null;
    }

    /// <summary>
    /// Reads a DIB palette following <c>BITMAPINFOHEADER</c>. Entries are stored B,G,R,0 and returned as
    /// R,G,B triples. Absent or truncated palettes yield an empty array (the reader then assumes grayscale).
    /// </summary>
    private static byte[] ReadPalette(Stream stream, long offset, long available, BitmapInfoHeader bitmap)
    {
        int entries = bitmap.ClrUsed is > 0 and <= 256 ? (int)bitmap.ClrUsed : 256;
        entries = (int)Math.Min(entries, available / 4);
        if (entries <= 0)
            return Array.Empty<byte>();

        var raw = new byte[entries * 4];
        if (!TryReadExactlyAt(stream, offset, raw))
            return Array.Empty<byte>();

        var rgb = new byte[entries * 3];
        for (int i = 0; i < entries; i++)
        {
            rgb[i * 3] = raw[i * 4 + 2];
            rgb[i * 3 + 1] = raw[i * 4 + 1];
            rgb[i * 3 + 2] = raw[i * 4];
        }

        return rgb;
    }

    /// <summary>Collects video chunk offsets from a <c>movi</c> list, descending into nested LIST chunks.</summary>
    private static void ScanMovi(Stream stream, long start, long end, List<AviFrameLocation> frames, int depth)
    {
        long pos = start;
        while (pos + 8 <= end)
        {
            if (!TryReadChunkHeader(stream, pos, out uint id, out uint size))
                break;

            long dataPos = pos + 8;
            long available = end - dataPos;
            if (size > available)
                size = (uint)Math.Max(0, available);
            long chunkEnd = dataPos + size;

            if (id == AviIds.List && size >= 4 && depth < MaxListDepth)
            {
                ScanMovi(stream, dataPos + 4, chunkEnd, frames, depth + 1);
            }
            else if (id == AviIds.Db || id == AviIds.Dc)
            {
                long remaining = stream.Length - dataPos;
                long effective = Math.Min((long)size, Math.Max(0, remaining));
                if (effective > int.MaxValue)
                    throw new InvalidDataException($"AVI chunk at {pos} declares {effective} bytes, which is too large.");
                frames.Add(new AviFrameLocation(dataPos, (int)effective, id));
            }

            long next = chunkEnd + (size & 1);
            if (next <= pos)
                break;
            pos = next;
        }
    }

    /// <summary>
    /// Rebuilds the frame list from <c>idx1</c> when the <c>movi</c> walk produced nothing. Both common
    /// offset conventions are tried: relative to the <c>movi</c> FourCC, and relative to the payload just
    /// after it.
    /// </summary>
    private static void ResolveFramesFromIndex(Stream stream, AviStructure structure)
    {
        if (structure.MoviTypePosition < 0)
            return;

        int count = structure.Idx1Size / 16;
        long moviContentStart = structure.MoviTypePosition + 4;
        Span<byte> entry = stackalloc byte[16];

        for (int i = 0; i < count; i++)
        {
            if (!TryReadExactlyAt(stream, structure.Idx1Position + i * 16L, entry))
                break;

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            if (id != AviIds.Db && id != AviIds.Dc)
                continue;

            foreach (long candidate in new[] { structure.MoviTypePosition + offset, moviContentStart + offset })
            {
                if (candidate < moviContentStart || candidate + 8 > structure.MoviEnd)
                    continue;
                if (!TryReadChunkHeader(stream, candidate, out uint chunkId, out uint chunkSize))
                    continue;
                if (chunkId != id)
                    continue;

                long payload = candidate + 8;
                long effective = Math.Min((long)chunkSize, Math.Max(0, structure.RiffEnd - payload));
                structure.Frames.Add(new AviFrameLocation(payload, (int)Math.Min(effective, int.MaxValue), chunkId));
                break;
            }
        }
    }

    private static bool TryReadChunkHeader(Stream stream, long position, out uint id, out uint size)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (!TryReadExactlyAt(stream, position, buffer))
        {
            id = 0;
            size = 0;
            return false;
        }

        id = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        size = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        return true;
    }

    private static bool TryReadUInt32(Stream stream, long position, out uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!TryReadExactlyAt(stream, position, buffer))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        return true;
    }

    private static bool TryReadExactlyAt(Stream stream, long position, Span<byte> buffer)
    {
        if (position < 0 || position + buffer.Length > stream.Length)
            return false;
        stream.Position = position;
        return TryReadExactly(stream, buffer);
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static int ToInt(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    /// <summary>Temporary holder for one <c>strl</c> list while its chunks are parsed.</summary>
    private sealed class AviStream
    {
        public AviStreamHeader Header;
        public bool HasHeader;
        public BitmapInfoHeader Bitmap;
        public bool HasBitmap;
        public byte[] Palette = Array.Empty<byte>();
    }
}
