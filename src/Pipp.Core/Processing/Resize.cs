namespace Pipp.Core.Processing;

using Pipp.Core.Frames;

/// <summary>
/// Bilinear resampling (PIPP's <c>-resize_width</c>/<c>-resize_height</c> stage).
/// </summary>
/// <remarks>
/// The sampling grid matches the original engine's stretch routine (pipp_core <c>FUN_14005ca60</c>,
/// the one that prints "Stretching the image..."): the source coordinate for destination pixel
/// <c>i</c> advances by <c>sourceSize / destinationSize</c> per step starting at zero, so
/// <c>i = 0</c> samples the first source pixel exactly (corner aligned, not pixel centred). The
/// interpolation is <c>a*(1-frac) + b*frac + 0.5</c> truncated to an integer sample, and the
/// neighbour index is clamped to the last row/column at the edges. Both axes use the same mapping,
/// which is also what makes upscales and downscales consistent with the original output.
/// </remarks>
public static class Resize
{
    /// <summary>Resamples the frame to exactly <paramref name="width"/> x <paramref name="height"/>.</summary>
    /// <param name="frame">Source frame (1 or 3 channels).</param>
    /// <param name="width">Destination width in pixels, at least 1.</param>
    /// <param name="height">Destination height in pixels, at least 1.</param>
    /// <returns>A new frame with the requested dimensions and the source bit depth.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    public static Frame Bilinear(Frame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "destination width must be positive");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "destination height must be positive");
        if (width == frame.Width && height == frame.Height) return frame.Clone();

        int sw = frame.Width, sh = frame.Height, c = frame.Channels;
        var src = frame.Samples;
        var dst = new ushort[width * height * c];
        int max = ProcessingUtil.MaxValue(frame);

        var x0 = new int[width];
        var x1 = new int[width];
        var fx = new double[width];
        for (int x = 0; x < width; x++)
        {
            double sx = (double)x * sw / width;
            if (sx < 0) sx = 0;
            if (sx > sw - 1) sx = sw - 1;
            int i0 = (int)sx;
            x0[x] = i0;
            x1[x] = Math.Min(i0 + 1, sw - 1);
            fx[x] = sx - i0;
        }

        var y0 = new int[height];
        var y1 = new int[height];
        var fy = new double[height];
        for (int y = 0; y < height; y++)
        {
            double sy = (double)y * sh / height;
            if (sy < 0) sy = 0;
            if (sy > sh - 1) sy = sh - 1;
            int i0 = (int)sy;
            y0[y] = i0;
            y1[y] = Math.Min(i0 + 1, sh - 1);
            fy[y] = sy - i0;
        }

        for (int y = 0; y < height; y++)
        {
            int rowTop = y0[y] * sw;
            int rowBottom = y1[y] * sw;
            double wy = fy[y];
            for (int x = 0; x < width; x++)
            {
                int left = x0[x];
                int right = x1[x];
                double wx = fx[x];
                int topLeft = (rowTop + left) * c;
                int topRight = (rowTop + right) * c;
                int bottomLeft = (rowBottom + left) * c;
                int bottomRight = (rowBottom + right) * c;
                int d = (y * width + x) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    double top = src[topLeft + ch] * (1 - wx) + src[topRight + ch] * wx;
                    double bottom = src[bottomLeft + ch] * (1 - wx) + src[bottomRight + ch] * wx;
                    double value = top * (1 - wy) + bottom * wy;
                    dst[d + ch] = ProcessingUtil.ClampSample(
                        (long)Math.Round(value, MidpointRounding.AwayFromZero), max);
                }
            }
        }

        return ProcessingUtil.NewLike(frame, width, height, c, dst);
    }

    /// <summary>
    /// Resizes while deriving one dimension from the source aspect ratio.
    /// Pass -1 for exactly one of <paramref name="width"/> or <paramref name="height"/>; the derived
    /// value is <c>round(other * sourceHeight / sourceWidth)</c> or the reciprocal, clamped to at least 1.
    /// Two concrete dimensions behave exactly like <see cref="Bilinear"/>.
    /// </summary>
    /// <param name="frame">Source frame.</param>
    /// <param name="width">Destination width, or -1 to derive it from <paramref name="height"/>.</param>
    /// <param name="height">Destination height, or -1 to derive it from <paramref name="width"/>.</param>
    /// <returns>A new resized frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A supplied dimension is zero, negative other than -1, or the derived dimension is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">Both dimensions are -1.</exception>
    public static Frame ResizeKeepAspect(Frame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (width == -1 && height == -1)
            throw new ArgumentException("at most one dimension may be -1", nameof(width));
        if (width == 0 || width < -1)
            throw new ArgumentOutOfRangeException(nameof(width), width, "width must be positive or -1");
        if (height == 0 || height < -1)
            throw new ArgumentOutOfRangeException(nameof(height), height, "height must be positive or -1");

        if (width == -1)
        {
            width = (int)Math.Max(1, Math.Round(
                (double)height * frame.Width / frame.Height, MidpointRounding.AwayFromZero));
        }
        else if (height == -1)
        {
            height = (int)Math.Max(1, Math.Round(
                (double)width * frame.Height / frame.Width, MidpointRounding.AwayFromZero));
        }

        return Bilinear(frame, width, height);
    }
}
