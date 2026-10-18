using Pipp.Core.Frames;

namespace Pipp.Core.Formats;

/// <summary>Metadata discovered by <see cref="FitsProbe"/> without decoding pixel data.</summary>
public sealed class FitsProbeResult
{
    /// <summary>Image width in pixels (NAXIS1).</summary>
    public required int Width { get; init; }

    /// <summary>Image height in pixels (NAXIS2).</summary>
    public required int Height { get; init; }

    /// <summary>1 for mono/Bayer data, 3 for RGB cubes.</summary>
    public required int Channels { get; init; }

    /// <summary>PIPP working bit depth the image maps to: 8 or 16.</summary>
    public required int BitDepth { get; init; }

    /// <summary>Number of sequential image HDUs with the same shape (usually 1).</summary>
    public required int FrameCount { get; init; }

    /// <summary>Bayer pattern when BAYERPAT is present and recognised.</summary>
    public required BayerPattern Bayer { get; init; }

    /// <summary>BAYOFFX, or -1 when absent.</summary>
    public required int BayerOffsetX { get; init; }

    /// <summary>BAYOFFY, or -1 when absent.</summary>
    public required int BayerOffsetY { get; init; }

    /// <summary>Original FITS BITPIX of the first image HDU.</summary>
    public required int BitPix { get; init; }

    /// <summary>NAXIS of the first image HDU.</summary>
    public required int Naxis { get; init; }

    /// <summary>BZERO of the first image HDU, or 0 when absent.</summary>
    public required double BZero { get; init; }

    /// <summary>BSCALE of the first image HDU, or 1 when absent.</summary>
    public required double BScale { get; init; }

    /// <summary>DATE card text from the first image HDU, or null.</summary>
    public string? Date { get; init; }

    /// <summary>True when the file is shorter than the declared pixel payload.</summary>
    public required bool IsTruncated { get; init; }

    /// <summary>Description of the truncation, when any.</summary>
    public string? TruncationMessage { get; init; }

    /// <summary>The parsed header of the first image HDU.</summary>
    public required FitsHeader Header { get; init; }
}

/// <summary>Reads FITS metadata from a path without loading pixel data.</summary>
public static class FitsProbe
{
    /// <summary>Probes a FITS file for its image geometry, bit depth and Bayer metadata.</summary>
    /// <param name="path">Path to the FITS file.</param>
    /// <returns>The probe result.</returns>
    /// <exception cref="FitsFormatException">No supported image HDU was found.</exception>
    public static FitsProbeResult Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new FitsReader(path);
        return new FitsProbeResult
        {
            Width = reader.Width,
            Height = reader.Height,
            Channels = reader.Channels,
            BitDepth = reader.BitDepth,
            FrameCount = reader.FrameCount,
            Bayer = reader.Bayer,
            BayerOffsetX = reader.BayerOffsetX,
            BayerOffsetY = reader.BayerOffsetY,
            BitPix = reader.BitPix,
            Naxis = reader.Naxis,
            BZero = reader.BZero,
            BScale = reader.BScale,
            Date = reader.Header.GetString("DATE"),
            IsTruncated = reader.IsTruncated,
            TruncationMessage = reader.TruncationMessage,
            Header = reader.Header,
        };
    }
}
