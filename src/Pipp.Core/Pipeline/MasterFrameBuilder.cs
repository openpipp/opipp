using Pipp.Core.Formats;
using Pipp.Core.Frames;
using Pipp.Core.Quality;
using Pipp.Core.Stacking;

namespace Pipp.Core.Pipeline;

/// <summary>
/// Builds master calibration frames from a set of input files and writes the two artifacts the
/// front-end hands to the engine: a TIFF master and a normalised 8/16-bit PGM.
/// </summary>
/// <remarks>
/// This is the managed replacement for <c>pipp_dll!stack_frames</c>. The original contract (read via
/// REA) is: average the frames, optionally subtract one reference frame from every input before
/// accumulating, refuse inputs whose geometry or sample size differs, report progress, and return a
/// negative value on failure. The PGM is the copy the engine actually consumes (<c>-dark_pgm</c>),
/// so a colour master is reduced to luma for that file.
/// </remarks>
public static class MasterFrameBuilder
{
    /// <summary>Builds a master frame from the first <paramref name="frameLimit"/> frames found in <paramref name="files"/>.</summary>
    /// <param name="files">Calibration inputs (SER, AVI, FITS, BMP, TIFF or PNG).</param>
    /// <param name="subtractFile">Optional file whose first frame is subtracted from every input.</param>
    /// <param name="tiffPath">Destination TIFF, or null/empty to skip.</param>
    /// <param name="pgmPath">Destination PGM, or null/empty to skip.</param>
    /// <param name="frameLimit">Maximum frames to accumulate; 0 or less means all frames.</param>
    /// <param name="progress">Receives 0..100.</param>
    /// <returns>The number of frames stacked, or -1 when the inputs are unusable.</returns>
    public static int Build(
        IReadOnlyList<string> files,
        string? subtractFile,
        string? tiffPath,
        string? pgmPath,
        int frameLimit,
        Action<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0) return -1;

        try
        {
            Frame? subtract = null;
            if (!string.IsNullOrWhiteSpace(subtractFile) && File.Exists(subtractFile))
            {
                using var subSource = FrameSourceFactory.Open(subtractFile!);
                subtract = subSource.ReadFrame();
            }

            FrameStacker? stacker = null;
            int stacked = 0;
            int lastReported = -1;

            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;

                using var source = FrameSourceFactory.Open(file);
                while (true)
                {
                    Frame? frame = source.ReadFrame();
                    if (frame is null) break;

                    if (stacker is null)
                    {
                        stacker = new FrameStacker(frame.Width, frame.Height, frame.Channels, frame.BitDepth, StackMethod.Mean);
                    }
                    else if (frame.Width != stacker.Width || frame.Height != stacker.Height ||
                             frame.Channels != stacker.Channels || frame.BitDepth != stacker.BitDepth)
                    {
                        // Same failure mode as the native stacker: refuse mixed geometries.
                        return -1;
                    }

                    stacker.Add(frame, subtract);
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

            if (stacker is null || stacked == 0) return -1;

            Frame master = stacker.Result();

            if (!string.IsNullOrWhiteSpace(tiffPath))
            {
                using var tiff = new TiffWriter(tiffPath!, master.Width, master.Height, master.Channels, master.BitDepth);
                tiff.Write(master);
            }

            if (!string.IsNullOrWhiteSpace(pgmPath))
            {
                Frame pgm = master.Channels == 1
                    ? master
                    : new Frame(master.Width, master.Height, 1, master.BitDepth, QualityMetrics.ToLuma(master));
                PgmFile.Write(pgmPath!, pgm);
            }

            progress?.Invoke(100);
            return stacked;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return -1;
        }
    }
}
