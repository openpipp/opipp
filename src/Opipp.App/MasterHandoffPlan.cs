#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PIPP_Application;

/// <summary>Which calibration master a plan stage produces.</summary>
internal enum MasterKind
{
    Dark,
    FlatDark,
    Flat,
}

/// <summary>
/// Provenance of a master the front-end built earlier in this session: the file it was written to
/// and the exact input set that produced it.
/// </summary>
/// <remarks>
/// <para>
/// The flat master's <see cref="Corrected"/> flag describes the file, not the last run's toggles.
/// Keeping it beside the identity is what stops a run with flat calibration off from clearing
/// "already corrected" while leaving the corrected master on disk — the state that made the next
/// run hand the flat dark to the engine as well and subtract it twice.
/// </para>
/// <para>
/// <see cref="FrameCount"/> is the number of frames that actually went into the master, while
/// <see cref="InputSignature"/> covers the frames that were requested (paths plus frame limit). A
/// cached master is only reused when both still describe the current request.
/// </para>
/// </remarks>
internal sealed class CachedMaster
{
    /// <summary>Creates a cache record for one master artifact.</summary>
    public CachedMaster(
        string masterPath,
        string? pgmPath,
        int frameCount,
        string inputSignature,
        bool corrected,
        string? correctionSignature)
    {
        MasterPath = masterPath;
        PgmPath = pgmPath;
        FrameCount = frameCount;
        InputSignature = inputSignature;
        Corrected = corrected;
        CorrectionSignature = correctionSignature;
    }

    /// <summary>The <c>pipp_master_*.tmp</c> handed to the engine.</summary>
    public string MasterPath { get; }

    /// <summary>The legacy luma PGM, when the stage writes one.</summary>
    public string? PgmPath { get; }

    /// <summary>Frames accumulated into the master.</summary>
    public int FrameCount { get; }

    /// <summary>Identity of the requested input set (see <see cref="MasterHandoffPlanner.Signature"/>).</summary>
    public string InputSignature { get; }

    /// <summary>True when this is a flat master that already has its flat dark removed.</summary>
    public bool Corrected { get; }

    /// <summary>Identity of the flat-dark inputs baked into a corrected flat; null otherwise.</summary>
    public string? CorrectionSignature { get; }
}

/// <summary>One calibration stage as the checkboxes and file lists currently describe it.</summary>
internal sealed class MasterStageRequest
{
    /// <summary>Creates a request for one stage.</summary>
    public MasterStageRequest(MasterKind kind)
    {
        Kind = kind;
    }

    /// <summary>Which master this stage produces.</summary>
    public MasterKind Kind { get; }

    /// <summary>True when the operator enabled the stage and selected frames for it.</summary>
    public bool Enabled { get; set; }

    /// <summary>The selected frame paths, in list order.</summary>
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();

    /// <summary>Frames selected in the list (before any test-run limit).</summary>
    public int FrameCount { get; set; }

    /// <summary>Destination <c>.tmp</c> master, whether or not it is rebuilt.</summary>
    public string MasterPath { get; set; } = "";

    /// <summary>Destination PGM for the stages that write one.</summary>
    public string? PgmPath { get; set; }
}

/// <summary>Everything the handoff decision needs: the three stages, their caches and the mode.</summary>
internal sealed class MasterHandoffRequest
{
    /// <summary>Test runs cap the master at ten frames; see <see cref="MasterHandoffPlanner"/>.</summary>
    public bool TestRun { get; set; }

    /// <summary>True when the operator asked for the master flat dark to be saved as a TIFF.</summary>
    public bool SaveFlatDark { get; set; }

    /// <summary>The master dark stage.</summary>
    public MasterStageRequest Dark { get; set; } = new MasterStageRequest(MasterKind.Dark);

    /// <summary>The master flat-dark stage.</summary>
    public MasterStageRequest FlatDark { get; set; } = new MasterStageRequest(MasterKind.FlatDark);

    /// <summary>The master flat stage.</summary>
    public MasterStageRequest Flat { get; set; } = new MasterStageRequest(MasterKind.Flat);

    /// <summary>Cache record from an earlier run of this form, or null.</summary>
    public CachedMaster? DarkCache { get; set; }

    /// <summary>Cache record from an earlier run of this form, or null.</summary>
    public CachedMaster? FlatDarkCache { get; set; }

    /// <summary>Cache record from an earlier run of this form, or null.</summary>
    public CachedMaster? FlatCache { get; set; }
}

/// <summary>What one calibration stage must do on this run.</summary>
internal sealed class MasterStagePlan
{
    /// <summary>Creates a stage decision.</summary>
    internal MasterStagePlan(
        MasterKind kind,
        bool enabled,
        bool rebuild,
        int frameLimit,
        IReadOnlyList<string> files,
        string masterPath,
        string? pgmPath,
        string? subtractPath,
        bool corrected,
        string inputSignature,
        string? correctionSignature)
    {
        Kind = kind;
        Enabled = enabled;
        Rebuild = rebuild;
        FrameLimit = frameLimit;
        Files = files;
        MasterPath = masterPath;
        PgmPath = pgmPath;
        SubtractPath = subtractPath;
        Corrected = corrected;
        InputSignature = inputSignature;
        CorrectionSignature = correctionSignature;
    }

