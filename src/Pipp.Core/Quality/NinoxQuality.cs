using Pipp.Core.Frames;

namespace Pipp.Core.Quality;

/// <summary>
/// The original Ninox quality metric (<c>-qold</c>), recovered from
/// <c>pipp_core!FUN_1400af2d0</c> (algorithm 1).
/// </summary>
/// <remarks>
/// <para>
/// Ninox runs the same stride sweep, subsampling, rescale and blur as <see cref="EngineQuality"/>
/// but differs in two places:
/// </para>
/// <list type="bullet">
/// <item>every measured stride is accumulated unweighted (the engine skips the
/// <c>qmin²/stride²</c> multiply for algorithm 1), and the final value is <c>sqrt(total)</c> with no
/// exposure (bright-50) factor;</item>
/// <item>the per-stride metric is the mean <em>absolute</em> difference to the neighbour one row up
/// and one column right over the marked pixels, times 50 and divided by the mean of the
/// above-threshold samples (<c>FUN_1400aecf0</c> at 0x1400af05b: <c>sum/marked * 50.0 /
/// meanAbove</c>), not the squared-difference form the default algorithm uses.</item>
/// </list>
/// <para>
/// Like <see cref="EngineQuality"/> the mask buffer is reused across frames without being cleared,
/// so one instance must be kept for the whole run to reproduce the engine's scores. A fresh instance
/// measures a frame as PIPP would measure the first frame of a run.
/// </para>
/// </remarks>
public sealed class NinoxQuality
{
    private readonly EngineQuality _engine;

    /// <summary>Creates the metric with the engine's stride sweep.</summary>
    /// <param name="qmin">Smallest stride (<c>-qmin</c>), at least 1.</param>
    /// <param name="qmax">Largest stride (<c>-qmax</c>), at least <paramref name="qmin"/>.</param>
    /// <param name="qinc">Stride step (<c>-qinc</c>), at least 1.</param>
    public NinoxQuality(int qmin = 3, int qmax = 5, int qinc = 1)
    {
        _engine = new EngineQuality(qmin, qmax, qinc, ninox: true);
    }

    /// <summary>Computes the Ninox score; larger is better.</summary>
    /// <param name="frame">Frame to score, at its working precision.</param>
    /// <param name="threshold8">
    /// The run's detection threshold (<c>image[4]</c>, the logged "Calculated minpixel value"), used
    /// as <c>threshold8 &lt;&lt; 8</c> on the 16-bit working scale.
    /// </param>
    public double Score(Frame frame, int threshold8)
    {
        // The engine keeps the box in the work image: the object finder rewrites it before quality,
        // and algorithms 0/1 expand it by one pixel per side. The ambient box carries that write
        // across from the run's EngineQuality instance; without detection it stays zero and the
        // first call measures the whole frame.
        if (QualityMetrics.TryTakeDetectedBox(out int left, out int top, out int right, out int bottom))
        {
            _engine.ApplyDetectedBox(left, top, right, bottom);
        }

        return _engine.Score(frame, threshold8);
    }

    /// <summary>Installs the object finder's box, exactly as the engine's finder does.</summary>
    public void SetDetectedBox(int left, int top, int right, int bottom) =>
        _engine.ApplyDetectedBox(left, top, right, bottom);

    /// <summary>Zeroes the object box, as the engine's finder does on every failure path.</summary>
    public void ClearDetectedBox() => _engine.ApplyDetectedBox(0, 0, 0, 0);

    /// <summary>Forgets the accumulated mask and box, returning the instance to its fresh state.</summary>
    public void Reset() => _engine.Reset();
}
