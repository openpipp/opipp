using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace PIPP_Application;

/// <summary>
/// The single place that knows how to find the processing engine.
/// </summary>
/// <remarks>
/// The original UI launched <c>pipp_core.exe</c> next to PIPP.exe and piped the command line into its
/// standard input. The reconstruction's engine is <c>Opipp.Cli.exe</c>, which speaks the same stdin
/// protocol and the same <c>@TOKEN</c> stdout progress protocol, so the UI can drive it unchanged.
/// The legacy native binary is still accepted as a fallback so an installed copy keeps working.
/// </remarks>
internal static class EngineLocator
{
    /// <summary>File name of the managed engine that replaces <c>pipp_core.exe</c>.</summary>
    public const string ManagedEngineName = "Opipp.Cli.exe";

    /// <summary>File name of the original native engine, used only as a fallback.</summary>
    public const string LegacyEngineName = "pipp_core.exe";

    /// <summary>
    /// Returns the full path of the executable to launch: the managed engine when it can be found
    /// (application directory, then the sibling build tree), otherwise the legacy binary.
    /// </summary>
    public static string FindEngineExecutable()
    {
        string appDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? AppContext.BaseDirectory;

        foreach (string candidate in EnumerateCandidates(appDirectory))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Nothing found: name the managed engine so the error points at the intended target.
        return Path.Combine(appDirectory, ManagedEngineName);
    }

    /// <summary>True when <paramref name="path"/> is the managed engine rather than the legacy binary.</summary>
    public static bool IsManagedEngine(string path) =>
        string.Equals(Path.GetFileName(path), ManagedEngineName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateCandidates(string appDirectory)
    {
        yield return Path.Combine(appDirectory, ManagedEngineName);
        yield return Path.Combine(appDirectory, LegacyEngineName);

        // Running from src/Opipp.App/bin/<Configuration>/net10.0-windows: look for a sibling build.
        string? directory = appDirectory;
        for (int depth = 0; depth < 6 && directory is not null; depth++)
        {
            directory = Path.GetDirectoryName(directory);
            if (directory is null) break;

            foreach (string configuration in new[] { "Debug", "Release" })
            {
                yield return Path.Combine(directory, "src", "Opipp.Cli", "bin", configuration, "net10.0", ManagedEngineName);
            }
        }
    }
}
