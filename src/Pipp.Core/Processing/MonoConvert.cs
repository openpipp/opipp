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

    /// <summary>Engine 3: Rec.601 luma of red, green and blue.</summary>
    All = 3,

    /// <summary>Engine 4: luma-weighted red+green pair, renormalised.</summary>
    RedGreen = 4,

    /// <summary>Engine 5: luma-weighted red+blue pair, renormalised.</summary>
    RedBlue = 5,

    /// <summary>Engine 6: luma-weighted green+blue pair, renormalised.</summary>
    GreenBlue = 6,
}

/// <summary>
/// Colour-to-mono conversion (PIPP's <c>-convmono</c> stage).
/// </summary>
/// <remarks>
/// <para>
/// The seven modes were bisected against <c>pipp_core.exe</c> with a 1,056-combination 8-bit colour
/// grid plus 300 random 16-bit triples: modes 0-2 keep one channel, mode 3 is
/// <c>(299R + 587G + 114B) / 1000</c>, and modes 4-6 apply the same Rec.601 weights to two channels
/// and divide by their sum (<c>886</c>, <c>413</c>, <c>701</c>). All matching with zero
/// disagreements on both depths. Integer arithmetic truncates, like the engine's C division, and
/// avoids the float rounding that makes <c>(128,128,128)</c> come out as 127.
/// </para>
/// <para>
/// Mono input is returned as a clone regardless of the requested mode.
/// </para>
/// </remarks>
public static class MonoConvert
{
    private const int RedWeight = 299;
    private const int GreenWeight = 587;
    private const int BlueWeight = 114;

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
            int r = src[s];
            int g = src[s + 1];
            int b = src[s + 2];
            dst[i] = mode switch
            {
                MonoMode.Blue => (ushort)b,
                MonoMode.Green => (ushort)g,
                MonoMode.Red => (ushort)r,
                MonoMode.All => (ushort)((RedWeight * r + GreenWeight * g + BlueWeight * b) / 1000),
                MonoMode.RedGreen => (ushort)((RedWeight * r + GreenWeight * g) / (RedWeight + GreenWeight)),
                MonoMode.RedBlue => (ushort)((RedWeight * r + BlueWeight * b) / (RedWeight + BlueWeight)),
                MonoMode.GreenBlue => (ushort)((GreenWeight * g + BlueWeight * b) / (GreenWeight + BlueWeight)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown mono conversion mode"),
            };
        }

        return ProcessingUtil.NewLike(frame, w, h, 1, dst);
    }
}
