namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;

/// <summary>
/// Exact per-pixel median over a selection small enough to keep resident: one sample copy of each
/// frame in managed memory, then the shared band pass.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The disk-backed route spills every frame and re-reads it per band, which is
/// the right answer for thousands of frames but wrong for a short selection: with a 512 MiB scratch
/// budget a 44-frame Full-HD RGB selection (522 MiB of scratch) was refused even though 44 is below
/// <see cref="IntegrationOptions.MaxRetainedMedianFrames"/>. This route keeps those frames in memory
/// and produces the identical median, because the arithmetic is <see cref="MedianBandPass"/>'s.
/// </para>
/// <para>
/// <b>Budget.</b> It is only entered when the selection is at or below
/// <see cref="IntegrationOptions.MaxRetainedMedianFrames"/> <em>and</em> its estimated bytes
/// (frames x width x height x channels x 2, plus the band table) fit
/// <see cref="IntegrationOptions.EffectiveMedianInMemoryBudgetBytes"/>. Those are exactly the rules
/// <see cref="MedianIntegrator"/> routes by, and this class re-checks them so a direct call cannot
/// quietly retain 10,000 frames. <see cref="FinalImageIntegrator"/> is the supported entry point.
/// </para>
/// <para>
/// <b>Copies, not references.</b> Each frame's samples are copied (and translated by its placement)
/// into a buffer owned by this method. A caller that reuses one <see cref="Frame"/> while delivering a
/// selection — the shape a reader loop produces — cannot rewrite a frame that was already accepted, the
/// same guarantee <c>FrameStacker</c> gives by snapshotting on <c>Add</c>.
/// </para>
/// </remarks>
public static class InMemoryMedianIntegrator
{
    /// <summary>Integrates <paramref name="frames"/> with an exact in-memory median.</summary>
    /// <param name="frames">Frames in delivery order. All must share geometry, depth and CFA phase.</param>
    /// <param name="shifts">Per-frame alignment offset, index-aligned with <paramref name="frames"/>.</param>
    /// <param name="options">Must select <see cref="IntegrationMethod.Median"/>; the budgets and band
    /// height are honoured.</param>
    /// <param name="cancellationToken">Checked per frame and per band.</param>
    /// <exception cref="ArgumentException">No frames, mismatched lists, mismatched geometry/depth/channels,
    /// a non-finite offset, or no pixel covered by every frame under
    /// <see cref="CoveragePolicy.CropToOverlap"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A median option is out of range.</exception>
    /// <exception cref="BayerPhaseException">The effective CFA phases differ, or a raw mosaic frame needs
    /// a fractional shift.</exception>
    /// <exception cref="InvalidOperationException">The selection exceeds the frame cap or the in-memory
    /// byte budget.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        IntegrationSupport.RequireMedianOptions(options);
        IntegrationSupport.RequireMedianSelection(
            frames, shifts, options, out var placement, out var selection, out var sourceNames);

        long estimatedBytes = IntegrationSupport.EstimatedInMemoryBytes(
            frames.Count, selection.Width, selection.Height, selection.Channels, options);

        return IntegrateRetained(
            frames, placement, selection, sourceNames, options, MedianRoute.InMemory, estimatedBytes, cancellationToken);
    }

    /// <summary>
    /// The shared body: enforces the retention limits, copies each frame into its own canvas-sized
    /// buffer with the placement applied, then runs the band pass. <paramref name="route"/> lets the
    /// router report what it chose without a second estimate.
    /// </summary>
    internal static IntegrationResult IntegrateRetained(
        IReadOnlyList<Frame> frames,
        SubpixelSampler[] placement,
        IntegrationSelection selection,
        string?[] sourceNames,
        IntegrationOptions options,
        MedianRoute route,
        long estimatedBytes,
        CancellationToken cancellationToken)
    {
        if (frames.Count > options.MaxRetainedMedianFrames)
        {
            throw new InvalidOperationException(
                $"in-memory median retains at most {options.MaxRetainedMedianFrames} frame(s) " +
                $"({nameof(IntegrationOptions.MaxRetainedMedianFrames)}) but the selection has " +
                $"{frames.Count}. Use {nameof(FinalImageIntegrator)} (which routes to the disk-backed " +
                "path) or raise the cap.");
        }

        long budget = IntegrationSupport.InMemoryBudget(options);
        if (estimatedBytes > budget)
        {
            throw new InvalidOperationException(
                $"in-memory median needs about {estimatedBytes:N0} bytes for {frames.Count} frame(s) of " +
                $"{selection.Width}x{selection.Height}x{selection.Channels} ({nameof(IntegrationOptions.EffectiveMedianInMemoryBudgetBytes)} " +
                $"is {budget:N0}). Use {nameof(FinalImageIntegrator)} (which routes to the disk-backed path), " +
                $"lower {nameof(IntegrationOptions.MedianBandRows)}, or raise " +
                $"{nameof(IntegrationOptions.MedianInMemoryBudgetBytes)}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        int maxValue = selection.BitDepth == 8 ? 255 : 65535;
        var retained = new Frame[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The copy is the snapshot: the store the band pass reads from owns its samples, so a
            // source frame may be reused or mutated the moment this iteration ends.
            var copy = new Frame(selection.Width, selection.Height, selection.Channels, selection.BitDepth)
            {
                Bayer = frames[i].Bayer,
            };
            IntegrationSupport.PrepareFrame(frames[i], placement[i], copy, maxValue, cancellationToken);
            retained[i] = copy;
        }

        return MedianBandPass.Run(
            i => retained[i],
            frames.Count,
            placement,
            sourceNames,
            selection,
            options,
            route,
            estimatedBytes,
            cancellationToken);
    }
}
