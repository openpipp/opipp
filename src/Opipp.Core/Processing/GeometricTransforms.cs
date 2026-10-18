namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>
/// Lossless orientation and crop operations from PIPP's flip/rotate and crop pipeline stages.
/// Every operation returns a new <see cref="Frame"/>; the input is never mutated.
/// Channel order is preserved (R,G,B stay interleaved) and Bayer/provenance metadata is copied.
/// </summary>
public static class GeometricTransforms
{
    /// <summary>Mirrors the frame left-to-right: output (x, y) = source (Width-1-x, y).</summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>A new frame with the same dimensions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame FlipHorizontal(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int w = frame.Width, h = frame.Height, c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int s = (row + x) * c;
                int d = (row + (w - 1 - x)) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    dst[d + ch] = src[s + ch];
                }
            }
        }

        return ProcessingUtil.NewLike(frame, w, h, c, dst);
    }

    /// <summary>Mirrors the frame top-to-bottom: output (x, y) = source (x, Height-1-y).</summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>A new frame with the same dimensions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame FlipVertical(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int w = frame.Width, h = frame.Height, c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        for (int y = 0; y < h; y++)
        {
            int s = (h - 1 - y) * w * c;
            int d = y * w * c;
            Array.Copy(src, s, dst, d, w * c);
        }

        return ProcessingUtil.NewLike(frame, w, h, c, dst);
    }

    /// <summary>
    /// Rotates 90 degrees clockwise. Output is Height wide and Width high:
    /// output (x, y) = source (y, Height-1-x).
    /// </summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>A new frame of size <c>Height x Width</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame Rotate90Clockwise(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int w = frame.Width, h = frame.Height, c = frame.Channels;
        int outW = h, outH = w;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int s = ((h - 1 - x) * w + y) * c;
                int d = (y * outW + x) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    dst[d + ch] = src[s + ch];
                }
            }
        }

        return ProcessingUtil.NewLike(frame, outW, outH, c, dst);
    }

    /// <summary>Rotates 180 degrees: output (x, y) = source (Width-1-x, Height-1-y).</summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>A new frame with the same dimensions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame Rotate180(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int w = frame.Width, h = frame.Height, c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int s = ((h - 1 - y) * w + (w - 1 - x)) * c;
                int d = (y * w + x) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    dst[d + ch] = src[s + ch];
                }
            }
        }

        return ProcessingUtil.NewLike(frame, w, h, c, dst);
    }

    /// <summary>
    /// Rotates 90 degrees counter-clockwise (PIPP's "270"). Output is Height wide and Width high:
    /// output (x, y) = source (Width-1-y, x).
    /// </summary>
    /// <param name="frame">Source frame.</param>
    /// <returns>A new frame of size <c>Height x Width</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public static Frame Rotate270(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int w = frame.Width, h = frame.Height, c = frame.Channels;
        int outW = h, outH = w;
        var src = frame.Samples;
        var dst = new ushort[src.Length];
        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int s = (x * w + (w - 1 - y)) * c;
                int d = (y * outW + x) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    dst[d + ch] = src[s + ch];
                }
            }
        }

        return ProcessingUtil.NewLike(frame, outW, outH, c, dst);
    }

    /// <summary>
    /// Extracts an axis-aligned rectangle. The rectangle must lie entirely inside the frame;
    /// the origin may be zero and the rectangle may cover the whole frame.
    /// </summary>
    /// <param name="frame">Source frame.</param>
    /// <param name="x">Left edge, 0-based, in pixels.</param>
    /// <param name="y">Top edge, 0-based, in pixels.</param>
    /// <param name="width">Rectangle width in pixels, at least 1.</param>
    /// <param name="height">Rectangle height in pixels, at least 1.</param>
    /// <returns>A new frame of size <paramref name="width"/> x <paramref name="height"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="x"/> or <paramref name="y"/> is negative, or <paramref name="width"/> or
    /// <paramref name="height"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">The rectangle extends past the right or bottom edge.</exception>
    public static Frame Crop(Frame frame, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), x, "crop origin must be non-negative");
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y), y, "crop origin must be non-negative");
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "crop width must be positive");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "crop height must be positive");
        if (x + width > frame.Width || y + height > frame.Height)
        {
            throw new ArgumentException(
                $"crop rectangle ({x},{y}) {width}x{height} exceeds frame bounds {frame.Width}x{frame.Height}");
        }

        int c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[width * height * c];
        for (int row = 0; row < height; row++)
        {
            int s = ((y + row) * frame.Width + x) * c;
            int d = row * width * c;
            Array.Copy(src, s, dst, d, width * c);
        }

        return ProcessingUtil.NewLike(frame, width, height, c, dst);
    }
}
