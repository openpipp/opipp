namespace Opipp.Image.Tests;

using System.Text;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Xunit;

/// <summary>
/// GIF output tests. The LZW round-trip is checked with an independent decoder written here, so the
/// encoder cannot pass by agreeing with itself.
/// </summary>
public class GifTests
{
    private static Frame Mono(int width, int height, params int[] samples) =>
        new(width, height, 1, 8, samples.Select(v => (ushort)v).ToArray());

    private static Frame Rgb(int width, int height, params int[] samples) =>
        new(width, height, 3, 8, samples.Select(v => (ushort)v).ToArray());

    [Fact]
    public void Encode_WritesGif89aHeaderAndTrailer()
    {
        byte[] gif = GifWriter.Encode([Mono(2, 2, 0, 64, 128, 255)]);

        Assert.Equal("GIF89a", Encoding.ASCII.GetString(gif, 0, 6));
        Assert.Equal(0x3B, gif[^1]);
    }

    [Fact]
    public void Encode_WritesLogicalScreenDimensions()
    {
        byte[] gif = GifWriter.Encode([Mono(7, 5, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34)]);

        Assert.Equal(7, gif[6] | (gif[7] << 8));
        Assert.Equal(5, gif[8] | (gif[9] << 8));
    }

    [Fact]
    public void Encode_MonoPaletteIsGreyscaleRamp()
    {
        byte[] gif = GifWriter.Encode([Mono(1, 1, 200)]);

        // Global colour table starts after the 13-byte header; entry 200 must be (200,200,200).
        int table = 13;
        Assert.Equal(200, gif[table + (200 * 3)]);
        Assert.Equal(200, gif[table + (200 * 3) + 1]);
        Assert.Equal(200, gif[table + (200 * 3) + 2]);
    }

    [Fact]
    public void Encode_Animated_ContainsLoopExtensionAndTwoFrames()
    {
        byte[] gif = GifWriter.Encode(
        [
            Mono(2, 2, 1, 2, 3, 4),
            Mono(2, 2, 5, 6, 7, 8),
        ]);

        Assert.Contains("NETSCAPE2.0", Encoding.ASCII.GetString(gif));
        Assert.Equal(2, CountImageDescriptors(gif));
    }

