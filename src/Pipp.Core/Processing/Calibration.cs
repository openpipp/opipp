using Pipp.Core.Frames;

namespace Pipp.Core.Processing;

/// <summary>
/// Sensor calibration with master frames.
/// </summary>
/// <remarks>
/// PIPP builds master darks/flats with <c>pipp_dll!stack_frames</c> (mean of the input frames,
/// with an optional down-reference frame subtracted) and then applies them to every science frame:
/// a dark is subtracted with clamping at zero, and a flat divides the frame after normalisation by
/// the flat's own mean. The engine passes the masters as <c>-dark_tmp</c>/<c>-dark_pgm</c> and
/// <c>-flat_tmp</c>; the PGM is the normalised 8-bit copy used for the divide.
/// </remarks>
public static class Calibration
{
    /// <summary>Subtracts a master dark, clamping negative results to zero.</summary>
    /// <exception cref="ArgumentException">Geometry, channel count or depth differ.</exception>
    public static Frame SubtractDark(Frame frame, Frame dark)
    {
        ValidatePair(frame, dark, nameof(dark));
        var dst = frame.Clone();
        var s = frame.Samples;
        var d = dark.Samples;
        var o = dst.Samples;
        for (int i = 0; i < o.Length; i++)
        {
            int v = s[i] - d[i];
            o[i] = (ushort)(v < 0 ? 0 : v);
        }
        return dst;
    }

    /// <summary>
    /// Divides by a master flat and rescales by the flat's mean so overall brightness is preserved.
    /// Flat pixels of zero are treated as 1 to avoid division by zero.
    /// </summary>
    /// <exception cref="ArgumentException">Geometry, channel count or depth differ.</exception>
    public static Frame DivideFlat(Frame frame, Frame flat)
    {
        ValidatePair(frame, flat, nameof(flat));
        var dst = frame.Clone();
        var s = frame.Samples;
        var f = flat.Samples;
        var o = dst.Samples;
        int channels = frame.Channels;

        // Per-channel flat mean keeps colour balance; mono frames have a single mean.
        var mean = new double[channels];
        var count = new double[channels];
        for (int i = 0; i < f.Length; i++)
        {
            int c = i % channels;
            mean[c] += f[i];
            count[c]++;
        }
        for (int c = 0; c < channels; c++)
        {
            mean[c] = count[c] > 0 ? mean[c] / count[c] : 1.0;
        }

        int max = frame.BitDepth == 8 ? 255 : 65535;
        for (int i = 0; i < o.Length; i++)
        {
            int c = i % channels;
            double denom = f[i] <= 0 ? 1.0 : f[i];
            double v = s[i] * mean[c] / denom;
            o[i] = (ushort)Math.Clamp((int)Math.Round(v), 0, max);
        }
        return dst;
    }

    /// <summary>
    /// Flat division for a Bayer mosaic (<c>-flat_bayer</c>): each of the four mosaic phases is
    /// normalised by its own mean instead of the frame-wide mean, so the CFA's sensitivity
    /// differences cancel. Measured against the engine: a constant-100 input under a flat whose
    /// phases are R=100/G=150/G=150/B=200 gives 150/100/100/75 without the switch and a flat 100
    /// with it.
    /// </summary>
    public static Frame DivideFlatBayer(Frame frame, Frame flat)
    {
        ValidatePair(frame, flat, nameof(flat));
        if (frame.Channels != 1 || flat.Channels != 1)
        {
            return DivideFlat(frame, flat);
        }

        var dst = frame.Clone();
        var s = frame.Samples;
        var f = flat.Samples;
        var o = dst.Samples;
        int w = frame.Width, h = frame.Height;

        var sum = new double[4];
        var count = new double[4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = ((y & 1) << 1) | (x & 1);
                sum[p] += f[(y * w) + x];
                count[p]++;
            }
        }

        for (int p = 0; p < 4; p++)
        {
            sum[p] = count[p] > 0 ? sum[p] / count[p] : 1.0;
        }

        int max = frame.BitDepth == 8 ? 255 : 65535;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w) + x;
                int p = ((y & 1) << 1) | (x & 1);
                double denom = f[i] <= 0 ? 1.0 : f[i];
                double v = s[i] * sum[p] / denom;
                o[i] = (ushort)Math.Clamp((int)Math.Round(v), 0, max);
            }
        }

        return dst;
    }

    /// <summary>
    /// Builds a master frame from a set of calibration frames (mean), which is what the GUI's
    /// <c>stack_frames</c> call produces before handing the result to the engine.
    /// </summary>
    /// <param name="frames">Calibration frames; all must share geometry.</param>
    /// <param name="subtract">Optional frame subtracted from each input before averaging.</param>
    public static Frame BuildMaster(IEnumerable<Frame> frames, Frame? subtract = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        using var e = frames.GetEnumerator();
        if (!e.MoveNext()) throw new ArgumentException("no calibration frames supplied", nameof(frames));

        var first = e.Current;
        var stacker = new Stacking.FrameStacker(first.Width, first.Height, first.Channels, first.BitDepth, Stacking.StackMethod.Mean);
        stacker.Add(first, subtract);
        while (e.MoveNext())
        {
            stacker.Add(e.Current, subtract);
        }
        return stacker.Result();
    }

    private static void ValidatePair(Frame frame, Frame other, string paramName)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(other);
        if (frame.Width != other.Width || frame.Height != other.Height ||
            frame.Channels != other.Channels || frame.BitDepth != other.BitDepth)
        {
            throw new ArgumentException(
                $"{other.Width}x{other.Height}x{other.Channels}@{other.BitDepth} does not match frame {frame.Width}x{frame.Height}x{frame.Channels}@{frame.BitDepth}",
                paramName);
        }
    }
}
