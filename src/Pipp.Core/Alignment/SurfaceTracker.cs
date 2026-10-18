using Pipp.Core.Frames;
using Pipp.Core.Processing;
using Pipp.Core.Quality;

namespace Pipp.Core.Alignment;

/// <summary>
/// Surface-feature tracking (<c>-en_surface_feature_tracking</c>): follows the pattern inside the
/// user's alignment box (<c>-afb_x/-afb_y/-afb_width/-afb_height</c>) from the reference frame.
/// </summary>
/// <remarks>
/// <para>
/// This is a port of the engine's own tracker, not a generic correlator. The engine's call chain is
/// <c>FUN_1400a7470</c> → <c>FUN_1400a6e40</c> (coarse seed on a <c>box/16</c> grid) →
/// <c>FUN_1400a5f40</c> (fine refinement). Both stages score candidates the same way:
/// </para>
/// <list type="number">
///   <item>the moving frame is averaged with a 5×5 integer box blur (sum ÷ 25),</item>
///   <item>the reference block is captured through the same blur and then stretched so its 5th and
///         95th percentiles map to 0 and 255 (<c>FUN_1400a5400</c> + <c>FUN_1400a4d80</c>),</item>
///   <item>each candidate gets its own brightness/contrast LUT: an integer mean derived from the
///         candidate's even-even subsample scaled by the reference's low percentile, then a gain that
///         lands the candidate's mean on the reference's stretched mean,</item>
///   <item>the score is the sum of squared differences between the LUT-mapped candidate and the
///         stretched reference, over the same even-even subsample,</item>
///   <item>the first strictly-better candidate in dy-then-dx order wins.</item>
/// </list>
/// <para>
/// The search geometry is the engine's: the coarse stage returns a seed quantised to
/// <c>box/16</c>, and the fine stage scans an asymmetric window of
/// <c>[seed − box/16, seed + box/16 − 1]</c> on both axes. That asymmetry is why the engine reports
/// −3…+2 for a 48-pixel box rather than ±3, and reproducing it matters: a symmetric or wider search
/// finds correlations the engine never considers.
/// </para>
/// <para>
/// Bayer frames are demosaiced and reduced to Rec.601 luma before tracking, because the engine
/// resolves colour before it measures motion; tracking the raw mosaic measurably disagrees with the
/// original on real captures.
/// </para>
/// <para>
/// Convention: the returned offset is the displacement of the tracked content, matching
/// <see cref="FrameAligner.EstimateTranslation"/> — positive X means the pattern sits that many pixels
/// further right in the frame; positive Y means further down.
/// </para>
/// </remarks>
public static class SurfaceTracker
{
    /// <summary>Largest allowed value of the engine's LUT output.</summary>
    private const double LutCeiling = 255.0;

    /// <summary>Blur kernel is 5×5 with integer division by 25 (0x19 in the engine).</summary>
    private const int BlurSumDivisor = 25;

    /// <summary>Reference statistics the fine stage needs; build once per run with <see cref="BuildReference"/>.</summary>
    public sealed class TrackerReference
    {
        internal TrackerReference(byte[] block, int width, int height, byte low, long sum, long sumStretched,
            ulong threshold, bool hasFeature)
        {
            Block = block;
            Width = width;
            Height = height;
            Low = low;
            Sum = sum;
            SumStretched = sumStretched;
            Threshold = threshold;
            HasFeature = hasFeature;
        }

        internal byte[] Block { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal byte Low { get; }
        internal long Sum { get; }
        internal long SumStretched { get; }
        internal ulong Threshold { get; }
        internal bool HasFeature { get; }
    }

    /// <summary>
    /// Captures the reference template from <paramref name="frame"/>. The engine does this once, after
    /// the first tracked frame, and reuses the template for the rest of the run.
    /// </summary>
    public static TrackerReference BuildReference(Frame frame, PixelRect box)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Clip(box, frame.Width, frame.Height) is not { } region)
            throw new ArgumentException("the alignment box does not intersect the frame", nameof(box));

