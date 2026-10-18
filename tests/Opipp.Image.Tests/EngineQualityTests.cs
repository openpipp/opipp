namespace Opipp.Image.Tests;

using Opipp.Core.Frames;
using Opipp.Core.Quality;
using Xunit;

/// <summary>
/// The recovered PIPP Default quality metric (<c>pipp_core!FUN_1400af2d0</c>). The expected values
/// were measured on the shipped engine: a 128x128 eight-bit frame, four byte-identical copies,
/// <c>-q=4 -qmin=3 -qmax=5 -qinc=1</c>, with the engine's computed threshold of 68. The engine's log
/// reads 257.596259 for all four frames.
/// </summary>
public class EngineQualityTests
{
    private const int Threshold = 68;          // the engine's "Calculated minpixel value" for this fixture
    private const double Expected = 257.596259;

    /// <summary>The oracle fixture, reproduced sample for sample.</summary>
    private static Frame Fixture()
    {
        const int size = 128;
        var frame = new Frame(size, size, 1, 8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double radius = Math.Sqrt(((x - 64) * (x - 64)) + ((y - 64) * (y - 64)));
                double bump = Math.Max(0.0, 80.0 - radius) * 1.8;
                int value = (int)(6 + (((x * 2) + (y * 3)) % 21) + ((x * y) % 9) + bump);
                frame.Samples[(y * size) + x] = (ushort)Math.Min(255, value);
            }
        }
        return frame;
    }

    [Fact]
    public void FourIdenticalFrames_ReproduceTheEnginesQualityLog()
    {
        var frame = Fixture();
        var quality = new EngineQuality(qmin: 3, qmax: 5, qinc: 1);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(Expected, quality.Score(frame, Threshold), 6);
        }
    }

    [Fact]
    public void SingleStride_ScoresEveryCopyAlike()
    {
        // with qmin == qmax the mask holds only marks that the current pass rewrites, so repeated
        // identical frames are identical — the engine's table shows this for 3, 4 and 5
        var frame = Fixture();
        var quality = new EngineQuality(qmin: 3, qmax: 3, qinc: 1);

        double first = quality.Score(frame, Threshold);
        Assert.True(first > 0);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(first, quality.Score(frame, Threshold), 6);
        }
    }

    [Fact]
    public void Reset_ForgetsTheReusedMask()
    {
        var frame = Fixture();
        var quality = new EngineQuality(qmin: 3, qmax: 5, qinc: 1);

        double first = quality.Score(frame, Threshold);
        quality.Score(frame, Threshold);
        quality.Reset();

        Assert.Equal(first, quality.Score(frame, Threshold), 6);
    }

    [Fact]
    public void DetectedBox_ChangesTheMeasuredRegion()
    {
        var frame = Fixture();
        var whole = new EngineQuality(qmin: 3, qmax: 3, qinc: 1);
        var boxed = new EngineQuality(qmin: 3, qmax: 3, qinc: 1);

        double wholeScore = whole.Score(frame, Threshold);
        boxed.SetDetectedBox(40, 40, 90, 90);
        double boxScore = boxed.Score(frame, Threshold);

        Assert.True(boxScore > 0);
        Assert.NotEqual(wholeScore, boxScore);
    }

    [Fact]
    public void ClearDetectedBox_ReturnsToTheWholeFrame()
    {
        var frame = Fixture();
        var quality = new EngineQuality(qmin: 3, qmax: 3, qinc: 1);
        double wholeScore = quality.Score(frame, Threshold);

        quality.SetDetectedBox(40, 40, 90, 90);
        quality.ClearDetectedBox();

        Assert.True(quality.MeasuresWholeFrame);
        Assert.Equal(wholeScore, quality.Score(frame, Threshold), 6);
    }

    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(3, 5, 0)]
    public void InvalidStrideRange_IsRejected(int qmin, int qmax, int qinc)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EngineQuality(qmin, qmax, qinc));
    }
}