    /// <summary>Which master this stage produces.</summary>
    public MasterKind Kind { get; }

    /// <summary>True when the stage runs at all.</summary>
    public bool Enabled { get; }

    /// <summary>True when the master must be built; false when the cached artifact is reused.</summary>
    public bool Rebuild { get; }

    /// <summary>Frames to accumulate (the test-run cap already applied).</summary>
    public int FrameLimit { get; }

    /// <summary>The frame paths to accumulate when <see cref="Rebuild"/> is true.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>Where the master is (or will be) written.</summary>
    public string MasterPath { get; }

    /// <summary>The PGM destination for the stages that emit one.</summary>
    public string? PgmPath { get; }

    /// <summary>For the flat stage: the flat-dark master the build must subtract, or null.</summary>
    public string? SubtractPath { get; }

    /// <summary>True when the resulting flat master already has the flat dark removed.</summary>
    public bool Corrected { get; }

    /// <summary>Identity of this stage's requested inputs.</summary>
    public string InputSignature { get; }

    /// <summary>Identity of the flat-dark inputs baked into a corrected flat; null otherwise.</summary>
    public string? CorrectionSignature { get; }

    /// <summary>Records the master that was just built as this stage's cache entry.</summary>
    /// <param name="frameCount">Frames the build actually accumulated.</param>
    public CachedMaster CreateEntry(int frameCount) =>
        new CachedMaster(MasterPath, PgmPath, frameCount, InputSignature, Corrected, CorrectionSignature);
}

/// <summary>The complete handoff: what to build, what to reuse, and the switches the engine gets.</summary>
internal sealed class MasterHandoffPlan
{
    /// <summary>Creates a plan.</summary>
    internal MasterHandoffPlan(
        MasterStagePlan dark,
        MasterStagePlan flatDark,
        MasterStagePlan flat,
        IReadOnlyList<string> arguments,
        string? flatDarkMasterToSave)
    {
        Dark = dark;
        FlatDark = flatDark;
        Flat = flat;
        Arguments = arguments;
        FlatDarkMasterToSave = flatDarkMasterToSave;
    }

    /// <summary>The master dark decision.</summary>
    public MasterStagePlan Dark { get; }

    /// <summary>The master flat-dark decision.</summary>
    public MasterStagePlan FlatDark { get; }

    /// <summary>The master flat decision.</summary>
    public MasterStagePlan Flat { get; }

    /// <summary>
    /// The master switches to append, in the order the engine expects. Only a flat that has not been
    /// corrected carries <c>-flat_dark_tmp</c>.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// The flat-dark <c>.tmp</c> the front-end must save as a TIFF itself (<c>-save_flat_dark</c>),
    /// or null when nothing is to be saved. The engine cannot do it: a corrected flat is handed over
    /// without <c>-flat_dark_tmp</c>, so the engine never loads a flat-dark master.
    /// </summary>
    public string? FlatDarkMasterToSave { get; }
}

/// <summary>
/// Decides, from the current checkboxes and the masters built earlier in the session, whether each
/// calibration master must be rebuilt and which master switches the engine receives.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place that knows the rebuild-vs-reuse rule and the flat-dark handoff rule.
/// It is deliberately free of UI and I/O except for the existence check on a cached artifact: a
/// cache entry is only trusted while its file is still on disk, its input identity still matches,
/// and — for a corrected flat — it was corrected from the flat-dark set that is in play now.
/// </para>
/// <para>
/// The flat rule is what fixes the double subtraction. The front-end builds its flat with the flat
/// dark already removed (<see cref="MasterStagePlan.Corrected"/>), so the engine must not receive
/// <c>-flat_dark_tmp</c> for it; only an uncorrected master needs that switch. Because "corrected"
/// lives in the cache entry that describes the file, toggling flat calibration off and on again
/// cannot leave the flag describing a different master.
/// </para>
/// </remarks>
internal static class MasterHandoffPlanner
{
    /// <summary>Builds the plan for one run.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static MasterHandoffPlan Create(MasterHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool flatInPlay = request.Flat is { Enabled: true } && request.Flat.Files.Count > 0;

        MasterStagePlan dark = PlanIndependentStage(request.Dark, request.TestRun, request.DarkCache);
        MasterStagePlan flatDark = PlanIndependentStage(request.FlatDark, request.TestRun, request.FlatDarkCache, forceDisabled: !flatInPlay);
        MasterStagePlan flat = PlanFlatStage(request.Flat, request.TestRun, request.FlatCache, flatDark);

