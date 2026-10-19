using System;
using System.Reflection;

namespace PIPP_Application;

/// <summary>
/// Single source for the version string shown in the window title, the About box and the output pane.
/// </summary>
/// <remarks>
/// The version is read from the assembly's informational version, which comes from
/// <c>Directory.Build.props</c> at build time. Hard-coded title strings went stale twice; this cannot.
/// </remarks>
internal static class VersionInfo
{
    public static string Display { get; } = Resolve();

    private static string Resolve()
    {
        Assembly assembly = typeof(VersionInfo).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // SourceLink appends "+<commit>" in some builds; the user-facing version excludes it.
            return informational.Split('+')[0];
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