    [Fact]
    public void Encode_SingleFrame_HasNoLoopExtension()
    {
        byte[] gif = GifWriter.Encode([Mono(2, 2, 1, 2, 3, 4)]);

        Assert.DoesNotContain("NETSCAPE2.0", Encoding.ASCII.GetString(gif));
        Assert.Equal(1, CountImageDescriptors(gif));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 255, 0, 128, 64 })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })]
    public void Lzw_RoundTripsPixels(byte[] pixels)
    {
        byte[] encoded = GifLzwAccess.Encode(pixels, 8);
        byte[] decoded = TestGifDecoder.DecodeSubBlocks(encoded, 8);

        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Lzw_RoundTripsALongRepeatingSequence()
    {
        var pixels = new byte[5000];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 37);
        }

        byte[] decoded = TestGifDecoder.DecodeSubBlocks(GifLzwAccess.Encode(pixels, 8), 8);

        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Encode_Colour_UsesNearestPaletteEntries()
    {
        byte[] gif = GifWriter.Encode([Rgb(2, 1, 255, 0, 0, 0, 255, 0)]);
        (byte[] indices, _) = TestGifDecoder.DecodeFirstImage(gif);

        Assert.NotEqual(indices[0], indices[1]);
    }

    [Fact]
    public void Encode_RejectsMixedDimensions()
    {
        Assert.Throws<ArgumentException>(() => GifWriter.Encode([Mono(2, 2, 1, 2, 3, 4), Mono(3, 2, 1, 2, 3, 4, 5, 6)]));
    }

    private static int CountImageDescriptors(byte[] gif)
    {
        int count = 0;
        for (int i = 0; i < gif.Length - 9; i++)
        {
            // Image descriptor: 0x2C followed by a zero left/top origin.
            if (gif[i] == 0x2C && gif[i + 1] == 0 && gif[i + 2] == 0 && gif[i + 3] == 0 && gif[i + 4] == 0)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void StreamingWrite_MatchesEncode_ForMonoAnimation()
    {
        Frame[] frames =
        [
            Mono(2, 2, 0, 64, 128, 255),
            Mono(2, 2, 16, 80, 144, 240),
            Mono(2, 2, 32, 96, 160, 224),
        ];
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "opipp-gif-" + Guid.NewGuid().ToString("n") + ".gif");
        try
        {
            byte[] buffered = GifWriter.Encode(frames);
            int written = GifWriter.Write(path, () => frames);

            Assert.Equal(frames.Length, written);
            Assert.Equal(buffered, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void StreamingWrite_SingleFrame_MatchesEncodeWithoutLoopExtension()
    {
        Frame[] frames = [Mono(2, 2, 0, 64, 128, 255)];
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "opipp-gif-" + Guid.NewGuid().ToString("n") + ".gif");
        try
        {
            byte[] buffered = GifWriter.Encode(frames);
            int written = GifWriter.Write(path, () => frames);

            Assert.Equal(1, written);
            Assert.Equal(buffered, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void StreamingWrite_MatchesEncode_ForColour()
    {
        Frame[] frames =
        [
            Rgb(2, 2, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255),
            Rgb(2, 2, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120),
        ];
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "opipp-gif-" + Guid.NewGuid().ToString("n") + ".gif");
        try
        {
            byte[] buffered = GifWriter.Encode(frames);
            int written = GifWriter.Write(path, () => frames);

            Assert.Equal(frames.Length, written);
            Assert.Equal(buffered, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Bounded palette sampling: a global reservoir cap, deterministic, shared by both encoders ----

    [Theory]
    [InlineData(GifQuantisation.NeuralNet)]
    [InlineData(GifQuantisation.MedianCut)]
    public void StreamingWrite_MatchesEncode_WhenCaptureExceedsPaletteSampleCap(GifQuantisation quantisation)
    {
        Frame[] frames = LargeColourCapture();
        long offered = (long)frames.Length * frames[0].Width * frames[0].Height;
        Assert.True(offered > GifWriter.MaxColourSamples, $"capture offers only {offered} samples");
        var options = new GifOptions(Quantisation: quantisation);

        string path = TempGifPath();
        try
        {
            byte[] buffered = GifWriter.Encode(frames, options);
            int written = GifWriter.Write(path, () => frames, options);

            Assert.Equal(frames.Length, written);
            Assert.Equal(buffered, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Encode_AboveCapColourCapture_KeepsThePinnedBytes()
    {
        // Recorded before the colour-lookup cache was bounded: 24 frames of LCG noise, 294,912
        // samples sampled, 262,144 retained. Quantiser is the default NeuralNet (depth 8); the
        // median-cut entry pins the same fixture through the other quantiser.
        Frame[] frames = LargeColourCapture();

        byte[] neural = GifWriter.Encode(frames);
        byte[] median = GifWriter.Encode(
            frames, new GifOptions(Quantisation: GifQuantisation.MedianCut));

        Assert.Equal(399_466, neural.Length);
        Assert.Equal("419B72A7A93B1A8FCBACD3D2A71CED68AB289D17C160431FF6EF43FBFA1AB1D5", Sha256(neural));
        Assert.Equal(402_909, median.Length);
        Assert.Equal("F0AF9213C5355B5A9A003A2A9A5EEF1AE173F8229D5D5DFFB5DC7FF8F2DEEF54", Sha256(median));
    }

    [Theory]
    [InlineData(GifQuantisation.NeuralNet)]
    [InlineData(GifQuantisation.MedianCut)]
    public void BuildColourPalette_LateDistinctiveColourInfluencesTheRetainedPalette(GifQuantisation quantisation)
    {
        Frame[] capture = EarlyAndLateColourCapture();

        (byte[] palette, _, int sampleCount) =
            GifWriter.BuildColourPalette(capture, 128, 96, 16, quantisation);

        // The reservoir was full, so only replacement sampling can explain a late colour surviving;
        // an implementation that stopped considering samples after the cap would keep red only.
        Assert.Equal(GifWriter.MaxColourSamples, sampleCount);
        Assert.Contains(Enumerable.Range(0, palette.Length / 3), i =>
            palette[(i * 3) + 0] > 200 && palette[(i * 3) + 1] < 60 && palette[(i * 3) + 2] < 60);
        Assert.Contains(Enumerable.Range(0, palette.Length / 3), i =>
            palette[(i * 3) + 0] < 60 && palette[(i * 3) + 1] < 60 && palette[(i * 3) + 2] > 200);
    }

    [Fact]
    public void ColourLookupCache_NeverExceedsItsBudgetForAColourRichCapture()
    {
        Frame frame = DistinctColourFrame(300, 300);
        int distinct = DistinctColours(frame);
        Assert.True(
            distinct > GifWriter.MaxColourCacheEntries,
            $"frame offers only {distinct} distinct colours");

        var cache = new ColourLookupCache();
        (_, Func<Frame, byte[]> indexer, _) = GifWriter.BuildColourPalette(
            [frame], frame.Width, frame.Height, 256, GifQuantisation.MedianCut, cache);

        byte[] first = indexer(frame);

        Assert.Equal(GifWriter.MaxColourCacheEntries, cache.Capacity);
        Assert.Equal(GifWriter.MaxColourCacheEntries, cache.Count);

        // Past the budget every lookup misses and recomputes; the second pass must still agree.
        byte[] second = indexer(frame);
        Assert.Equal(first, second);
        Assert.Equal(GifWriter.MaxColourCacheEntries, cache.Count);
    }

    [Fact]
    public void PaletteSampleReservoir_HoldsAtMostTheCapForAnyNumberOfSamples()
    {
        var reservoir = new PaletteSampleReservoir();
        int maxCount = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            reservoir.Add((byte)i, (byte)(i >> 8), (byte)(i * 7));
            maxCount = Math.Max(maxCount, reservoir.Count);
        }

        Assert.Equal(PaletteSampleReservoir.Capacity, maxCount);
        Assert.Equal(PaletteSampleReservoir.Capacity, reservoir.Count);
        Assert.Equal(1_000_000, reservoir.Seen);
    }

    [Fact]
    public void BuildColourPalette_RetainsAtMostTheCapAndStaysInsideTheTable()
    {
        (byte[] palette, _, int sampleCount) =
            GifWriter.BuildColourPalette(LargeColourCapture(), 128, 96, 256, GifQuantisation.NeuralNet);

        Assert.Equal(GifWriter.MaxColourSamples, sampleCount);
        Assert.Equal(256 * 3, palette.Length);
    }

    [Fact]
    public void Encode_SmallColourCapture_KeepsThePreCapPaletteAndBytes()
    {
        // Hashes recorded from the unbounded sampler before the cap was introduced; a capture below
        // the cap must keep sampling every colour, in traversal order, so the palette cannot move.
        Frame[] frames = SmallColourCapture();

        byte[] neural = GifWriter.Encode(frames);
        byte[] median = GifWriter.Encode(
            frames, new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.MedianCut));

        Assert.Equal(1069, neural.Length);
        Assert.Equal("FAFCD1404ABF773C8E3724136543331371AE3B8FB03F6EDD4E00D6DE9FD1B8E7", Sha256(neural));
        Assert.Equal("6A6CC2A2AF355DD96D21FCB3F05ABF854F11658C4E6486BE546FA5B323DF1CAF", Sha256(median));

        (_, _, int sampleCount) = GifWriter.BuildColourPalette(frames, 8, 8, 256, GifQuantisation.NeuralNet);
        Assert.Equal(3 * 8 * 8, sampleCount);
    }

    [Fact]
    public void Encode_ColourCaptureAboveTheCap_IsDeterministicAcrossRunsAndEncoders()
    {
        byte[] first = GifWriter.Encode(LargeColourCapture());
        byte[] second = GifWriter.Encode(LargeColourCapture());
        Assert.Equal(first, second);

        string firstPath = TempGifPath();
        string secondPath = TempGifPath();
        try
        {
            Assert.Equal(24, GifWriter.Write(firstPath, () => LargeColourCapture()));
            Assert.Equal(24, GifWriter.Write(secondPath, () => LargeColourCapture()));

            Assert.Equal(first, File.ReadAllBytes(firstPath));
            Assert.Equal(first, File.ReadAllBytes(secondPath));
        }
        finally
        {
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
        }
    }

    private static string TempGifPath() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opipp-gif-" + Guid.NewGuid().ToString("n") + ".gif");

    private static string Sha256(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));

    /// <summary>Three 8x8 colour frames: 192 samples, far below the palette cap.</summary>
    private static Frame[] SmallColourCapture()
    {
        var frames = new Frame[3];
        for (int f = 0; f < frames.Length; f++)
        {
            int[] samples = new int[8 * 8 * 3];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (f * 37 + i * 11) % 256;
            }

            frames[f] = Rgb(8, 8, samples);
        }

        return frames;
    }

    /// <summary>
    /// Twenty-four 128x96 colour frames. The first twenty are pure red and the last four are pure
    /// blue: 245,760 red samples fill most of the reservoir, then 32,768+ blue samples arrive after
    /// the 262,144 cap and can survive only through replacement sampling.
    /// </summary>
    private static Frame[] EarlyAndLateColourCapture()
    {
        const int width = 128;
        const int height = 96;
        var frames = new Frame[24];
        for (int f = 0; f < frames.Length; f++)
        {
            int[] samples = new int[width * height * 3];
            bool late = f >= 20;
            for (int i = 0; i < samples.Length; i += 3)
            {
                samples[i + 0] = late ? 0 : 255;
                samples[i + 1] = 0;
                samples[i + 2] = late ? 255 : 0;
            }

            frames[f] = Rgb(width, height, samples);
        }

        return frames;
    }

    /// <summary>One frame whose every pixel is a different 8-bit RGB colour (90,000 distinct).</summary>
    private static Frame DistinctColourFrame(int width, int height)
    {
        int[] samples = new int[width * height * 3];
        for (int p = 0; p < width * height; p++)
        {
            samples[(p * 3) + 0] = p & 0xFF;
            samples[(p * 3) + 1] = (p >> 8) & 0xFF;
            samples[(p * 3) + 2] = (p >> 16) & 0xFF;
        }

        return Rgb(width, height, samples);
    }

    private static int DistinctColours(Frame frame)
    {
        var seen = new HashSet<int>();
        for (int p = 0; p < frame.Width * frame.Height; p++)
        {
            int i = p * 3;
            seen.Add((frame.Samples[i] << 16) | (frame.Samples[i + 1] << 8) | frame.Samples[i + 2]);
        }

        return seen.Count;
    }

    /// <summary>
    /// Twenty-four 128x96 colour frames: 12,288 samples per frame, 294,912 in total, which is above
    /// <see cref="GifWriter.MaxColourSamples"/> and therefore exercises reservoir replacement.
    /// </summary>
    private static Frame[] LargeColourCapture()
    {
        const int width = 128;
        const int height = 96;
        var frames = new Frame[24];
        for (int f = 0; f < frames.Length; f++)
        {
            int[] samples = new int[width * height * 3];
            uint state = 0x9E3779B9u * (uint)(f + 1);
            for (int i = 0; i < samples.Length; i++)
            {
                state = (state * 1664525u) + 1013904223u;
                samples[i] = (int)(state >> 24);
            }

            frames[f] = Rgb(width, height, samples);
        }

        return frames;
    }
}

/// <summary>Exposes the internal LZW encoder to the tests without making it public API.</summary>
internal static class GifLzwAccess
{
    public static byte[] Encode(byte[] indices, int minCodeSize) => GifLzw.Encode(indices, minCodeSize);
}

/// <summary>Minimal independent GIF reader: extracts the first image's indices and decodes LZW.</summary>
internal static class TestGifDecoder
{
    public static (byte[] Indices, int Width) DecodeFirstImage(byte[] gif)
    {
        int width = gif[6] | (gif[7] << 8);
        int height = gif[8] | (gif[9] << 8);
        int pos = 13;
        int packed = gif[10];
        if ((packed & 0x80) != 0)
        {
            int entries = 2 << (packed & 0x07);
            pos += entries * 3;
        }

        while (pos < gif.Length)
        {
            byte block = gif[pos++];
            if (block == 0x21)
            {
                pos++; // extension label
                pos = SkipSubBlocks(gif, pos);
            }
            else if (block == 0x2C)
            {
                pos += 9; // image descriptor remainder
                int minCodeSize = gif[pos++];
                int start = pos;
                pos = SkipSubBlocks(gif, pos);
                byte[] indices = DecodeSubBlocks(gif[start..pos], minCodeSize);
                Assert.Equal(width * height, indices.Length);
                return (indices, width);
            }
            else if (block == 0x3B)
            {
                break;
            }
            else
            {
                pos = SkipSubBlocks(gif, pos);
            }
        }

        throw new InvalidOperationException("No image block found.");
    }

    private static int SkipSubBlocks(byte[] data, int pos)
    {
        while (pos < data.Length)
        {
            int length = data[pos++];
            if (length == 0)
            {
                return pos;
            }

            pos += length;
        }

        return pos;
    }

    public static byte[] DecodeSubBlocks(byte[] blocks, int minCodeSize)
    {
        var packed = new List<byte>();
        int pos = 0;
        while (pos < blocks.Length)
        {
            int length = blocks[pos++];
            if (length == 0)
            {
                break;
            }

            packed.AddRange(blocks.AsSpan(pos, length).ToArray());
            pos += length;
        }

        return DecodeLzw(packed.ToArray(), minCodeSize);
    }

    private static byte[] DecodeLzw(byte[] packed, int minCodeSize)
    {
        int clear = 1 << minCodeSize;
        int end = clear + 1;
        int codeWidth = minCodeSize + 1;
        int next = end + 1;

        var dictionary = new List<byte[]>();
        for (int i = 0; i < clear; i++)
        {
            dictionary.Add([(byte)i]);
        }

        dictionary.Add([]); // clear
        dictionary.Add([]); // end

        var output = new List<byte>();
        byte[]? previous = null;
        int bitPos = 0;

        while (bitPos + codeWidth <= packed.Length * 8)
        {
            int code = ReadCode(packed, bitPos, codeWidth);
            bitPos += codeWidth;

            if (code == clear)
            {
                dictionary.RemoveRange(clear, dictionary.Count - clear);
                dictionary.Add([]);
                dictionary.Add([]);
                codeWidth = minCodeSize + 1;
                next = end + 1;
                previous = null;
                continue;
            }

            if (code == end)
            {
                break;
            }

            byte[] entry;
            if (code < dictionary.Count && dictionary[code].Length > 0)
            {
                entry = dictionary[code];
            }
            else if (code == next && previous is not null)
            {
                entry = [.. previous, previous[0]];
            }
            else
            {
                throw new InvalidOperationException($"Bad LZW code {code} (next {next}).");
            }

            output.AddRange(entry);

            if (previous is not null && next < 4096)
            {
                dictionary.Add([.. previous, entry[0]]);
                next++;
                if (next == (1 << codeWidth) && codeWidth < 12)
                {
                    codeWidth++;
                }
            }

            previous = entry;
        }

        return output.ToArray();
    }

    private static int ReadCode(byte[] data, int bitPos, int width)
    {
        int value = 0;
        for (int i = 0; i < width; i++)
        {
            int bit = bitPos + i;
            int b = (data[bit >> 3] >> (bit & 7)) & 1;
            value |= b << i;
        }

        return value;
    }

}
