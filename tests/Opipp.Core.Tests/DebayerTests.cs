using Opipp.Core.Frames;
using Opipp.Core.Processing;

namespace Opipp.Core.Tests;

public class DebayerTests
{
    private static Frame Mosaic(int width, int height, BayerPattern pattern = BayerPattern.RGGB) =>
        new(width, height, 1, 8) { Bayer = pattern };

    [Fact]
    public void GreenAtRedSite_TruncatesTheFourSampleAverage()
    {
        // Sum 7 over four green neighbours: the original truncates to 1, round-half-up would give 2.
        var frame = Mosaic(6, 6);
        frame.Samples[1 * 6 + 2] = 1; // north
        frame.Samples[2 * 6 + 1] = 2; // west
        frame.Samples[2 * 6 + 3] = 2; // east
        frame.Samples[3 * 6 + 2] = 2; // south

        var rgb = Debayer.ToColour(frame);

        int index = (2 * 6 + 2) * 3;
        Assert.Equal(1, rgb.Samples[index + 1]);
    }

    [Fact]
    public void BorderRing_AveragesSameColourSamplesInTheClippedWindow()
    {
        // The outer ring is recomputed from the mosaic, not bilinearly interpolated: at (0,0) the
        // two green samples in the clipped 3x3 are 30 and 60, so green is 45 (not 30/15).
        var frame = Mosaic(6, 6);
        frame.Samples[0 * 6 + 0] = 10; // R, own sample
        frame.Samples[0 * 6 + 1] = 30; // G
        frame.Samples[1 * 6 + 0] = 60; // G
        frame.Samples[1 * 6 + 1] = 20; // B

        var rgb = Debayer.ToColour(frame);

        Assert.Equal(10, rgb.Samples[0]);
        Assert.Equal(45, rgb.Samples[1]);
        Assert.Equal(20, rgb.Samples[2]);
    }

    [Fact]
    public void BorderGreenSite_IsRecomputedRatherThanCopied()
    {
        // dcraw's border pass also rewrites the pixel's own colour, so a border green site is the
        // mean of the green samples in its window: (5 + 60 + 0) / 3 = 21, not the measured 5.
        var frame = Mosaic(6, 6);
        frame.Samples[0 * 6 + 1] = 5;  // own green sample
        frame.Samples[1 * 6 + 0] = 60;
        frame.Samples[1 * 6 + 2] = 0;

        var rgb = Debayer.ToColour(frame);

        int index = (0 * 6 + 1) * 3;
        Assert.Equal(21, rgb.Samples[index + 1]);
    }

    [Theory]
    [InlineData(0, BayerPattern.GBRG)]
    [InlineData(1, BayerPattern.BGGR)]
    [InlineData(2, BayerPattern.RGGB)]
    [InlineData(3, BayerPattern.GRBG)]
    public void FromPippIndex_UsesTheEnginesDocumentedOrder(int index, BayerPattern expected) =>
        Assert.Equal(expected, Debayer.FromPippIndex(index));
}
