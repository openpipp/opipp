namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>How a binning block is reduced.</summary>
public enum BinningMode
{
    /// <summary>Block average, rounded to nearest with integer arithmetic.</summary>
    Average = 0,

    /// <summary>Block sum, clamped to the frame's maximum sample value.</summary>
    Sum = 1,
}

/// <summary>
/// Pixel binning (PIPP's <c>-bin</c>/<c>-bin_sum</c> stage). The frame is divided into
/// <c>factor</c> x <c>factor</c> blocks starting at the top-left corner.
/// If a dimension is not divisible by the factor, the trailing partial block is ignored:
/// the output size is <c>floor(width/factor)</c> x <c>floor(height/factor)</c>.
/// </summary>
public static class Binning
{
    /// <summary>Bins the frame by an integer factor from 1 to 8.</summary>
    /// <param name="frame">Source frame (1 or 3 channels).</param>
    /// <param name="factor">Block size, 1..8. A factor of 1 returns an exact clone.</param>
    /// <param name="mode">Average (rounded) or clamped Sum.</param>
    /// <returns>A new binned frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="factor"/> is outside 1..8, or <paramref name="mode"/> is not a defined value.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="factor"/> is larger than the frame's width or height, so no full block exists.
    /// </exception>
    public static Frame Apply(Frame frame, int factor, BinningMode mode)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (factor is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(factor), factor, "binning factor must be 1..8");
        if (mode is not (BinningMode.Average or BinningMode.Sum))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown binning mode");
        if (factor == 1) return frame.Clone();

        int outW = frame.Width / factor;
        int outH = frame.Height / factor;
        if (outW == 0 || outH == 0)
            throw new ArgumentException(
                $"binning factor {factor} exceeds frame size {frame.Width}x{frame.Height}; " +
                "the trailing partial block is not used", nameof(factor));

        int w = frame.Width, c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[outW * outH * c];
        int count = factor * factor;
        int max = ProcessingUtil.MaxValue(frame);

        for (int by = 0; by < outH; by++)
        {
            for (int bx = 0; bx < outW; bx++)
            {
                int d = (by * outW + bx) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    long sum = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int row = (by * factor + dy) * w;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            sum += src[(row + bx * factor + dx) * c + ch];
                        }
                    }

                    dst[d + ch] = mode == BinningMode.Average
                        ? (ushort)((sum + count / 2) / count)
                        : ProcessingUtil.ClampSample(sum, max);
                }
            }
        }

        return ProcessingUtil.NewLike(frame, outW, outH, c, dst);
    }
}
