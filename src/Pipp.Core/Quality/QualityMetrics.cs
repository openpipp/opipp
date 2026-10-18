using Pipp.Core.Frames;

namespace Pipp.Core.Quality;

/// <summary>Frame-quality estimators exposed by PIPP (<c>-qold</c>, <c>-qhisto</c>, default).</summary>
public enum QualityAlgorithm
{
    /// <summary>PIPP's default sharpness estimator.</summary>
    PippDefault = 0,

    /// <summary>Original Ninox algorithm (<c>-qold</c>).</summary>
    Ninox = 1,

    /// <summary>Histogram based estimator (<c>-qhisto</c>).</summary>
    Histogram = 2,
}

/// <summary>
/// Frame quality scoring used for sorting and frame selection.
/// </summary>
/// <remarks>
/// The original scores are computed on a downsampled region (see PIPP's <c>-qmin/-qmax/-qinc</c>),
/// and its subsampler projects colour pixels to Rec.601 luma with the weights observed in
/// <c>pipp_core!FUN_1400af990</c> (0.299 R + 0.587 G + 0.114 B over BGR-ordered samples).
/// The three scalar formulas below are reconstructions: the surrounding contract (downsample,
/// ordering, "% of best" reporting) is observed, the exact native expression is not yet
/// verified against a labelled frame set. They are deterministic and exposure-normalised so
/// that sorting behaviour is stable.
/// </remarks>
public static class QualityMetrics
{
    /// <summary>Computes a quality score; larger is better.</summary>
    public static double Compute(Frame frame, QualityAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var luma = ToLuma(frame);
        return algorithm switch
        {
            QualityAlgorithm.PippDefault => Tenengrad(luma, frame.Width, frame.Height),
            QualityAlgorithm.Ninox => LaplacianVariance(luma, frame.Width, frame.Height),
            QualityAlgorithm.Histogram => HistogramEntropy(luma, frame.BitDepth),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
    }

    /// <summary>
    /// Projects a frame to a single-plane luma buffer (Rec.601). Mono frames are copied as-is.
    /// </summary>
    public static ushort[] ToLuma(Frame frame)
    {
        if (frame.Channels == 1)
        {
            return (ushort[])frame.Samples.Clone();
        }

        int pixels = frame.Width * frame.Height;
        var luma = new ushort[pixels];
        var s = frame.Samples;
        for (int i = 0, j = 0; i < pixels; i++, j += 3)
        {
            int y = (299 * s[j] + 587 * s[j + 1] + 114 * s[j + 2] + 500) / 1000;
            luma[i] = (ushort)y;
        }
        return luma;
    }

    /// <summary>Mean squared Sobel gradient, normalised by mean intensity (Tenengrad-style sharpness).</summary>
    private static double Tenengrad(ushort[] luma, int w, int h)
    {
        if (w < 3 || h < 3) return 0;
        double sum = 0, energy = 0;
        long n = 0;

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                int tl = luma[i - w - 1], tc = luma[i - w], tr = luma[i - w + 1];
                int ml = luma[i - 1], mr = luma[i + 1];
                int bl = luma[i + w - 1], bc = luma[i + w], br = luma[i + w + 1];

                int gx = (tr + 2 * mr + br) - (tl + 2 * ml + bl);
                int gy = (bl + 2 * bc + br) - (tl + 2 * tc + tr);
                sum += (double)gx * gx + (double)gy * gy;
                energy += luma[i];
                n++;
            }
        }

        if (n == 0) return 0;
        double meanEnergy = energy / n;
        return meanEnergy <= 0 ? 0 : sum / n / meanEnergy;
    }

    /// <summary>Variance of the 3x3 Laplacian response.</summary>
    private static double LaplacianVariance(ushort[] luma, int w, int h)
    {
        if (w < 3 || h < 3) return 0;
        double sum = 0, sumSq = 0;
        long n = 0;

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                int lap = 4 * luma[i] - luma[i - 1] - luma[i + 1] - luma[i - w] - luma[i + w];
                sum += lap;
                sumSq += (double)lap * lap;
                n++;
            }
        }

        if (n == 0) return 0;
        double mean = sum / n;
        return sumSq / n - mean * mean;
    }

    /// <summary>Shannon entropy of a 64-bin luma histogram, in bits.</summary>
    private static double HistogramEntropy(ushort[] luma, int bitDepth)
    {
        const int bins = 64;
        var hist = new long[bins];
        int max = bitDepth == 8 ? 255 : 65535;
        double scale = (bins - 1) / (double)max;

        foreach (var v in luma)
        {
            int b = (int)(v * scale);
            if (b >= bins) b = bins - 1;
            hist[b]++;
        }

        double total = luma.Length;
        double entropy = 0;
        foreach (var c in hist)
        {
            if (c == 0) continue;
            double p = c / total;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    /// <summary>
    /// Converts raw scores to PIPP's "percent of best" ordering, preserving input order in
    /// <see cref="QualityEntry.Index"/>.
    /// </summary>
    public static QualityEntry[] Rank(IReadOnlyList<double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        double best = 0;
        foreach (var s in scores)
        {
            if (s > best) best = s;
        }

        var entries = new QualityEntry[scores.Count];
        for (int i = 0; i < scores.Count; i++)
        {
            double pct = best <= 0 ? 0 : scores[i] / best * 100.0;
            entries[i] = new QualityEntry(i, scores[i], pct);
        }
        Array.Sort(entries, (a, b) => b.PercentOfBest.CompareTo(a.PercentOfBest));
        return entries;
    }
}

/// <summary>One frame's quality result.</summary>
/// <param name="Index">Zero-based input order.</param>
/// <param name="Score">Raw score.</param>
/// <param name="PercentOfBest">Score expressed as a percentage of the best frame, as PIPP's quality log reports it.</param>
public readonly record struct QualityEntry(int Index, double Score, double PercentOfBest);
