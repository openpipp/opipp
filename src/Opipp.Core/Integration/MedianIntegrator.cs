namespace Opipp.Core.Integration;

using Opipp.Core.Alignment;
using Opipp.Core.Frames;

/// <summary>
/// Routes a median integration to the in-memory or the disk-backed implementation and exposes the
/// choice on the result.
/// </summary>
/// <remarks>
/// <para>
/// <b>Routing rule.</b> A selection runs in memory when <em>both</em> limits hold:
/// <list type="number">
/// <item><description>its frame count is at or below
/// <see cref="IntegrationOptions.MaxRetainedMedianFrames"/> (default 64), and</description></item>
/// <item><description>its estimated bytes — one <see cref="ushort"/> sample copy per frame
/// (<c>frames x width x height x channels x 2</c>) plus the band table
/// (<c>bandRows x width x channels x frames x 2</c>) — fit
/// <see cref="IntegrationOptions.EffectiveMedianInMemoryBudgetBytes"/> (default 1 GiB, derived as twice
/// <see cref="IntegrationOptions.MedianScratchBudgetBytes"/>).</description></item>
/// </list>
/// Anything above either limit goes to <see cref="TiledMedianIntegrator"/>, which spills every frame to
/// scratch and re-reads it per band; that route enforces
/// <see cref="IntegrationOptions.MedianScratchBudgetBytes"/> and fails before writing scratch when the
/// budget cannot hold the selection. Both routes run the same <see cref="MedianBandPass"/>, so the
/// pixels are identical — the route is a memory decision, never a numerical one, and
/// <see cref="IntegrationResult.MedianRoute"/> reports which was taken.
/// </para>
/// <para>
/// <b>Why a cap this low is safe.</b> The default in-memory allowance is 1 GiB, which holds 64 Full-HD
/// RGB frames at 759 MiB. The reported failure — 44 Full-HD RGB frames needing 522 MiB of scratch under
/// a 512 MiB disk budget — now runs in memory, and a selection of 65 frames or more keeps the bounded
/// peak the tiled route was built for.
/// </para>
/// </remarks>
public static class MedianIntegrator
{
    /// <summary>Integrates a list-shaped median selection by the routing rule above.</summary>
    /// <param name="frames">Frames in delivery order.</param>
    /// <param name="shifts">Per-frame alignment offsets, index-aligned with <paramref name="frames"/>.</param>
    /// <param name="options">Median options; the budgets and band height select the route.</param>
    /// <param name="cancellationToken">Checked per frame and per band.</param>
    /// <exception cref="ArgumentException">No frames, mismatched lists, or geometry/depth/channel mismatch.</exception>
    /// <exception cref="BayerPhaseException">The effective CFA phases differ, or a raw mosaic frame needs
    /// a fractional shift.</exception>
    /// <exception cref="InvalidOperationException">The chosen route's budget cannot hold the selection.</exception>
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

        cancellationToken.ThrowIfCancellationRequested();

        MedianRoute route = IntegrationSupport.DecideRoute(
            frames.Count, selection.Width, selection.Height, selection.Channels, options, out long estimatedBytes);

