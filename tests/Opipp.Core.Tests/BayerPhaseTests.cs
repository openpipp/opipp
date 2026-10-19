using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Processing;

namespace Opipp.Core.Tests;

/// <summary>
/// The CFA phase rule for geometry: a mosaic is a 2x2 lattice, so the pattern a frame carries is
/// part of its geometry, not provenance, and every lattice-preserving stage must report the pattern
/// its output samples actually have. Sample values encode the site colour (R = 10, G = 20, B = 30)
/// so each expectation can be checked against the pixels themselves, not only against another copy
/// of the arithmetic.
/// </summary>
public class BayerPhaseTests
{
    private const ushort Red = 10;
    private const ushort Green = 20;
    private const ushort Blue = 30;

    private static readonly BayerPattern[] Patterns =
        [BayerPattern.RGGB, BayerPattern.GRBG, BayerPattern.GBRG, BayerPattern.BGGR];

    /// <summary>The 2x2 tile each pattern names, written out here independently of ProcessingUtil.</summary>
    private static (int C00, int C01, int C10, int C11) TileOf(BayerPattern pattern) => pattern switch
    {
        BayerPattern.RGGB => (0, 1, 1, 2),
        BayerPattern.GRBG => (1, 0, 2, 1),
        BayerPattern.GBRG => (1, 2, 0, 1),
        BayerPattern.BGGR => (2, 1, 1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "not a Bayer pattern"),
    };

    /// <summary>Colour (0 = R, 1 = G, 2 = B) the pattern claims for a pixel site.</summary>
    private static int ColourOf(BayerPattern pattern, int x, int y)
    {
        var (c00, c01, c10, c11) = TileOf(pattern);
        return (x & 1, y & 1) switch
        {
            (0, 0) => c00,
            (1, 0) => c01,
            (0, 1) => c10,
            _ => c11,
        };
    }

    private static ushort ValueOf(int colour) => colour switch { 0 => Red, 1 => Green, _ => Blue };

    private static int ColourOfValue(ushort sample) => sample switch
    {
        Red => 0,
        Green => 1,
        Blue => 2,
        _ => throw new InvalidOperationException($"sample {sample} does not encode a colour"),
    };

