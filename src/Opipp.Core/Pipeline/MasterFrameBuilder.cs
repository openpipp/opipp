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
    /// <param name="cancellationToken">Stops the build between frames and before each write/publish.</param>
    /// <returns>
    /// A <see cref="MasterBuildResult"/>: <see cref="MasterBuildResult.Success"/> with the actual frame
    /// count and published paths, or a failure carrying the reason. A failed or cancelled build leaves
    /// no output behind.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.
    /// Cancellation is not reported as a build failure; the caller asked for it.</exception>
    /// <remarks>
    /// Both outputs are written to temporary files next to their destinations and only renamed into
    /// place once every write succeeded, so a failure never leaves a half-updated master set (for
    /// example a new <c>.tmp</c> beside a stale PGM).
    /// </remarks>
    public static MasterBuildResult Build(
        IReadOnlyList<string> files,
        string? subtractFile,
        string? masterPath,
        string? pgmPath,
        int frameLimit,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
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
            bool pgmPublished = false;

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

                // Publish the fallback first and the authoritative .tmp last: a .tmp is what the
                // loader prefers, so it must never appear beside a PGM from a previous build.
                if (pgmStaging is not null)
                {
                    File.Move(pgmStaging, pgmFull!, overwrite: true);
                    pgmPublished = true;
                }

                if (masterStaging is not null)
                {
                    try
                    {
                        File.Move(masterStaging, masterFull!, overwrite: true);
                    }
                    catch
                    {
                        // Do not leave a new PGM published without its .tmp beside it.
                        if (pgmPublished) TryDelete(pgmFull!);
                        throw;
                    }
                }
            }
            finally
            {
                if (masterStaging is not null) TryDelete(masterStaging);
                if (pgmStaging is not null) TryDelete(pgmStaging);
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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
