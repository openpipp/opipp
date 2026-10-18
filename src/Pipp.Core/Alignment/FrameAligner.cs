using Pipp.Core.Frames;
using Pipp.Core.Quality;

namespace Pipp.Core.Alignment;

/// <summary>A translation measured in pixels; positive X is right, positive Y is down.</summary>
public readonly record struct AlignmentOffset(double Dx, double Dy)
{
    /// <summary>No shift.</summary>
    public static AlignmentOffset Zero => default;

    /// <summary>Rounds to whole pixels.</summary>
    public (int X, int Y) Round() => ((int)Math.Round(Dx), (int)Math.Round(Dy));
}

/// <summary>A rectangular region of interest in pixel coordinates.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Clamps the rectangle to a frame, returning null when nothing remains.</summary>
    public PixelRect? Clamp(int frameWidth, int frameHeight)
    {
        int x = Math.Clamp(X, 0, Math.Max(0, frameWidth - 1));
        int y = Math.Clamp(Y, 0, Math.Max(0, frameHeight - 1));
        int w = Math.Min(Width, frameWidth - x);
        int h = Math.Min(Height, frameHeight - y);
        return w <= 0 || h <= 0 ? null : new PixelRect(x, y, w, h);
    }
}

/// <summary>
/// Frame alignment for PIPP's two stabilisation modes.
/// </summary>
/// <remarks>
/// PIPP separates "centre the object" (<c>-ctr</c>, driven by the object centroid) from
/// "surface feature tracking" (<c>-en_surface_feature_tracking</c> with an <c>-afb_*</c> box),
/// which correlates a user box between frames. Both are implemented here as: project to luma,
/// measure an integer translation by normalised cross-correlation over a bounded search, then
/// refine to sub-pixel with a parabola fit on the correlation peak. That matches the observable
/// contract (per-frame offsets, reject when the correlation is too weak); the exact native
/// scoring function has not been verified against the original's output.
/// </remarks>
public static class FrameAligner
{
    /// <summary>
    /// Sub-pixel intensity-weighted centroid of the region above <paramref name="threshold"/>.
    /// </summary>
    /// <returns>Centroid in pixel coordinates, or null when no pixel passes the threshold.</returns>
    public static (double X, double Y)? Centroid(Frame frame, int threshold, PixelRect? region = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var luma = QualityMetrics.ToLuma(frame);
        int w = frame.Width, h = frame.Height;
        var r = region?.Clamp(w, h) ?? (region is null ? new PixelRect(0, 0, w, h) : (PixelRect?)null);
        if (r is null) return null;

        double sum = 0, sx = 0, sy = 0;
        for (int y = r.Value.Y; y < r.Value.Y + r.Value.Height; y++)
        {
            for (int x = r.Value.X; x < r.Value.X + r.Value.Width; x++)
            {
                int v = luma[y * w + x];
                if (v < threshold) continue;
                double weight = v - threshold + 1;
                sum += weight;
                sx += weight * x;
                sy += weight * y;
            }
        }

        return sum <= 0 ? null : (sx / sum, sy / sum);
    }

    /// <summary>
    /// Estimates the translation that best aligns <paramref name="moving"/> onto
    /// <paramref name="reference"/> by normalised cross-correlation.
    /// </summary>
    /// <param name="searchRadius">Maximum absolute shift searched, in pixels.</param>
    /// <param name="region">Box to correlate (PIPP's AFB); the whole frame when null.</param>
    /// <param name="minCorrelation">Peak score below which the frame is considered unalignable.</param>
    /// <returns>The displacement, or null when the box is featureless or the peak is too weak.</returns>
    public static AlignmentOffset? EstimateTranslation(
        Frame reference,
        Frame moving,
        int searchRadius = 32,
        PixelRect? region = null,
        double minCorrelation = 0.5)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(moving);
        if (reference.Width != moving.Width || reference.Height != moving.Height)
            throw new ArgumentException("frames must have the same geometry", nameof(moving));

        int w = reference.Width, h = reference.Height;
        var refLuma = QualityMetrics.ToLuma(reference);
        var movLuma = QualityMetrics.ToLuma(moving);

        var box = region?.Clamp(w, h) ?? new PixelRect(w / 4, h / 4, Math.Max(1, w / 2), Math.Max(1, h / 2));

