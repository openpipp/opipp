namespace Pipp.Image.Tests;

using Pipp.Core.Alignment;
using Pipp.Core.Frames;
using Xunit;

/// <summary>
/// The engine's object finder (<c>pipp_core!FUN_140077560</c> / <c>FUN_140076ae0</c>), exercised on
/// the synthetic fixtures recovered from the binary: an 8-bit luma bar or disc, threshold 40 unless
/// stated. Every expectation here is one of the oracle-verified rows in <c>notes/detection.md</c>.
/// </summary>
public class PlanetDetectorTests
{
    private const int Size = 96;
    private const int Threshold = 40;

    /// <summary>A frame with a saturated rectangle; coordinates inclusive and top-down.</summary>
    private static Frame Bar(int x0, int x1, int y0, int y1, int value = 255, int bitDepth = 8)
    {
        var frame = new Frame(Size, Size, 1, bitDepth);
        int level = bitDepth == 8 ? value : value << 8;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                frame.Samples[(y * Size) + x] = (ushort)level;
            }
        }
        return frame;
    }

    private static Frame Disc(int radius)
    {
        var frame = new Frame(Size, Size, 1, 8);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int dx = x - 48, dy = y - 48;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    frame.Samples[(y * Size) + x] = 255;
                }
            }
        }
        return frame;
    }

    [Fact]
    public void VerticalBar_IsBoxedOnePixelLeftAndOnePixelRight()
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(40, 50, 20, 75), Threshold, 0);

        Assert.True(d.Found);
        Assert.Equal(39, d.Left);
        Assert.Equal(51, d.Right);
        Assert.Equal(12, d.Width);
        Assert.Equal(55, d.Height);
    }

    [Fact]
    public void HorizontalBar_IsElevenRowsTallMinusOne()
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(10, 85, 40, 50), Threshold, 0);

        Assert.True(d.Found);
        Assert.Equal(77, d.Width);
        Assert.Equal(10, d.Height);
    }

    [Fact]
    public void Disc_GrowsUntilTheChordFallsBelowTenPixels()
    {
        PlanetDetection d = PlanetDetector.Detect(Disc(15), Threshold, 0);

        Assert.True(d.Found);
        Assert.Equal(28, d.Height);          // 2*floor(sqrt(15^2-5^2))
    }

    [Theory]
    [InlineData(1, 59)]
    [InlineData(2, 58)]
    [InlineData(3, 57)]
    public void ObjectStartingAtRow_ReportsOneLessThanItsHeight(int top, int expectedHeight)
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, top, 60), Threshold, 0);

        Assert.True(d.Found);
        Assert.Equal(expectedHeight, d.Height);
    }

    [Fact]
    public void TallBar_IsTallerThanTheSeedScanWindow()
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 30, 93), Threshold, 0);

        Assert.True(d.Found);
        Assert.Equal(63, d.Height);
    }

    [Fact]
    public void ObjectTouchingTheTopEdge_IsReportedAsNoObject()
    {
        // The upward pass runs off the frame and keeps its sentinel, so the height check yields -1
        // rather than the -2 a horizontal border touch produces.
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 0, 60), Threshold, 0);

        Assert.Equal(PlanetDetectionStatus.NoObject, d.Status);
        Assert.False(d.Found);
    }

    [Fact]
    public void ObjectBelowTheSeedScanWindow_IsReportedAsNoObject()
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 91, 95), Threshold, 0);

        Assert.Equal(PlanetDetectionStatus.NoObject, d.Status);
    }

    [Fact]
    public void TwoRowObject_IsNoObjectBeforeItIsTooSmall()
    {
        // the height check runs before the minimum-size test; -ms never sees a two-row object
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 40, 41), Threshold, 1);

        Assert.Equal(PlanetDetectionStatus.NoObject, d.Status);
    }

    [Fact]
    public void ThreeRowObject_IsBelowTheMinimumSize()
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 40, 42), Threshold, 5);

        Assert.Equal(PlanetDetectionStatus.TooSmall, d.Status);
    }

    [Theory]
    [InlineData(2, PlanetDetectionStatus.PartialObject)]
    [InlineData(3, PlanetDetectionStatus.Found)]
    public void LeftBorder_IsPartialFromColumnTwo(int x0, PlanetDetectionStatus expected)
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(x0, 30, 40, 50), Threshold, 0);

        Assert.Equal(expected, d.Status);
    }

    [Theory]
    [InlineData(91, PlanetDetectionStatus.PartialObject)]
    [InlineData(90, PlanetDetectionStatus.Found)]
    public void RightBorder_IsPartialFromColumnNMinusFour(int x1, PlanetDetectionStatus expected)
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, x1, 40, 50), Threshold, 0);

        Assert.Equal(expected, d.Status);
    }

    [Theory]
    [InlineData(94, PlanetDetectionStatus.PartialObject)]
    [InlineData(93, PlanetDetectionStatus.Found)]
    public void BottomBorder_IsPartialFromColumnNMinusOne(int y1, PlanetDetectionStatus expected)
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 40, y1), Threshold, 0);

        Assert.Equal(expected, d.Status);
    }

    [Fact]
    public void FourTapMean_NeedsFourSaturatedSamplesAtAHighThreshold()
    {
        // at -mp = 200 a single saturated sample cannot fill the four-tap sum, so an 11-wide bar
        // produces no ten-column run at all
        PlanetDetection d = PlanetDetector.Detect(Bar(40, 50, 20, 75), 200, 0);

        Assert.Equal(PlanetDetectionStatus.NoObject, d.Status);
    }

    [Fact]
    public void SixteenBitFrame_TakesTheSamePathAsEightBit()
    {
        PlanetDetection eight = PlanetDetector.Detect(Bar(40, 50, 20, 75), Threshold, 0);
        PlanetDetection sixteen = PlanetDetector.Detect(Bar(40, 50, 20, 75, bitDepth: 16), Threshold, 0);

        Assert.True(sixteen.Found);
        Assert.Equal(eight.Left, sixteen.Left);
        Assert.Equal(eight.Top, sixteen.Top);
        Assert.Equal(eight.Right, sixteen.Right);
        Assert.Equal(eight.Bottom, sixteen.Bottom);
    }

    [Fact]
    public void FailedDetection_CarriesTheEnginesZeroedBox()
    {
        PlanetDetection d = PlanetDetector.Detect(Bar(20, 70, 0, 60), Threshold, 0);

        Assert.Equal(0, d.Left);
        Assert.Equal(0, d.Top);
        Assert.Equal(0, d.Right);
        Assert.Equal(0, d.Bottom);
    }

    [Fact]
    public void FramesOfHeightSix_NeverDetect()
    {
        var frame = new Frame(32, 6, 1, 8);
        for (int x = 0; x < 32; x++)
        {
            frame.Samples[(2 * 32) + x] = 255;
        }

        PlanetDetection d = PlanetDetector.Detect(frame, 10, 0);

        Assert.Equal(PlanetDetectionStatus.NoObject, d.Status);
    }
}
