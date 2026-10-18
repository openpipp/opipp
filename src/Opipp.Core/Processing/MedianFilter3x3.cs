namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>
/// 3x3 median filter (PIPP's <c>-median</c> stage). Each channel is filtered independently.
/// Edge pixels use replicated (clamped) addressing: a sample outside the frame repeats the nearest
/// edge sample, so border pixels are filtered with a full 3x3 window instead of being left untouched.
/// </summary>
public static class MedianFilter3x3
{
    /// <summary>Applies a 3x3 median filter and returns a new frame of the same size.</summary>
    /// <param name="frame">Source frame (1 or 3 channels).</param>
    /// <returns>A filtered copy; the input is not modified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame Apply(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int w = frame.Width, h = frame.Height, c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        Span<ushort> window = stackalloc ushort[9];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int ch = 0; ch < c; ch++)
                {
                    int k = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int sy = Math.Clamp(y + dy, 0, h - 1);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = Math.Clamp(x + dx, 0, w - 1);
                            window[k++] = src[(sy * w + sx) * c + ch];
                        }
                    }

                    window.Sort();
                    dst[(y * w + x) * c + ch] = window[4];
                }
            }
        }

        return ProcessingUtil.NewLike(frame, w, h, c, dst);
    }
}
