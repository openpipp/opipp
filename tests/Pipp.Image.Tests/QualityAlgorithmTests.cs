namespace Pipp.Image.Tests;

using Pipp.Core.Alignment;
using Pipp.Core.Frames;
using Pipp.Core.Quality;
using Xunit;

/// <summary>
/// The recovered <c>-qold</c> (Ninox, algorithm 1) and <c>-qhisto</c> (Histogram, algorithm 2)
/// quality metrics of <c>pipp_core!FUN_1400af2d0</c>. Expected values are the engine's own quality
/// log rows measured on the fixtures below.
/// </summary>
public class QualityAlgorithmTests
{
    /// <summary>
    /// The fixture of <see cref="EngineQualityTests"/>: a 128x128 eight-bit mono ramp with a soft
    /// disc. Four identical copies, <c>-q=4 -qold</c>, log 3.768767 for every frame (threshold 68).
    /// </summary>
    private static Frame Fixture(int size = 128, double amplitude = 1.8)
    {
        var frame = new Frame(size, size, 1, 8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double radius = Math.Sqrt(((x - 64) * (x - 64)) + ((y - 64) * (y - 64)));
                double bump = Math.Max(0.0, 80.0 - radius) * amplitude;
                int value = (int)(6 + (((x * 2) + (y * 3)) % 21) + ((x * y) % 9) + bump);
                frame.Samples[(y * size) + x] = (ushort)Math.Min(255, value);
            }
        }
        return frame;
    }

    [Fact]
    public void Ninox_FourIdenticalFrames_ReproduceTheEnginesQualityLog()
    {
        var frame = Fixture();
        var ninox = new NinoxQuality();

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(3.768767, ninox.Score(frame, 68), 6);
        }
    }

    [Fact]
    public void Ninox_VaryingFrames_ReproduceTheEnginesValuesAndOrder()
    {
        // Four copies of the fixture with disc amplitudes 1.2, 1.8, 2.4 and 3.0. The engine logs
        // (in source order) 3.604447, 3.773431, 3.869594, 3.928318 and ranks 4, 3, 2, 1.
        var frames = new[]
        {
            Fixture(amplitude: 1.2),
            Fixture(amplitude: 1.8),
            Fixture(amplitude: 2.4),
            Fixture(amplitude: 3.0),
        };

        int threshold = DetectionThreshold.ComputeOrDefault(frames[0]);
        Assert.Equal(52, threshold);

        var ninox = new NinoxQuality();
        var scores = new double[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            scores[i] = ninox.Score(frames[i], threshold);
        }

        Assert.Equal(3.604447, scores[0], 6);
        Assert.Equal(3.773431, scores[1], 6);
        Assert.Equal(3.869594, scores[2], 6);
        Assert.Equal(3.928318, scores[3], 6);

        var ranked = QualityMetrics.Rank(scores);
        Assert.Equal([3, 2, 1, 0], ranked.Select(e => e.Index).ToArray());
    }

    [Fact]
    public void Ninox_Reset_ForgetsTheReusedMask()
    {
        var frame = Fixture();
        var ninox = new NinoxQuality();

        double first = ninox.Score(frame, 68);
        ninox.Score(frame, 68);
        ninox.Reset();

        Assert.Equal(first, ninox.Score(frame, 68), 6);
    }

    [Fact]
    public void Histogram_WithoutDetection_IsPositiveInfinity()
    {
        // pipp_core returns early for algorithm 2, before the box life-cycle, so the divisor is the
        // all-zero box; the engine prints 1.#INF00 for every row of a -qhisto run.
        QualityMetrics.ResetRunState();

        double score = QualityMetrics.Compute(Fixture(), QualityAlgorithm.Histogram);

        Assert.True(double.IsPositiveInfinity(score));
    }

    [Fact]
    public void Histogram_WithDetectedBox_DividesBright50ByTheBoxArea()
    {
        QualityMetrics.ResetRunState();
        try
        {
            // Exactly fifty saturated samples: the bright-50 sum is 50 * 255^2 = 3,251,250.
            var frame = new Frame(128, 128, 1, 8);
            for (int i = 0; i < 50; i++)
            {
                frame.Samples[i] = 255;
            }

            // The engine uses the raw finder box, unexpanded: (110-10) * (110-10) = 10,000.
            var box = new EngineQuality();
            box.SetDetectedBox(10, 10, 110, 110);

            double score = QualityMetrics.Compute(frame, QualityAlgorithm.Histogram);

            Assert.Equal(325.125, score, 9);
        }
        finally
        {
            QualityMetrics.ResetRunState();
        }
    }

    [Fact]
    public void Rank_EqualScores_PreserveSourceOrder()
    {
        // -qhisto scores every frame +inf, and the engine's log keeps source order (0% everywhere).
        var ranked = QualityMetrics.Rank([double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity]);

        Assert.Equal([0, 1, 2], ranked.Select(e => e.Index).ToArray());
        Assert.All(ranked, e => Assert.Equal(0.0, e.PercentOfBest));
    }
}
