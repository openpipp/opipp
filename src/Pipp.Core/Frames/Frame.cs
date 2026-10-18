namespace Pipp.Core.Frames;

/// <summary>Bayer mosaic pattern of a single-plane frame. Values match PIPP's debayer pattern indices.</summary>
public enum BayerPattern : byte
{
    None = 0,
    RGGB = 1,
    GRBG = 2,
    GBRG = 3,
    BGGR = 4,
}

/// <summary>
/// One image frame at PIPP's working precision. Samples are unsigned 16-bit regardless of the source
/// bit depth; 8-bit sources are stored in the 0..255 range and flagged with <see cref="BitDepth"/> = 8.
/// Colour frames are interleaved R,G,B per pixel (3 channels); mono/Bayer frames have 1 channel.
/// </summary>
public sealed class Frame
{
    public Frame(int width, int height, int channels, int bitDepth, ushort[]? samples = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (channels is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(channels));
        if (bitDepth is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(bitDepth));

        Width = width;
        Height = height;
        Channels = channels;
        BitDepth = bitDepth;
        int required = checked(width * height * channels);
        if (samples is null)
        {
            Samples = new ushort[required];
        }
        else
        {
            if (samples.Length != required)
                throw new ArgumentException($"expected {required} samples, got {samples.Length}", nameof(samples));
            Samples = samples;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }
    public int BitDepth { get; }
    public int SampleCount => Samples.Length;
    public bool IsColour => Channels == 3;

    /// <summary>Row-major, channel-interleaved sample buffer.</summary>
    public ushort[] Samples { get; }

    public BayerPattern Bayer { get; set; } = BayerPattern.None;

    /// <summary>Frame index inside its source (0-based), used by progress and naming.</summary>
    public long Index { get; set; }

    /// <summary>Source file the frame came from, if any.</summary>
    public string? SourceName { get; set; }

    public ref ushort this[int x, int y, int c = 0] => ref Samples[(y * Width + x) * Channels + c];

    public Frame Clone()
    {
        var f = new Frame(Width, Height, Channels, BitDepth, (ushort[])Samples.Clone())
        {
            Bayer = Bayer,
            Index = Index,
            SourceName = SourceName,
        };
        return f;
    }

    public void Clear() => Array.Clear(Samples);
}

/// <summary>A sequential reader over a frame container (SER, FITS, AVI, image folder, RAW).</summary>
public interface IFrameSource : IDisposable
{
    int Width { get; }
    int Height { get; }
    int Channels { get; }
    int BitDepth { get; }
    int FrameCount { get; }
    BayerPattern Bayer { get; }

    /// <summary>Reads one frame; returns null when the source is exhausted.</summary>
    Frame? ReadFrame();
}

/// <summary>A writer for one output container.</summary>
public interface IFrameSink : IDisposable
{
    void Write(Frame frame);
}