        var arguments = new List<string>();
        if (dark.Enabled)
        {
            arguments.Add("-dark_tmp=" + dark.MasterPath);
            if (!string.IsNullOrEmpty(dark.PgmPath))
            {
                arguments.Add("-dark_pgm=" + dark.PgmPath);
            }
        }

        if (flat.Enabled)
        {
            arguments.Add("-flat_tmp=" + flat.MasterPath);
            if (!flat.Corrected && flatDark.Enabled)
            {
                arguments.Add("-flat_dark_tmp=" + flatDark.MasterPath);
            }
        }

        string? save = request.SaveFlatDark && flatDark.Enabled && !string.IsNullOrEmpty(flatDark.MasterPath)
            ? flatDark.MasterPath
            : null;

        return new MasterHandoffPlan(dark, flatDark, flat, arguments, save);
    }

    /// <summary>
    /// Stable identity for a frame set: the frame limit plus the normalised paths in list order.
    /// </summary>
    /// <param name="files">Selected frame paths.</param>
    /// <param name="frameLimit">Maximum frames that will be accumulated.</param>
    /// <remarks>
    /// The order is part of the identity because a capped build (a test run) accumulates the first
    /// N frames; reordering the list can therefore change the master even though the set is equal.
    /// </remarks>
    public static string Signature(IReadOnlyList<string> files, int frameLimit)
    {
        ArgumentNullException.ThrowIfNull(files);

        IEnumerable<string> normalized = files
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(Normalize);

        return frameLimit.ToString(CultureInfo.InvariantCulture) + "|" + string.Join("\n", normalized);
    }

    /// <summary>A dark or flat-dark stage: reuse only against an identical input set and an existing file.</summary>
    private static MasterStagePlan PlanIndependentStage(
        MasterStageRequest request,
        bool testRun,
        CachedMaster? cache,
        bool forceDisabled = false)
    {
        bool enabled = request.Enabled && !forceDisabled && request.Files.Count > 0;
        int frameLimit = FrameLimit(testRun, cache?.FrameCount ?? 0, request.FrameCount);
        string signature = Signature(request.Files, frameLimit);

        bool reusable = enabled
            && cache is not null
            && cache.Corrected == false
            && ArtifactStillMatches(cache, request.MasterPath, signature);

        return new MasterStagePlan(
            request.Kind,
            enabled,
            enabled && !reusable,
            frameLimit,
            request.Files,
            request.MasterPath,
            request.PgmPath,
            subtractPath: null,
            corrected: false,
            signature,
            correctionSignature: null);
    }

    /// <summary>
    /// The flat stage: a cached corrected flat is only valid while the same flat-dark set is in play,
    /// because that correction is already baked into the file. A cached uncorrected flat stays valid
    /// even when flat-dark subtraction is on — the engine subtracts it once, through the switch.
    /// </summary>
    private static MasterStagePlan PlanFlatStage(
        MasterStageRequest request,
        bool testRun,
        CachedMaster? cache,
        MasterStagePlan flatDark)
    {
        bool enabled = request.Enabled && request.Files.Count > 0;
        int frameLimit = FrameLimit(testRun, cache?.FrameCount ?? 0, request.FrameCount);
        string signature = Signature(request.Files, frameLimit);

        bool reusable = enabled
            && cache is not null
            && ArtifactStillMatches(cache, request.MasterPath, signature)
            && (!cache.Corrected
                || (flatDark.Enabled
                    && string.Equals(cache.CorrectionSignature, flatDark.InputSignature, StringComparison.Ordinal)));

        bool rebuild = enabled && !reusable;
        bool corrected = enabled && (rebuild ? flatDark.Enabled : cache!.Corrected);
        string? subtract = rebuild && flatDark.Enabled ? flatDark.MasterPath : null;

        return new MasterStagePlan(
            MasterKind.Flat,
            enabled,
            rebuild,
            frameLimit,
            request.Files,
            request.MasterPath,
            pgmPath: null,
            subtract,
            corrected,
            signature,
            corrected ? flatDark.InputSignature : null);
    }

    /// <summary>True when the cache describes this destination and its input set, and the file is still there.</summary>
    private static bool ArtifactStillMatches(CachedMaster cache, string masterPath, string signature)
    {
        if (string.IsNullOrEmpty(cache.MasterPath) || string.IsNullOrEmpty(masterPath))
        {
            return false;
        }

        if (!string.Equals(Normalize(cache.MasterPath), Normalize(masterPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(cache.InputSignature, signature, StringComparison.Ordinal))
        {
            return false;
        }

        // The count alone is not enough: the temp directory can be cleaned between runs.
        return File.Exists(cache.MasterPath);
    }

    /// <summary>
    /// The frame limit for one stage. A test run inspects ten frames, but only while the cached
    /// master does not already contain more: shrinking an existing master would change the result.
    /// </summary>
    private static int FrameLimit(bool testRun, int cachedFrameCount, int requestedFrameCount)
    {
        int requested = Math.Max(0, requestedFrameCount);
        return testRun && cachedFrameCount <= 10 && requested > 10 ? 10 : requested;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }
}
