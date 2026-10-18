namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>
/// Histogram stretch stage (PIPP's <c>-hs</c>/<c>-hs=xx%</c>/<c>-hs_bp</c>/<c>-hsrgb</c> handling).
/// </summary>
/// <remarks>
/// <para>
/// Recovered from the engine's stretch routine <c>FUN_14009b4a0</c> (called from the frame loop of
/// <c>FUN_14007e8e0</c> for the option at <c>options + 0x1DC</c>). The switch is <b>not</b> a constant
/// gain: the engine measures a white point from the frame itself and normalises to it, so the same
/// <c>-hs</c> value produces a different gain on every frame.
/// </para>
/// <para>
/// The measured white point is the mean of the frame's <see cref="TailSampleCount"/> brightest samples
/// (per channel), accumulated from the top of the histogram with a weighted boundary bin and divided
/// with integer truncation - <c>0x19</c> = 25 samples in the decompiled code. The gain per channel is
/// then <c>whitePoint * (max / 100) / (white - black)</c>, where <c>max</c> is 255 or 65535 and
/// <c>black</c> is 0 unless <c>-hs_bp</c> asked for the black point to be measured the same way (mean
/// of the 25 darkest samples). Values map as <c>truncate((v - black) * gain)</c>, clipped to the
/// sample range.
/// </para>
/// <para>
/// Because the gain divides by <c>white - black</c>, a frame whose brightest 25 samples average 255
/// yields exactly <c>whitePoint/100</c> (2.04 at the default 204), while a real capture whose bright
/// tail averages 225 yields 2.312 from the same switch. That image dependence is what the oracle shows
/// and why the earlier constant-gain model matched only synthetic fixtures. Verified pixel-exact on
/// all 30 frames of a real 640x640 Saturn capture (gains 2.2916-2.3223 at <c>-hs=204</c>).
/// </para>
/// <para>
/// On colour frames PIPP computes one gain per channel and, unless <c>-hsrgb</c> is set, uses the
/// smallest of the three for all channels (<c>options + 0x1E0</c> gates that choice). Note that the
/// engine does not equalise the channels: <c>-hsrgb</c> means "separate gain per channel", not
/// "equalise".
/// </para>
/// </remarks>
public static class HistogramStretch
{
    /// <summary>
    /// Number of tail samples the engine averages to locate the white and black points
    /// (<c>0x19</c> in the decompiled stretch routine).
    /// </summary>
    public const int TailSampleCount = 25;

    /// <summary>
    /// Stretches the frame so its measured white point reaches <paramref name="whitePoint"/> percent of
    /// full scale, truncating and clipping at full scale.
    /// </summary>
    /// <remarks>
    /// The white point is measured, not assumed: see the type remarks. With
    /// <paramref name="preserveBlackPoint"/> the measured black point (mean of the
    /// <see cref="TailSampleCount"/> darkest samples) is subtracted instead of zero, which is PIPP's
    /// <c>-hs_bp</c>. With <paramref name="perChannelGains"/> each colour channel keeps its own gain
    /// (<c>-hsrgb</c>); otherwise the smallest channel gain is applied to all of them.
    /// </remarks>
    /// <param name="frame">Source frame (1 or 3 channels).</param>
    /// <param name="whitePoint">Gain percentage, 1..255 (PIPP default 204).</param>
    /// <param name="preserveBlackPoint">Measure and subtract the frame's black point (<c>-hs_bp</c>).</param>
    /// <param name="perChannelGains">Keep a separate gain per colour channel (<c>-hsrgb</c>).</param>
    /// <returns>A new stretched frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="whitePoint"/> is outside 1..255.</exception>
    public static Frame StretchToWhitePoint(
        Frame frame,
        int whitePoint,
        bool preserveBlackPoint = false,
        bool perChannelGains = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (whitePoint is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(whitePoint), whitePoint, "white point must be 1..255");

        return StretchCore(frame, whitePoint, preserveBlackPoint, perChannelGains);
    }

