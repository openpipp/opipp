namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;

/// <summary>
/// Exact per-pixel median over any number of frames, computed from a disk-backed spill so the whole
/// selection never has to sit in RAM.
/// </summary>
/// <remarks>
/// <para>
/// <b>When this route runs.</b> <see cref="MedianIntegrator"/> (behind
/// <see cref="FinalImageIntegrator"/>) chooses this route when the selection is larger than
/// <see cref="IntegrationOptions.MaxRetainedMedianFrames"/> or does not fit
/// <see cref="IntegrationOptions.EffectiveMedianInMemoryBudgetBytes"/>. Calling this class directly
/// always spills, even for two frames, which is what the route tests rely on.
/// </para>
/// <para>
/// <b>Band strategy.</b> Every frame is written to a <see cref="FrameStore"/> (a private scratch file)
/// once, already translated by its alignment shift. The output is then produced in horizontal bands of
/// <see cref="IntegrationOptions.MedianBandRows"/> rows by <see cref="MedianBandPass"/>: for one band,
/// each frame is re-read from the store and its samples for those rows are gathered into a per-band
/// table laid out so that every output sample owns a contiguous run of contributions. Each run is
/// sorted in place and the middle value taken, so the result is the exact median of the same samples an
/// in-memory median would see — no approximation, no histogram binning, no truncation of the sample set.
/// </para>
/// <para>
/// <b>Memory bound.</b> Peak managed memory is O(one frame) — the staging frame used for shifted
/// writes plus the one store frame materialised per read — and then the band table
/// (<c>bandRows x outWidth x channels x frames x 2</c>), a per-band cursor array
/// (<c>bandRows x outWidth x 4</c>), the output (<c>outWidth x outHeight x channels x 2</c>) and the
/// coverage array (<c>width x height x 4</c>). No term grows with the total number of pixels across
/// frames: 10,000 frames cost the same RAM as 65 with the same geometry and band height. The store
/// itself lives on disk and is deleted on every exit path, including cancellation and failure.
/// </para>
/// <para>
/// <b>Registration and CFA.</b> Offsets use the same convention as <see cref="FrameIntegrator"/>:
/// positive <c>Dx</c> moves content right, positive <c>Dy</c> moves it down. A fractional offset is
/// applied by the shared <see cref="SubpixelSampler"/> for a mono or colour frame, but a raw mosaic
/// frame is rejected — bilinear resampling would blend different colour sites — and a mosaic whose
/// effective phase differs from the selection's is rejected too, so no run silently averages two CFA
/// phases. A mosaic frame's whole-pixel placement moves its pattern, so the result is tagged with the
/// pattern its samples actually have at the output origin, never with the first input's header.
/// </para>
/// <para>
/// <b>Scratch budget.</b> <see cref="IntegrationOptions.MedianScratchBudgetBytes"/> caps the pixel
/// bytes the <see cref="FrameStore"/> may hold. The requirement is known before the first frame is
/// spilled, so an over-budget selection fails immediately with
/// <see cref="InvalidOperationException"/> instead of filling the disk and failing later; the message
/// says so explicitly when the budget cannot hold even one frame's band. The band table is bounded by
/// <see cref="IntegrationOptions.MedianBandRows"/> and must fit the in-memory budget, which is checked
/// before the spill for the same reason.
/// </para>
/// <para>
/// <b>Validation.</b> All frames must share geometry, depth and effective CFA phase; a mismatch is
/// rejected before any frame is spilled or any metadata is built, matching the integration contract
/// that incompatible selections fail with a diagnostic naming the reason.
/// </para>
/// </remarks>
public static class TiledMedianIntegrator
{
    /// <summary>Scratch file prefix; the store deletes the file it owns.</summary>
    private const string ScratchPrefix = "opipp-median-";

    /// <summary>Scratch file extension.</summary>
    private const string ScratchExtension = ".bin";

