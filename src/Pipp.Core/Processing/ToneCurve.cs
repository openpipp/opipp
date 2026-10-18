namespace Pipp.Core.Processing;

using Pipp.Core.Frames;

/// <summary>
/// Input gain and gamma correction (PIPP's input gain/gamma stage), implemented as a lookup table
/// over the frame's full sample range. The same curve is applied to every channel and out-of-range
/// values are clamped to the frame maximum (255 for 8-bit, 65535 for 16-bit).
/// </summary>
/// <remarks>
/// Gain: <c>out = clamp(round(v * gain))</c>.<br/>
/// Gamma: <c>out = clamp(round(max * (v / max) ^ (1 / gamma)))</c>, the conventional display-gamma
/// curve, so <c>gamma &gt; 1</c> brightens and <c>gamma &lt; 1</c> darkens mid-tones while 0 and
/// <c>max</c> stay fixed.
/// </remarks>
public static class ToneCurve
{
    /// <summary>Multiplies every sample by <paramref name="gain"/> and clamps at full scale.</summary>
    /// <param name="frame">Source frame.</param>
    /// <param name="gain">Non-negative, finite multiplier (1.0 is identity).</param>
    /// <returns>A new frame with the same dimensions and bit depth.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="gain"/> is negative, NaN or infinite.
    /// </exception>
    public static Frame ApplyGain(Frame frame, double gain)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!double.IsFinite(gain) || gain < 0)
            throw new ArgumentOutOfRangeException(nameof(gain), gain, "gain must be finite and non-negative");

        int max = ProcessingUtil.MaxValue(frame);
        var lut = new ushort[max + 1];
        for (int v = 0; v <= max; v++)
        {
            double scaled = v * gain;
            lut[v] = scaled >= max
                ? (ushort)max
                : (ushort)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        return ApplyLut(frame, lut);
    }

    /// <summary>Applies the gamma curve <c>max * (v/max)^(1/gamma)</c> with clamping.</summary>
    /// <param name="frame">Source frame.</param>
    /// <param name="gamma">Positive, finite gamma (1.0 is identity; values above 1 brighten).</param>
    /// <returns>A new frame with the same dimensions and bit depth.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="gamma"/> is not positive, NaN or infinite.
    /// </exception>
    public static Frame ApplyGamma(Frame frame, double gamma)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!double.IsFinite(gamma) || gamma <= 0)
            throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "gamma must be finite and positive");

        int max = ProcessingUtil.MaxValue(frame);
        double exponent = 1.0 / gamma;
        var lut = new ushort[max + 1];
        for (int v = 0; v <= max; v++)
        {
            double normalized = v == 0 ? 0.0 : Math.Pow((double)v / max, exponent);
            lut[v] = ProcessingUtil.ClampSample(
                (long)Math.Round(max * normalized, MidpointRounding.AwayFromZero), max);
        }

        return ApplyLut(frame, lut);
    }

    private static Frame ApplyLut(Frame frame, ushort[] lut)
    {
        int max = lut.Length - 1;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            int v = src[i];
            dst[i] = lut[v > max ? max : v];
        }

        return ProcessingUtil.NewLike(frame, frame.Width, frame.Height, frame.Channels, dst);
    }
}
