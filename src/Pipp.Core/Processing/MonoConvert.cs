namespace Pipp.Core.Processing;

using Pipp.Core.Frames;

/// <summary>PIPP mono-conversion modes (the <c>-convmono</c> engine values 0..6).</summary>
public enum MonoMode
{
    /// <summary>Engine 0: keep the blue channel only.</summary>
    Blue = 0,

    /// <summary>Engine 1: keep the green channel only.</summary>
    Green = 1,

    /// <summary>Engine 2: keep the red channel only.</summary>
    Red = 2,

    /// <summary>Engine 3: average of red, green and blue.</summary>
    All = 3,

    /// <summary>Engine 4: average of red and green.</summary>
    RedGreen = 4,

    /// <summary>Engine 5: average of red and blue.</summary>
    RedBlue = 5,

    /// <summary>Engine 6: average of green and blue.</summary>
    GreenBlue = 6,
}

/// <summary>
/// Colour-to-mono conversion (PIPP's <c>-convmono</c> stage). Averages use truncating integer
/// arithmetic, matching the engine's C-style division: <c>(a + b) / 2</c> and <c>(r + g + b) / 3</c>.
/// Mono input is returned as a clone regardless of the requested mode.
/// </summary>
public static class MonoConvert
{
    /// <summary>Converts a colour frame to a single-channel frame using the requested mode.</summary>
    /// <param name="frame">Source frame.</param>
    /// <param name="mode">One of the seven PIPP conversion modes.</param>
    /// <returns>
    /// A new one-channel frame with the source bit depth and metadata. For mono input this is a clone.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    public static Frame Apply(Frame frame, MonoMode mode)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown mono conversion mode");
        if (!frame.IsColour) return frame.Clone();

        int w = frame.Width, h = frame.Height;
        var src = frame.Samples;
        var dst = new ushort[w * h];
        int pixels = w * h;
        for (int i = 0; i < pixels; i++)
        {
            int s = i * 3;
            ushort r = src[s];
            ushort g = src[s + 1];
            ushort b = src[s + 2];
            dst[i] = mode switch
            {
                MonoMode.Blue => b,
                MonoMode.Green => g,
                MonoMode.Red => r,
                MonoMode.All => (ushort)((r + g + b) / 3),
                MonoMode.RedGreen => (ushort)((r + g) / 2),
                MonoMode.RedBlue => (ushort)((r + b) / 2),
                MonoMode.GreenBlue => (ushort)((g + b) / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown mono conversion mode"),
            };
        }

        return ProcessingUtil.NewLike(frame, w, h, 1, dst);
    }
}
