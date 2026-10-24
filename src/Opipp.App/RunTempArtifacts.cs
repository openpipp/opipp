#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace PIPP_Application;

/// <summary>
/// The files one run created under its output directory that may be deleted when the run is
/// cancelled.
/// </summary>
/// <remarks>
/// <para>
/// Cleanup used to enumerate every <c>*.tmp</c> in the output directory and delete it. The output
/// directory is often an existing folder the operator owns (and with the default settings it is the
/// source folder itself), so an unrelated <c>keeper.tmp</c> was destroyed by pressing Cancel. A run
/// may only remove what it registered here, and registration is refused for a path outside the run's
/// own output directory, so no bug can reach a file the run did not create.
/// </para>
/// <para>
/// The calibration masters are <b>not</b> registered: they live in the session temp directory
/// (<see cref="Form1.TempDirectoryForTest"/>), are reused across runs and are removed when the form
/// closes. No current stage writes a scratch <c>.tmp</c> into the output tree; the registry exists so
/// the only reachable deletion is a path the run itself created.
/// </para>
/// <para>
/// The one file the engine stages in the output tree is its integrated image, written inside a
/// run-owned directory as <c>.opipp-staging/&lt;run-guid&gt;/&lt;name&gt;.integrating&lt;ext&gt;</c> and
/// moved onto the final name only after the whole integration succeeded; the engine removes that
/// directory in its own <c>finally</c>, and a later run removes stale GUID directories left by a hard
/// kill (older than an hour). A hard kill of the engine process — which is how the GUI cancels a run
/// — skips that <c>finally</c>, so a run directory can survive a cancel until the next run. The
/// front-end never creates that path and does not sweep it: the engine owns the cleanup, and a GUI
/// pattern match could delete another run's staging directory or an operator's own file.
/// </para>
/// </remarks>
internal sealed class RunTempArtifacts
{
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry for one run writing under <paramref name="outputRoot"/>.</summary>
    /// <param name="outputRoot">The run's output directory (quotes and padding are ignored).</param>
    public RunTempArtifacts(string? outputRoot)
    {
        Root = Normalize(outputRoot);
    }

    /// <summary>The run's output directory, without quotes or padding; empty when unknown.</summary>
    public string Root { get; }

    /// <summary>Number of artifacts registered by this run.</summary>
    public int Count => _paths.Count;

    /// <summary>
    /// Records a scratch file this run just created. A path outside <see cref="Root"/>, an empty
    /// path, or a path that does not exist is refused.
    /// </summary>
    /// <returns>True when the path is owned by this run and will be cleaned up on cancel.</returns>
    public bool Register(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Root.Length == 0)
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!IsUnder(full, Root))
        {
            return false;
        }

        if (!File.Exists(full))
        {
            return false;
        }

        _paths.Add(full);
        return true;
    }

    /// <summary>
    /// Deletes every artifact this run created, and only those. A file that is already gone is not
    /// an error; a file that cannot be deleted (still held open by a just-killed engine) is skipped
    /// so one locked artifact cannot stop the rest of the cleanup or the form's reset.
    /// </summary>
    /// <returns>The number of files actually removed.</returns>
    public int DeleteAll()
    {
        int deleted = 0;
        foreach (string path in _paths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        _paths.Clear();
        return deleted;
    }

    private static bool IsUnder(string path, string root)
    {
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim('"', ' '))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }
}
