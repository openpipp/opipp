using Opipp.Core.Frames;

namespace Opipp.Core.Quality;

/// <summary>
/// PIPP's default quality metric, recovered from <c>pipp_core!FUN_1400af2d0</c>.
/// </summary>
/// <remarks>
/// <para>
/// The score is <c>sqrt(Σ weight(s)·metric(s)) · bright50 / 3251250</c>, where the weight is
/// <c>(qmin² / s²)</c> computed in 32-bit integer arithmetic. Because <c>qinc ≥ 1</c>, every stride
/// larger than <c>qmin</c> has an integer weight of zero: strides after <c>qmin</c> are still
/// measured (they consume CPU and, crucially, write into the mask buffer) but do not change the
/// value.
/// </para>
/// <para>
/// <b>This class is stateful on purpose.</b> The engine's mask buffer (<c>image+0xc48</c>) comes from
/// a grow-only allocator that never clears it, so a frame's score depends on every frame scored
/// before it in the same run. That reuse is what makes the first frame of a run score about 1.3x
/// higher than the same pixels in a later position. Use one instance per run to reproduce the
/// engine; use a fresh instance (or <see cref="Reset"/>) for a single-frame measurement, which
/// corresponds to PIPP's frame 1.
/// </para>
/// <para>
/// The engine also keeps the detection box in the same image struct and grows it by one pixel on
/// each side before measuring (see <see cref="SetDetectedBox"/>). With no detection the box stays
/// all-zero and the whole frame is measured.
/// </para>
/// </remarks>
public sealed class EngineQuality
{
    /// <summary>Number of brightest samples the final exposure factor integrates (the engine's "50").</summary>
    private const int BrightSampleCount = 50;

    /// <summary>50 · 255²: the factor that makes a frame with 50 saturated samples score ×1.</summary>
    private const double BrightScale = BrightSampleCount * 255.0 * 255.0;

    /// <summary>Samples at or above this value are ignored when picking the rescale reference.</summary>
    private const int ReferenceCeiling = 0xFFFA;

    private readonly int _qmin;
    private readonly int _qmax;
    private readonly int _qinc;

    /// <summary>True for the Ninox variant (<c>-qold</c>): unweighted strides, absolute-difference metric.</summary>
    private readonly bool _ninox;

    /// <summary>Mask buffer (<c>image+0xc48</c>): grow-only, never cleared.</summary>
    private byte[] _mask = [];

    /// <summary>The engine's object box (<c>image+0x374..0x377</c>), inclusive, top-down pixels.</summary>
    private int _boxLeft;
    private int _boxTop;
    private int _boxRight;
    private int _boxBottom;

    /// <summary>Creates a metric with the engine's stride sweep.</summary>
    /// <param name="qmin">Smallest stride (<c>-qmin</c>), at least 1.</param>
    /// <param name="qmax">Largest stride (<c>-qmax</c>), at least <paramref name="qmin"/> for any work to happen.</param>
    /// <param name="qinc">Stride step (<c>-qinc</c>), at least 1.</param>
    public EngineQuality(int qmin = 3, int qmax = 5, int qinc = 1)
        : this(qmin, qmax, qinc, ninox: false)
    {
        // The Processor creates one EngineQuality per run, so this is the run boundary for the
        // ambient state the stateless entry points (QualityMetrics) keep: the Ninox mask and the
        // finder's box. The internal Ninox instance does not reset here (it would discard the
        // detection box that is about to be measured).
        QualityMetrics.ResetRunState();
    }

    /// <summary>Creates the PIPP Default or the Ninox (<c>-qold</c>) metric over the same stride sweep.</summary>
    internal EngineQuality(int qmin, int qmax, int qinc, bool ninox)
    {
        if (qmin < 1) throw new ArgumentOutOfRangeException(nameof(qmin), "the engine divides by qmin²");
        if (qinc < 1) throw new ArgumentOutOfRangeException(nameof(qinc), "a step below one cannot advance the sweep");
        if (qmax < qmin) throw new ArgumentOutOfRangeException(nameof(qmax), "qmax must be at least qmin");

        _qmin = qmin;
        _qmax = qmax;
        _qinc = qinc;
        _ninox = ninox;
    }

