namespace Pipp.Core.Formats;

/// <summary>
/// Still-image metadata reported by the codec probe methods: geometry plus the depth and channel count
/// a <see cref="Pipp.Core.Frames.Frame"/> decoded from the file would have. Probing reads headers only,
/// never pixel data.
/// </summary>
public readonly record struct ImageInfo
{
    /// <summary>Creates a metadata record.</summary>
    /// <param name="width">Image width in pixels (positive).</param>
    /// <param name="height">Image height in pixels (positive).</param>
    /// <param name="bitDepth"><see cref="Pipp.Core.Frames.Frame.BitDepth"/> of decoded frames: 8 or 16.</param>
    /// <param name="channels">Decoded channel count: 1 for mono, 3 for colour.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its supported range.</exception>
    public ImageInfo(int width, int height, int bitDepth, int channels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "width must be positive");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "height must be positive");
        if (bitDepth is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "bit depth must be 8 or 16");
        if (channels is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(channels), channels, "channels must be 1 or 3");

        Width = width;
        Height = height;
        BitDepth = bitDepth;
        Channels = channels;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Bit depth of the per-channel samples: 8 or 16.</summary>
    public int BitDepth { get; }

    /// <summary>Channel count: 1 for mono, 3 for colour.</summary>
    public int Channels { get; }

    /// <summary>True when the image decodes to three interleaved colour channels.</summary>
    public bool IsColour => Channels == 3;

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height} {BitDepth}-bit {Channels}ch";
}
