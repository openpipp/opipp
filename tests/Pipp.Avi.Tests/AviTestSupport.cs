using System.Buffers.Binary;
using System.Text;
using Pipp.Core.Formats;
using Pipp.Core.Frames;

namespace Pipp.Avi.Tests;

/// <summary>Helpers shared by the AVI tests: frame generators, a minimal independent chunk walker and a
/// hand-built AVI fixture writer for cases the production writer cannot produce (foreign codecs/palettes).</summary>
internal static class AviTestSupport
{
    public static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pipp-avi-{Guid.NewGuid():N}.avi");

    public static Frame MonoFrame(int width, int height, int seed)
    {
        var frame = new Frame(width, height, 1, 8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                frame[x, y] = (ushort)((x * 37 + y * 11 + seed * 53) % 256);
        }

        return frame;
    }

    public static Frame ColourFrame(int width, int height, int seed)
    {
        var frame = new Frame(width, height, 3, 8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame[x, y, 0] = (ushort)(10 + seed * 50 + y * 20 + x);
                frame[x, y, 1] = (ushort)(120 + y * 20 + x);
                frame[x, y, 2] = (ushort)(230 - y * 20 - x);
            }
        }

        return frame;
    }

    public static List<Frame> ReadAll(string path)
    {
        using var reader = new AviReader(path);
        var frames = new List<Frame>();
        while (reader.ReadFrame() is { } frame)
            frames.Add(frame);
        return frames;
    }

    public static uint Fcc(string text)
    {
        if (text.Length != 4)
            throw new ArgumentException("FourCC must have four characters.", nameof(text));
        return (uint)(text[0] | (text[1] << 8) | (text[2] << 16) | (text[3] << 24));
    }

    public static string FccText(uint value)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte b = (byte)(value >> (8 * i));
            if (b is < 0x20 or > 0x7E)
                return $"0x{value:X8}";
            chars[i] = (char)b;
        }

        return new string(chars);
    }

    public static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    public static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    public static int ReadInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));

    /// <summary>One chunk discovered by <see cref="WalkChunks"/>; list chunks carry their list type in <see cref="Id"/>.</summary>
    public sealed record ChunkEntry(string Id, int Offset, int Size, int DataOffset, int Depth);

    /// <summary>
    /// Independent RIFF walker (no production parser involved). List chunks are recorded under their list
    /// type ("hdrl", "strl", "movi") and then descended into.
    /// </summary>
    public static List<ChunkEntry> WalkChunks(byte[] file)
    {
        var chunks = new List<ChunkEntry>();
        using var stream = new MemoryStream(file, writable: false);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        string riff = ReadFcc(reader);
        uint riffSize = reader.ReadUInt32();
        string form = ReadFcc(reader);
        if (riff != "RIFF" || form != "AVI ")
            throw new InvalidDataException($"unexpected RIFF header '{riff}' form '{form}'");

        chunks.Add(new ChunkEntry("RIFF", 0, (int)riffSize, 8, 0));
        chunks.Add(new ChunkEntry(form, 8, 0, 12, 0));
        Walk(12, 8 + (long)riffSize, 1);
        return chunks;

        void Walk(long start, long end, int depth)
        {
            long pos = start;
            while (pos + 8 <= end)
            {
                stream.Position = pos;
                string id = ReadFcc(reader);
                int size = checked((int)reader.ReadUInt32());
                long data = pos + 8;

                if (id == "LIST")
                {
                    string listType = ReadFcc(reader);
                    chunks.Add(new ChunkEntry(listType, (int)pos, size, (int)data, depth));
                    Walk(data + 4, Math.Min(data + size, end), depth + 1);
                }
                else
                {
                    chunks.Add(new ChunkEntry(id, (int)pos, size, (int)data, depth));
                }

                pos = data + size + (size & 1);
            }
        }
    }

    /// <summary>
    /// Writes a minimal AVI 1.0 file by hand. Used to create inputs the production writer never emits:
    /// a foreign compression FourCC, or an 8bpp DIB with a custom palette. Frames are supplied as raw
    /// payload bytes (callers must already have padded DIB rows).
    /// </summary>
    public static byte[] BuildAvi(
        int width,
        int height,
        ushort bitCount,
        uint compression,
        int biHeight,
        IReadOnlyList<byte[]> frames,
        byte[]? paletteBgra = null,
        int rate = 25,
        int scale = 1,
        string chunkId = "00db",
        bool includeIndex = true)
    {
        int rowBytes = bitCount == 24 ? width * 3 : width;
        int stride = compression == Fcc("Y800") ? rowBytes : (rowBytes + 3) & ~3;
        int frameBytes = stride * height;
        int paletteBytes = paletteBgra?.Length ?? 0;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        WriteFcc(writer, "RIFF");
        writer.Write(0u); // patched below
        WriteFcc(writer, "AVI ");

        int hdrlSizePosition = BeginList(writer, "hdrl");
        WriteFcc(writer, "avih");
        writer.Write(56u);
        uint microSeconds = (uint)Math.Round(1_000_000.0 * scale / rate);
        writer.Write(microSeconds);
        writer.Write((uint)(frameBytes * rate / scale)); // max bytes/sec
        writer.Write(0u); // padding granularity
        writer.Write(0x10u); // AVIF_HASINDEX
        writer.Write((uint)frames.Count);
        writer.Write(0u); // initial frames
        writer.Write(1u); // streams
        writer.Write((uint)(8 + frameBytes));
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);

        int strlSizePosition = BeginList(writer, "strl");
        WriteFcc(writer, "strh");
        writer.Write(56u);
        WriteFcc(writer, "vids");
        WriteFcc(writer, compression == Fcc("Y800") ? "Y800" : "DIB ");
        writer.Write(0u); // flags
        writer.Write((ushort)0); // priority
        writer.Write((ushort)0); // language
        writer.Write(0u); // initial frames
        writer.Write((uint)scale);
        writer.Write((uint)rate);
        writer.Write(0u); // start
        writer.Write((uint)frames.Count);
        writer.Write((uint)(8 + frameBytes));
        writer.Write(uint.MaxValue); // quality
        writer.Write((uint)frameBytes);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)width);
        writer.Write((short)height);

        WriteFcc(writer, "strf");
        writer.Write((uint)(40 + paletteBytes));
        writer.Write(40u); // biSize
        writer.Write(width);
        writer.Write(biHeight);
        writer.Write((ushort)1); // planes
        writer.Write(bitCount);
        writer.Write(compression);
        writer.Write((uint)frameBytes); // biSizeImage
        writer.Write(0); // x ppm
        writer.Write(0); // y ppm
        writer.Write(paletteBgra is null ? 0u : 256u); // biClrUsed
        writer.Write(0u); // biClrImportant
        if (paletteBgra is not null)
            writer.Write(paletteBgra);
        EndList(writer, strlSizePosition);
        EndList(writer, hdrlSizePosition);

        WriteFcc(writer, "LIST");
        int moviSizePosition = (int)stream.Position;
        writer.Write(0u);
        int moviTypePosition = (int)stream.Position;
        WriteFcc(writer, "movi");

        var chunkOffsets = new List<int>();
        var chunkSizes = new List<int>();
        foreach (byte[] payload in frames)
        {
            chunkOffsets.Add((int)stream.Position - moviTypePosition);
            chunkSizes.Add(payload.Length);
            WriteFcc(writer, chunkId);
            writer.Write((uint)payload.Length);
            writer.Write(payload);
            if ((payload.Length & 1) != 0)
                writer.Write((byte)0);
        }

        int moviEnd = (int)stream.Position;
        Patch(writer, moviSizePosition, (uint)(moviEnd - (moviSizePosition + 4)));

        if (includeIndex)
        {
            WriteFcc(writer, "idx1");
            writer.Write((uint)(16 * frames.Count));
            for (int i = 0; i < frames.Count; i++)
            {
                WriteFcc(writer, chunkId);
                writer.Write(0x10u); // AVIIF_KEYFRAME
                writer.Write((uint)chunkOffsets[i]);
                writer.Write((uint)chunkSizes[i]);
            }
        }

        int end = (int)stream.Position;
        Patch(writer, 4, (uint)(end - 8));
        writer.Flush();
        return stream.ToArray();
    }

    private static int BeginList(BinaryWriter writer, string type)
    {
        WriteFcc(writer, "LIST");
        int sizePosition = (int)writer.BaseStream.Position;
        writer.Write(0u);
        WriteFcc(writer, type);
        return sizePosition;
    }

    private static void EndList(BinaryWriter writer, int sizePosition)
    {
        int end = (int)writer.BaseStream.Position;
        Patch(writer, sizePosition, (uint)(end - (sizePosition + 4)));
    }

    private static void Patch(BinaryWriter writer, int position, uint value)
    {
        long saved = writer.BaseStream.Position;
        writer.BaseStream.Position = position;
        writer.Write(value);
        writer.BaseStream.Position = saved;
    }

    private static void WriteFcc(BinaryWriter writer, string text) => writer.Write(Encoding.ASCII.GetBytes(text));

    private static string ReadFcc(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
            throw new EndOfStreamException("truncated FourCC");
        return Encoding.ASCII.GetString(bytes);
    }
}