    /// <summary>Smallest stride measured.</summary>
    public int QMin => _qmin;

    /// <summary>Largest stride measured.</summary>
    public int QMax => _qmax;

    /// <summary>Stride step.</summary>
    public int QInc => _qinc;

    /// <summary>True when the next <see cref="Score"/> call measures the whole frame.</summary>
    public bool MeasuresWholeFrame => _boxLeft == 0 && _boxTop == 0 && _boxRight == 0 && _boxBottom == 0;

    /// <summary>
    /// Installs the object finder's box, as the engine does after a successful detection. The
    /// coordinates are inclusive, top-down image pixels (<c>image+0x374..0x377</c>).
    /// </summary>
    public void SetDetectedBox(int left, int top, int right, int bottom)
    {
        ApplyDetectedBox(left, top, right, bottom);
        QualityMetrics.NoteDetectedBox(left, top, right, bottom);
    }

    /// <summary>Installs the finder's box without telling the ambient run state (used by the Ninox delegate).</summary>
    internal void ApplyDetectedBox(int left, int top, int right, int bottom)
    {
        _boxLeft = left;
        _boxTop = top;
        _boxRight = right;
        _boxBottom = bottom;
    }

    /// <summary>Zeroes the object box, as the engine's finder does on every failure path.</summary>
    public void ClearDetectedBox()
    {
        _boxLeft = 0;
        _boxTop = 0;
        _boxRight = 0;
        _boxBottom = 0;
        QualityMetrics.NoteDetectionCleared();
    }

