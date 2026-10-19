namespace Opipp.Core.Pipeline;

/// <summary>
/// Outcome of a <see cref="MasterFrameBuilder.Build"/> call: whether the master was produced, how many
/// frames actually went into it, and where the artifacts were published.
/// </summary>
/// <remarks>
/// The builder used to return <c>-1</c> on every failure, which callers could not distinguish from a
/// legitimate count and which the GUI ignored. A failure now carries the diagnostic so the front-end
/// can show it and refuse to launch the engine with a master that does not exist.
/// </remarks>
public sealed class MasterBuildResult
{
    private MasterBuildResult(bool success, int frameCount, string? masterPath, string? pgmPath, string? error)
    {
        Success = success;
        FrameCount = frameCount;
        MasterPath = masterPath;
        PgmPath = pgmPath;
        Error = error;
    }

    /// <summary>True when every requested output was written and published.</summary>
    public bool Success { get; }

    /// <summary>Number of frames actually accumulated (0 on failure); never the requested limit.</summary>
    public int FrameCount { get; }

    /// <summary>The published <c>.tmp</c> master, or null when none was requested or the build failed.</summary>
    public string? MasterPath { get; }

    /// <summary>The published PGM, or null when none was requested or the build failed.</summary>
    public string? PgmPath { get; }

    /// <summary>Human-readable failure reason; null on success.</summary>
    public string? Error { get; }

    /// <summary>Records a successful build; paths must be the ones actually published.</summary>
    /// <param name="frameCount">Frames actually accumulated.</param>
    /// <param name="masterPath">Published <c>.tmp</c> path, or null when not requested.</param>
    /// <param name="pgmPath">Published PGM path, or null when not requested.</param>
    public static MasterBuildResult Built(int frameCount, string? masterPath, string? pgmPath) =>
        new(true, frameCount, masterPath, pgmPath, null);

    /// <summary>Records a failed build. Nothing is published when a build fails.</summary>
    /// <param name="error">Why the master could not be produced.</param>
    public static MasterBuildResult Failed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(false, 0, null, null, error);
    }

    /// <inheritdoc />
    public override string ToString() =>
        Success ? $"master built from {FrameCount} frame(s)" : $"master build failed: {Error}";
}
