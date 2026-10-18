using Opipp.Core.Alignment;
using Opipp.Core.Frames;

namespace Opipp.Core.Quality;

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
/// <see cref="QualityAlgorithm.Ninox"/> (<c>-qold</c>) is the same sweep with unweighted strides,
/// an absolute-difference metric and no exposure factor — see <see cref="NinoxQuality"/>. Its mask
/// is reused across calls as well, so <see cref="Compute"/> keeps one instance per thread, i.e. per
/// run in the CLI. Call <see cref="ResetRunState"/> to start a new run in the same process.
/// </para>
/// <para>
/// <see cref="QualityAlgorithm.Histogram"/> (<c>-qhisto</c>) is algorithm 2 in
/// <c>pipp_core!FUN_1400af2d0</c>: <c>bright50SumSquares / ((boxBottom-boxTop)*(boxRight-boxLeft))</c>
/// with 32-bit box arithmetic. The engine returns early for this algorithm, before the box
/// initialisation, so a run without object detection divides by the all-zero box: every score is
/// <c>+inf</c> (the engine's log prints <c>1.#INF00</c>). The same happens in PIPP itself, so this
/// is reproduced rather than corrected.
/// </para>
/// </remarks>
public static class QualityMetrics
{
    /// <summary>The Ninox instance of the current run (one per thread, which is one per CLI run).</summary>
    [ThreadStatic]
    private static NinoxQuality? _ninox;

    /// <summary>The run's threshold, computed once from the first scored frame like the engine.</summary>
    [ThreadStatic]
    private static int? _ninoxThreshold;

    /// <summary>The work image's object box, written by the finder and read by algorithms 1 and 2.</summary>
    [ThreadStatic]
    private static int _detectedLeft;
    [ThreadStatic]
    private static int _detectedTop;
    [ThreadStatic]
    private static int _detectedRight;
    [ThreadStatic]
    private static int _detectedBottom;

    /// <summary>True when the finder wrote (or cleared) the box since the last Ninox score.</summary>
    [ThreadStatic]
    private static bool _detectionPending;

    /// <summary>Computes a quality score; larger is better.</summary>
    public static double Compute(Frame frame, QualityAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return algorithm switch
        {
            QualityAlgorithm.PippDefault => ComputeDefault(frame),
            QualityAlgorithm.Ninox => ComputeNinox(frame),
            QualityAlgorithm.Histogram => ComputeHistogram(frame),
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
    /// Ninox on the run's single mask-reusing instance. The threshold is computed from the first
    /// frame and then reused, matching the engine's <c>image[4]</c>.
    /// </summary>
    private static double ComputeNinox(Frame frame)
    {
        _ninox ??= new NinoxQuality();
        _ninoxThreshold ??= DetectionThreshold.ComputeOrDefault(frame);
        return _ninox.Score(frame, _ninoxThreshold.Value);
    }

    /// <summary>
    /// Algorithm 2's whole body: the bright-50 sum divided by the box area, with the engine's
    /// 32-bit <c>(bottom-top)*(right-left)</c>. With no detection the box is all-zero, so the
    /// division is by zero and the score is <c>+inf</c> (NaN for an all-black frame).
    /// </summary>
    private static double ComputeHistogram(Frame frame)
    {
        double bright = EngineQuality.Bright50Squares(frame);
        int area = (_detectedBottom - _detectedTop) * (_detectedRight - _detectedLeft);
        return bright / area;
    }

    /// <summary>
    /// Forgets the per-run Ninox mask, threshold and detected box. Call between runs when the same
    /// process hosts more than one; the CLI's one run per process does not need it.
    /// </summary>
    public static void ResetRunState()
    {
        _ninox = null;
        _ninoxThreshold = null;
        ClearDetectedBoxState();
        _detectionPending = false;
    }

    /// <summary>Records the finder's box for the algorithms that share the engine's work image.</summary>
    internal static void NoteDetectedBox(int left, int top, int right, int bottom)
    {
        _detectedLeft = left;
        _detectedTop = top;
        _detectedRight = right;
        _detectedBottom = bottom;
        _detectionPending = true;
    }

    /// <summary>Records the finder's all-zero failure box.</summary>
    internal static void NoteDetectionCleared()
    {
        ClearDetectedBoxState();
        _detectionPending = true;
    }

    /// <summary>
    /// Hands the pending detection box to the Ninox instance once, so its own box life-cycle starts
    /// from what the finder wrote this frame. False when the finder has not run.
    /// </summary>
    internal static bool TryTakeDetectedBox(out int left, out int top, out int right, out int bottom)
    {
        left = _detectedLeft;
        top = _detectedTop;
        right = _detectedRight;
        bottom = _detectedBottom;
        if (!_detectionPending)
        {
            return false;
        }

        _detectionPending = false;
        return true;
    }

    private static void ClearDetectedBoxState()
    {
        _detectedLeft = 0;
        _detectedTop = 0;
        _detectedRight = 0;
        _detectedBottom = 0;
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

    /// <summary>
    /// Converts raw scores to PIPP's "percent of best" ordering, preserving input order for equal
    /// scores (the engine's quality log keeps source order when every value is identical, e.g. the
    /// all-<c>+inf</c> <c>-qhisto</c> table).
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
            // The engine computes (int)(score/best*100). With best = +inf that ratio is NaN and the
            // MSVC cast lands on 0, which is why its -qhisto log shows 0% for every frame.
            double ratio = best > 0 ? scores[i] / best * 100.0 : 0.0;
            double pct = double.IsNaN(ratio) ? 0.0 : ratio;
            entries[i] = new QualityEntry(i, scores[i], pct);
        }

        Array.Sort(entries, (a, b) =>
        {
            int byQuality = b.PercentOfBest.CompareTo(a.PercentOfBest);
            return byQuality != 0 ? byQuality : a.Index.CompareTo(b.Index);
        });
        return entries;
    }
}

/// <summary>One frame's quality result.</summary>
/// <param name="Index">Zero-based input order.</param>
/// <param name="Score">Raw score.</param>
/// <param name="PercentOfBest">Score expressed as a percentage of the best frame, as PIPP's quality log reports it.</param>
public readonly record struct QualityEntry(int Index, double Score, double PercentOfBest);
