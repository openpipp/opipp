namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>Shared helpers for the processing stages. Not part of the public API.</summary>
internal static class ProcessingUtil
{
    /// <summary>Largest sample value representable at the frame's bit depth (255 or 65535).</summary>
    internal static int MaxValue(Frame frame) => frame.BitDepth == 8 ? 255 : 65535;

    /// <summary>
    /// Creates an output frame with the same bit depth and provenance metadata as <paramref name="source"/>.
    /// The sample buffer is taken as-is.
    /// </summary>
    internal static Frame NewLike(Frame source, int width, int height, int channels, ushort[] samples) =>
        new(width, height, channels, source.BitDepth, samples)
        {
            Bayer = source.Bayer,
            Index = source.Index,
            SourceName = source.SourceName,
        };

    /// <summary>Clamps a sample value to the frame's representable range.</summary>
    internal static ushort ClampSample(long value, int max) =>
        (ushort)Math.Clamp(value, 0L, max);
}
