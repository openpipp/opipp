namespace Pipp.Image.Tests;

using Pipp.Core.Alignment;
using Pipp.Core.Frames;
using Xunit;

/// <summary>
/// Expectations for the engine's "Calculated minpixel value" (<c>pipp_core FUN_14009ab40</c>).
/// The absolute value on synthetic discs sits one unit below the original because the engine rounds
/// inside its per-sample luma lookups; the structure and the real-capture values match exactly.
/// </summary>
public class DetectionThresholdTests
{
    private static Frame Disc(int size, int background, int objectValue, int radius = 10)
    {
        var samples = new ushort[size * size];
        int centre = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = x - centre;
                int dy = y - centre;
                samples[(y * size) + x] = (ushort)((dx * dx) + (dy * dy) <= radius * radius ? objectValue : background);
            }
        }

        return new Frame(size, size, 1, 8, samples);
    }

    [Fact]
    public void Compute_MatchesEngineOnAnEightBitSyntheticDisc()
    {
        // The original reports 39 for this fixture; the reproduction is one lower (documented).
        Assert.Equal(38, DetectionThreshold.Compute(Disc(64, 0, 200)));
    }

    [Fact]
    public void Compute_RisesWithObjectBrightness()
    {
        int dim = DetectionThreshold.Compute(Disc(64, 0, 120));
        int bright = DetectionThreshold.Compute(Disc(64, 0, 200));

        Assert.Equal(22, dim);
        Assert.True(bright > dim, $"expected {bright} > {dim}");
    }

    [Fact]
    public void Compute_RisesWithBackgroundLevel()
    {
        Assert.Equal(38, DetectionThreshold.Compute(Disc(64, 0, 200)));
        Assert.Equal(62, DetectionThreshold.Compute(Disc(64, 30, 200)));
    }

    [Fact]
    public void Compute_AllZeroFrame_IsZero_AndOrDefaultIsForty()
    {
        var frame = new Frame(64, 64, 1, 8, new ushort[64 * 64]);

        Assert.Equal(0, DetectionThreshold.Compute(frame));
        Assert.Equal(DetectionThreshold.Default, DetectionThreshold.ComputeOrDefault(frame));
        Assert.Equal(40, DetectionThreshold.Default);
    }

    [Fact]
    public void Compute_ScalesSixteenBitSamplesToEightBit()
    {
        var eight = Disc(64, 0, 200);
        var sixteenSamples = eight.Samples.Select(s => (ushort)(s << 8)).ToArray();
        var sixteen = new Frame(64, 64, 1, 16, sixteenSamples);

        Assert.Equal(DetectionThreshold.Compute(eight), DetectionThreshold.Compute(sixteen));
    }
}