        // A featureless box correlates perfectly with itself at every offset (cosine similarity is
        // scale-free), so an unguarded search would return the first candidate in the window. Reject
        // it here: there is no translation to measure.
        double sum = 0, sumSquares = 0;
        for (int y = box.Y; y < box.Y + box.Height; y++)
        {
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                double v = refLuma[(y * w) + x];
                sum += v;
                sumSquares += v * v;
            }
        }

        double pixels = (double)box.Width * box.Height;
        if (sumSquares - (sum * sum / pixels) <= 1e-9) return null;

        // Score every integer offset in the search window by NCC over the box.
        int r = Math.Max(1, searchRadius);
        int bestX = 0, bestY = 0;
        double best = double.NegativeInfinity;
        var scores = new double[2 * r + 1, 2 * r + 1];

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                double num = 0, denomA = 0, denomB = 0;
                for (int y = box.Y; y < box.Y + box.Height; y++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    for (int x = box.X; x < box.X + box.Width; x++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w) continue;
                        double a = refLuma[y * w + x];
                        double b = movLuma[yy * w + xx];
                        num += a * b;
                        denomA += a * a;
                        denomB += b * b;
                    }
                }

                double score = denomA <= 0 || denomB <= 0 ? 0 : num / Math.Sqrt(denomA * denomB);
                scores[dx + r, dy + r] = score;
                if (score > best)
                {
                    best = score;
                    bestX = dx;
                    bestY = dy;
                }
            }
        }

        if (best < minCorrelation) return null;

        // Parabola refinement in each axis using the neighbouring correlation samples.
        double subX = SubPixel(scores, bestX + r, bestY + r, 1, 0);
        double subY = SubPixel(scores, bestX + r, bestY + r, 0, 1);
        return new AlignmentOffset(bestX + subX, bestY + subY);
    }

    private static double SubPixel(double[,] s, int cx, int cy, int dx, int dy)
    {
        int n = s.GetLength(0);
        int ax = cx - dx, ay = cy - dy;
        int bx = cx + dx, by = cy + dy;
        if (ax < 0 || ay < 0 || bx >= n || by >= s.GetLength(1)) return 0;

        double left = s[ax, ay], centre = s[cx, cy], right = s[bx, by];
        double denom = left - 2 * centre + right;
        if (Math.Abs(denom) < 1e-12) return 0;
        double delta = 0.5 * (left - right) / denom;
        return Math.Clamp(delta, -0.5, 0.5);
    }

    /// <summary>
    /// Applies a sub-pixel translation with bilinear sampling and clamped edges, returning a new frame.
    /// </summary>
    /// <summary>
    /// Offset that moves the frame's bright object to the engine's centring target.
    /// </summary>
    /// <remarks>
    /// Measured against the original binary: a disc centred at (48,48) in a 96x96 frame comes out at
    /// (48,47), so the target is <c>x = width/2</c> and <c>y = height - 1 - height/2</c>. That is
    /// exactly <c>height/2</c> expressed in the engine's bottom-up buffers, which is why the y target
    /// sits one row above centre on even-height frames.
    /// </remarks>
    public static AlignmentOffset CentreOffset(Frame frame, int threshold)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // The engine does not use a centroid: it run-detects an object box and centres that box's
        // origin. See PlanetCentring (recovered from FUN_140076ae0 / FUN_140077560) and
        // notes/centring.md; this reproduces all 30 frames of the real capture pixel-exactly.
        return PlanetCentring.CentreOffset(frame, threshold);
    }

    /// <summary>
    /// Integer translation with a caller-chosen fill for the vacated border. The engine uses the
    /// frame's median for <c>-ctr</c> and zero for surface-feature tracking; <see cref="Shift"/>
    /// resamples and clamps instead, which is right for sub-pixel stacking but not for either.
    /// </summary>
    /// <param name="frame">Frame to translate.</param>
    /// <param name="offset">Translation to apply, in pixels.</param>
    /// <param name="fill">
    /// Value written into vacated pixels; null uses <see cref="MedianSample"/>. The engine uses the
    /// frame median for <c>-ctr</c> but zero for surface-feature tracking, so the caller picks.
    /// </param>
    public static Frame ShiftWithBackground(Frame frame, AlignmentOffset offset, ushort? fill = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int dx = (int)Math.Round(offset.Dx, MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round(offset.Dy, MidpointRounding.AwayFromZero);
        if (dx == 0 && dy == 0)
        {
            return frame.Clone();
        }

        int w = frame.Width, h = frame.Height, c = frame.Channels;
        var dst = new Frame(w, h, c, frame.BitDepth)
        {
            Bayer = frame.Bayer,
            Index = frame.Index,
            SourceName = frame.SourceName,
        };

        ushort background = fill ?? MedianSample(frame);
        Array.Fill(dst.Samples, background);

        for (int y = 0; y < h; y++)
        {
            int sy = y - dy;
            if (sy < 0 || sy >= h)
            {
                continue;
            }

            for (int x = 0; x < w; x++)
            {
                int sx = x - dx;
                if (sx < 0 || sx >= w)
                {
                    continue;
                }

                int src = ((sy * w) + sx) * c;
                int d = ((y * w) + x) * c;
                for (int ch = 0; ch < c; ch++)
                {
                    dst.Samples[d + ch] = frame.Samples[src + ch];
                }
            }
        }

        return dst;
    }

    /// <summary>Median sample value across all channels, used as the engine's background fill.</summary>
    public static ushort MedianSample(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int levels = frame.BitDepth == 8 ? 256 : 65536;
        var histogram = new int[levels];
        foreach (ushort sample in frame.Samples)
        {
            histogram[Math.Min(sample, levels - 1)]++;
        }

        int half = frame.Samples.Length / 2;
        int accumulated = 0;
        for (int i = 0; i < levels; i++)
        {
            accumulated += histogram[i];
            if (accumulated > half)
            {
                return (ushort)i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Otsu threshold over the frame's samples, used when the caller has no explicit minimum pixel
    /// value. The engine prints its own "Calculated minpixel value" (85 on the parity fixture), which
    /// this reproduces closely enough that the centroid - and therefore the shift - is identical.
    /// </summary>
    public static int OtsuThreshold(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int max = frame.BitDepth == 8 ? 255 : 65535;
        var histogram = new long[max + 1];
        foreach (ushort sample in frame.Samples)
        {
            histogram[Math.Min(sample, max)]++;
        }

        long total = frame.Samples.Length;
        double sum = 0;
        for (int i = 0; i <= max; i++) sum += (double)i * histogram[i];

        double sumBackground = 0;
        long weightBackground = 0;
        double bestVariance = -1;
        int bestThreshold = 0;
        for (int t = 0; t <= max; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0) continue;
            long weightForeground = total - weightBackground;
            if (weightForeground == 0) break;

            sumBackground += (double)t * histogram[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double variance = (double)weightBackground * weightForeground *
                              (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                bestThreshold = t;
            }
        }

        return bestThreshold;
    }

    public static Frame Shift(Frame frame, AlignmentOffset offset)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (offset.Dx == 0 && offset.Dy == 0) return frame.Clone();

        int w = frame.Width, h = frame.Height, c = frame.Channels;
        var dst = new Frame(w, h, c, frame.BitDepth)
        {
            Bayer = frame.Bayer,
            Index = frame.Index,
            SourceName = frame.SourceName,
        };

        var src = frame.Samples;
        var o = dst.Samples;
        int max = frame.BitDepth == 8 ? 255 : 65535;

        for (int y = 0; y < h; y++)
        {
            double sy = y - offset.Dy;
            int y0 = (int)Math.Floor(sy);
            double fy = sy - y0;
            int y1 = y0 + 1;
            y0 = Math.Clamp(y0, 0, h - 1);
            y1 = Math.Clamp(y1, 0, h - 1);

            for (int x = 0; x < w; x++)
            {
                double sx = x - offset.Dx;
                int x0 = (int)Math.Floor(sx);
                double fx = sx - x0;
                int x1 = x0 + 1;
                x0 = Math.Clamp(x0, 0, w - 1);
                x1 = Math.Clamp(x1, 0, w - 1);

                for (int ch = 0; ch < c; ch++)
                {
                    double v00 = src[(y0 * w + x0) * c + ch];
                    double v01 = src[(y0 * w + x1) * c + ch];
                    double v10 = src[(y1 * w + x0) * c + ch];
                    double v11 = src[(y1 * w + x1) * c + ch];
                    double v = v00 * (1 - fx) * (1 - fy) + v01 * fx * (1 - fy) + v10 * (1 - fx) * fy + v11 * fx * fy;
                    o[(y * w + x) * c + ch] = (ushort)Math.Clamp((int)Math.Round(v), 0, max);
                }
            }
        }

        return dst;
    }
}
