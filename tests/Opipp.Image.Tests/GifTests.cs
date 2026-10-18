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
