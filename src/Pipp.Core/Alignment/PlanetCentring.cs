using Pipp.Core.Frames;

namespace Pipp.Core.Alignment;

/// <summary>Object box in the engine's bottom-up buffer row coordinates.</summary>
public readonly record struct PlanetBox(int Left, int Right, int Top, int Bottom)
{
    public int CentreX => Left + (Right - Left) / 2;
    public int CentreY => Top + (Bottom - Top) / 2;   // buffer rows, 0 = bottom
}

public static class PlanetCentring
{
    /// <summary>
    /// pipp_core FUN_140076ae0 (8-bit) / FUN_140077560 (16-bit).
    /// <paramref name="luma"/> is width*height 8-bit luma with row 0 = BOTTOM row.
    /// </summary>
    public static PlanetBox? Detect(byte[] luma, int w, int h, int threshold)
    {
        int T4 = threshold << 2;

        // ---- phase 1: longest horizontal run (> 9 px of 4-sample sum >= 4*thr) ----
        int bestLen = 0, bestRowLabel = 0, bestStart = 0, bestEnd = 0, bestRow = 0;
        int label = 2;
        if (2 < h - 4)
        {
            do
            {
                int sum = 0, runLen = 0, runStart = 0;
                int[] ring = new int[4];
                int row = label - 1;
                for (int x = 0; x < w; x++)
                {
                    int slot = x & 3;
                    int v = luma[row * w + x];
                    sum += v - ring[slot];
                    ring[slot] = v;
                    if (sum < T4)
                    {
                        if (runLen > 9 && bestLen < runLen)
                        {
                            bestEnd = x; bestLen = runLen;
                            bestRowLabel = label; bestStart = runStart;
                        }
                        runLen = 0;
                    }
                    else
                    {
                        if (runLen == 0) runStart = x;
                        runLen++;
                    }
                }
                bestRow = bestRowLabel;
                if (runLen > 9 && bestLen < runLen)
                {
                    bestRow = label; bestEnd = w - 1; bestLen = runLen;
                    bestRowLabel = label; bestStart = runStart;
                }
                label++;
            } while (label < h - 4);
        }
        if (bestLen == 0) return null;

        // ---- phase 2: extent towards increasing buffer rows ----------------------
        int minX = w - 1, bottom = 0, maxX = 0, minX2 = w - 1;
        int right = bestEnd, startX = bestStart, row2 = bestRow;
        if (row2 < h)
        {
            do
            {
                int sum = 0, runLen = 0, runMax = 0, runStart = 0, prevStart = 0;
                int flag = 0, flagCopy = 0;
                int[] ring = new int[4];
                bool stop = false;
                for (int x = 0; x < w; x++)
                {
                    runStart = prevStart;
                    int slot = x & 3;
                    int v = luma[row2 * w + x];
                    sum += v - ring[slot];
                    ring[slot] = v;
                    if (sum < T4)
                    {
                        if (runLen < 10)
                        {
                            if (right < x) { flagCopy = flag; stop = true; }
                        }
                        else if (runStart <= right && startX <= x)
                        {
                            flag = 1;
                            if (runStart < minX) minX = runStart;
                            if (runMax < x) runMax = x;
                            if (runStart < minX2) minX2 = runStart;
                            if (maxX < x) maxX = x;
                        }
                        if (stop) break;
                        runLen = 0;
                    }
                    else
                    {
                        if (runLen == 0) runStart = x;
                        runLen++;
                    }
                    flagCopy = flag; prevStart = runStart;
                }
                if (minX != w - 1) startX = minX;
                if (runMax != 0) right = runMax;
                if (runLen < 10 || right < runStart)
                {
                    if (flagCopy == 0) { bottom = row2 - 1; break; }
                }
                else
                {
                    right = w - 1; maxX = w - 1; startX = runStart;
                    if (runStart < minX2) minX2 = runStart;
                }
                minX = w - 1;
                row2++;
            } while (row2 < h);
        }

        // ---- phase 3: extent towards decreasing buffer rows ----------------------
        int top = h - 1, row3 = bestRowLabel;
        while (row3 >= 0)
        {
            int sum = 0, runLen = 0, runMax = 0, runStart = 0, prevStart = 0;
            int flag = 0, flagCopy = 0;
            int[] ring = new int[4];
            bool stop = false;
            for (int x = 0; x < w; x++)
            {
                runStart = prevStart;
                int slot = x & 3;
                int v = luma[row3 * w + x];
                sum += v - ring[slot];
                ring[slot] = v;
                if (sum < T4)
                {
                    if (runLen < 10)
                    {
                        if (bestEnd < x) { flagCopy = flag; stop = true; }
                    }
                    else if (runStart <= bestEnd && bestStart <= x)
                    {
                        flag = 1;
                        if (runStart < minX) minX = runStart;
                        if (runMax < x) runMax = x;
                        if (runStart < minX2) minX2 = runStart;
                        if (maxX < x) maxX = x;
                    }
                    if (stop) break;
                    runLen = 0;
                }
                else
                {
                    if (runLen == 0) runStart = x;
                    runLen++;
                }
                flagCopy = flag; prevStart = runStart;
            }
            if (runMax != 0) bestEnd = runMax;
            if (runLen < 10 || bestEnd < runStart)
            {
                if (flagCopy == 0) { top = row3 + 1; break; }
            }
            else
            {
                maxX = w - 1; right = w - 1; bestStart = runStart;
                if (runStart < minX2) minX2 = runStart;
            }
            row3--;
        }

        return new PlanetBox(minX2 - 1, maxX - 3, top, bottom);
    }

    /// <summary>-ctr offset for one frame, in top-down pixel coordinates.</summary>
    public static AlignmentOffset CentreOffset(Frame frame, int threshold)
    {
        int w = frame.Width, h = frame.Height;
        var luma = new byte[w * h];
        for (int y = 0; y < h; y++)                       // Frame is top-down
        {
            int dst = (h - 1 - y) * w;                    // engine buffer is bottom-up
            for (int x = 0; x < w; x++)
                luma[dst + x] = Luma8(frame, x, y);
        }

        if (Detect(luma, w, h, threshold) is not { } box)
            return AlignmentOffset.Zero;                  // engine drops the frame instead

        return new AlignmentOffset(w / 2 - box.CentreX, box.CentreY - h / 2);
    }

    /// <summary>Rec.601 8-bit luma; mono frames return their sample unchanged.</summary>
    private static byte Luma8(Frame frame, int x, int y)
    {
        if (!frame.IsColour) return (byte)frame.Samples[y * frame.Width + x];
        int i = (y * frame.Width + x) * 3;
        int b = frame.Samples[i], g = frame.Samples[i + 1], r = frame.Samples[i + 2];
        return (byte)((299 * r + 587 * g + 114 * b) / 1000);
    }
}
