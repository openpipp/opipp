using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Quality;
using Opipp.Core.Stacking;

namespace Opipp.Core.Pipeline;

/// <summary>
/// Builds master calibration frames from a set of input files and writes the two artifacts the
/// front-end hands to the engine: the private <c>.tmp</c> master and a normalised 8/16-bit PGM.
/// </summary>
/// <remarks>
/// This is the managed replacement for <c>pipp_dll!stack_frames</c>. The original contract (read via
/// REA) is: average the frames, optionally subtract one reference frame from every input before
/// accumulating, refuse inputs whose geometry or sample size differs, report progress, and return a
/// negative value on failure. This implementation keeps the numerical contract but reports failure as
/// a typed <see cref="MasterBuildResult"/> so the front-end can show it. The master is written in the
/// engine's private container (<see cref="MasterTmpFile"/>), which is the only format
/// <c>-flat_tmp</c>/<c>-flat_dark_tmp</c> accept; it keeps every channel and the true sample depth.
/// The PGM is written alongside as the engine's legacy luma fallback for <c>-dark_pgm</c>, so a
/// colour master is reduced to luma for that file only.
/// </remarks>
public static class MasterFrameBuilder
{
    /// <summary>Builds a master frame from the first <paramref name="frameLimit"/> frames found in <paramref name="files"/>.</summary>
    /// <param name="files">Calibration inputs (SER, AVI, FITS, BMP, TIFF or PNG). Every path must exist.</param>
    /// <param name="subtractFile">Optional master whose first frame is subtracted from every input
    /// (the flat-dark handoff). A non-empty path must exist, and is read through
    /// <see cref="FrameSourceFactory"/> so both an image file and a <c>pipp_master_*.tmp</c> are valid.</param>
    /// <param name="masterPath">Destination master (<c>pipp_master_*.tmp</c>), or null/empty to skip.</param>
    /// <param name="pgmPath">Destination PGM (the engine's legacy luma handoff), or null/empty to skip.</param>
    /// <param name="frameLimit">Maximum frames to accumulate; 0 or less means all frames.</param>
    /// <param name="progress">Receives 0..100 while frames are accumulated, then 100 once the outputs are published.</param>
    /// <param name="cancellationToken">Stops the build between frames and before staging or publication
    /// begins; once the pair is being published it is not interrupted.</param>
    /// <returns>
    /// A <see cref="MasterBuildResult"/>: <see cref="MasterBuildResult.Success"/> with the actual frame
    /// count and published paths, or a failure carrying the reason. A failed build leaves the previous
    /// master pair exactly as it was (or no output at all when there was none), never a new PGM beside
    /// an old <c>.tmp</c> or the reverse; if restoring that pair itself fails, the error says which
    /// <c>.previous-</c> backups were kept.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.
    /// Cancellation is not reported as a build failure; the caller asked for it. Cancellation is only
    /// observed before publication starts, so a published pair is never cancelled halfway.</exception>
    /// <remarks>
    /// Publication is transactional for the duration of the call. Both outputs are first written to
    /// staging files beside their destinations, so nothing at a final path is touched until every
    /// write has succeeded. Each existing destination is then renamed to a private <c>.previous-</c>
    /// backup, the PGM fallback is renamed into place first and the authoritative <c>.tmp</c> last,
    /// and the backups are deleted once both renames have succeeded. If either rename fails, every
    /// file already published is removed and every backup is renamed back, so the call leaves either
    /// the complete new pair or the complete previous pair - never one output from each build. The
    /// <c>.tmp</c> is published last so that even an abrupt process termination between the two
    /// renames leaves the previous authoritative master in place: the loader prefers the <c>.tmp</c>
    /// over the PGM, so that half-state still resolves to the previous master. Only if restoring a
    /// backup itself fails does a mixed pair remain; the failure then names the kept backups.
    /// </remarks>
    public static MasterBuildResult Build(
        IReadOnlyList<string> files,
        string? subtractFile,
        string? masterPath,
        string? pgmPath,
        int frameLimit,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        Build(files, subtractFile, masterPath, pgmPath, frameLimit, progress, cancellationToken, null);

    /// <summary>Test seam for <see cref="Build(IReadOnlyList{string}, string?, string?, string?, int, Action{int}?, CancellationToken)"/>.</summary>
    /// <param name="beforePublish">Invoked immediately before each staging file is renamed onto its
    /// final path; a test can throw from it to force publication to fail at a chosen step. Null uses
    /// the real publication path unchanged.</param>
    /// <remarks>Internal so the failure ordering can be exercised without timing or permission hacks.</remarks>
    internal static MasterBuildResult Build(
        IReadOnlyList<string> files,
        string? subtractFile,
        string? masterPath,
        string? pgmPath,
        int frameLimit,
        Action<int>? progress,
        CancellationToken cancellationToken,
        Action<string, string>? beforePublish)
    {
        ArgumentNullException.ThrowIfNull(files);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Missing selected inputs are a configuration error, not a frame to skip: silently
            // dropping one produces a master from fewer frames than the user selected.
            var inputs = new List<string>(files.Count);
            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file))
                    return MasterBuildResult.Failed("The calibration input list contains an empty path.");
                if (!File.Exists(file))
                    return MasterBuildResult.Failed($"Calibration input file not found: {file}");
                inputs.Add(file);
            }

            if (inputs.Count == 0)
                return MasterBuildResult.Failed("No calibration input files were selected.");

            Frame? subtract = null;
            if (!string.IsNullOrWhiteSpace(subtractFile))
            {
                if (!File.Exists(subtractFile))
                    return MasterBuildResult.Failed($"Subtraction master not found: {subtractFile}");

                using var subSource = FrameSourceFactory.Open(subtractFile!);
                subtract = subSource.ReadFrame();
                if (subtract is null)
                    return MasterBuildResult.Failed($"Subtraction master contains no frame: {subtractFile}");
            }

            FrameStacker? stacker = null;
            string? stackerGeometry = null;
            string? stackerFile = null;
            int stacked = 0;
            int lastReported = -1;

            foreach (string file in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var source = FrameSourceFactory.Open(file);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Frame? frame = source.ReadFrame();
                    if (frame is null) break;

                    if (stacker is null)
                    {
                        stacker = new FrameStacker(frame.Width, frame.Height, frame.Channels, frame.BitDepth, StackMethod.Mean);
                        stackerGeometry = Describe(frame);
                        stackerFile = file;
                    }
                    else if (frame.Width != stacker.Width || frame.Height != stacker.Height ||
                             frame.Channels != stacker.Channels || frame.BitDepth != stacker.BitDepth)
                    {
                        // Same failure mode as the native stacker: refuse mixed geometries, but say
                        // which files disagree instead of returning an opaque -1.
                        return MasterBuildResult.Failed(
                            $"Calibration frames differ in geometry: '{file}' is {Describe(frame)} but " +
                            $"'{stackerFile}' is {stackerGeometry}. A master must be built from frames " +
                            "with identical geometry and sample depth.");
                    }

                    try
                    {
                        stacker.Add(frame, subtract);
                    }
                    catch (ArgumentException ex) when (subtract is not null)
                    {
                        return MasterBuildResult.Failed(
                            $"The subtraction master '{subtractFile}' does not match the calibration frames: {ex.Message}");
                    }

                    stacked++;

                    if (progress is not null && frameLimit > 0)
                    {
                        int percent = Math.Clamp(stacked * 100 / frameLimit, 0, 100);
                        if (percent != lastReported)
                        {
                            progress(percent);
                            lastReported = percent;
                        }
                    }

                    if (frameLimit > 0 && stacked >= frameLimit) break;
                }

                if (frameLimit > 0 && stacked >= frameLimit) break;
            }

            if (stacker is null || stacked == 0)
                return MasterBuildResult.Failed("No frames could be read from the selected calibration files.");

            Frame master = stacker.Result();
            cancellationToken.ThrowIfCancellationRequested();

            string? masterFull = string.IsNullOrWhiteSpace(masterPath) ? null : Path.GetFullPath(masterPath!);
            string? pgmFull = string.IsNullOrWhiteSpace(pgmPath) ? null : Path.GetFullPath(pgmPath!);
            string? masterStaging = masterFull is null ? null : StagingPath(masterFull);
            string? pgmStaging = pgmFull is null ? null : StagingPath(pgmFull);
            var publications = new List<Publication>(2);
            bool preserveBackups = false;

            try
            {
                // Stage both outputs. Nothing at the final paths is touched until every write worked.
                if (masterStaging is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MasterTmpFile.Write(master, masterStaging);
                }

                if (pgmStaging is not null)
                {
                    Frame pgm = master.Channels == 1
                        ? master
                        : new Frame(master.Width, master.Height, 1, master.BitDepth, QualityMetrics.ToLuma(master));
                    cancellationToken.ThrowIfCancellationRequested();
                    PgmFile.Write(pgmStaging, pgm);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Publish the fallback first and the authoritative .tmp last. The two renames are a
                // transaction: the previous pair is renamed aside first, and if either rename fails
                // both are put back, so the destination pair is always entirely old or entirely new.
                // Publishing the .tmp last also means a process killed between the renames leaves the
                // previous .tmp - the file the loader prefers - in place.
                if (pgmStaging is not null)
                    publications.Add(new Publication(pgmStaging, pgmFull!, BackupPath(pgmFull!)));
                if (masterStaging is not null)
                    publications.Add(new Publication(masterStaging, masterFull!, BackupPath(masterFull!)));

                try
                {
                    // Phase 1: move the previous pair aside. Renames are cheap and keep the old bytes
                    // untouched, so they can be put back exactly if publication does not complete.
                    foreach (Publication publication in publications)
                    {
                        if (File.Exists(publication.Destination))
                            File.Move(publication.Destination, publication.Backup);
                    }

                    // Phase 2: move the staged outputs into place, PGM first, .tmp last.
                    for (int i = 0; i < publications.Count; i++)
                    {
                        Publication publication = publications[i];
                        beforePublish?.Invoke(publication.Staging, publication.Destination);
                        File.Move(publication.Staging, publication.Destination, overwrite: true);
                        publication.Published = true;
                    }
                }
                catch (Exception publishError)
                {
                    // Undo in reverse: remove any new file, then restore its backup. Restoring the
                    // .tmp first means a crash during recovery still leaves the previous
                    // authoritative master beside a possibly-new PGM, which the loader ignores.
                    var restoreFailures = new List<string>();
                    for (int i = publications.Count - 1; i >= 0; i--)
                    {
                        Publication publication = publications[i];
                        if (publication.Published && !TryDelete(publication.Destination))
                            restoreFailures.Add($"could not remove newly published '{publication.Destination}'");

                        if (!File.Exists(publication.Backup)) continue;
                        try
                        {
                            File.Move(publication.Backup, publication.Destination, overwrite: true);
                        }
                        catch (Exception restoreError)
                        {
                            // Rollback is best-effort for every entry: record this one and continue
                            // so a second failure cannot be masked by the first.
                            restoreFailures.Add(
                                $"could not restore '{publication.Destination}' from '{publication.Backup}': {restoreError.Message}");
                        }
                    }

                    if (restoreFailures.Count > 0)
                    {
                        // Keep the backups: they are the only remaining copy of the previous pair.
                        preserveBackups = true;
                        throw new IOException(
                            $"Publishing the calibration master failed ({publishError.Message}), and the previous " +
                            $"master pair could not be fully restored: {string.Join("; ", restoreFailures)}. " +
                            "The '.previous-' backups beside the destinations were kept.", publishError);
                    }

                    throw;
                }
            }
            finally
            {
                if (masterStaging is not null) TryDelete(masterStaging);
                if (pgmStaging is not null) TryDelete(pgmStaging);
                if (!preserveBackups)
                {
                    foreach (Publication publication in publications) TryDelete(publication.Backup);
                }
            }

            progress?.Invoke(100);
            return MasterBuildResult.Built(stacked, masterFull, pgmFull);
        }
        catch (OperationCanceledException)
        {
            // A cancelled build is a request, not a failure: no result, no outputs.
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or
                                       ArgumentException or UnauthorizedAccessException)
        {
            return MasterBuildResult.Failed(ex.Message);
        }
    }

    private static string Describe(Frame frame) =>
        $"{frame.Width}x{frame.Height}x{frame.Channels}@{frame.BitDepth}";

    private static string StagingPath(string destination) =>
        destination + ".partial-" + Guid.NewGuid().ToString("N");

    /// <summary>Private name the previous destination is renamed to while the new pair is published.</summary>
    private static string BackupPath(string destination) =>
        destination + ".previous-" + Guid.NewGuid().ToString("N");

    /// <summary>One staged output and the final path it will replace.</summary>
    private sealed class Publication(string staging, string destination, string backup)
    {
        public string Staging { get; } = staging;
        public string Destination { get; } = destination;
        public string Backup { get; } = backup;
        public bool Published { get; set; }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes one master frame as a TIFF in the engine's master layout: the frame's own geometry,
    /// channel count and bit depth, uncompressed, one strip.
    /// </summary>
    /// <param name="master">Master frame to write.</param>
    /// <param name="tiffPath">Destination path; parent directories are created when missing.</param>
    /// <remarks>
    /// This is the user-facing master dump, not the engine handoff: the processor's
    /// <c>-save_dark</c>/<c>-save_flat</c> stage calls it to emit <c>logs/pipp_master_dark.tif</c> and
    /// friends next to the output. The <c>-dark_tmp</c>/<c>-flat_tmp</c> handoff uses
    /// <see cref="MasterTmpFile"/> instead, because the engine's loader only understands that one.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="master"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tiffPath"/> is empty.</exception>
    public static void WriteMaster(Frame master, string tiffPath)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentException.ThrowIfNullOrWhiteSpace(tiffPath);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(tiffPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var tiff = new TiffWriter(tiffPath, master.Width, master.Height, master.Channels, master.BitDepth);
        tiff.Write(master);
    }
}