        byte[] luma = ToTrackerLuma(frame);
        byte[] blurred = Blur(luma, frame.Width, frame.Height, region.X, region.Y, region.Width, region.Height);

        var (low, high) = Percentiles(blurred);
        var lut = new byte[256];
        if (high - low > 14 && low < high)
        {
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Clamp((i - low) * LutCeiling / (high - low), 0, LutCeiling);
        }

        var stretched = new byte[blurred.Length];
        long sum = 0, sumStretched = 0;
        for (int i = 0; i < blurred.Length; i++)
        {
            stretched[i] = lut[blurred[i]];
            sum += blurred[i];
            sumStretched += stretched[i];
        }

        // FUN_1400a7d00: half the sum of squared deviations of the stretched template.
        // The fine stage rejects a match whose score exceeds it.
        double mean = stretched.Length == 0 ? 0 : (double)sumStretched / stretched.Length;
        ulong threshold = 0;
        foreach (byte p in stretched)
        {
            double d = mean - p;
            threshold += (ulong)(long)(d * d);
        }
        threshold >>= 1;

        // A block whose 5th..95th percentile span is 14 or less carries no surface feature: the
        // engine's stretch LUT (FUN_1400a4d80) stays all-zero in that case, so every candidate ties
        // at zero and the measurement is meaningless.
        bool hasFeature = high - low > 14 && sumStretched > 0;
        return new TrackerReference(stretched, region.Width, region.Height, low, sum, sumStretched, threshold, hasFeature);
    }

    /// <summary>
    /// Measures where the template sits in <paramref name="frame"/>.
    /// </summary>
    /// <param name="reference">Template captured once with <see cref="BuildReference"/>.</param>
    /// <param name="frame">Frame to measure.</param>
    /// <param name="box">The alignment box, in the same coordinates it was captured with.</param>
    /// <returns>The displacement, or null when the engine's own rejection criterion fires.</returns>
    public static AlignmentOffset? Measure(TrackerReference reference, Frame frame, PixelRect box)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(frame);
        if (Clip(box, frame.Width, frame.Height) is not { } region) return null;
        if (region.Width != reference.Width || region.Height != reference.Height) return null;
        if (!reference.HasFeature) return null;

        byte[] luma = ToTrackerLuma(frame);
        int e8 = region.Width >> 4, f8 = region.Height >> 4;

        // Seed at the reference box. The engine's coarse stage (FUN_1400a6e40) exists to catch
        // motion larger than the fine window, but it only lands reliably because of acceptance
        // gates on the candidate's percentiles and brightness ratio; without those gates a global
        // SSD scan picks auto-correlation echoes and measurably degrades real captures. On the
        // Saturn fixture the gated stage seeds at the box anyway, so this is the same search for
        // any capture whose motion stays inside the fine window, which is the case the tracker is
        // for. Re-enable once the gates are ported.
        int seedX = region.X, seedY = region.Y;

        // Fine stage: asymmetric window around the seed (FUN_1400a5f40).
        if (seedX < e8 + 1 || seedX > frame.Width - e8 - 1 || seedY < f8 + 1 || seedY > frame.Height - f8 - 1)
            return null;

        int bw = region.Width + 2 * e8, bh = region.Height + 2 * f8;
        byte[] buf = Blur(luma, frame.Width, frame.Height, seedX - e8, seedY - f8, bw, bh);

        int bestX = 0, bestY = 0;
        ulong best = 0xff000000UL;
        int pixels = region.Width * region.Height;
        var lut = new byte[256];

        for (int dy = 0; dy < 2 * f8; dy++)
        {
            for (int dx = 0; dx < 2 * e8; dx++)
            {
                // Candidate statistics: even-even subsample, ×4 as the engine does.
                long sum4 = 0;
                for (int y = 0; y < region.Height; y += 2)
                {
                    int row = (dy + y) * bw + dx;
                    for (int x = 0; x < region.Width; x += 2)
                        sum4 += buf[row + x];
                }
                sum4 *= 4;

                int mean = reference.Sum == 0 ? 0 : (int)((double)sum4 / reference.Sum * reference.Low);
                if (mean > 255) mean = 255;

                long denominator = sum4 - (long)mean * pixels;
                double gain = denominator == 0 ? 0 : (double)reference.SumStretched / denominator;
                for (int i = 0; i < 256; i++)
                    lut[i] = (byte)Math.Clamp((i - mean) * gain, 0, LutCeiling);

                // Score: SSD over the same subsample, against the stretched template.
                ulong score = 0;
                int r0 = 0;
                for (int y = 0; y < region.Height; y += 2)
                {
                    int row = (dy + y) * bw + dx;
                    int rrow = r0;
                    for (int x = 0; x < region.Width; x += 2)
                    {
                        int d = lut[buf[row + x]] - reference.Block[rrow + x];
                        score += (ulong)(d * d);
                    }
                    r0 += 2 * region.Width;   // sampled rows skip every other reference row
                }

                if (score * 4 < best)
                {
                    best = score * 4;
                    bestX = dx;
                    bestY = dy;
                }

            }
        }

        if (reference.Threshold < best) return null;

        return new AlignmentOffset(bestX - e8, bestY - f8);
    }

    /// <summary>
    /// Compatibility overload: captures the template from <paramref name="reference"/> and tracks
    /// <paramref name="frame"/>. <paramref name="searchRadius"/> is accepted for callers that still
    /// pass one but no longer changes the search — the engine's window is <c>box/16</c> and using a
    /// wider one finds matches the original never considers.
    /// </summary>
    public static AlignmentOffset? Measure(Frame reference, Frame frame, int searchRadius, PixelRect box)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegative(searchRadius);
        if (reference.Width != frame.Width || reference.Height != frame.Height)
            throw new ArgumentException("frames must have the same geometry", nameof(frame));

        if (Clip(box, reference.Width, reference.Height) is null) return null;
        var template = BuildReference(reference, box);
        return Measure(template, frame, box);
    }

    /// <summary>Coarse search on the block-downsampled frame; returns an absolute seed position.</summary>
    private static (int X, int Y) CoarseSeed(byte[] luma, int width, int height, TrackerReference reference,
        PixelRect box, int e8, int f8)
    {
        if (e8 <= 0 || f8 <= 0) return (box.X, box.Y);

        int w2 = width / e8, h2 = height / f8;
        int cw = Math.Max(1, box.Width / e8), ch = Math.Max(1, box.Height / f8);
        if (w2 <= cw + 2 || h2 <= ch + 2) return (box.X, box.Y);

        var small = new byte[w2 * h2];
        for (int y = 0; y < h2; y++)
            for (int x = 0; x < w2; x++)
            {
                int sum = 0;
                for (int yy = 0; yy < f8; yy++)
                {
                    int row = (y * f8 + yy) * width + x * e8;
                    for (int xx = 0; xx < e8; xx++) sum += luma[row + xx];
                }
                small[y * w2 + x] = (byte)(sum / (e8 * f8));
            }

        // Coarse template: the same block downsample of the reference box region, exactly as
        // FUN_1400a7ec0 captures it (FUN_1400a4ef0 with the box factors).
        var coarse = new byte[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int sum = 0;
                for (int yy = 0; yy < f8; yy++)
                {
                    int row = (box.Y + y * f8 + yy) * width + box.X + x * e8;
                    for (int xx = 0; xx < e8; xx++) sum += luma[row + xx];
                }
                coarse[y * cw + x] = (byte)(sum / (e8 * f8));
            }

        var (low, high) = Percentiles(coarse);
        var cl = new byte[256];
        if (high - low > 14)
            for (int i = 0; i < 256; i++) cl[i] = (byte)Math.Clamp((i - low) * LutCeiling / (high - low), 0, LutCeiling);
        long coarseSum = 0, coarseStretched = 0;
        for (int i = 0; i < coarse.Length; i++) coarseSum += coarse[i];
        for (int i = 0; i < coarse.Length; i++) { coarse[i] = cl[coarse[i]]; coarseStretched += coarse[i]; }

        int bestCol = 0, bestRow = 0;
        ulong best = ulong.MaxValue;
        int cpx = cw * ch;
        var lut = new byte[256];

        for (int row = 1; row < h2 - ch - 1; row++)
            for (int col = 1; col < w2 - cw - 1; col++)
            {
                long sum = 0;
                for (int y = 0; y < ch; y++)
                {
                    int baseIdx = (row + y) * w2 + col;
                    for (int x = 0; x < cw; x++) sum += small[baseIdx + x];
                }
                if (sum == 0) continue;

                int mean = (int)((double)sum / coarseSum * low);
                if (mean > 255) mean = 255;
                long denom = sum - (long)mean * cpx;
                if (denom == 0) continue;
                double gain = (double)coarseStretched / denom;
                for (int i = 0; i < 256; i++) lut[i] = (byte)Math.Clamp((i - mean) * gain, 0, LutCeiling);

                ulong score = 0;
                for (int y = 0; y < ch; y++)
                {
                    int baseIdx = (row + y) * w2 + col;
                    int rbase = y * cw;
                    for (int x = 0; x < cw; x++)
                    {
                        int d = lut[small[baseIdx + x]] - coarse[rbase + x];
                        score += (ulong)(d * d);
                    }
                }
                if (score < best) { best = score; bestCol = col; bestRow = row; }
            }

        return (bestCol * e8, bestRow * f8);
    }

    /// <summary>5×5 integer box blur with edge clamping, over an arbitrary region.</summary>
    private static byte[] Blur(byte[] image, int width, int height, int x0, int y0, int w, int h)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = y0 + y;
            for (int x = 0; x < w; x++)
            {
                int sx = x0 + x;
                int sum = 0;
                for (int dy = -2; dy <= 2; dy++)
                {
                    int yy = Math.Clamp(sy + dy, 0, height - 1);
                    int row = yy * width;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = Math.Clamp(sx + dx, 0, width - 1);
                        sum += image[row + xx];
                    }
                }
                dst[y * w + x] = (byte)(sum / BlurSumDivisor);
            }
        }
        return dst;
    }

    /// <summary>5th/95th percentile as the engine computes them: the 5th pixel's value, averaged over a partial bin.</summary>
    private static (byte Low, byte High) Percentiles(byte[] block)
    {
        var hist = new int[256];
        foreach (byte b in block) hist[b]++;

        int acc = 0, need = 5;
        for (int i = 0; i < 256; i++)
        {
            int c = hist[i];
            if (c == 0) continue;
            if (need <= c) { acc += i * need; break; }
            acc += c * i;
            need -= c;
        }
        int low = acc / 5;

        acc = 0; need = 5;
        for (int i = 255; i >= 0; i--)
        {
            int c = hist[i];
            if (c == 0) continue;
            if (need <= c) { acc += i * need; break; }
            acc += c * i;
            need -= c;
        }
        int high = acc / 5;
        return ((byte)low, (byte)high);
    }

    /// <summary>8-bit luma in the engine's colour space; Bayer frames are demosaiced first.</summary>
    private static byte[] ToTrackerLuma(Frame frame)
    {
        var source = frame.Bayer != BayerPattern.None ? Debayer.ToColour(frame) : frame;
        ushort[] luma = QualityMetrics.ToLuma(source);
        var bytes = new byte[luma.Length];
        if (frame.BitDepth == 16)
        {
            // FUN_1400a4a50 keeps the high byte of a 16-bit sample.
            for (int i = 0; i < luma.Length; i++) bytes[i] = (byte)(luma[i] >> 8);
        }
        else
        {
            for (int i = 0; i < luma.Length; i++) bytes[i] = (byte)Math.Min(255, (int)luma[i]);
        }
        return bytes;
    }

    private static PixelRect? Clip(PixelRect box, int width, int height)
    {
        if (box.Width <= 0 || box.Height <= 0) return null;
        int x0 = Math.Max(0, box.X);
        int y0 = Math.Max(0, box.Y);
        int x1 = (int)Math.Min(width, (long)box.X + box.Width);
        int y1 = (int)Math.Min(height, (long)box.Y + box.Height);
        return x1 <= x0 || y1 <= y0 ? null : new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }
}
