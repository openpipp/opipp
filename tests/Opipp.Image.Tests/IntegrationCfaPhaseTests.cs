namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Opipp.Core.Processing;

/// <summary>
/// R1: raw-mosaic integration must be phase-correct. Registration is a relocation of the 2x2 CFA
/// lattice, so the pattern that describes a contribution is the pattern its <em>placement</em> produces,
/// not the pattern in the input header, and a fractional placement cannot describe a CFA image at all.
/// </summary>
/// <remarks>
/// The expectations here are independent of <c>GeometricTransforms</c>: a mosaic is modelled as four
/// tile colours, the colour of an output sample is derived by mapping it back through the placement, and
/// the correct tag is found by brute-forcing the four patterns. A retagging table that disagreed with
/// its own geometry would fail these tests rather than confirm them.
/// </remarks>
public sealed class IntegrationCfaPhaseTests
{
    private const ushort Red = 100;
    private const ushort Green = 200;
    private const ushort Blue = 300;

    private static readonly BayerPattern[] Patterns =
        [BayerPattern.RGGB, BayerPattern.GRBG, BayerPattern.GBRG, BayerPattern.BGGR];

    /// <summary>Site colour at a pixel, with the lattice extended to negative coordinates.</summary>
    private static char ColourAt(BayerPattern pattern, int x, int y)
    {
        string tile = pattern switch
        {
            BayerPattern.RGGB => "RGGB",
            BayerPattern.GRBG => "GRBG",
            BayerPattern.GBRG => "GBRG",
            BayerPattern.BGGR => "BGGR",
            _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "not a mosaic"),
        };