        return route == MedianRoute.InMemory
            ? InMemoryMedianIntegrator.IntegrateRetained(
                frames, placement, selection, sourceNames, options, route, estimatedBytes, cancellationToken)
            : TiledMedianIntegrator.IntegrateSpilled(
                frames, placement, selection, sourceNames, options, cancellationToken,
                scratchDirectory: null, storeObserver: null, estimatedBytes: estimatedBytes);
    }

    /// <summary>
    /// Streaming median: reads frames from <paramref name="readFrame"/> once to validate the selection
    /// and once per output band to gather it, so nothing is retained and nothing is spilled.
    /// </summary>
    /// <remarks>
    /// <para><b>Read contract.</b> Index 0 is read once to fix the geometry, depth, channels, CFA phase
    /// and provenance; indices 1..n-1 are then read once each to validate them; finally every index is
    /// read once per band, in ascending order. Total reads per index are therefore
    /// <c>1 + bands</c>, with <c>bands = ceil(outputHeight / MedianBandRows)</c>. A source may return the
    /// same <see cref="Frame"/> (or the same sample buffer) every time: each read is consumed before the
    /// next one, and no frame is held across a call.</para>
    /// <para><b>Memory.</b> The band table is the only resident pixel array; it must fit
    /// <see cref="IntegrationOptions.EffectiveMedianInMemoryBudgetBytes"/>, and a configuration that
    /// cannot hold one band is refused up front with the band height in the message. That is why the
    /// result reports <see cref="MedianRoute.Streamed"/> rather than InMemory: the frames were never
    /// resident, so the frame cap does not apply.</para>
    /// </remarks>
    /// <param name="readFrame">Supplies frame <c>i</c>; must not return null.</param>
    /// <param name="frameCount">Number of frames to read.</param>
    /// <param name="shifts">Per-frame alignment offsets, index-aligned with the reads.</param>
    /// <param name="options">Median options; <see cref="IntegrationOptions.MedianBandRows"/> controls the
    /// number of re-read passes.</param>
    /// <param name="cancellationToken">Checked per read and per band.</param>
    /// <exception cref="ArgumentException">A mismatched list, or a non-finite offset.</exception>
    /// <exception cref="InvalidOperationException">The frame source returned null, or one band cannot fit
    /// the in-memory budget.</exception>
    /// <exception cref="BayerPhaseException">The effective CFA phases differ, or a raw mosaic frame needs
    /// a fractional shift.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IntegrationResult Integrate(
        Func<int, Frame> readFrame,
        int frameCount,
        IReadOnlyList<AlignmentOffset> shifts,
        IntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readFrame);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(options);

        IntegrationSupport.RequireMedianOptions(options);

        if (frameCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "at least one frame is required");
        }

        if (shifts.Count != frameCount)
        {
            throw new ArgumentException(
                $"{shifts.Count} shifts for {frameCount} frames; the lists must be index-aligned",
                nameof(shifts));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var placement = IntegrationSupport.Placements(shifts, options, nameof(shifts));

        IntegrationSelection selection = Probe(readFrame, frameCount, placement, out var sourceNames);
        IntegrationSupport.RequireBandTableBudget(selection, frameCount, options);

        // Validate the rest of the selection before any band is gathered, so a mismatched frame is a
        // failure rather than something discovered half-way through the output.
        for (int i = 1; i < frameCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Frame frame = ReadFrame(readFrame, i, frameCount);
            IntegrationSupport.RequireCompatibleFrame(
                frame, i, selection.Width, selection.Height, selection.Channels, selection.BitDepth, nameof(readFrame));
            IntegrationSupport.RequireSameEffectivePhase(i, frame.Bayer, placement[i], selection.CanvasPattern);
            sourceNames[i] = frame.SourceName;
        }

        // The band pass reads frames that are already in the first frame's canvas coordinates, so each
        // read is translated on the way in — that is the work the tiled route did when it spilled. One
        // staging frame is reused: the pass finishes one frame's gather before the next read.
        int maxValue = selection.BitDepth == 8 ? 255 : 65535;
        var staging = new Frame(selection.Width, selection.Height, selection.Channels, selection.BitDepth)
        {
            Bayer = selection.CanvasPattern,
        };

        Frame ReadPrepared(int index)
        {
            Frame frame = ReadFrame(readFrame, index, frameCount);
            IntegrationSupport.PrepareFrame(frame, placement[index], staging, maxValue, cancellationToken);
            return staging;
        }

        return MedianBandPass.Run(
            ReadPrepared,
            frameCount,
            placement,
            sourceNames,
            selection,
            options,
            MedianRoute.Streamed,
            0,
            cancellationToken);
    }

    /// <summary>
    /// Reads frame 0 and derives the selection from it. The frame itself does not escape: a streaming
    /// source may reuse its buffer, so only the copied metadata survives the call.
    /// </summary>
    private static IntegrationSelection Probe(
        Func<int, Frame> readFrame, int frameCount, SubpixelSampler[] placement, out string?[] sourceNames)
    {
        Frame first = ReadFrame(readFrame, 0, frameCount);
        IntegrationSupport.RequireCompatibleFrame(
            first, 0, first.Width, first.Height, first.Channels, first.BitDepth, nameof(readFrame));

        BayerPattern canvasPattern = IntegrationSupport.EffectivePattern(0, first.Bayer, placement[0]);
        sourceNames = new string?[frameCount];
        sourceNames[0] = first.SourceName;

        return new IntegrationSelection(
            first.Width, first.Height, first.Channels, first.BitDepth, canvasPattern, first.Index, first.SourceName);
    }

    private static Frame ReadFrame(Func<int, Frame> readFrame, int index, int frameCount) =>
        readFrame(index)
            ?? throw new InvalidOperationException(
                $"readFrame({index}) returned null; the source must supply {frameCount} frame(s)");
}
