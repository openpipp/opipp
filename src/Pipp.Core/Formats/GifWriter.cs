using System.Buffers.Binary;
using System.Text;
using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>The colour-quantisation algorithm used to build a GIF palette (<c>-gif_quant_type</c>).</summary>
public enum GifQuantisation
{
    /// <summary>A Kohonen self-organising map, the family the engine's "neural net" option names (0).</summary>
    NeuralNet = 0,

    /// <summary>Median-cut quantisation (1).</summary>
    MedianCut = 1,
}

/// <summary>Options controlling GIF output.</summary>
/// <param name="DelayCentiseconds">Per-frame delay in hundredths of a second (10 = 100 ms).</param>
/// <param name="Loop">When true, the animation loops (NETSCAPE2.0 extension).</param>
/// <param name="LoopCount">
/// NETSCAPE2.0 repeat count: 0 repeats forever, <c>n</c> plays the animation <c>n</c> extra times
/// (PIPP's <c>-anim_all_rep</c> writes its value here). Only used when <paramref name="Loop"/> is true.
/// </param>
/// <param name="ColorTableDepth">
/// Bits per palette index, 2..8; the global colour table holds 2^<paramref name="ColorTableDepth"/>
/// entries. PIPP's parser accepts 4..8 (<c>-gif_col_tbl_depth</c>) and its writer produces exactly that
/// many entries (oracle: 4 -> 16 entries, 6 -> 64, 8 -> 256).
/// </param>
/// <param name="Quantisation">Palette builder for colour frames (<c>-gif_quant_type</c>).</param>
/// <param name="BorderTolerance">
/// Unchanged-border tolerance (<c>-gif_border_tol</c>), -1 to disable. Carried for diagnostics only:
/// PIPP's writer never emits image blocks, so the engine value has no observable effect, and this
/// writer emits every frame in full. See the remarks on <see cref="GifWriter"/>.
/// </param>
/// <param name="TransparentPixelTolerance">
/// Transparent-pixel tolerance (<c>-gif_trns_pxl_tol</c>), -1 to disable. When enabled the engine
/// reserves palette entry 1 as black, an observable one-entry change the writer reproduces.
/// </param>
/// <param name="LossyCompression">
/// Lossy-compression strength (<c>-gif_lossy_comp</c>), 0 to disable. Carried for diagnostics only;
/// there is no lossy pre-quantisation stage in this writer.
/// </param>
public sealed record GifOptions(
    int DelayCentiseconds = 10,
    bool Loop = true,
    int LoopCount = 0,
    int ColorTableDepth = 8,
    GifQuantisation Quantisation = GifQuantisation.NeuralNet,
    int BorderTolerance = 5,
    int TransparentPixelTolerance = 5,
    int LossyCompression = 0)
{
    /// <summary>Default options: 100 ms per frame, looping, 256-entry median-cut/neural palette.</summary>
    public static GifOptions Default { get; } = new();

    /// <summary>Number of palette entries implied by <see cref="ColorTableDepth"/>.</summary>
    public int PaletteEntries => 1 << ColorTableDepth;

    internal void Validate()
    {
        if (ColorTableDepth is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ColorTableDepth), ColorTableDepth, "GIF colour-table depth must be between 2 and 8.");
        }

        if (!Enum.IsDefined(Quantisation))
        {
            throw new ArgumentOutOfRangeException(nameof(Quantisation), Quantisation, "Unknown GIF quantisation.");
        }

        if (BorderTolerance < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(BorderTolerance), BorderTolerance, "GIF border tolerance must be -1 or greater.");
        }

        if (TransparentPixelTolerance < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TransparentPixelTolerance), TransparentPixelTolerance, "GIF transparent tolerance must be -1 or greater.");
        }

        if (LossyCompression < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LossyCompression), LossyCompression, "GIF lossy compression must be zero or greater.");
        }
    }
}

/// <summary>
/// Writes GIF89a files: a global colour table, optional looping, and one image block per frame.
/// </summary>
/// <remarks>
/// <para>
/// Monochrome frames map to a greyscale ramp of 2^depth entries, so an 8-bit mono frame is written
/// without any loss. Colour frames build a palette from a sample of all frames and are mapped to the
/// nearest entry. 16-bit samples are reduced to 8 bits by keeping the high byte, matching how the
/// engine converts to its 8-bit output formats.
/// </para>
/// <para>
/// The shipped engine's GIF writer is a stub: it writes the header, global colour table (mono only),
/// NETSCAPE loop extension, a comment and the trailer, but no image descriptors at all, so every
/// palette-related switch except <c>-gif_col_tbl_depth</c> and the entry-1 reservation behind
/// <c>-gif_trns_pxl_tol</c> is unobservable there. This writer deliberately emits a real animation
/// instead; <see cref="GifOptions.Quantisation"/> therefore selects a live quantiser rather than
/// reproducing an engine effect.
/// </para>
/// </remarks>
public static class GifWriter
{
    private const int PaletteBytesPerEntry = 3;

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
        options.Validate();