        // i = 0 is (0,0), 1 is (1,0), 2 is (0,1), 3 is (1,1); & on a negative int follows two's
        // complement, so -1 & 1 == 1 and -2 & 1 == 0.
        return tile[(x & 1) + (2 * (y & 1))];
    }

    private static ushort SiteValue(char colour) => colour switch
    {
        'R' => Red,
        'G' => Green,
        'B' => Blue,
        _ => throw new ArgumentOutOfRangeException(nameof(colour)),
    };

    /// <summary>A 16-bit mosaic whose sample at each pixel encodes that pixel's colour site.</summary>
    private static Frame Mosaic(BayerPattern pattern, int width, int height)
    {
        var frame = new Frame(width, height, 1, 16) { Bayer = pattern };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Samples[(y * width) + x] = SiteValue(ColourAt(pattern, x, y));
            }
        }

        return frame;
    }

    /// <summary>
    /// The one pattern whose site colours agree with the source over every output pixel, given the
    /// output origin in canvas coordinates and the applied whole-pixel placement. Brute-forced.
    /// </summary>
    private static BayerPattern ExpectedTag(BayerPattern source, int dx, int dy, int originX, int originY)
    {
        foreach (BayerPattern candidate in Patterns)
        {
            bool matches = true;
            for (int y = 0; y < 4 && matches; y++)
            {
                for (int x = 0; x < 4 && matches; x++)
                {
                    matches = ColourAt(candidate, x, y) == ColourAt(source, originX + x - dx, originY + y - dy);
                }
            }

            if (matches)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("no mosaic pattern describes the registered output");
    }

    private static ushort At(Frame frame, int x, int y) => frame.Samples[(y * frame.Width) + x];

    private static IntegrationResult Mean(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        CoveragePolicy coverage = CoveragePolicy.CropToOverlap)
    {
        return FinalImageIntegrator.Integrate(
            frames,
            shifts,
            null,
            new IntegrationOptions { Method = IntegrationMethod.Mean, Coverage = coverage, Subpixel = true });
    }

    // ------------------------------------------------------------ the reported reproduction

    [Fact]
    public void OddColumnShift_OnIdenticalMosaics_IsRejectedInsteadOfBlendingSites()
    {
        // R1's exact repro: two identical RGGB frames, the second registered one column across. The
        // old path averaged a red site with a green one and tagged the result RGGB.
        var first = new Frame(4, 2, 1, 16, [100, 200, 100, 200, 200, 300, 200, 300])
        {
            Bayer = BayerPattern.RGGB,
        };
        var second = first.Clone();

        var error = Assert.Throws<BayerPhaseException>(() => Mean(
            [first, second],
            [AlignmentOffset.Zero, new AlignmentOffset(1, 0)]));

        Assert.Contains("RGGB", error.Message);
        Assert.Contains("GRBG", error.Message);
        Assert.Contains("effective pattern", error.Message);
    }

    [Fact]
    public void SingleFrame_NegativeOddShift_RetagsTheOutputInsteadOfKeepingTheInputHeader()
    {
        // A single RGGB frame moved one column left is a GRBG mosaic at its own origin: no pixels are
        // averaged, so this is legal, but the stale RGGB tag would have been wrong.
        var result = Mean([Mosaic(BayerPattern.RGGB, 5, 4)], [new AlignmentOffset(-1, 0)], CoveragePolicy.KeepCanvas);

        Assert.Equal(BayerPattern.GRBG, result.Frame.Bayer);
        Assert.Equal(BayerPattern.GRBG, ExpectedTag(BayerPattern.RGGB, -1, 0, 0, 0));

        // The written samples are the source lattice, not a blend: every covered pixel keeps a site value
        // and the one column no frame can supply stays the fill sentinel.
        for (int y = 0; y < result.Frame.Height; y++)
        {
            for (int x = 0; x < result.Frame.Width; x++)
            {
                if (result.Coverage[(y * 5) + x] == 0)
                {
                    Assert.Equal(FrameIntegrator.FillSample, At(result.Frame, x, y));
                    continue;
                }

                Assert.Contains(At(result.Frame, x, y), new ushort[] { Red, Green, Blue });
            }
        }
    }

    // ------------------------------------------------------------ single frame, every phase

    [Theory]
    [InlineData(BayerPattern.RGGB, 0, 0)]
    [InlineData(BayerPattern.RGGB, 1, 0)]
    [InlineData(BayerPattern.RGGB, -1, 0)]
    [InlineData(BayerPattern.RGGB, 2, 0)]
    [InlineData(BayerPattern.RGGB, -2, 0)]
    [InlineData(BayerPattern.RGGB, 0, 1)]
    [InlineData(BayerPattern.RGGB, 0, -1)]
    [InlineData(BayerPattern.RGGB, 0, 2)]
    [InlineData(BayerPattern.RGGB, 0, -2)]
    [InlineData(BayerPattern.GRBG, 1, 0)]
    [InlineData(BayerPattern.GRBG, -1, 0)]
    [InlineData(BayerPattern.GRBG, 0, 1)]
    [InlineData(BayerPattern.GRBG, 0, -1)]
    [InlineData(BayerPattern.GRBG, 2, 0)]
    [InlineData(BayerPattern.GBRG, 1, 0)]
    [InlineData(BayerPattern.GBRG, -1, 0)]
    [InlineData(BayerPattern.GBRG, 0, 1)]
    [InlineData(BayerPattern.GBRG, 0, -1)]
    [InlineData(BayerPattern.GBRG, 0, -2)]
    [InlineData(BayerPattern.BGGR, 1, 0)]
    [InlineData(BayerPattern.BGGR, -1, 0)]
    [InlineData(BayerPattern.BGGR, 0, 1)]
    [InlineData(BayerPattern.BGGR, 0, -1)]
    [InlineData(BayerPattern.BGGR, -2, 0)]
    public void SingleFrame_TagAndSamplesDescribeThePlacement(BayerPattern pattern, int dx, int dy)
    {
        Frame source = Mosaic(pattern, 6, 5);
        foreach (CoveragePolicy coverage in new[] { CoveragePolicy.CropToOverlap, CoveragePolicy.KeepCanvas })
        {
            IntegrationResult result = Mean([source], [new AlignmentOffset(dx, dy)], coverage);

            BayerPattern expectedTag = ExpectedTag(pattern, dx, dy, result.OutputRect.X, result.OutputRect.Y);
            Assert.Equal(expectedTag, result.Frame.Bayer);
            Assert.Equal(
                expectedTag,
                GeometricTransforms.TranslatePattern(
                    GeometricTransforms.TranslatePattern(pattern, dx, dy), -result.OutputRect.X, -result.OutputRect.Y));

            for (int y = 0; y < result.Frame.Height; y++)
            {
                for (int x = 0; x < result.Frame.Width; x++)
                {
                    int sourceX = result.OutputRect.X + x - dx;
                    int sourceY = result.OutputRect.Y + y - dy;
                    bool inside = sourceX >= 0 && sourceX < source.Width && sourceY >= 0 && sourceY < source.Height;

                    if (inside)
                    {
                        Assert.Equal(SiteValue(ColourAt(pattern, sourceX, sourceY)), At(result.Frame, x, y));
                        Assert.Equal(1, result.Coverage[((result.OutputRect.Y + y) * source.Width) + result.OutputRect.X + x]);
                    }
                    else
                    {
                        Assert.Equal(CoveragePolicy.KeepCanvas, coverage);
                        Assert.Equal(FrameIntegrator.FillSample, At(result.Frame, x, y));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    [InlineData(BayerPattern.BGGR)]
    public void SingleFrame_OddCropOrigin_RestoresThePhaseAtTheNewOrigin(BayerPattern pattern)
    {
        // CropToOverlap with an odd shift drops an odd number of leading columns, so the output's first
        // tile is the untranslated pattern again — the crop moves the phase back.
        IntegrationResult result = Mean([Mosaic(pattern, 6, 4)], [new AlignmentOffset(1, 0)]);

        Assert.Equal(1, result.OutputRect.X);
        Assert.Equal(pattern, result.Frame.Bayer);
    }

    [Theory]
    [InlineData(BayerPattern.RGGB, 1, 0)]
    [InlineData(BayerPattern.GRBG, -1, 0)]
    [InlineData(BayerPattern.GBRG, 0, 1)]
    [InlineData(BayerPattern.BGGR, 0, -1)]
    public void Median_RetagsASingleShiftedMosaicTheSameWay(BayerPattern pattern, int dx, int dy)
    {
        Frame source = Mosaic(pattern, 6, 4);

        IntegrationResult result = FinalImageIntegrator.Integrate(
            [source],
            [new AlignmentOffset(dx, dy)],
            null,
            new IntegrationOptions { Method = IntegrationMethod.Median, Coverage = CoveragePolicy.KeepCanvas });

        Assert.Equal(ExpectedTag(pattern, dx, dy, 0, 0), result.Frame.Bayer);
        Assert.Equal(Mean([source], [new AlignmentOffset(dx, dy)], CoveragePolicy.KeepCanvas).Frame.Bayer, result.Frame.Bayer);
    }

    // ------------------------------------------------------------ multi-frame phase agreement

    [Theory]
    [InlineData(BayerPattern.RGGB, 2, 0)]
    [InlineData(BayerPattern.RGGB, 0, -2)]
    [InlineData(BayerPattern.GRBG, 2, 0)]
    [InlineData(BayerPattern.GBRG, 0, 2)]
    [InlineData(BayerPattern.BGGR, -2, 0)]
    public void EvenShifts_KeepThePhaseAndCombineExactly(BayerPattern pattern, int dx, int dy)
    {
        Frame first = Mosaic(pattern, 8, 6);
        Frame second = Mosaic(pattern, 8, 6);

        IntegrationResult result = Mean([first, second], [AlignmentOffset.Zero, new AlignmentOffset(dx, dy)]);

        Assert.Equal(pattern, result.Frame.Bayer);
        Assert.All(result.Frame.Samples, sample =>
            Assert.Contains(sample, new ushort[] { Red, Green, Blue }));
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    [InlineData(BayerPattern.BGGR)]
    public void FramesTaggedAtThePhaseTheirShiftProduces_AreAccepted(BayerPattern pattern)
    {
        // Frame 1 is `pattern` unshifted; frame 2 is the same lattice one column across, which is a
        // different header tag but the *same* effective phase. Comparing effective patterns accepts it,
        // comparing headers (the old rule) would have rejected a correct selection.
        BayerPattern shifted = GeometricTransforms.TranslatePattern(pattern, 1, 0);
        Assert.NotEqual(pattern, shifted);

        Frame first = Mosaic(pattern, 8, 6);
        Frame second = Mosaic(shifted, 8, 6);

        foreach (CoveragePolicy coverage in new[] { CoveragePolicy.CropToOverlap, CoveragePolicy.KeepCanvas })
        {
            IntegrationResult result = Mean([first, second], [AlignmentOffset.Zero, new AlignmentOffset(1, 0)], coverage);

            // The tag still comes from the output origin: KeepCanvas keeps the canvas phase, while the
            // crop to the overlap starts one column in (an odd origin) and therefore reports GRBG.
            Assert.Equal(ExpectedTag(pattern, 0, 0, result.OutputRect.X, result.OutputRect.Y), result.Frame.Bayer);

            // Both frames describe the same physical pixel, so the mean is that pixel's own site value.
            for (int y = 0; y < result.Frame.Height; y++)
            {
                for (int x = 0; x < result.Frame.Width; x++)
                {
                    if (result.Coverage[((result.OutputRect.Y + y) * 8) + result.OutputRect.X + x] == 0)
                    {
                        Assert.Equal(FrameIntegrator.FillSample, At(result.Frame, x, y));
                        continue;
                    }

                    Assert.Contains(At(result.Frame, x, y), new ushort[] { Red, Green, Blue });
                }
            }
        }

        IntegrationResult canvas = Mean([first, second], [AlignmentOffset.Zero, new AlignmentOffset(1, 0)], CoveragePolicy.KeepCanvas);
        Assert.Equal(pattern, canvas.Frame.Bayer);
    }

    [Theory]
    [InlineData(BayerPattern.RGGB, 1, 0)]
    [InlineData(BayerPattern.RGGB, -1, 0)]
    [InlineData(BayerPattern.RGGB, 0, 1)]
    [InlineData(BayerPattern.RGGB, 0, -1)]
    [InlineData(BayerPattern.GRBG, 1, 0)]
    [InlineData(BayerPattern.GRBG, -1, 0)]
    [InlineData(BayerPattern.GRBG, 0, 1)]
    [InlineData(BayerPattern.GRBG, 0, -1)]
    [InlineData(BayerPattern.GBRG, 1, 0)]
    [InlineData(BayerPattern.GBRG, -1, 0)]
    [InlineData(BayerPattern.GBRG, 0, 1)]
    [InlineData(BayerPattern.GBRG, 0, -1)]
    [InlineData(BayerPattern.BGGR, 1, 0)]
    [InlineData(BayerPattern.BGGR, -1, 0)]
    [InlineData(BayerPattern.BGGR, 0, 1)]
    [InlineData(BayerPattern.BGGR, 0, -1)]
    public void OddShifts_OnSameTaggedMosaics_AreRejectedOnBothAxes(BayerPattern pattern, int dx, int dy)
    {
        Frame first = Mosaic(pattern, 8, 6);
        Frame second = Mosaic(pattern, 8, 6);

        var error = Assert.Throws<BayerPhaseException>(() => Mean(
            [first, second],
            [AlignmentOffset.Zero, new AlignmentOffset(dx, dy)]));

        Assert.Contains("colour sites", error.Message);
    }

    [Fact]
    public void DifferentHeadersAtDifferentPhases_AreStillRejected()
    {
        // RGGB and BGGR unshifted describe different lattices; no placement makes them agree.
        Frame first = Mosaic(BayerPattern.RGGB, 6, 4);
        Frame second = Mosaic(BayerPattern.BGGR, 6, 4);

        Assert.Throws<BayerPhaseException>(() => Mean([first, second], [AlignmentOffset.Zero, AlignmentOffset.Zero]));
    }

    [Fact]
    public void MosaicAndPlainMono_AreRejectedBecauseOnePlaneHasNoCfa()
    {
        Frame mosaic = Mosaic(BayerPattern.RGGB, 6, 4);
        var mono = new Frame(6, 4, 1, 16);
        Array.Fill(mono.Samples, (ushort)42);

        var error = Assert.Throws<BayerPhaseException>(() => Mean([mosaic, mono], [AlignmentOffset.Zero, AlignmentOffset.Zero]));
        Assert.Contains("plain", error.Message);

        Assert.Throws<BayerPhaseException>(() => Mean([mono, mosaic], [AlignmentOffset.Zero, AlignmentOffset.Zero]));
    }

    // ------------------------------------------------------------ fractional registration

    [Theory]
    [InlineData(IntegrationMethod.Mean)]
    [InlineData(IntegrationMethod.Sum)]
    [InlineData(IntegrationMethod.Median)]
    public void FractionalShiftOnARawMosaic_IsRejectedAndNamesTheWorkaround(IntegrationMethod method)
    {
        // Bilinear resampling reads four neighbours, which in a CFA image are different colours.
        Frame first = Mosaic(BayerPattern.RGGB, 8, 6);
        Frame second = Mosaic(BayerPattern.RGGB, 8, 6);

        var error = Assert.Throws<BayerPhaseException>(() => FinalImageIntegrator.Integrate(
            [first, second],
            [AlignmentOffset.Zero, new AlignmentOffset(0.5, 0)],
            null,
            new IntegrationOptions { Method = method, Subpixel = true }));

        Assert.Contains("fractional", error.Message);
        Assert.Contains("Debayer", error.Message);
        Assert.Contains("sub-pixel", error.Message);
    }

    [Fact]
    public void FractionalShiftOnARawMosaic_IsRejectedForEveryRouteAndAxis()
    {
        foreach (BayerPattern pattern in Patterns)
        {
            Frame source = Mosaic(pattern, 8, 6);
            foreach (AlignmentOffset shift in new[] { new AlignmentOffset(0.5, 0), new AlignmentOffset(0, -0.25) })
            {
                Assert.Throws<BayerPhaseException>(() => FrameIntegrator.Integrate(
                    [source], [shift], null, new IntegrationOptions { Subpixel = true }));
                Assert.Throws<BayerPhaseException>(() => FinalImageIntegrator.Integrate(
                    [source], [shift], null,
                    new IntegrationOptions { Method = IntegrationMethod.Median, Subpixel = true }));
                Assert.Throws<BayerPhaseException>(() => InMemoryMedianIntegrator.Integrate(
                    [source], [shift], new IntegrationOptions { Method = IntegrationMethod.Median, Subpixel = true }));
                Assert.Throws<BayerPhaseException>(() => TiledMedianIntegrator.Integrate(
                    [source], [shift], new IntegrationOptions { Method = IntegrationMethod.Median, Subpixel = true }));
            }
        }
    }

    [Fact]
    public void FractionalShiftOnPlainMono_IsStillResampled()
    {
        // The restriction is about CFA lattices, not about mono data: a plain frame keeps interpolating.
        var frame = new Frame(2, 1, 1, 8, [0, 100]);

        IntegrationResult result = FrameIntegrator.Integrate(
            [frame], [new AlignmentOffset(0.5, 0)], null, new IntegrationOptions { Subpixel = true });

        Assert.Equal(new PixelRect(1, 0, 1, 1), result.OutputRect);
        Assert.Equal(50.0, result.PrecisionSamples![1], 12);
    }

    [Fact]
    public void SubpixelOff_RoundsAMosaicOffsetAndKeepsThePhaseRule()
    {
        // 0.4 rounds to 0: no phase change. 1.4 rounds to 1: an odd placement, which a single frame may
        // take (the tag follows) but two frames at the same tag may not.
        Frame source = Mosaic(BayerPattern.RGGB, 6, 4);

        IntegrationResult unshifted = FrameIntegrator.Integrate(
            [source], [new AlignmentOffset(0.4, 0)], null,
            new IntegrationOptions { Subpixel = false, Coverage = CoveragePolicy.KeepCanvas });
        Assert.Equal(new PixelRect(0, 0, 6, 4), unshifted.OutputRect);
        Assert.Equal(BayerPattern.RGGB, unshifted.Frame.Bayer);

        IntegrationResult shifted = FrameIntegrator.Integrate(
            [source], [new AlignmentOffset(1.4, 0)], null,
            new IntegrationOptions { Subpixel = false, Coverage = CoveragePolicy.KeepCanvas });
        Assert.Equal(BayerPattern.GRBG, shifted.Frame.Bayer);
        Assert.Equal(ExpectedTag(BayerPattern.RGGB, 1, 0, 0, 0), shifted.Frame.Bayer);

        Assert.Throws<BayerPhaseException>(() => FrameIntegrator.Integrate(
            [source, source.Clone()],
            [new AlignmentOffset(0.4, 0), new AlignmentOffset(1.4, 0)],
            null,
            new IntegrationOptions { Subpixel = false }));
    }
}
