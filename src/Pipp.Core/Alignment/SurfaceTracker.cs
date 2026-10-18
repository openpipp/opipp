using Pipp.Core.Frames;
using Pipp.Core.Quality;

namespace Pipp.Core.Alignment;

/// <summary>
/// Surface-feature tracking for PIPP's <c>-en_surface_feature_tracking</c> mode: measures how far the
/// pattern inside a user box (<c>-afb_x/-afb_y/-afb_width/-afb_height</c>) moved between two frames.
/// </summary>
/// <remarks>
/// <para>
/// The score is zero-mean normalised cross-correlation (ZNCC) over the box, evaluated at every integer
/// offset inside the search radius and refined to sub-pixel with a parabola fit on the correlation peak.
/// ZNCC subtracts each window's mean and divides by its standard deviation, so the result is invariant to
/// the brightness and gain changes a capture run actually produces (thin cloud, exposure drift, gamma):
/// a frame that is uniformly 20% brighter correlates exactly as well as the original. The correlation
/// version in <see cref="FrameAligner.EstimateTranslation"/> is the un-normalised (cosine) form, which
/// rewards bright pixels rather than structure; this type is the feature-tracking measurement proper.
/// </para>
/// <para>
/// Everything is measured on Rec.601 luma (<see cref="QualityMetrics.ToLuma"/>), so mono, Bayer and
/// 3-channel frames share one code path, and 8-bit and 16-bit frames behave identically because the
/// correlation is scale-free.
/// </para>
/// <para>
/// Convention, identical to <see cref="FrameAligner.EstimateTranslation"/> so the two are
/// interchangeable: the returned offset is the <em>displacement of the tracked content</em>, i.e.
/// <c>reference(box)</c> matches <c>frame(box + offset)</c>. A positive X means the pattern sits that
/// many pixels further right in <paramref name="frame"/>; positive Y means further down.
/// </para>
/// </remarks>
public static class SurfaceTracker
{
    /// <summary>
    /// Peak correlation below which the box is considered untrackable (occluded, flat, or moved out of
    /// the search window). Same acceptance level the aligner uses.
    /// </summary>
    public const double MinCorrelation = 0.5;

    /// <summary>Variance below which a window is treated as featureless rather than correlated.</summary>
    private const double FlatVariance = 1e-9;

    /// <summary>
    /// Measures the translation of the pattern inside <paramref name="box"/> from
    /// <paramref name="reference"/> to <paramref name="frame"/>.
    /// </summary>
    /// <param name="reference">The frame the box was drawn on (PIPP's first accepted frame).</param>
    /// <param name="frame">The frame to measure against the reference.</param>
    /// <param name="searchRadius">Maximum absolute displacement searched, in pixels; 0 tests only "no movement".</param>
    /// <param name="box">Region to correlate (PIPP's AFB); clipped to the frame. A box that misses the frame returns null.</param>
    /// <returns>
    /// The displacement of the tracked content (see the type remarks), or null when the box holds no
    /// surface feature, no candidate offset reaches <see cref="MinCorrelation"/>, or the two frames do
    /// not share a geometry.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="reference"/> or <paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="searchRadius"/> is negative.</exception>
    /// <exception cref="ArgumentException">The frames have different dimensions.</exception>
    public static AlignmentOffset? Measure(Frame reference, Frame frame, int searchRadius, PixelRect box)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegative(searchRadius);
        if (reference.Width != frame.Width || reference.Height != frame.Height)
            throw new ArgumentException("frames must have the same geometry", nameof(frame));

        int w = reference.Width, h = reference.Height;
        if (Clip(box, w, h) is not { } region) return null;

        var referenceLuma = QualityMetrics.ToLuma(reference);
        var frameLuma = QualityMetrics.ToLuma(frame);