    /// <summary>A mosaic whose sample values say which colour each site carries.</summary>
    private static Frame Mosaic(int width, int height, BayerPattern pattern)
    {
        var frame = new Frame(width, height, 1, 16) { Bayer = pattern };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Samples[(y * width) + x] = ValueOf(ColourOf(pattern, x, y));
            }
        }

        return frame;
    }

    /// <summary>Every sample in the region must carry the colour the frame's marker claims for its site.</summary>
    private static void AssertMarkerAgreesWithSamples(Frame frame, int x0, int y0, int width, int height)
    {
        Assert.NotEqual(BayerPattern.None, frame.Bayer);
        for (int y = y0; y < y0 + height; y++)
        {
            for (int x = x0; x < x0 + width; x++)
            {
                Assert.Equal(ColourOf(frame.Bayer, x, y), ColourOfValue(frame.Samples[(y * frame.Width) + x]));
            }
        }
    }

    // ---------------------------------------------------------------- reflections

    [Fact]
    public void FlipHorizontal_EvenWidth_MovesRggbToGrbg()
    {
        var result = GeometricTransforms.FlipHorizontal(Mosaic(4, 4, BayerPattern.RGGB));

        // The old marker (RGGB) would claim red at (0,0); the pixel there is the former (3,0) green.
        Assert.Equal(BayerPattern.GRBG, result.Bayer);
        Assert.Equal(Green, result.Samples[0]);
        Assert.Equal(Red, result.Samples[1]);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 4);
    }

    [Fact]
    public void FlipHorizontal_OddWidth_KeepsThePattern()
    {
        var result = GeometricTransforms.FlipHorizontal(Mosaic(5, 4, BayerPattern.RGGB));

        Assert.Equal(BayerPattern.RGGB, result.Bayer);
        Assert.Equal(Red, result.Samples[0]);
        AssertMarkerAgreesWithSamples(result, 0, 0, 5, 4);
    }

    [Fact]
    public void FlipVertical_EvenHeight_MovesRggbToGbrg()
    {
        var result = GeometricTransforms.FlipVertical(Mosaic(4, 4, BayerPattern.RGGB));

        // (0,0) now holds the former (0,3): a green site, with blue beside it.
        Assert.Equal(BayerPattern.GBRG, result.Bayer);
        Assert.Equal(Green, result.Samples[0]);
        Assert.Equal(Blue, result.Samples[1]);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 4);
    }

    [Fact]
    public void FlipVertical_OddHeight_KeepsThePattern()
    {
        var result = GeometricTransforms.FlipVertical(Mosaic(4, 5, BayerPattern.RGGB));

        Assert.Equal(BayerPattern.RGGB, result.Bayer);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 5);
    }

    // ---------------------------------------------------------------- rotations

    [Fact]
    public void Rotate90Clockwise_EvenSquare_MovesRggbToGrbg()
    {
        var result = GeometricTransforms.Rotate90Clockwise(Mosaic(4, 4, BayerPattern.RGGB));

        Assert.Equal(BayerPattern.GRBG, result.Bayer);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 4);
    }

    [Fact]
    public void Rotate180_EvenSquare_MovesRggbToBggr()
    {
        var result = GeometricTransforms.Rotate180(Mosaic(4, 4, BayerPattern.RGGB));

        Assert.Equal(BayerPattern.BGGR, result.Bayer);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 4);
    }

    [Fact]
    public void Rotate270_EvenSquare_MovesRggbToGbrg()
    {
        var result = GeometricTransforms.Rotate270(Mosaic(4, 4, BayerPattern.RGGB));

        Assert.Equal(BayerPattern.GBRG, result.Bayer);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 4);
    }

    // ---------------------------------------------------------------- crops

    [Fact]
    public void Crop_OddOriginShiftsThePhaseInBothAxes()
    {
        // (1,1) of an RGGB mosaic is a blue site, so the cropped frame starts BGGR.
        var result = GeometricTransforms.Crop(Mosaic(6, 6, BayerPattern.RGGB), 1, 1, 4, 4);

        Assert.Equal(BayerPattern.BGGR, result.Bayer);
        AssertMarkerAgreesWithSamples(result, 0, 0, 4, 4);
    }

    [Fact]
    public void Crop_OddOriginInOneAxisShiftsOneAxis()
    {
        var xOnly = GeometricTransforms.Crop(Mosaic(6, 6, BayerPattern.RGGB), 1, 0, 4, 4);
        var yOnly = GeometricTransforms.Crop(Mosaic(6, 6, BayerPattern.RGGB), 0, 1, 4, 4);

        Assert.Equal(BayerPattern.GRBG, xOnly.Bayer);
        Assert.Equal(BayerPattern.GBRG, yOnly.Bayer);
        AssertMarkerAgreesWithSamples(xOnly, 0, 0, 4, 4);
        AssertMarkerAgreesWithSamples(yOnly, 0, 0, 4, 4);
    }

    [Fact]
    public void Crop_EvenOriginAndFullFrameKeepThePattern()
    {
        Assert.Equal(BayerPattern.RGGB, GeometricTransforms.Crop(Mosaic(6, 6, BayerPattern.RGGB), 2, 4, 4, 2).Bayer);
        Assert.Equal(BayerPattern.RGGB, GeometricTransforms.Crop(Mosaic(6, 6, BayerPattern.RGGB), 0, 0, 6, 6).Bayer);
    }

    // ---------------------------------------------------------------- the general rule

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    [InlineData(BayerPattern.BGGR)]
    public void EveryOrientationAndCrop_ReportsThePatternTheSamplesHave(BayerPattern pattern)
    {
        // Even sizes flip the phase; the odd-sized source exercises the other parity too.
        foreach (var source in new[] { Mosaic(4, 6, pattern), Mosaic(5, 7, pattern) })
        {
            Frame[] results =
            [
                GeometricTransforms.FlipHorizontal(source),
                GeometricTransforms.FlipVertical(source),
                GeometricTransforms.Rotate90Clockwise(source),
                GeometricTransforms.Rotate270(source),
                GeometricTransforms.Rotate180(source),
                GeometricTransforms.Crop(source, 1, 1, 3, 4),
                GeometricTransforms.Crop(source, 0, 1, 3, 4),
                GeometricTransforms.Crop(source, 1, 0, 3, 4),
            ];

            foreach (Frame result in results)
            {
                Assert.NotEqual(BayerPattern.None, result.Bayer);
                AssertMarkerAgreesWithSamples(result, 0, 0, result.Width, result.Height);
            }
        }
    }

    [Fact]
    public void Geometry_LeavesAnUnpatternedFrameUnpatterned()
    {
        var source = new Frame(4, 4, 1, 16);
        Assert.Equal(BayerPattern.None, GeometricTransforms.FlipHorizontal(source).Bayer);
        Assert.Equal(BayerPattern.None, GeometricTransforms.Rotate90Clockwise(source).Bayer);
        Assert.Equal(BayerPattern.None, GeometricTransforms.Crop(source, 1, 1, 2, 2).Bayer);
    }

    // ---------------------------------------------------------------- translation

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    [InlineData(BayerPattern.BGGR)]
    public void TranslatePattern_MatchesTheEquivalentGeometry(BayerPattern pattern)
    {
        Assert.Equal(pattern, GeometricTransforms.TranslatePattern(pattern, 0, 0));
        Assert.Equal(pattern, GeometricTransforms.TranslatePattern(pattern, 2, 4));

        // A one-pixel move is indistinguishable from a reflection across the same parity.
        Assert.Equal(
            GeometricTransforms.FlipHorizontal(Mosaic(4, 2, pattern)).Bayer,
            GeometricTransforms.TranslatePattern(pattern, 1, 0));
        Assert.Equal(
            GeometricTransforms.FlipVertical(Mosaic(2, 4, pattern)).Bayer,
            GeometricTransforms.TranslatePattern(pattern, 0, 1));
        Assert.Equal(
            GeometricTransforms.Rotate180(Mosaic(4, 4, pattern)).Bayer,
            GeometricTransforms.TranslatePattern(pattern, 1, 1));
    }

    [Fact]
    public void TranslatePattern_LeavesNoPatternWithoutOne() =>
        Assert.Equal(BayerPattern.None, GeometricTransforms.TranslatePattern(BayerPattern.None, 1, 0));

    [Fact]
    public void AlignmentShift_AgreesWithTheTranslatedPattern()
    {
        var source = Mosaic(6, 6, BayerPattern.RGGB);

        Frame shifted = FrameAligner.ShiftWithBackground(source, new AlignmentOffset(1, 0));
        // What the pipeline does after shifting: retag for the phase the copy actually has.
        shifted.Bayer = GeometricTransforms.TranslatePattern(source.Bayer, 1, 0);

        Assert.Equal(BayerPattern.GRBG, shifted.Bayer);
        // Column 0 is the background fill; every copied pixel must carry the pattern's colour.
        AssertMarkerAgreesWithSamples(shifted, 1, 0, 5, 6);
    }

    // ---------------------------------------------------------------- resampling stages

    [Fact]
    public void Binning_DropsThePatternBecauseABinnedFrameIsNoLongerAMosaic()
    {
        var binned = Binning.Apply(Mosaic(4, 4, BayerPattern.RGGB), 2, BinningMode.Average);

        Assert.Equal(BayerPattern.None, binned.Bayer);
    }

    [Fact]
    public void Binning_FactorOne_IsACopyAndKeepsThePattern()
    {
        var copy = Binning.Apply(Mosaic(4, 4, BayerPattern.BGGR), 1, BinningMode.Average);

        Assert.Equal(BayerPattern.BGGR, copy.Bayer);
    }

    [Fact]
    public void Resize_DropsThePattern_unlessItIsANoOp()
    {
        Assert.Equal(BayerPattern.None, Resize.Bilinear(Mosaic(4, 4, BayerPattern.RGGB), 2, 2).Bayer);
        Assert.Equal(BayerPattern.RGGB, Resize.Bilinear(Mosaic(4, 4, BayerPattern.RGGB), 4, 4).Bayer);
    }

    [Fact]
    public void Debayer_ProducesAFrameWithoutAPattern()
    {
        Frame rgb = Debayer.ToColour(Mosaic(4, 4, BayerPattern.RGGB));

        Assert.Equal(3, rgb.Channels);
        Assert.Equal(BayerPattern.None, rgb.Bayer);
    }
}