    /// <summary>
    /// Forgets everything the run has accumulated (mask contents and box), returning the instance
    /// to its fresh-process state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_mask);
        ClearDetectedBox();
    }

    /// <summary>Computes the PIPP Default score; larger is better. NaN when nothing is above threshold.</summary>
    /// <param name="frame">Frame to score, at its working precision.</param>
    /// <param name="threshold8">
    /// The engine's detection threshold (<c>image[4]</c>, the logged "Calculated minpixel value"),
    /// used as <c>threshold8 &lt;&lt; 8</c> on the 16-bit working scale.
    /// </param>
    public double Score(Frame frame, int threshold8)
    {
        ArgumentNullException.ThrowIfNull(frame);

        ushort[] plane = BuildWorkingPlane(frame);

        int width = frame.Width;
        int height = frame.Height;
        ExpandBox(width, height, out int left, out int top, out int right, out int bottom);

        // The engine stores the grown box back; that only matters when a later frame is measured
        // without a fresh detection, where the expansion accumulates until the clamp saturates.
        _boxLeft = left;
        _boxTop = top;
        _boxRight = right;
        _boxBottom = bottom;

        // The work buffer is bottom-up, so the box's top edge in buffer rows is the image bottom.
        int bufferTop = height - 1 - bottom;
        int boxWidth = right - left + 1;
        int boxHeight = bottom - top + 1;

        double total = 0.0;
        int stride = _qmin;
        while (stride <= _qmax)
        {
            int nCols = boxWidth / stride;
            int nRows = boxHeight / stride;
            if (nCols < 2 || nRows < 2)
            {
                break;
            }

            double metric = Measure(plane, width, left, bufferTop, stride, nCols, nRows, threshold8);

            // 32-bit integer division: 1 at qmin, 0 at every larger stride. Ninox sums every
            // measured stride unweighted (the engine skips this multiply when algorithm == 1).
            int weight = _ninox ? 1 : (_qmin * _qmin) / (stride * stride);
            total += weight * metric;

            // Skip strides that reproduce the same geometry: the engine keeps the smallest one.
            int next = stride + _qinc;
            while (next <= _qmax && boxWidth / next == nCols && boxHeight / next == nRows)
            {
                next += _qinc;
            }

            stride = next;
        }

        double root = Math.Sqrt(total);
        return _ninox ? root : root * Bright50Squares(frame) / BrightScale;
    }

    /// <summary>
    /// Applies the engine's box life-cycle: an all-zero box becomes the whole frame, anything else
    /// grows one pixel on each side and is clamped.
    /// </summary>
    private void ExpandBox(int width, int height, out int left, out int top, out int right, out int bottom)
    {
        if (MeasuresWholeFrame)
        {
            left = 0;
            top = 0;
            right = width - 1;
            bottom = height - 1;
            return;
        }

        left = Math.Max(_boxLeft - 1, 0);
        top = Math.Max(_boxTop - 1, 0);
        right = Math.Min(_boxRight + 1, width - 1);
        bottom = Math.Min(_boxBottom + 1, height - 1);
    }

    /// <summary>One stride's measurement: subsample, rescale, blur, threshold, mean squared difference.</summary>
    /// <param name="top">Box top edge in the engine's bottom-up buffer rows.</param>
    private double Measure(ushort[] plane, int width, int left, int top, int stride, int nCols, int nRows, int threshold8)
    {
        // 1. Box-average subsampling: the stride x stride block at the box's top-left corner is
        //    averaged, then the block steps by `stride`. The work buffer is bottom-up, so the block
        //    rows `boxHeight % stride` leaves over are the top rows of the image (on a 640x640 frame
        //    at stride 3 that is one row, worth ~0.16%) and the leftover columns stay on the right.
        var buf = new int[nCols * nRows];
        int block = stride * stride;
        for (int y = 0; y < nRows; y++)
        {
            int rowBase = (top + (y * stride)) * width + left;
            int dst = y * nCols;
            for (int x = 0; x < nCols; x++)
            {
                long acc = 0;
                int origin = rowBase + (x * stride);
                for (int dy = 0; dy < stride; dy++)
                {
                    int offset = origin + (dy * width);
                    for (int dx = 0; dx < stride; dx++)
                    {
                        acc += plane[offset + dx];
                    }
                }

                buf[dst + x] = (int)(acc / block);
            }
        }

        // 2. Reference = brightest interior sample below the saturation ceiling; rescale it to 60000.
        int reference = 0;
        for (int y = 1; y < nRows - 1; y++)
        {
            int rowStart = y * nCols;
            for (int x = 0; x < nCols; x++)
            {
                int v = buf[rowStart + x];
                if (v > reference && v < ReferenceCeiling)
                {
                    reference = v;
                }
            }
        }

        if (reference > 0)
        {
            double gain = 60000.0 / reference;
            for (int i = 0; i < buf.Length; i++)
            {
                int scaled = (int)(buf[i] * gain);
                buf[i] = scaled > 65535 ? 65535 : scaled;
            }
        }

        // 3. 3x3 box blur, integer /9, interior only; the untouched border stays zero.
        var blurred = new int[nCols * nRows];
        for (int y = 1; y < nRows - 1; y++)
        {
            int p = (y * nCols) + 1;
            for (int x = 1; x < nCols - 1; x++, p++)
            {
                blurred[p] = (buf[p - nCols - 1] + buf[p - nCols] + buf[p - nCols + 1]
                    + buf[p - 1] + buf[p] + buf[p + 1]
                    + buf[p + nCols - 1] + buf[p + nCols] + buf[p + nCols + 1]) / 9;
            }
        }

        // 4. Threshold inside the 10%-plus-one crop and dilate the hits by 3x3 into the shared mask.
        int ct = 1 + (nRows / 10);
        int cl = 1 + (nCols / 10);
        int threshold = threshold8 << 8;

        int need = nCols * nRows;
        if (_mask.Length < need)
        {
            var bigger = new byte[Math.Max(need, _mask.Length * 2)];
            Array.Copy(_mask, bigger, _mask.Length);
            _mask = bigger;
        }

        byte[] mask = _mask;

        int above = 0;
        double sumAbove = 0.0;
        for (int y = ct; y < nRows - ct; y++)
        {
            int p = (y * nCols) + cl;
            for (int x = cl; x < nCols - cl; x++, p++)
            {
                if (blurred[p] < threshold)
                {
                    continue;
                }

                above++;
                sumAbove += blurred[p];
                int q = p - nCols;
                mask[q - 1] = 1;
                mask[q] = 1;
                mask[q + 1] = 1;
                mask[p - 1] = 1;
                mask[p] = 1;
                mask[p + 1] = 1;
                q = p + nCols;
                mask[q - 1] = 1;
                mask[q] = 1;
                mask[q + 1] = 1;
            }
        }

        if (above == 0)
        {
            return -1.0;                       // constant at 0x140132950 in the engine
        }

        // 5. Mean difference to the neighbouring pixel one step further into the bottom-up
        //    buffer (the row ABOVE in image coordinates) and one column to the right, over every
        //    marked pixel — including marks the previous frame left behind. The default metric uses
        //    squared differences and divides by 10; Ninox (algorithm 1) sums absolute differences
        //    and normalises by the mean of the above-threshold samples times 50.
        double sum = 0.0;
        int marked = 0;
        for (int y = ct; y < nRows - ct; y++)
        {
            int p = (y * nCols) + cl;
            for (int x = cl; x < nCols - cl; x++, p++)
            {
                if (mask[p] == 0)
                {
                    continue;
                }

                int c = blurred[p];
                int up = c - blurred[p + nCols];
                int right = c - blurred[p + 1];
                sum += _ninox
                    ? Math.Abs(up) + Math.Abs(right)
                    : ((double)up * up) + ((double)right * right);
                marked++;
            }
        }

        if (marked == 0 || above == 0)
        {
            return -1.0;
        }

        return _ninox
            ? sum / marked * 50.0 / (sumAbove / above)
            : sum / marked / 10.0;
    }

    /// <summary>
    /// Sum of the squares of the 50 brightest 8-bit samples (<c>FUN_1400b00b0</c>): a 256-bin
    /// histogram walked from bin 255 down, taking at most fifty samples.
    /// </summary>
    internal static double Bright50Squares(Frame frame)
    {
        var histogram = new int[256];
        var samples = frame.Samples;
        int pixels = frame.Width * frame.Height;

        if (frame.Channels == 1)
        {
            if (frame.BitDepth == 8)
            {
                for (int i = 0; i < pixels; i++)
                {
                    histogram[samples[i]]++;
                }
            }
            else
            {
                for (int i = 0; i < pixels; i++)
                {
                    histogram[samples[i] >> 8]++;
                }
            }
        }
        else
        {
            bool eightBit = frame.BitDepth == 8;
            for (int i = 0, j = 0; i < pixels; i++, j += 3)
            {
                int luma = ((299 * samples[j]) + (587 * samples[j + 1]) + (114 * samples[j + 2])) / 1000;
                histogram[(eightBit ? luma : luma >> 8) & 0xFF]++;
            }
        }

        int remaining = BrightSampleCount;
        double sum = 0.0;
        for (int bin = 255; bin >= 0 && remaining > 0; bin--)
        {
            int take = Math.Min(histogram[bin], remaining);
            sum += (double)take * bin * bin;
            remaining -= take;
        }

        return sum;
    }

    /// <summary>
    /// Projects the frame onto the engine's 16-bit working buffer: 8-bit samples are shifted left
    /// eight, 16-bit mono samples are used as-is, colour is projected to Rec.601 luma. Rows are
    /// stored bottom-up, as in the engine's work image — the mask layout depends on that order.
    /// </summary>
    private static ushort[] BuildWorkingPlane(Frame frame)
    {
        int width = frame.Width;
        int height = frame.Height;
        var plane = new ushort[width * height];
        var samples = frame.Samples;

        for (int y = 0; y < height; y++)
        {
            int source = y * width;
            int destination = (height - 1 - y) * width;

            if (frame.Channels == 1)
            {
                if (frame.BitDepth == 8)
                {
                    for (int x = 0; x < width; x++)
                    {
                        plane[destination + x] = (ushort)(samples[source + x] << 8);
                    }
                }
                else
                {
                    Array.Copy(samples, source, plane, destination, width);
                }
            }
            else if (frame.BitDepth == 8)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (source + x) * 3;
                    int luma = ((299 * samples[i]) + (587 * samples[i + 1]) + (114 * samples[i + 2])) / 1000;
                    plane[destination + x] = (ushort)(luma << 8);
                }
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (source + x) * 3;
                    int luma = ((299 * samples[i]) + (587 * samples[i + 1]) + (114 * samples[i + 2])) / 1000;
                    plane[destination + x] = (ushort)luma;
                }
            }
        }

        return plane;
    }
}
