using Opipp.Core.Alignment;
using Opipp.Core.Frames;

namespace Opipp.Core.Integration;

/// <summary>
/// The single entry point for final-image integration. Routes by method so callers (pipeline, CLI,
/// GUI, tests) never have to know which implementation handles a method.
/// </summary>
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
            if (weights is not null && weights.Any(weight => Math.Abs(weight - 1.0) > 1e-9))
            {
                throw new NotSupportedException(
                    "median integration does not support non-uniform frame weights; " +
                    "duplicate the frames you want to count twice, or use mean/sum.");
            }

            return TiledMedianIntegrator.Integrate(frames, shifts, options, cancellationToken);
        }

        return FrameIntegrator.Integrate(frames, shifts, weights, options, cancellationToken);
    }
}
