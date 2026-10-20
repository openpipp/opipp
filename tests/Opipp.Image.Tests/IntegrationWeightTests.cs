namespace Opipp.Image.Tests;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;
using Opipp.Core.Integration;
using Xunit;

/// <summary>
/// Weight validation and accumulator overflow. Weights are relative, so a set that cannot be
/// accumulated must be refused with a diagnostic that says so — never divided into an infinity and
/// quantised into a plausible-looking pixel.
/// </summary>
public sealed class IntegrationWeightTests
{
    private static Frame Solid(ushort value, int bitDepth = 8)
    {
        var frame = new Frame(1, 1, 1, bitDepth);
        frame.Samples[0] = value;
        return frame;
    }

    private static IntegrationResult Run(
        IntegrationMethod method, double[] weights, params ushort[] samples)
    {
        Frame[] frames = samples.Select(value => Solid(value, bitDepth: 16)).ToArray();
        var shifts = new AlignmentOffset[frames.Length];
        Array.Fill(shifts, AlignmentOffset.Zero);

        return FinalImageIntegrator.Integrate(frames, shifts, weights,
            new IntegrationOptions { Method = method, Subpixel = false });
    }

    // ------------------------------------------------------------ overflow

    [Theory]
    [InlineData(IntegrationMethod.Mean)]
    [InlineData(IntegrationMethod.Sum)]
    public void AFiniteWeightThatOverflowsTheAccumulator_IsRejected(IntegrationMethod method)
    {
        // 1e308 is finite and legal to validate, but 1e308 * 200 is 2e310: the precision sample would be
        // +infinity and the quantised pixel 255, which looks like a real measurement.
        var error = Assert.Throws<ArgumentException>(() => Run(method, [1e308], 200));

        Assert.Contains("overflowed", error.Message);
        Assert.Contains("weights", error.Message);
    }

    [Theory]
    [InlineData(IntegrationMethod.Mean)]
    [InlineData(IntegrationMethod.Sum)]
    public void AFiniteWeightTotalThatOverflows_IsRejectedBeforeAccumulating(IntegrationMethod method)
    {
        var error = Assert.Throws<ArgumentException>(() => Run(method, [1e308, 1e308], 10, 20));

        Assert.Contains("finite", error.Message);
        Assert.Contains("weights", error.Message);
    }

    [Fact]
    public void AHugeButRepresentableWeight_StillProducesAnExactMean()
    {
        // 1e300 * 100 = 1e302, doubled and divided by 2e300: no overflow, and the mean is exact.
        IntegrationResult result = Run(IntegrationMethod.Mean, [1e300, 1e300], 100, 100);

        Assert.Equal(100.0, result.PrecisionSamples![0]);
        Assert.Equal(100, result.Frame.Samples[0]);
    }

    [Fact]
    public void AHugeWeightOnASingleFrame_StillMeansTheSample()
    {
        IntegrationResult result = Run(IntegrationMethod.Mean, [1e300], 137);

        Assert.Equal(137.0, result.PrecisionSamples![0]);
        Assert.Equal(137, result.Frame.Samples[0]);
    }

    // ------------------------------------------------------------ invalid values

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteWeightsAreRejectedForMeanAndMedian(double weight)
    {
        foreach (IntegrationMethod method in new[] { IntegrationMethod.Mean, IntegrationMethod.Median })
        {
            var error = Assert.Throws<ArgumentException>(() => FinalImageIntegrator.Integrate(
                [Solid(1), Solid(2)],
                [AlignmentOffset.Zero, AlignmentOffset.Zero],
                [weight, 1.0],
                new IntegrationOptions { Method = method }));

            Assert.Contains("finite", error.Message);
        }
    }

    [Fact]
    public void NegativeWeightsAreRejectedForMeanAndMedian()
    {
        foreach (IntegrationMethod method in new[] { IntegrationMethod.Mean, IntegrationMethod.Median })
        {
            var error = Assert.Throws<ArgumentException>(() => FinalImageIntegrator.Integrate(
                [Solid(1), Solid(2)],
                [AlignmentOffset.Zero, AlignmentOffset.Zero],
                [1.0, -0.5],
                new IntegrationOptions { Method = method }));

            Assert.Contains("non-negative", error.Message);
        }
    }

    [Fact]
    public void MedianAcceptsOnlyUniformWeights()
    {
        var error = Assert.Throws<NotSupportedException>(() => FinalImageIntegrator.Integrate(
            [Solid(1), Solid(2)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            [1.0, 1.5],
            new IntegrationOptions { Method = IntegrationMethod.Median }));

        Assert.Contains("non-uniform", error.Message);
    }

    [Fact]
    public void MedianAcceptsAbsentOrUniformWeights()
    {
        IntegrationResult absent = FinalImageIntegrator.Integrate(
            [Solid(10), Solid(20)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            null,
            new IntegrationOptions { Method = IntegrationMethod.Median });

        IntegrationResult uniform = FinalImageIntegrator.Integrate(
            [Solid(10), Solid(20)],
            [AlignmentOffset.Zero, AlignmentOffset.Zero],
            [1.0, 1.0],
            new IntegrationOptions { Method = IntegrationMethod.Median });

        Assert.Equal(absent.Frame.Samples, uniform.Frame.Samples);
        Assert.All(uniform.Contributors, contributor => Assert.Equal(1.0, contributor.Weight));
    }

    [Fact]
    public void AllZeroWeightsAreStillRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => Run(IntegrationMethod.Mean, [0, 0], 10, 20));
        Assert.Contains("positive", error.Message);
    }

    [Fact]
    public void ZeroWeightsOnSomeFramesAreAllowedAndExcluded()
    {
        IntegrationResult result = Run(IntegrationMethod.Mean, [0, 1], 10, 20);

        Assert.Equal(20.0, result.PrecisionSamples![0]);
        Assert.Equal(20, result.Frame.Samples[0]);
    }

    [Fact]
    public void StreamingMeanRejectsAnOverflowingWeight()
    {
        Frame Read(int index) => Solid(200, bitDepth: 16);

        var error = Assert.Throws<ArgumentException>(() => FinalImageIntegrator.Integrate(
            Read, 2, [AlignmentOffset.Zero, AlignmentOffset.Zero], [1e308, 1e308],
            new IntegrationOptions()));

        Assert.Contains("finite", error.Message);
    }
}
