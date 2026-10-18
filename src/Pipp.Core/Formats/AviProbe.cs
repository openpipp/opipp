namespace Pipp.Core.Formats;

/// <summary>
/// Metadata-only AVI inspector. <see cref="GetInfo(string)"/> reports the first video stream's geometry,
/// pixel format, frame rate and frame count without decoding frames, so it also works for compressed codecs
/// that <see cref="AviReader"/> refuses.
/// </summary>
public static class AviProbe
{
    /// <summary>Reads the AVI metadata from a file.</summary>
    /// <param name="path">Path to the AVI file.</param>
    /// <returns>The first video stream's metadata.</returns>
    /// <exception cref="InvalidDataException">The file is not a readable RIFF/AVI file or has no video stream header.</exception>
    public static AviInfo GetInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return GetInfo(stream);
    }

    /// <summary>Reads the AVI metadata from a seekable stream positioned at the file's first byte.</summary>
    /// <param name="stream">Readable, seekable stream containing an AVI file.</param>
    /// <returns>The first video stream's metadata.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    /// <exception cref="InvalidDataException">The stream is not a readable RIFF/AVI file or has no video stream header.</exception>
    public static AviInfo GetInfo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return AviStructureReader.CreateInfo(AviStructureReader.Read(stream));
    }
}
