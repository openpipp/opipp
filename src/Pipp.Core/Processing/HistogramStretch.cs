namespace Pipp.Core.Processing;

using Pipp.Core.Frames;

/// <summary>
/// Histogram stretch stage (PIPP's <c>-hs</c>/<c>-hs=xx%</c>/<c>-hs_bp</c> handling).
/// </summary>
/// <remarks>
/// The white point is always expressed in 8-bit units (PIPP's default is 204, i.e. 80% of 255) and is
/// scaled to the frame's bit depth by multiplying by 257 for 16-bit frames. With
/// <c>preserveBlackPoint = false</c> the mapping is a plain linear scale of every sample:
/// <c>out = clamp(round(v * max / white), 0, max)</c>. With <c>preserveBlackPoint = true</c> the
/// measured black point is the minimum sample in the frame (across all channels) and the mapping is
/// <c>out = clamp(round((v - black) * max / (white - black)), 0, max)</c> for <c>v &gt;= black</c>;
/// smaller values become 0. If <c>white &lt;= black</c> the stretch is degenerate and every value at or
/// above the black point maps to <c>max</c>.
/// </remarks>
public static class HistogramStretch
{
    /// <summary>
    /// Scales the frame by <c>whitePoint/100</c>, truncating and clipping at full scale.
    /// </summary>
    /// <remarks>
    /// This is the integer form of the engine's <c>-hs=xxxx</c> switch, measured against the original
    /// binary rather than inferred from its help text. For an 8-bit frame the oracle maps
    /// 38 -&gt; 77 and 123 -&gt; 250 at <c>whitePoint = 204</c>, 38 -&gt; 58 and 123 -&gt; 188 at 153,
    /// and 38 -&gt; 38 and 123 -&gt; 125 at 102: exactly <c>truncate(v * whitePoint / 100)</c> with a
    /// clamp. The help text's "stretch to maximum pixel value 204" therefore means a 2.04x gain,
    /// not "204 becomes 255".
    /// </remarks>
    /// <param name="frame">Source frame (1 or 3 channels).</param>
    /// <param name="whitePoint">Gain percentage, 1..255 (PIPP default 204).</param>
    /// <param name="preserveBlackPoint">
    /// When true, the frame's minimum sample is treated as black and offset to 0 before the gain;
    /// when false every value is scaled from zero.
    /// </param>
    /// <returns>A new stretched frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="whitePoint"/> is outside 1..255.</exception>
    public static Frame StretchToWhitePoint(Frame frame, int whitePoint, bool preserveBlackPoint = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (whitePoint is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(whitePoint), whitePoint, "white point must be 1..255");

        return StretchCore(frame, whitePoint, 100, preserveBlackPoint);
    }

    /// <summary>
    /// Maps the frame using the percentage spelling of the switch (<c>-hs=xx%</c>), interpreted as a
    /// white point at <paramref name="percent"/> of full scale, i.e. a gain of <c>100/percent</c>.
    /// </summary>
    /// <remarks>
    /// The integer spelling is verified against the original engine (see
    /// <see cref="StretchToWhitePoint"/>); this percentage form has not been measured against the
    /// oracle yet, so it is documented as inferred. 100% is a no-op.
    /// </remarks>
    /// <param name="frame">Source frame.</param>
    /// <param name="percent">White point as a percentage of full scale, 1..100.</param>
    /// <param name="preserveBlackPoint">See <see cref="StretchToWhitePoint"/>.</param>
    /// <returns>A new stretched frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is outside 1..100.</exception>
    public static Frame StretchToPercent(Frame frame, int percent, bool preserveBlackPoint = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (percent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "percentage must be 1..100");
        return StretchCore(frame, 100, percent, preserveBlackPoint);
    }

    /// <summary>
    /// Equalises the colour channels before stretching: each of R, G and B is scaled so its maximum
    /// sample reaches the largest channel maximum in the frame (<c>out = round(v * target / channelMax)</c>).
    /// This is PIPP's colour equalisation step; call it before <see cref="StretchToWhitePoint"/>.
    /// Mono frames are returned as a clone.
    /// </summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>A new colour-balanced frame, or a clone for mono input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame EqualiseRgb(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.IsColour) return frame.Clone();

        var src = frame.Samples;
        var channelMax = new long[3];
        for (int i = 0; i < src.Length; i += 3)
        {
            if (src[i] > channelMax[0]) channelMax[0] = src[i];
            if (src[i + 1] > channelMax[1]) channelMax[1] = src[i + 1];
            if (src[i + 2] > channelMax[2]) channelMax[2] = src[i + 2];
        }

        long target = Math.Max(channelMax[0], Math.Max(channelMax[1], channelMax[2]));
        if (target == 0)
        {
            var zero = new ushort[src.Length];
            return ProcessingUtil.NewLike(frame, frame.Width, frame.Height, 3, zero);
        }

        int max = ProcessingUtil.MaxValue(frame);
        var dst = new ushort[src.Length];
        for (int i = 0; i < src.Length; i += 3)
        {
            for (int ch = 0; ch < 3; ch++)
            {
                long channel = channelMax[ch];
                dst[i + ch] = channel == 0
                    ? (ushort)0
                    : ProcessingUtil.ClampSample((src[i + ch] * target + channel / 2) / channel, max);
            }
        }

        return ProcessingUtil.NewLike(frame, frame.Width, frame.Height, 3, dst);
    }

    /// <summary>
    /// Builds a lookup table applying <c>(int)((v - black) * gain)</c>, where the gain is a
    /// single-precision value.
    /// </summary>
    /// <remarks>
    /// The engine carries a one-unit bias: measured against the original at <c>-hs=204</c>,
    /// 50 -&gt; 101 (not 102), 75 -&gt; 152, 100 -&gt; 203, 38 -&gt; 77, 123 -&gt; 250; at 153,
    /// 38 -&gt; 58 and 123 -&gt; 188; at 102, 38 -&gt; 38 and 123 -&gt; 125. Every point fits
    /// <c>(v * whitePoint - 1) / 100</c> with truncation, which is what this table applies.
    /// </remarks>
    private static Frame StretchCore(Frame frame, long numerator, long denominator, bool preserveBlackPoint)
    {
        int max = ProcessingUtil.MaxValue(frame);
        long black = preserveBlackPoint ? frame.Samples.Min() : 0;
        var lut = new ushort[max + 1];
        for (int v = 0; v <= max; v++)
        {
            if (v < black)
            {
                lut[v] = 0;
                continue;
            }

            long mapped = denominator <= 0
                ? max
                : ((v - black) * numerator - 1) / denominator;
            lut[v] = ProcessingUtil.ClampSample(mapped < 0 ? 0 : mapped, max);
        }

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