    /// <summary>
    /// Maps the frame using the percentage spelling of the switch (<c>-hs=xx%</c> or <c>-hs=xxp</c>).
    /// </summary>
    /// <remarks>
    /// The engine's command-line parser converts the percentage to the integer spelling before storing
    /// it - <c>percent * 255 / 100</c>, truncated - so <c>-hs=80%</c> and <c>-hs=204</c> are the same
    /// setting. (An earlier version treated the percentage as a white point of <c>percent</c> percent
    /// of full scale with gain <c>100/percent</c>; that was inferred from the help text and is wrong.)
    /// </remarks>
    /// <param name="frame">Source frame.</param>
    /// <param name="percent">White point as a percentage of full scale, 1..100.</param>
    /// <param name="preserveBlackPoint">See <see cref="StretchToWhitePoint"/>.</param>
    /// <param name="perChannelGains">See <see cref="StretchToWhitePoint"/>.</param>
    /// <returns>A new stretched frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is outside 1..100.</exception>
    public static Frame StretchToPercent(
        Frame frame,
        int percent,
        bool preserveBlackPoint = false,
        bool perChannelGains = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (percent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "percentage must be 1..100");

        return StretchToWhitePoint(frame, percent * 255 / 100, preserveBlackPoint, perChannelGains);
    }

    /// <summary>
    /// Measures the frame's white point per channel: the mean of the
    /// <see cref="TailSampleCount"/> brightest samples, truncated to an integer.
    /// </summary>
    /// <remarks>
    /// Mirrors the engine's accumulation exactly, including its quirks: bins are visited from full
    /// scale down to 1 (bin 0 is never reached), the bin that crosses the 25-sample budget contributes
    /// only the remaining samples, and the total is divided by 25 with integer truncation. A frame with
    /// fewer than 25 samples above 0 therefore reports a white point below its true maximum - which is
    /// what the original does too.
    /// </remarks>
    /// <param name="frame">Frame to measure.</param>
    /// <returns>One white point per channel, in the frame's sample units.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static int[] MeasureWhiteSamples(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int max = ProcessingUtil.MaxValue(frame);
        var histograms = Histograms(frame);

        var result = new int[frame.Channels];
        for (int ch = 0; ch < result.Length; ch++)
        {
            int[] histogram = histograms[ch];
            int remaining = TailSampleCount;
            long sum = 0;
            for (int v = max; v > 0; v--)
            {
                int count = histogram[v];
                if (remaining <= count)
                {
                    sum += (long)remaining * v;
                    break;
                }

                sum += (long)count * v;
                remaining -= count;
            }

            result[ch] = (int)(sum / TailSampleCount);
        }

