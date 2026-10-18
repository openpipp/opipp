using Opipp.Core.Frames;

namespace Opipp.Core.Alignment;

/// <summary>
/// The engine's "Calculated minpixel value": the detection threshold it prints in its log and uses
/// whenever <c>-mp</c> is absent. Recovered from <c>pipp_core</c> <c>FUN_14009ab40</c>.
/// </summary>
/// <remarks>
/// The engine does not threshold on the full-resolution histogram. It builds a 1/3-scale copy of the
/// frame (each pixel the integer mean of a 3x3 block), takes each row's maximum, keeps the five
/// <em>smallest</em> row maxima, pads with zero to five and averages them, then averages every
/// small-image pixel strictly above that mean and moves a quarter of the way towards it:
/// <c>T = m + (meanAbove - m) / 4</c> with integer division. If no pixel exceeds the mean the result
/// is zero, and the caller substitutes the default of 40.
///
/// The five smallest row maxima matter: on a frame with a planet, most rows are empty, so
/// <c>m</c> lands near the background level. That is why the engine's threshold (17-18 on the real
/// Saturn capture) is far below an Otsu split (59-60), which the earlier reconstruction used.
///
/// Verified exact on 4/4 sampled real frames. Synthetic fixtures come out one unit lower because the
/// engine's per-sample luma conversion rounds inside its lookup tables; the difference is recorded in
/// <c>docs/status.md</c>.
/// </remarks>
public static class DetectionThreshold
{
    private const int AccumulatorSlots = 5;

    /// <summary>Default the engine substitutes when the computation yields zero.</summary>
    public const int Default = 40;

    /// <summary>
    /// Computes the threshold for <paramref name="frame"/>. Returns zero when the frame has no pixel
    /// above the row-maximum mean, matching the engine (which then falls back to <see cref="Default"/>).
    /// </summary>
    public static int Compute(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int smallWidth = frame.Width / 3;
        int smallHeight = frame.Height / 3;
        if (smallWidth <= 0 || smallHeight <= 0)
        {
            return 0;
        }

        var small = new byte[smallWidth * smallHeight];
        for (int by = 0; by < smallHeight; by++)
        {
            for (int bx = 0; bx < smallWidth; bx++)
            {
                int sum = 0;
                for (int dy = 0; dy < 3; dy++)
                {
                    for (int dx = 0; dx < 3; dx++)
                    {
                        sum += Luma8(frame, (bx * 3) + dx, (by * 3) + dy);
                    }
                }

                small[(by * smallWidth) + bx] = (byte)(sum / 9);
            }
        }

        // Row maxima, then the five smallest (the engine initialises its slots high and keeps the
        // smallest values it sees), padded with zero when the frame has fewer than five rows.
        var maxima = new int[smallHeight];
        for (int y = 0; y < smallHeight; y++)
        {
            int max = 0;
            int rowStart = y * smallWidth;
            for (int x = 0; x < smallWidth; x++)
            {
                int v = small[rowStart + x];
                if (v > max)
                {
                    max = v;
                }
            }

            maxima[y] = max;
        }

        Array.Sort(maxima);
        int slots = Math.Min(AccumulatorSlots, maxima.Length);
        int sumOfSlots = 0;
        for (int i = 0; i < slots; i++)
        {
            sumOfSlots += maxima[i];
        }

        int meanOfRowMaxima = sumOfSlots / AccumulatorSlots;

        long total = 0;
        int count = 0;
        foreach (byte v in small)
        {
            if (meanOfRowMaxima < v)
            {
                count++;
                total += v;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        int meanAbove = (int)(total / count);
        return meanOfRowMaxima + ((meanAbove - meanOfRowMaxima) >> 2);
    }

    /// <summary>Computes the threshold, substituting <see cref="Default"/> when the engine would.</summary>
    public static int ComputeOrDefault(Frame frame)
    {
        int value = Compute(frame);
        return value == 0 ? Default : value;
    }

    private static int Luma8(Frame frame, int x, int y)
    {
        int index = ((y * frame.Width) + x) * frame.Channels;
        int value;
        if (frame.Channels == 1)
        {
            value = frame.Samples[index];
        }
        else
        {
            value = (int)((0.299 * frame.Samples[index])
                + (0.587 * frame.Samples[index + 1])
                + (0.114 * frame.Samples[index + 2]));
        }

        return frame.BitDepth == 16 ? value >> 8 : value;
    }
}
