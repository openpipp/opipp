namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;

/// <summary>How selected science frames are combined into one final image.</summary>
public enum IntegrationMethod
{
    /// <summary>Arithmetic mean. Precision is carried in <see cref="IntegrationResult.PrecisionSamples"/>.</summary>
    Mean = 0,

    /// <summary>Sum with clamping at the output depth.</summary>
    Sum = 1,

    /// <summary>Per-pixel median. Exact, with a disk-backed path above the in-memory cap.</summary>
    Median = 2,
}

/// <summary>What happens to pixels some frames cannot supply after registration.</summary>
public enum CoveragePolicy
{
    /// <summary>Output covers only the region every contributor can fill. No synthetic border pixels.</summary>
    CropToOverlap = 0,

    /// <summary>Output keeps the first frame's canvas; pixels with no contributor are excluded from
    /// the image (written as the fill sample) and reported through <see cref="IntegrationResult.Coverage"/>.</summary>
    KeepCanvas = 1,
}

/// <summary>Options for one final-image integration.</summary>
public sealed class IntegrationOptions
{
    /// <summary>Combination method.</summary>
    public IntegrationMethod Method { get; set; } = IntegrationMethod.Mean;

    /// <summary>Border/coverage policy.</summary>
    public CoveragePolicy Coverage { get; set; } = CoveragePolicy.CropToOverlap;

    /// <summary>
    /// Apply the fractional part of each alignment offset with bilinear resampling. When false only
    /// whole-pixel shifts are applied, matching pre-0.2 behaviour.
    /// </summary>
    public bool Subpixel { get; set; } = true;

    /// <summary>Frames the in-memory median may retain before the disk-backed path is required.</summary>
    public int MaxRetainedMedianFrames { get; set; } = 64;

    /// <summary>Scratch budget for the disk-backed median path.</summary>
    public long MedianScratchBudgetBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Rows per tile in the disk-backed median path.</summary>
    public int MedianBandRows { get; set; } = 32;
}

/// <summary>One frame's contribution to an integration, in delivery order.</summary>
public readonly record struct IntegrationContributor(string SourceName, double Weight, AlignmentOffset Shift);

/// <summary>The result of integrating a selection.</summary>
public sealed class IntegrationResult
{
    /// <summary>Quantised output frame at the input depth.</summary>
    public required Frame Frame { get; init; }

    /// <summary>Number of contributing frames per output pixel, row-major.</summary>
    public required int[] Coverage { get; init; }

    /// <summary>Contributors in delivery order, with their weights and applied shifts.</summary>
    public required IReadOnlyList<IntegrationContributor> Contributors { get; init; }

    /// <summary>Method used.</summary>
    public required IntegrationMethod Method { get; init; }

    /// <summary>Region of the first frame's canvas that the output covers.</summary>
    public required PixelRect OutputRect { get; init; }

    /// <summary>
    /// Unquantised samples in the output depth's scale, row-major, when the method produces them
    /// (mean and sum). This is the documented high-precision intermediate: a mean of 100 and 101 is
    /// 100.5 here even though the quantised frame holds one integer.
    /// </summary>
    public double[]? PrecisionSamples { get; init; }
}
