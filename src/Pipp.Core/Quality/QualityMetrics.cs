using Pipp.Core.Alignment;
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
/// <para>
/// <see cref="QualityAlgorithm.PippDefault"/> is the recovered engine metric: see
/// <see cref="EngineQuality"/> for the stride sweep, the mask-buffer reuse that makes the first
/// frame of a run score higher, and the exposure factor. A single call here uses a fresh mask and
/// the frame's own computed detection threshold, i.e. it reproduces PIPP's <em>frame 1</em>.
/// </para>
/// <para>
/// The other two estimators remain reconstructions: the original scores are computed on a
/// downsampled region (see PIPP's <c>-qmin/-qmax/-qinc</c>), and its subsampler projects colour
/// pixels to Rec.601 luma with the weights observed in <c>pipp_core!FUN_1400af990</c>
/// (0.299 R + 0.587 G + 0.114 B over BGR-ordered samples). Their formulas are deterministic and
/// exposure-normalised so that sorting behaviour is stable.
/// </para>
/// </remarks>
public static class QualityMetrics
{
    /// <summary>Computes a quality score; larger is better.</summary>
    public static double Compute(Frame frame, QualityAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return algorithm switch
        {
            QualityAlgorithm.PippDefault => ComputeDefault(frame),
            QualityAlgorithm.Ninox => LaplacianVariance(ToLuma(frame), frame.Width, frame.Height),
            QualityAlgorithm.Histogram => HistogramEntropy(ToLuma(frame), frame.BitDepth),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
    }

    /// <summary>
    /// One frame measured with a fresh mask and the frame's computed detection threshold — the
    /// engine's frame 1 of a run. Callers that score a whole sequence must keep one
    /// <see cref="EngineQuality"/> instead, or every frame looks like a first frame.
    /// </summary>
    private static double ComputeDefault(Frame frame)
    {
        var engine = new EngineQuality();
        return engine.Score(frame, DetectionThreshold.ComputeOrDefault(frame));
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
