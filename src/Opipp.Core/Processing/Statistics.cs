namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>
/// Frame statistics used by PIPP's overexposure rejection and test-run reporting
/// (<c>@MAX_PIXEL</c>, <c>@MAX_R_PIXEL</c>, ...). All counts and means are per channel,
/// in the frame's channel order, and are computed over pixels (not over all samples).
/// </summary>
public static class Statistics
{
    /// <summary>
    /// Counts, per channel, the samples whose value is greater than or equal to
    /// <paramref name="threshold"/>.
    /// </summary>
    /// <param name="frame">Source frame.</param>
    /// <param name="threshold">Inclusive threshold in sample units, 0..frame maximum.</param>
    /// <returns>An array of length <see cref="Frame.Channels"/> with one count per channel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threshold"/> is outside the sample range.</exception>
    public static int[] CountOverexposed(Frame frame, int threshold)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int max = ProcessingUtil.MaxValue(frame);
        if (threshold < 0 || threshold > max)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, $"threshold must be 0..{max}");

        var counts = new int[frame.Channels];
        var samples = frame.Samples;
        for (int i = 0; i < samples.Length; i += frame.Channels)
        {
            for (int ch = 0; ch < frame.Channels; ch++)
            {
                if (samples[i + ch] >= threshold) counts[ch]++;
            }
        }

        return counts;
    }

    /// <summary>Returns, per channel, the largest sample value in the frame.</summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>An array of length <see cref="Frame.Channels"/> with one maximum per channel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static int[] MaxPixel(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var maxima = new int[frame.Channels];
        var samples = frame.Samples;
        for (int i = 0; i < samples.Length; i += frame.Channels)
        {
            for (int ch = 0; ch < frame.Channels; ch++)
            {
                if (samples[i + ch] > maxima[ch]) maxima[ch] = samples[i + ch];
            }
        }

        return maxima;
    }

    /// <summary>Returns, per channel, the arithmetic mean sample value over all pixels.</summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>An array of length <see cref="Frame.Channels"/> with one mean per channel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static double[] MeanPixel(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var sums = new long[frame.Channels];
        var samples = frame.Samples;
        for (int i = 0; i < samples.Length; i += frame.Channels)
        {
            for (int ch = 0; ch < frame.Channels; ch++)
            {
                sums[ch] += samples[i + ch];
            }
        }

        int pixels = frame.Width * frame.Height;
        var means = new double[frame.Channels];
        for (int ch = 0; ch < frame.Channels; ch++)
        {
            means[ch] = (double)sums[ch] / pixels;
        }

        return means;
    }
}
