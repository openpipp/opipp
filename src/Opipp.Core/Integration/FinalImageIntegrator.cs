namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;

/// <summary>
/// The single entry point for final-image integration. Routes by method so callers (pipeline, CLI,
/// GUI, tests) never have to know which implementation handles a method.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mean and sum</b> go to <see cref="FrameIntegrator"/>, which accumulates in
/// <see cref="double"/> and exposes the unquantised result through
/// <see cref="IntegrationResult.PrecisionSamples"/>.
/// </para>
/// <para>
/// <b>Median</b> goes to <see cref="MedianIntegrator"/>, which routes by the rule documented there:
/// in memory when the selection is at or below <see cref="IntegrationOptions.MaxRetainedMedianFrames"/>
/// and its estimated bytes fit <see cref="IntegrationOptions.EffectiveMedianInMemoryBudgetBytes"/>, and
/// through the disk-backed <see cref="TiledMedianIntegrator"/> above either limit. Median accepts only
/// absent weights or uniform 1.0; the weighting is validated here so a facade-level caller gets the same
/// diagnostic whichever route runs.
/// </para>
/// <para>
/// <b>Streaming</b> overloads consume a <c>readFrame</c> source instead of a list. Mean/sum read each
/// index exactly once; median reads each index once to validate and once per band. Neither retains a
/// frame across a call, so a source may hand back a reused buffer.
/// </para>
/// </remarks>
public static class FinalImageIntegrator
{
    /// <summary>
    /// Integrates <paramref name="frames"/> into one final image.
    /// </summary>
    /// <param name="frames">Selected frames, in delivery order.</param>
    /// <param name="shifts">One alignment offset per frame; positive Dx moves content right.</param>
    /// <param name="weights">Optional per-frame weights; median supports only uniform weighting.</param>
    /// <param name="options">Method, coverage policy and precision options.</param>
    /// <param name="cancellationToken">Cancels the integration.</param>
    /// <exception cref="ArgumentException">No frames, mismatched lists, incompatible geometry, or an
    /// invalid weight.</exception>
    /// <exception cref="BayerPhaseException">The effective CFA phases differ, a raw mosaic frame needs a
    /// fractional shift, or a mosaic frame is mixed with a plain mono one.</exception>
    /// <exception cref="NotSupportedException">A median was requested with non-uniform weights.</exception>
    /// <exception cref="InvalidOperationException">The median route's budget cannot hold the
    /// selection.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IReadOnlyList<double>? weights,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Method == IntegrationMethod.Median)
        {
            IntegrationSupport.RequireUniformMedianWeights(weights, frames.Count);
            return MedianIntegrator.Integrate(frames, shifts, options, cancellationToken);
        }

        return FrameIntegrator.Integrate(frames, shifts, weights, options, cancellationToken);
    }

    /// <summary>
    /// Streaming overload: integrates <paramref name="frameCount"/> frames pulled one at a time from
    /// <paramref name="readFrame"/>, so the whole selection never has to be resident.
    /// </summary>
    /// <remarks>
    /// <para><b>Read contract.</b> Mean and sum request each index exactly once, in ascending order, and
    /// accumulate it before the next request. Median requests index 0 once to fix the selection, indices
    /// 1..n-1 once to validate them, and then every index once per output band. Nothing returned by
    /// <paramref name="readFrame"/> is held across a call, so a source may reuse one <see cref="Frame"/>
    /// or one sample buffer for every index. The returned frame is a new one owned by the integrator.</para>
    /// <para><b>Memory.</b> Mean/sum hold the output, the coverage map and the precision buffer; median
    /// holds one band table (bounded by <see cref="IntegrationOptions.MedianBandRows"/>), and nothing is
    /// spilled to disk on this path.</para>
    /// </remarks>
    /// <param name="readFrame">Supplies frame <c>i</c>; must not return null.</param>
    /// <param name="frameCount">Number of frames to pull.</param>
    /// <param name="shifts">One alignment offset per frame, index-aligned with the reads.</param>
    /// <param name="weights">Optional per-frame weights; median supports only uniform weighting.</param>
    /// <param name="options">Method, coverage policy and precision options.</param>
    /// <param name="cancellationToken">Cancels the integration.</param>
    /// <exception cref="ArgumentException">A mismatched list, an invalid weight, or an incompatible frame.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="readFrame"/> returned null, or a median
    /// band cannot fit the in-memory budget.</exception>
    /// <exception cref="BayerPhaseException">The effective CFA phases differ, or a raw mosaic frame needs
    /// a fractional shift.</exception>
    /// <exception cref="NotSupportedException">A median was requested with non-uniform weights.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        Func<int, Frame> readFrame,
        int frameCount,
        IReadOnlyList<AlignmentOffset> shifts,
        IReadOnlyList<double>? weights,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readFrame);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Method == IntegrationMethod.Median)
        {
            IntegrationSupport.RequireUniformMedianWeights(weights, frameCount);
            return MedianIntegrator.Integrate(readFrame, frameCount, shifts, options, cancellationToken);
        }

        return FrameIntegrator.Integrate(readFrame, frameCount, shifts, weights, options, cancellationToken);
    }
}
