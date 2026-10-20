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

/// <summary>Which median implementation produced a result.</summary>
/// <remarks>
/// Median has three routes because the selection decides how much sample data has to be resident.
/// The route is reported on <see cref="IntegrationResult.MedianRoute"/> so a caller (or a test) can
/// tell whether the run stayed in memory, spilled to scratch, or re-read its own source, without
/// inferring it from timing or from a temp file appearing.
/// </remarks>
public enum MedianRoute
{
    /// <summary>The integration was not a median.</summary>
    NotApplicable = 0,

    /// <summary>The selection was retained in managed memory (one sample copy per frame) and medianed
    /// band by band. Chosen when the frame count and estimated bytes both fit the declared budgets.</summary>
    InMemory = 1,

    /// <summary>Every frame was spilled to a scratch <see cref="Opipp.Core.Frames.FrameStore"/> and
    /// re-read per band, so peak managed memory does not grow with the selection.</summary>
    Tiled = 2,

    /// <summary>The caller's <c>readFrame</c> source was re-read once per band: nothing was retained and
    /// nothing was spilled. Only produced by the streaming overload.</summary>
    Streamed = 3,
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

    /// <summary>
    /// Frames the in-memory median route may retain. At or below this count (and inside the in-memory
    /// byte budget) a median runs in memory; above it the exact disk-backed route is used, because a
    /// selection that large must not be held resident.
    /// </summary>
    public int MaxRetainedMedianFrames { get; set; } = 64;

    /// <summary>Scratch budget for the disk-backed median path.</summary>
    public long MedianScratchBudgetBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// Managed-memory budget for the in-memory median route. Zero derives it from
    /// <see cref="MedianScratchBudgetBytes"/> — see
    /// <see cref="EffectiveMedianInMemoryBudgetBytes"/> for the documented rule.
    /// </summary>
    public long MedianInMemoryBudgetBytes { get; set; }

    /// <summary>
    /// The in-memory median budget in force. <see cref="MedianInMemoryBudgetBytes"/> when it is
    /// positive; otherwise twice <see cref="MedianScratchBudgetBytes"/> (saturating at
    /// <see cref="long.MaxValue"/>).
    /// </summary>
    /// <remarks>
    /// The doubling is a deliberate policy, not an arithmetic accident: the in-memory route holds one
    /// sample copy of each frame and no scratch file, while the tiled route needs that same copy on
    /// disk <em>plus</em> its band table resident. With the defaults (512 MiB of scratch) the
    /// allowance is 1 GiB, which is enough for <see cref="MaxRetainedMedianFrames"/> Full-HD RGB frames
    /// (759 MiB), so the frame cap is what normally binds. The estimate compared against this budget is
    /// <c>frames x width x height x channels x 2</c> (the retained samples) plus
    /// <c>bandRows x width x channels x frames x 2</c> (the band table, upper-bounded with the full
    /// canvas width).
    /// </remarks>
    public long EffectiveMedianInMemoryBudgetBytes =>
        MedianInMemoryBudgetBytes > 0
            ? MedianInMemoryBudgetBytes
            : MedianScratchBudgetBytes > long.MaxValue / 2
                ? long.MaxValue
                : MedianScratchBudgetBytes * 2;

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

    /// <summary>
    /// Which median route ran, or <see cref="MedianRoute.NotApplicable"/> for mean/sum. Median has no
    /// other observable difference between routes — the arithmetic is shared — so this is the only way
    /// a caller can tell an in-memory run from a spilled one.
    /// </summary>
    public MedianRoute MedianRoute { get; init; } = MedianRoute.NotApplicable;

    /// <summary>
    /// Bytes the in-memory median route estimated for this selection before it was chosen
    /// (<see cref="IntegrationOptions.EffectiveMedianInMemoryBudgetBytes"/> is the limit). Zero for
    /// mean/sum and for the streaming route, which never estimates because it retains nothing.
    /// </summary>
    public long MedianEstimatedBytes { get; init; }
}