    /// <summary>
    /// Integrates <paramref name="frames"/> with an exact per-pixel median through a disk-backed spill.
    /// </summary>
    /// <param name="frames">Frames in delivery order. Every frame must share geometry, depth and CFA phase.</param>
    /// <param name="shifts">
    /// Per-frame alignment offset, index-aligned with <paramref name="frames"/>. The whole-pixel part
    /// is always applied; the fractional part is bilinear-resampled when
    /// <see cref="IntegrationOptions.Subpixel"/> is on and rounded away from zero when it is off, and is
    /// refused outright on a raw mosaic frame.
    /// </param>
    /// <param name="options">
    /// Integration options. <see cref="IntegrationOptions.Method"/> must be
    /// <see cref="IntegrationMethod.Median"/>.
    /// </param>
    /// <param name="cancellationToken">Checked per band, per frame read and periodically per sample.</param>
    /// <returns>
    /// The median frame, per-pixel coverage, contributors in delivery order, the covered rectangle and
    /// <see cref="IntegrationResult.PrecisionSamples"/> = null (a median of integers is an integer, so
    /// there is no high-precision intermediate to expose).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// No frames, mismatched list lengths, a frame's geometry/depth differs, an offset is not finite,
    /// or no pixel is covered by every frame under <see cref="CoveragePolicy.CropToOverlap"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A band height or budget is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scratch budget cannot hold the selection (or even one frame), the band table would exceed the
    /// maximum array length or the in-memory budget.
    /// </exception>
    /// <exception cref="BayerPhaseException">The frames' effective CFA phases differ, or a raw mosaic
    /// frame needs a fractional shift.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        return Integrate(frames, shifts, options, cancellationToken, scratchDirectory: null, storeObserver: null);
    }

    /// <summary>
    /// Test seam: the same integration with a caller-chosen scratch directory and a hook that sees the
    /// populated store before band processing starts.
    /// </summary>
    /// <remarks>
    /// Both extra arguments exist so tests can prove the frames really were spilled (the store reports
    /// its frame count, byte count and path) and can assert the scratch file is gone afterwards.
    /// Production callers use the four-argument overload.
    /// </remarks>
    internal static IntegrationResult Integrate(
        IReadOnlyList<Frame> frames,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        CancellationToken cancellationToken,
        string? scratchDirectory,
        Action<FrameStore>? storeObserver)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        if (scratchDirectory is not null && string.IsNullOrWhiteSpace(scratchDirectory))
        {
            throw new ArgumentException("scratch directory cannot be blank", nameof(scratchDirectory));
        }

        IntegrationSupport.RequireMedianOptions(options);
        IntegrationSupport.RequireMedianSelection(
            frames, shifts, options, out var placement, out var selection, out var sourceNames);

        cancellationToken.ThrowIfCancellationRequested();

        return IntegrateSpilled(
            frames, placement, selection, sourceNames, options, cancellationToken, scratchDirectory, storeObserver);
    }

    /// <summary>
    /// The spill-and-band core, for a selection already validated by
    /// <see cref="IntegrationSupport.RequireMedianSelection"/> — <see cref="MedianIntegrator"/> routes to
    /// it so a run is validated once and then handled by whichever route its budgets allow.
    /// </summary>
    internal static IntegrationResult IntegrateSpilled(
        IReadOnlyList<Frame> frames,
        SubpixelSampler[] placement,
        IntegrationSelection selection,
        string?[] sourceNames,
        IntegrationOptions options,
        CancellationToken cancellationToken,
        string? scratchDirectory,
        Action<FrameStore>? storeObserver,
        long estimatedBytes = 0)
    {
        int frameCount = frames.Count;
        long bytesPerFrame = (long)selection.Width * selection.Height * selection.Channels * sizeof(ushort);
        long requiredScratch = bytesPerFrame * frameCount;
        if (requiredScratch > options.MedianScratchBudgetBytes)
        {
            string evenOneBand = bytesPerFrame > options.MedianScratchBudgetBytes
                ? $" The budget cannot hold even one band: a single {selection.Width}x{selection.Height}x" +
                  $"{selection.Channels} frame needs {bytesPerFrame:N0} bytes. "
                : " ";

            throw new InvalidOperationException(
                $"disk-backed median needs {requiredScratch:N0} bytes of scratch for {frameCount} frame(s) of " +
                $"{selection.Width}x{selection.Height}x{selection.Channels}, but MedianScratchBudgetBytes is " +
                $"{options.MedianScratchBudgetBytes:N0}.{evenOneBand}Reduce the selection, raise the budget, " +
                "or use mean/sum.");
        }

        // The band table is resident on this route too, so bound it before anything is written to disk.
        IntegrationSupport.RequireBandTableBudget(selection, frameCount, options);

        // --- spill -------------------------------------------------------------------------------
        // One staging frame is reused for every prepared write; FrameStore copies the samples into the
        // scratch file during Append, so nothing here outlives the iteration.
        string directory = scratchDirectory ?? Path.GetTempPath();
        string scratchPath = Path.Combine(directory, ScratchPrefix + Guid.NewGuid().ToString("n") + ScratchExtension);

        var staging = new Frame(selection.Width, selection.Height, selection.Channels, selection.BitDepth)
        {
            Bayer = selection.CanvasPattern,
        };

        int maxValue = selection.BitDepth == 8 ? 255 : 65535;

        // The store is the only scratch this method creates and it owns deletion: the using scope
        // runs on every path, including cancellation and a failed band pass. Nothing partial is
        // returned either — the result is built by the band pass and handed back only after the last
        // band, so a throw discards it.
        using var store = new FrameStore(scratchPath);

        for (int i = 0; i < frameCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IntegrationSupport.PrepareFrame(frames[i], placement[i], staging, maxValue, cancellationToken);
            store.Append(staging);
        }

        cancellationToken.ThrowIfCancellationRequested();
        storeObserver?.Invoke(store);

        return MedianBandPass.Run(
            store.Read,
            frameCount,
            placement,
            sourceNames,
            selection,
            options,
            MedianRoute.Tiled,
            estimatedBytes,
            cancellationToken);
    }
}