        if (frames.Any(f => f.Width != frames[0].Width || f.Height != frames[0].Height))
        {
            throw new ArgumentException("All frames must share the same dimensions.", nameof(frames));
        }

        int width = frames[0].Width;
        int height = frames[0].Height;
        int entries = options.PaletteEntries;
        bool colour = frames[0].Channels == 3;

        (byte[] palette, Func<Frame, byte[]> indexer) = colour
            ? BuildColourPalette(frames, entries, options.Quantisation)
            : (BuildGreyscalePalette(entries, reserveTransparentEntry: options.TransparentPixelTolerance >= 0),
               frame => IndexMono(frame, options.ColorTableDepth));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));

        // Logical screen descriptor: global colour table present, 8-bit colour resolution, size 2^depth.
        // The engine writes 0xF0 | (depth - 1); depth 8 gives the familiar 0xF7.
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)(0xF0 | (options.ColorTableDepth - 1)));
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write(palette);

        if (options.Loop && frames.Count > 1)
        {
            writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
            writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 0x03, 0x01 });
            writer.Write((ushort)Math.Clamp(options.LoopCount, 0, ushort.MaxValue));
            writer.Write((byte)0x00);
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
            writer.Write((byte)options.ColorTableDepth);
            writer.Write(GifLzw.Encode(indices, options.ColorTableDepth));
        }

        writer.Write((byte)0x3B);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Builds a greyscale ramp of <paramref name="entries"/> entries, stepped by
    /// <c>256 / entries</c> — for a 256-entry table that is 0,1,2,... and for a 16-entry table
    /// 0,16,32,...240 (oracle: <c>-gif_col_tbl_depth=4</c> writes entry 2 as 32, not 34).
    /// </summary>
    /// <param name="entries">Palette size, a power of two from 4 to 256.</param>
    /// <param name="reserveTransparentEntry">
    /// When true, entry 1 is forced to black. The engine does this whenever <c>-gif_trns_pxl_tol</c>
    /// is not -1 (oracle: with the switch at -1 entry 1 is (1,1,1) for a 256-entry table, otherwise
    /// (0,0,0)).
    /// </param>
    private static byte[] BuildGreyscalePalette(int entries, bool reserveTransparentEntry)
    {
        var table = new byte[entries * PaletteBytesPerEntry];
        int step = 256 / entries;
        for (int i = 0; i < entries; i++)
        {
            byte value = (byte)(i * step);
            table[(i * PaletteBytesPerEntry) + 0] = value;
            table[(i * PaletteBytesPerEntry) + 1] = value;
            table[(i * PaletteBytesPerEntry) + 2] = value;
        }

        if (reserveTransparentEntry && entries > 1)
        {
            table[PaletteBytesPerEntry + 0] = 0;
            table[PaletteBytesPerEntry + 1] = 0;
            table[PaletteBytesPerEntry + 2] = 0;
        }

        return table;
    }

    /// <summary>
    /// Maps samples onto the greyscale ramp: <c>sample &gt;&gt; (bitDepth - depth)</c>, so a 16-entry
    /// table uses indices 0..15 instead of 0..255. For depth 8 this is the identity.
    /// </summary>
    private static byte[] IndexMono(Frame frame, int depth)
    {
        var indices = new byte[frame.Width * frame.Height];
        int shift = (frame.BitDepth == 16 ? 8 : 0) + (8 - depth);
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (byte)(frame.Samples[i] >> shift);
        }

        return indices;
    }

    private static (byte[] Palette, Func<Frame, byte[]> Indexer) BuildColourPalette(
        IReadOnlyList<Frame> frames, int entries, GifQuantisation quantisation)
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

        List<(byte R, byte G, byte B)> colours = quantisation == GifQuantisation.MedianCut
            ? MedianCut(sample, entries)
            : SelfOrganisingMap(sample, entries);

        var table = new byte[entries * PaletteBytesPerEntry];
        for (int i = 0; i < colours.Count; i++)
        {
            table[(i * PaletteBytesPerEntry) + 0] = colours[i].R;
            table[(i * PaletteBytesPerEntry) + 1] = colours[i].G;
            table[(i * PaletteBytesPerEntry) + 2] = colours[i].B;
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

    private static int Luma((byte R, byte G, byte B) colour) =>
        (299 * colour.R) + (587 * colour.G) + (114 * colour.B);

    /// <summary>
    /// Kohonen self-organising-map quantisation: a square grid of neurons initialised along the
    /// luma-sorted samples, trained with a shrinking Gaussian neighbourhood, then refined with three
    /// Lloyd (nearest-mean) passes. Deterministic, so a given capture always yields the same palette.
    /// </summary>
    /// <remarks>
    /// This is the family the engine's <c>-gif_quant_type=0</c> ("neural net") names, but it is not
    /// bit-comparable with the engine: PIPP's writer never quantises anything because it emits no
    /// image blocks. The Lloyd pass keeps the palette competitive with median cut (mean absolute
    /// error on a saturated RGB gradient: ~5-7 levels for both).
    /// </remarks>
    private static List<(byte R, byte G, byte B)> SelfOrganisingMap(
        List<(byte R, byte G, byte B)> pixels, int count)
    {
        // ceil so the grid always has at least `count` neurons (e.g. 32 entries -> 6x6)
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        int gridCount = side * side;
        int stride = Math.Max(1, pixels.Count / 8192);
        var sample = new List<(byte R, byte G, byte B)>(pixels.Count / stride + 1);
        for (int i = 0; i < pixels.Count; i += stride)
        {
            sample.Add(pixels[i]);
        }

        if (sample.Count == 0)
        {
            sample.Add((0, 0, 0));
        }

        var sorted = sample
            .OrderBy(Luma)
            .ThenBy(p => p.R)
            .ThenBy(p => p.G)
            .ThenBy(p => p.B)
            .ToList();

        var neurons = new double[side, side, 3];
        for (int row = 0; row < side; row++)
        {
            for (int column = 0; column < side; column++)
            {
                // serpentine fill keeps neighbouring neurons close in colour
                int slot = row * side + (row % 2 == 0 ? column : side - 1 - column);
                var seed = sorted[(int)((long)slot * (sorted.Count - 1) / Math.Max(1, gridCount - 1))];
                neurons[row, column, 0] = seed.R;
                neurons[row, column, 1] = seed.G;
                neurons[row, column, 2] = seed.B;
            }
        }

        double learningRate = 0.4;
        double radius = Math.Max(1.0, side / 4.0);

        for (int epoch = 0; epoch < 6; epoch++)
        {
            for (int p = 0; p < sorted.Count; p += Math.Max(1, stride / 2))
            {
                var pixel = sorted[p];
                int bestRow = 0, bestColumn = 0;
                double bestDistance = double.MaxValue;
                for (int row = 0; row < side; row++)
                {
                    for (int column = 0; column < side; column++)
                    {
                        double dr = neurons[row, column, 0] - pixel.R;
                        double dg = neurons[row, column, 1] - pixel.G;
                        double db = neurons[row, column, 2] - pixel.B;
                        double distance = (dr * dr) + (dg * dg) + (db * db);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestRow = row;
                            bestColumn = column;
                        }
                    }
                }

                int window = (int)Math.Ceiling(radius);
                int rowFrom = Math.Max(0, bestRow - window);
                int rowTo = Math.Min(side - 1, bestRow + window);
                int columnFrom = Math.Max(0, bestColumn - window);
                int columnTo = Math.Min(side - 1, bestColumn + window);
                double denominator = (2.0 * radius * radius) + 1e-9;
                for (int row = rowFrom; row <= rowTo; row++)
                {
                    for (int column = columnFrom; column <= columnTo; column++)
                    {
                        double distance = ((row - bestRow) * (row - bestRow)) + ((column - bestColumn) * (column - bestColumn));
                        double influence = Math.Exp(-distance / denominator);
                        double rate = learningRate * influence;
                        neurons[row, column, 0] += rate * (pixel.R - neurons[row, column, 0]);
                        neurons[row, column, 1] += rate * (pixel.G - neurons[row, column, 1]);
                        neurons[row, column, 2] += rate * (pixel.B - neurons[row, column, 2]);
                    }
                }
            }

            learningRate *= 0.65;
            radius = Math.Max(1.0, radius * 0.65);
        }

        var palette = new double[gridCount, 3];
        for (int row = 0; row < side; row++)
        {
            for (int column = 0; column < side; column++)
            {
                int index = (row * side) + column;
                palette[index, 0] = neurons[row, column, 0];
                palette[index, 1] = neurons[row, column, 1];
                palette[index, 2] = neurons[row, column, 2];
            }
        }

        // Lloyd refinement: reassign samples to the nearest neuron and move it to their mean.
        for (int pass = 0; pass < 3; pass++)
        {
            var sums = new double[gridCount, 3];
            var counts = new int[gridCount];
            for (int p = 0; p < sorted.Count; p++)
            {
                int best = 0;
                double bestDistance = double.MaxValue;
                for (int i = 0; i < gridCount; i++)
                {
                    double dr = palette[i, 0] - sorted[p].R;
                    double dg = palette[i, 1] - sorted[p].G;
                    double db = palette[i, 2] - sorted[p].B;
                    double distance = (dr * dr) + (dg * dg) + (db * db);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                sums[best, 0] += sorted[p].R;
                sums[best, 1] += sorted[p].G;
                sums[best, 2] += sorted[p].B;
                counts[best]++;
            }

            for (int i = 0; i < gridCount; i++)
            {
                if (counts[i] == 0) continue;
                palette[i, 0] = sums[i, 0] / counts[i];
                palette[i, 1] = sums[i, 1] / counts[i];
                palette[i, 2] = sums[i, 2] / counts[i];
            }
        }

        var result = new List<(byte R, byte G, byte B)>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add((
                (byte)Math.Clamp(Math.Round(palette[i, 0]), 0, 255),
                (byte)Math.Clamp(Math.Round(palette[i, 1]), 0, 255),
                (byte)Math.Clamp(Math.Round(palette[i, 2]), 0, 255)));
        }

        return result;
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
