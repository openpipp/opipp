using Opipp.Core.Frames;
using Opipp.Core.Formats;

namespace Opipp.Core.Pipeline;

/// <summary>Opens any supported container as a frame source, choosing by extension and magic bytes.</summary>
/// <remarks>
/// <c>.tmp</c> is accepted here because the calibration-master workflow reads a previously built
/// <c>pipp_master_*.tmp</c> as the subtraction reference. It is deliberately absent from
/// <see cref="Extensions"/>: that list is the set of user-selectable capture inputs, and a master is
/// not a capture.
/// </remarks>
public static class FrameSourceFactory
{
    /// <summary>Supported input extensions, lowercase, with the leading dot.</summary>
    public static readonly string[] Extensions = [".ser", ".fits", ".fit", ".fts", ".avi", ".bmp", ".tif", ".tiff", ".png", ".dng"];

    /// <summary>Returns true when the path has an extension this pipeline can decode.</summary>
    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Opens a container. The caller owns the returned source.</summary>
    /// <param name="path">Input file.</param>
    /// <param name="serBitDepthOverride">Optional SER bit-depth override (PIPP's <c>-ser_bpp</c>).</param>
    public static IFrameSource Open(string path, int? serBitDepthOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("input file not found", path);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ser" => new SerReader(path, new SerReaderOptions { BitDepthOverride = serBitDepthOverride }),
            ".fits" or ".fit" or ".fts" => new FitsReader(path),
            ".avi" => new AviReader(path),
            ".bmp" => new SingleFrameSource(new BmpReader(path)),
            ".tif" or ".tiff" => new SingleFrameSource(new TiffReader(path)),
            ".png" => new SingleFrameSource(new PngReader(path)),
            ".dng" => new SingleFrameSource(new RawDngReader(path)),
            ".tmp" => new SingleFrameSource(new MasterTmpSource(path)),
            _ => throw new NotSupportedException($"unsupported input format '{ext}'"),
        };
    }
}

/// <summary>
/// Adapts a <c>pipp_master_*.tmp</c> calibration master to the <see cref="IFrameSource"/> contract.
/// </summary>
/// <remarks>
/// The container records geometry, colour flag and sample size but no CFA pattern, so the frame is
/// returned with <see cref="BayerPattern.None"/>; callers that need Bayer semantics (a flat built for
/// <c>-flat_bayer</c>) read the master from the capture pipeline, not from this handoff file.
/// </remarks>
public sealed class MasterTmpSource : IFrameSource
{
    private readonly Frame _frame;
    private bool _read;

    /// <summary>Reads the master; the raster is decoded once and reused.</summary>
    /// <param name="path">A file written by <see cref="MasterTmpFile.Write"/> or the engine's stacker.</param>
    /// <exception cref="InvalidDataException">The file is not a valid master container.</exception>
    public MasterTmpSource(string path)
    {
        _frame = MasterTmpFile.Read(path);
    }

    /// <inheritdoc />
    public int Width => _frame.Width;

    /// <inheritdoc />
    public int Height => _frame.Height;

    /// <inheritdoc />
    public int Channels => _frame.Channels;

    /// <inheritdoc />
    public int BitDepth => _frame.BitDepth;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => _frame.Bayer;

    /// <inheritdoc />
    public Frame? ReadFrame()
    {
        if (_read) return null;
        _read = true;
        return _frame;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>Adapts a single-image reader to the <see cref="IFrameSource"/> contract.</summary>
public sealed class SingleFrameSource : IFrameSource
{
    private readonly IFrameSource _inner;
    private bool _read;

    /// <summary>Wraps an image reader that yields exactly one frame.</summary>
    public SingleFrameSource(IFrameSource inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public int Width => _inner.Width;

    /// <inheritdoc />
    public int Height => _inner.Height;

    /// <inheritdoc />
    public int Channels => _inner.Channels;

    /// <inheritdoc />
    public int BitDepth => _inner.BitDepth;

    /// <inheritdoc />
    public int FrameCount => 1;

    /// <inheritdoc />
    public BayerPattern Bayer => _inner.Bayer;

    /// <inheritdoc />
    public Frame? ReadFrame()
    {
        if (_read) return null;
        _read = true;
        return _inner.ReadFrame();
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}

/// <summary>Writes processed frames to the output container selected by extension.</summary>
public static class FrameSinkFactory
{
    /// <summary>Supported output extensions, lowercase, with the leading dot.</summary>
    public static readonly string[] Extensions = [".ser", ".fits", ".fit", ".avi", ".bmp", ".tif", ".tiff", ".png"];

    /// <summary>Creates a sink sized for <paramref name="template"/>'s geometry.</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="template">A frame describing the output geometry and depth.</param>
    /// <param name="frameCountHint">Expected frame count for containers that store it up front (SER).</param>
    /// <param name="fps">Frames per second for AVI/SER timing metadata.</param>
    /// <param name="aviCodec">Requested AVI codec (<c>-avi_codec</c>); only DIB is implementable.</param>
    /// <param name="aviQuality">Requested AVI codec quality, 1..10 (<c>-avi_quality</c>).</param>
    /// <param name="aviLegacyLayout">Write the legacy AVI header layout (<c>-avi_old</c>).</param>
    public static IFrameSink Create(
        string path,
        Frame template,
        int frameCountHint = 0,
        double fps = 0,
        AviCodec aviCodec = AviCodec.Dib,
        int aviQuality = 10,
        bool aviLegacyLayout = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(template);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ser" => new SerWriter(path, SerHeader.CreateOutput(
                template.Width, template.Height, template.Channels, template.BitDepth, frameCountHint,
                // The template is the delivered frame: its Bayer marker is already phase-corrected
                // for flips/rotations/crops and cleared by binning/resize, so CreateOutput derives the
                // ColorID from it. The source header only supplies the capture text fields.
                SerHeader.TryReadFromSource(template.SourceName), template.Bayer)),
            ".fits" or ".fit" => new FitsWriter(path),
            ".avi" => new AviWriter(path, template.Width, template.Height, new AviWriterOptions
            {
                // The writer defaults to mono; a colour frame must be stored as 24bpp BGR.
                PixelFormat = template.Channels == 3 ? AviPixelFormat.Bgr24 : AviPixelFormat.Mono8,
                Codec = aviCodec,
                Quality = aviQuality,
                // Keep sub-Hz precision for rates such as 29.97 fps.
                FpsRate = fps > 0 ? (int)Math.Round(fps * 1000.0) : 25,
                FpsScale = fps > 0 ? 1000 : 1,
                LegacyLayout = aviLegacyLayout,
            }),
            ".bmp" => new BmpWriter(path, template.Width, template.Height, template.Channels, template.BitDepth),
            ".tif" or ".tiff" => new TiffWriter(path, template.Width, template.Height, template.Channels, template.BitDepth),
            ".png" => new PngWriter(path, template.Width, template.Height, template.Channels, template.BitDepth),
            _ => throw new NotSupportedException($"unsupported output format '{ext}'"),
        };
    }
}