        // The reference box, mean-subtracted once. Every candidate score is then a single dot product:
        // sum((a - meanA) * b) already equals sum((a - meanA) * (b - meanB)) because the reference
        // deviations sum to zero, so the moving window only needs its own sums.
        int pixels = region.Width * region.Height;
        var deviations = new double[pixels];
        double sumA = 0, sumA2 = 0;
        for (int y = 0; y < region.Height; y++)
        {
            int row = (region.Y + y) * w + region.X;
            int i = y * region.Width;
            for (int x = 0; x < region.Width; x++, i++)
            {
                double v = referenceLuma[row + x];
                deviations[i] = v;
                sumA += v;
                sumA2 += v * v;
            }
        }

        double meanA = sumA / pixels;
        double varA = sumA2 - (sumA * meanA);
        if (varA <= FlatVariance) return null; // a flat box carries no surface feature to track

        for (int i = 0; i < pixels; i++) deviations[i] -= meanA;

        int radius = searchRadius;
        int side = (2 * radius) + 1;
        var scores = new double[side, side];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++) scores[x, y] = double.NaN;
        }

        int bestX = 0, bestY = 0;
        double best = double.NegativeInfinity;

        for (int dy = -radius; dy <= radius; dy++)
        {
            int top = region.Y + dy;
            if (top < 0 || top + region.Height > h) continue;

            for (int dx = -radius; dx <= radius; dx++)
            {
                int left = region.X + dx;
                // Candidates that would need pixels outside the frame are skipped rather than scored on
                // a clipped window: a window with a different shape has a different mean and would bias
                // the peak towards the frame edges.
                if (left < 0 || left + region.Width > w) continue;

                double num = 0, sumB = 0, sumB2 = 0;
                for (int y = 0; y < region.Height; y++)
                {
                    int src = ((top + y) * w) + left;
                    int i = y * region.Width;
                    for (int x = 0; x < region.Width; x++, i++)
                    {
                        double b = frameLuma[src + x];
                        num += deviations[i] * b;
                        sumB += b;
                        sumB2 += b * b;
                    }
                }

                double meanB = sumB / pixels;
                double varB = sumB2 - (sumB * meanB);
                double score = varB <= FlatVariance ? 0 : num / Math.Sqrt(varA * varB);
                score = Math.Clamp(score, -1.0, 1.0);

                scores[dx + radius, dy + radius] = score;
                if (score > best)
                {
                    best = score;
                    bestX = dx;
                    bestY = dy;
                }
            }
        }

        if (best < MinCorrelation) return null;

        double subX = SubPixel(scores, bestX + radius, bestY + radius, 1, 0);
        double subY = SubPixel(scores, bestX + radius, bestY + radius, 0, 1);
        return new AlignmentOffset(bestX + subX, bestY + subY);
    }

    /// <summary>Intersects a box with the frame, returning null when nothing of it lands on the image.</summary>
    private static PixelRect? Clip(PixelRect box, int width, int height)
    {
        if (box.Width <= 0 || box.Height <= 0) return null;

        int x0 = Math.Max(0, box.X);
        int y0 = Math.Max(0, box.Y);
        int x1 = (int)Math.Min(width, (long)box.X + box.Width);
        int y1 = (int)Math.Min(height, (long)box.Y + box.Height);
        return x1 <= x0 || y1 <= y0 ? null : new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Parabola vertex through the peak sample and its neighbour in one axis. Returns 0 when the
    /// neighbour is unavailable (the peak sits on the search boundary) or the fit is degenerate.
    /// </summary>
    private static double SubPixel(double[,] scores, int cx, int cy, int dx, int dy)
    {
        int nx = scores.GetLength(0), ny = scores.GetLength(1);
        int ax = cx - dx, ay = cy - dy;
        int bx = cx + dx, by = cy + dy;
        if (ax < 0 || ay < 0 || bx >= nx || by >= ny) return 0;

        double left = scores[ax, ay], centre = scores[cx, cy], right = scores[bx, by];
        if (double.IsNaN(left) || double.IsNaN(right)) return 0;

        double denominator = left - (2 * centre) + right;
        if (Math.Abs(denominator) < 1e-12) return 0;

        double delta = 0.5 * (left - right) / denominator;
        return Math.Clamp(delta, -0.5, 0.5);
    }
}
