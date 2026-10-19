#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Opipp.Core.Formats;
using Opipp.Core.Frames;
using Opipp.Core.Pipeline;

namespace PIPP_Application;

/// <summary>Builds one calibration master for the front-end.</summary>
/// <remarks>
/// The production implementation is <see cref="MasterFrameBuild"/>. The indirection exists so the
/// cancellation path can be exercised deterministically: a real build of a handful of frames
/// finishes before a test can press Cancel.
/// </remarks>
internal interface IMasterFrameBuild
{
    /// <summary>Builds one master; the contract is <see cref="MasterFrameBuilder.Build"/>.</summary>
    MasterBuildResult Build(
        IReadOnlyList<string> files,
        string? subtractFile,
        string? masterPath,
        string? pgmPath,
        int frameLimit,
        Action<int>? progress,
        CancellationToken cancellationToken);
}

/// <summary>The production master builder: <see cref="MasterFrameBuilder.Build"/>.</summary>
internal sealed class MasterFrameBuild : IMasterFrameBuild
{
    /// <inheritdoc />
    public MasterBuildResult Build(
        IReadOnlyList<string> files,
        string? subtractFile,
        string? masterPath,
        string? pgmPath,
        int frameLimit,
        Action<int>? progress,
        CancellationToken cancellationToken) =>
        MasterFrameBuilder.Build(files, subtractFile, masterPath, pgmPath, frameLimit, progress, cancellationToken);
}

/// <summary>
/// Writes the master artifacts the engine can no longer write for the front-end.
/// </summary>
internal static class MasterHandoffArtifacts
{
    /// <summary>Name the engine uses for the saved master flat dark (<c>-save_flat_dark</c>).</summary>
    public const string FlatDarkTiffName = "pipp_master_flat_dark.tif";

    /// <summary>
    /// Saves a front-end-built flat-dark master where <c>-save_flat_dark</c> would have put it:
    /// <c>&lt;output&gt;/logs/pipp_master_flat_dark.tif</c>.
    /// </summary>
    /// <param name="masterTmpPath">The <c>.tmp</c> master the front-end built or reused.</param>
    /// <param name="outputDirectory">The run's <c>-od</c> directory (quotes and padding are ignored).</param>
    /// <returns>The TIFF path that was written.</returns>
    /// <remarks>
    /// The GUI prepares its flat itself and therefore does not pass <c>-flat_dark_tmp</c> to the
    /// engine. That is what stops the double subtraction, but it also means the engine's
    /// <c>-save_flat_dark</c> stage has no <see cref="Opipp.Core.Pipeline.PipelineSettings.MasterFlatDark"/>
    /// to re-emit, so the front-end has to write the file it owns.
    /// </remarks>
    /// <exception cref="InvalidDataException">The master file is not a valid <c>.tmp</c> master.</exception>
    public static string SaveFlatDarkMaster(string masterTmpPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterTmpPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string root = outputDirectory.Trim('"', ' ');
        string destination = Path.Combine(root, "logs", FlatDarkTiffName);

        Frame master = MasterTmpFile.Read(masterTmpPath);
        MasterFrameBuilder.WriteMaster(master, destination);
        return destination;
    }
}
