namespace Pipp.Image.Tests;

using System.Text;
using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Xunit;

/// <summary>
/// GIF encoder option tests. Expectations for the global colour table were taken from
/// <c>pipp_core.exe</c>, whose GIF writer is a stub that emits the header and palette but no image
/// blocks: <c>-gif_col_tbl_depth=4/5/6/7/8</c> produced 16/32/64/128/256 entries with a
/// <c>256/entries</c> greyscale step, and <c>-gif_trns_pxl_tol=-1</c> left palette entry 1 as (1,1,1)
/// where the enabled default forces it to black.
/// </summary>
public class GifOptionTests
{
    private static Frame Mono(int width, int height, params int[] samples) =>
        new(width, height, 1, 8, samples.Select(v => (ushort)v).ToArray());

    private static Frame Rgb(int width, int height, params int[] samples) =>
        new(width, height, 3, 8, samples.Select(v => (ushort)v).ToArray());

    private static int PaletteEntries(byte[] gif) => 2 << (gif[10] & 0x07);

    [Theory]
    [InlineData(4, 16, 0xF3)]
    [InlineData(5, 32, 0xF4)]
    [InlineData(6, 64, 0xF5)]
    [InlineData(7, 128, 0xF6)]
    [InlineData(8, 256, 0xF7)]
    public void ColourTableDepth_SizesTableAndWritesEnginePackedByte(int depth, int entries, int packed)
    {
        byte[] gif = GifWriter.Encode([Mono(1, 1, 200)], new GifOptions(ColorTableDepth: depth));

        Assert.Equal(packed, gif[10]);
        Assert.Equal(entries, PaletteEntries(gif));
        // 13-byte header + palette + the 8-byte graphic control extension that precedes the image.
        Assert.Equal(13 + (entries * 3) + 8, FindFirstImageDescriptor(gif));
    }

    [Fact]
    public void ColourTableDepth_UsesPowerOfTwoGreyscaleRamp()
    {
        // Oracle depth 4: 0,16,32,...240 with the reserved entry 1 forced to black.
        byte[] gif = GifWriter.Encode([Mono(1, 1, 240)], new GifOptions(ColorTableDepth: 4));
        int table = 13;

        Assert.Equal(0, gif[table + 0]);
        Assert.Equal(0, gif[table + 3]);          // entry 1 reserved as the transparent/background slot
        Assert.Equal(32, gif[table + (2 * 3)]);   // entry 2 = 2 * 16, not 2 * 255 / 15
        Assert.Equal(240, gif[table + (15 * 3)]);
    }

    [Fact]
    public void TransparentToleranceDisabled_LeavesEntryOneAtItsRampValue()
    {
        byte[] enabled = GifWriter.Encode([Mono(1, 1, 0)], new GifOptions(TransparentPixelTolerance: 5));
        byte[] disabled = GifWriter.Encode([Mono(1, 1, 0)], new GifOptions(TransparentPixelTolerance: -1));

        Assert.Equal(0, enabled[16]);   // palette entry 1, red component
        Assert.Equal(1, disabled[16]);  // untouched ramp value
    }

    [Fact]
    public void Depth4_MonoIndicesStayInsideTheTable()
    {
        byte[] gif = GifWriter.Encode([Mono(4, 1, 0, 80, 160, 255)], new GifOptions(ColorTableDepth: 4));
        (byte[] indices, _) = TestGifDecoder.DecodeFirstImage(gif);

        Assert.All(indices, index => Assert.InRange(index, 0, 15));
        Assert.Equal(new byte[] { 0, 5, 10, 15 }, indices); // 80 >> 4, 160 >> 4, 255 >> 4
    }

    [Fact]
    public void Quantisation_BothAlgorithmsProduceDeterministicDecodablePalettes()
    {
        var frame = Rgb(4, 1, 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128);

        byte[] medianA = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.MedianCut));
        byte[] medianB = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.MedianCut));
        byte[] neuralA = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.NeuralNet));
        byte[] neuralB = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.NeuralNet));

        Assert.Equal(medianA, medianB);
        Assert.Equal(neuralA, neuralB);
        Assert.Equal(16, PaletteEntries(neuralA));

        (byte[] indices, _) = TestGifDecoder.DecodeFirstImage(neuralA);
        Assert.Equal(4, indices.Length);
        Assert.All(indices, index => Assert.InRange(index, 0, 15));
    }

    [Fact]
    public void Quantisation_NeuralNetPaletteCoversTheSampledColours()
    {
        var frame = Rgb(4, 1, 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128);
        byte[] gif = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.NeuralNet));

        var palette = new List<(int R, int G, int B)>();
        for (int i = 0; i < 16; i++)
        {
            palette.Add((gif[13 + (i * 3)], gif[14 + (i * 3)], gif[15 + (i * 3)]));
        }

        Assert.Contains(palette, p => p.R > 200 && p.G < 60 && p.B < 60);
        Assert.Contains(palette, p => p.G > 200 && p.R < 60 && p.B < 60);
        Assert.Contains(palette, p => p.B > 200 && p.R < 60 && p.G < 60);
    }

    [Fact]
    public void Quantisation_SelectionChangesThePalette()
    {
        // Smooth RGB gradient: median cut and the self-organising map converge on different centroids,
        // so the switch has a real effect even though the engine cannot exhibit one (no image blocks).
        var samples = new int[8 * 8 * 3];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int i = ((y * 8) + x) * 3;
                samples[i] = x * 32;
                samples[i + 1] = y * 32;
                samples[i + 2] = (x + y) * 16;
            }
        }

        var frame = Rgb(8, 8, samples);
        byte[] median = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.MedianCut));
        byte[] neural = GifWriter.Encode([frame], new GifOptions(ColorTableDepth: 4, Quantisation: GifQuantisation.NeuralNet));

        Assert.NotEqual(Convert.ToHexString(median, 13, 16 * 3), Convert.ToHexString(neural, 13, 16 * 3));
    }

    [Fact]
    public void BorderAndLossyOptions_DoNotChangeTheStream()
    {
        // The engine never writes image blocks, so neither tolerance has an observable engine
        // effect; this writer emits full frames and likewise ignores them.
        var frame = Mono(2, 2, 1, 2, 3, 4);
        byte[] a = GifWriter.Encode([frame], new GifOptions(BorderTolerance: -1, LossyCompression: 0));
        byte[] b = GifWriter.Encode([frame], new GifOptions(BorderTolerance: 50, LossyCompression: 50));

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void ColourTableDepth_OutsideTwoToEight_IsRejected(int depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GifWriter.Encode([Mono(1, 1, 0)], new GifOptions(ColorTableDepth: depth)));
    }

    private static int FindFirstImageDescriptor(byte[] gif)
    {
        for (int i = 13; i < gif.Length - 9; i++)
        {
            if (gif[i] == 0x2C && gif[i + 1] == 0 && gif[i + 2] == 0 && gif[i + 3] == 0 && gif[i + 4] == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