        return result;
    }

    /// <summary>
    /// Measures the frame's black point per channel: the mean of the
    /// <see cref="TailSampleCount"/> darkest samples, truncated to an integer.
    /// </summary>
    /// <remarks>
    /// This is the statistic behind <c>-hs_bp</c>. The engine only computes it when that switch is
    /// present; without it the black point is zero. Accumulated from the bottom of the histogram
    /// upwards, with the same weighted boundary bin and integer division as
    /// <see cref="MeasureWhiteSamples"/>.
    /// </remarks>
    /// <param name="frame">Frame to measure.</param>
    /// <returns>One black point per channel, in the frame's sample units.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static int[] MeasureBlackSamples(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int max = ProcessingUtil.MaxValue(frame);
        var histograms = Histograms(frame);

        var result = new int[frame.Channels];
        for (int ch = 0; ch < result.Length; ch++)
        {
            int[] histogram = histograms[ch];
            int remaining = TailSampleCount;
            long sum = 0;
            for (int v = 0; v < max; v++)
            {
                int count = histogram[v];
                if (remaining <= count)
                {
                    sum += (long)remaining * v;
                    break;
                }

                sum += (long)count * v;
                remaining -= count;
            }

            result[ch] = (int)(sum / TailSampleCount);
        }

        return result;
    }

    /// <summary>
    /// Equalises the colour channels before stretching: each of R, G and B is scaled so its maximum
    /// sample reaches the largest channel maximum in the frame (<c>out = round(v * target / channelMax)</c>).
    /// Mono frames are returned as a clone.
    /// </summary>
    /// <remarks>
    /// Not part of the <c>-hs</c> path: the engine's stretch never equalises, it only decides whether
    /// to share one gain across channels (<c>-hsrgb</c>). Kept as a standalone stage.
    /// </remarks>
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

    /// <summary>Applies the measured stretch: per-channel gains and lookup tables.</summary>
    private static Frame StretchCore(
        Frame frame,
        int whitePoint,
        bool preserveBlackPoint,
        bool perChannelGains)
    {
        int max = ProcessingUtil.MaxValue(frame);
        int[] white = MeasureWhiteSamples(frame);
        int[] black = preserveBlackPoint ? MeasureBlackSamples(frame) : new int[frame.Channels];

        var gain = new double[frame.Channels];
        for (int ch = 0; ch < gain.Length; ch++)
        {
            int range = white[ch] - black[ch];
            // The engine returns without touching the frame when its white point does not sit above
            // its black point - there is no range left to stretch.
            if (range <= 0) return frame.Clone();
            gain[ch] = whitePoint * (max / 100.0) / range;
        }

        if (!perChannelGains)
        {
            double common = gain.Min();
            Array.Fill(gain, common);
        }

        var luts = new ushort[frame.Channels][];
        for (int ch = 0; ch < luts.Length; ch++)
        {
            int offset = black[ch];
            double channelGain = gain[ch];
            var lut = new ushort[max + 1];
            for (int v = 0; v <= max; v++)
            {
                double mapped = (v - offset) * channelGain;
                lut[v] = mapped <= 0
                    ? (ushort)0
                    : mapped >= max
                        ? (ushort)max
                        : (ushort)(int)mapped;
            }

            luts[ch] = lut;
        }

        var src = frame.Samples;
        var dst = new ushort[src.Length];
        if (frame.Channels == 1)
        {
            ushort[] lut = luts[0];
            for (int i = 0; i < src.Length; i++)
            {
                int v = src[i];
                dst[i] = lut[v > max ? max : v];
            }
        }
        else
        {
            ushort[] red = luts[0];
            ushort[] green = luts[1];
            ushort[] blue = luts[2];
            for (int i = 0; i + 2 < src.Length; i += 3)
            {
                int r = src[i];
                int g = src[i + 1];
                int b = src[i + 2];
                dst[i] = red[r > max ? max : r];
                dst[i + 1] = green[g > max ? max : g];
                dst[i + 2] = blue[b > max ? max : b];
            }
        }

        return ProcessingUtil.NewLike(frame, frame.Width, frame.Height, frame.Channels, dst);
    }

    /// <summary>One histogram per channel, indexed by sample value.</summary>
    private static int[][] Histograms(Frame frame)
    {
        int max = ProcessingUtil.MaxValue(frame);
        int channels = frame.Channels;
        var histograms = new int[channels][];
        for (int ch = 0; ch < channels; ch++) histograms[ch] = new int[max + 1];

        var src = frame.Samples;
        if (channels == 1)
        {
            int[] histogram = histograms[0];
            for (int i = 0; i < src.Length; i++)
            {
                int v = src[i];
                histogram[v > max ? max : v]++;
            }
        }
        else
        {
            int[] red = histograms[0];
            int[] green = histograms[1];
            int[] blue = histograms[2];
            for (int i = 0; i + 2 < src.Length; i += 3)
            {
                int r = src[i];
                int g = src[i + 1];
                int b = src[i + 2];
                red[r > max ? max : r]++;
                green[g > max ? max : g]++;
                blue[b > max ? max : b]++;
            }
        }

        return histograms;
    }
}
