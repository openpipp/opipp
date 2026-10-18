using System.Buffers.Binary;
using System.Text;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>Options controlling GIF output.</summary>
/// <param name="DelayCentiseconds">Per-frame delay in hundredths of a second (10 = 100 ms).</param>
/// <param name="Loop">When true, the animation loops forever (NETSCAPE2.0 extension).</param>
public sealed record GifOptions(int DelayCentiseconds = 10, bool Loop = true)
{
    /// <summary>Default options: 100 ms per frame, looping.</summary>
    public static GifOptions Default { get; } = new();
}

/// <summary>
/// Writes GIF89a files: a global colour table, optional looping, and one image block per frame.
/// </summary>
/// <remarks>
/// Monochrome frames map to a 256-entry greyscale ramp, so an 8-bit mono frame is written without any
/// loss. Colour frames use a median-cut palette built from a sample of all frames and are mapped to
/// the nearest entry. 16-bit samples are reduced to 8 bits by keeping the high byte, matching how the
/// engine converts to its 8-bit output formats.
/// </remarks>
public static class GifWriter
{
    private const int PaletteEntries = 256;
    private const int PaletteBytes = PaletteEntries * 3;

    /// <summary>Writes <paramref name="frames"/> to <paramref name="path"/> as an animated GIF.</summary>
    public static void Write(string path, IReadOnlyList<Frame> frames, GifOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Encode(frames, options));
    }

    /// <summary>Encodes <paramref name="frames"/> into a GIF89a byte stream.</summary>
    public static byte[] Encode(IReadOnlyList<Frame> frames, GifOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        options ??= GifOptions.Default;

        if (frames.Any(f => f.Width != frames[0].Width || f.Height != frames[0].Height))
        {
            throw new ArgumentException("All frames must share the same dimensions.", nameof(frames));
        }

        int width = frames[0].Width;
        int height = frames[0].Height;
        bool colour = frames[0].Channels == 3;

        (byte[] palette, Func<Frame, byte[]> indexer) = colour
            ? BuildColourPalette(frames)
            : (BuildGreyscalePalette(), IndexMono);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));

        // Logical screen descriptor: global colour table present, 8-bit colour resolution, size 256.
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0xF7);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write(palette);

        if (options.Loop && frames.Count > 1)
        {
            writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
            writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        foreach (Frame frame in frames)
        {
            // Graphic control extension: disposal 1 (leave in place), no transparency.
            writer.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 });
            writer.Write((ushort)Math.Clamp(options.DelayCentiseconds, 0, 65535));
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);

            // Image descriptor: full frame, no local colour table, not interlaced.
            writer.Write((byte)0x2C);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0x00);

            byte[] indices = indexer(frame);
            writer.Write((byte)8);
            writer.Write(GifLzw.Encode(indices, 8));
        }

        writer.Write((byte)0x3B);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildGreyscalePalette()
    {
        var table = new byte[PaletteBytes];
        for (int i = 0; i < PaletteEntries; i++)
        {
            table[(i * 3) + 0] = (byte)i;
            table[(i * 3) + 1] = (byte)i;
            table[(i * 3) + 2] = (byte)i;
        }

        return table;
    }

    private static byte[] IndexMono(Frame frame)
    {
        var indices = new byte[frame.Width * frame.Height];
        int shift = frame.BitDepth == 16 ? 8 : 0;
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (byte)(frame.Samples[i] >> shift);
        }

        return indices;
    }

    private static (byte[] Palette, Func<Frame, byte[]> Indexer) BuildColourPalette(IReadOnlyList<Frame> frames)
    {
        var sample = new List<(byte R, byte G, byte B)>(1 << 16);
        int step = Math.Max(1, (frames[0].Width * frames[0].Height) / 16384);
        foreach (Frame frame in frames)
        {
            int shift = frame.BitDepth == 16 ? 8 : 0;
            for (int p = 0; p < frame.Width * frame.Height; p += step)
            {
                int i = p * 3;
                sample.Add((
                    (byte)(frame.Samples[i] >> shift),
                    (byte)(frame.Samples[i + 1] >> shift),
                    (byte)(frame.Samples[i + 2] >> shift)));
            }
        }

        List<(byte R, byte G, byte B)> colours = MedianCut(sample, PaletteEntries);
        var table = new byte[PaletteBytes];
        for (int i = 0; i < colours.Count; i++)
        {
            table[(i * 3) + 0] = colours[i].R;
            table[(i * 3) + 1] = colours[i].G;
            table[(i * 3) + 2] = colours[i].B;
        }

        var cache = new Dictionary<int, byte>(4096);
        byte[] Indexer(Frame frame)
        {
            var indices = new byte[frame.Width * frame.Height];
            int shift = frame.BitDepth == 16 ? 8 : 0;
            for (int p = 0; p < indices.Length; p++)
            {
                int i = p * 3;
                byte r = (byte)(frame.Samples[i] >> shift);
                byte g = (byte)(frame.Samples[i + 1] >> shift);
                byte b = (byte)(frame.Samples[i + 2] >> shift);
                int key = (r << 16) | (g << 8) | b;
                if (!cache.TryGetValue(key, out byte index))
                {
                    index = Nearest(colours, r, g, b);
                    cache[key] = index;
                }

                indices[p] = index;
            }

            return indices;
        }

        return (table, Indexer);
    }

    private static byte Nearest(List<(byte R, byte G, byte B)> colours, byte r, byte g, byte b)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < colours.Count; i++)
        {
            int dr = colours[i].R - r;
            int dg = colours[i].G - g;
            int db = colours[i].B - b;
            int distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (byte)best;
    }

    /// <summary>Median-cut quantisation: split the widest box along its widest channel until full.</summary>
    private static List<(byte R, byte G, byte B)> MedianCut(List<(byte R, byte G, byte B)> pixels, int count)
    {
        var boxes = new List<List<(byte R, byte G, byte B)>> { pixels };

        while (boxes.Count < count)
        {
            int widest = -1;
            int widestRange = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                {
                    continue;
                }

                (int range, _) = Extents(boxes[i]);
                if (range > widestRange)
                {
                    widestRange = range;
                    widest = i;
                }
            }

            if (widest < 0)
            {
                break;
            }

            List<(byte R, byte G, byte B)> box = boxes[widest];
            (_, int channel) = Extents(box);
            box.Sort((a, b) => Channel(a, channel).CompareTo(Channel(b, channel)));
            int middle = box.Count / 2;
            boxes[widest] = box.GetRange(0, middle);
            boxes.Add(box.GetRange(middle, box.Count - middle));
        }

        var result = new List<(byte R, byte G, byte B)>(boxes.Count);
        foreach (List<(byte R, byte G, byte B)> box in boxes)
        {
            long r = 0, g = 0, b = 0;
            foreach ((byte R, byte G, byte B) pixel in box)
            {
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
            }

            int n = Math.Max(1, box.Count);
            result.Add(((byte)(r / n), (byte)(g / n), (byte)(b / n)));
        }

        return result;
    }

    private static (int Range, int Channel) Extents(List<(byte R, byte G, byte B)> box)
    {
        int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
        foreach ((byte R, byte G, byte B) pixel in box)
        {
            minR = Math.Min(minR, pixel.R);
            maxR = Math.Max(maxR, pixel.R);
            minG = Math.Min(minG, pixel.G);
            maxG = Math.Max(maxG, pixel.G);
            minB = Math.Min(minB, pixel.B);
            maxB = Math.Max(maxB, pixel.B);
        }

        int dr = maxR - minR;
        int dg = maxG - minG;
        int db = maxB - minB;
        int range = Math.Max(dr, Math.Max(dg, db));
        int channel = range == dr ? 0 : range == dg ? 1 : 2;
        return (range, channel);
    }

    private static byte Channel((byte R, byte G, byte B) pixel, int channel) =>
        channel switch { 0 => pixel.R, 1 => pixel.G, _ => pixel.B };
}
