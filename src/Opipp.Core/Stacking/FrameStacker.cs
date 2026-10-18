using Opipp.Core.Alignment;
using Opipp.Core.Frames;

namespace Opipp.Core.Stacking;

/// <summary>How frames are combined.</summary>
public enum StackMethod
{
    /// <summary>Arithmetic mean (master dark/flat frames, quality-weighted stacks).</summary>
    Mean = 0,

    /// <summary>Sum with clamping (brightness-preserving accumulation).</summary>
    Sum = 1,

    /// <summary>Per-pixel median (outlier rejection).</summary>
    Median = 2,
}

/// <summary>
/// Accumulates frames into a master frame.
/// </summary>
/// <remarks>
/// PIPP's master dark/flat builder (<c>pipp_dll!stack_frames</c>, read via REA) accumulates frames
/// into a dedicated stacker object, prints <c>"  %d frames stacked"</c>, writes a TIFF plus an
/// intermediate PGM for the engine, and aborts cleanly when frame geometry or sample size changes.
/// It also accepts an optional frame to subtract from every input (used for flat-darks).
/// This class reproduces that contract: same geometry checks, optional subtraction, mean/median
/// output, and a hard cap on retained frames for median mode.
/// <para>
/// Rounding and accumulation follow the native stacker exactly: its per-pixel accumulator is a
/// 32-bit integer and the final average is an unsigned integer division, so every fractional mean
/// truncates toward zero (10 and 11 average to 10). The accumulator here is 64-bit, which produces
/// the same samples for any run the native 32-bit accumulator does not overflow.
/// </para>
/// </remarks>
public sealed class FrameStacker
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly int _bitDepth;
    private readonly int _max;
    private readonly StackMethod _method;
    private readonly long[] _sum;
    private readonly List<ushort[]>? _retained;
    private int _count;

    /// <summary>Creates a stacker for frames of the given geometry.</summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="channels">1 or 3.</param>
    /// <param name="bitDepth">8 or 16.</param>
    /// <param name="method">Combination rule.</param>
    /// <param name="maxMedianFrames">Refuse media mode beyond this many frames (memory guard).</param>
    public FrameStacker(int width, int height, int channels, int bitDepth, StackMethod method = StackMethod.Mean, int maxMedianFrames = 64)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (channels is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(channels));
        if (bitDepth is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(bitDepth));

        _width = width;
        _height = height;
        _channels = channels;
        _bitDepth = bitDepth;
        _max = bitDepth == 8 ? 255 : 65535;
        _method = method;
        _sum = new long[checked(width * height * channels)];
        if (method == StackMethod.Median)
        {
            _retained = new List<ushort[]>(Math.Min(maxMedianFrames, 16));
            MaxMedianFrames = maxMedianFrames;
        }
    }

    /// <summary>Frame width the stacker accepts.</summary>
    public int Width => _width;

    /// <summary>Frame height the stacker accepts.</summary>
    public int Height => _height;

    /// <summary>Channel count the stacker accepts (1 or 3).</summary>
    public int Channels => _channels;

    /// <summary>Bit depth the stacker accepts (8 or 16).</summary>
    public int BitDepth => _bitDepth;

    /// <summary>Maximum frames retained in <see cref="StackMethod.Median"/> mode.</summary>
    public int MaxMedianFrames { get; }

    /// <summary>Frames accumulated so far.</summary>
    public int FrameCount => _count;

    /// <summary>Adds one frame, optionally subtracting a reference frame first (flat-dark correction).</summary>
    /// <exception cref="ArgumentException">Geometry or depth does not match the stacker.</exception>
    /// <exception cref="InvalidOperationException">Median mode exceeded its frame cap.</exception>
    public void Add(Frame frame, Frame? subtract = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != _width || frame.Height != _height || frame.Channels != _channels)
            throw new ArgumentException(
                $"frame is {frame.Width}x{frame.Height}x{frame.Channels}, stacker expects {_width}x{_height}x{_channels}", nameof(frame));
        if (frame.BitDepth != _bitDepth)
            throw new ArgumentException($"frame depth {frame.BitDepth} != stacker depth {_bitDepth}", nameof(frame));

        var src = frame.Samples;
        if (subtract is not null)
        {
            if (subtract.Width != _width || subtract.Height != _height || subtract.Channels != _channels ||
                subtract.BitDepth != _bitDepth)
            {
                throw new ArgumentException("subtract frame geometry/depth does not match", nameof(subtract));
            }
            var sub = subtract.Samples;
            var tmp = new ushort[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                int v = src[i] - sub[i];
                tmp[i] = (ushort)(v < 0 ? 0 : v);
            }
            src = tmp;
        }

        if (_method == StackMethod.Median)
        {
            if (_retained!.Count >= MaxMedianFrames)
                throw new InvalidOperationException($"median stacking is limited to {MaxMedianFrames} frames");
            _retained.Add(src);
        }
        else
        {
            var sum = _sum;
            for (int i = 0; i < src.Length; i++)
            {
                sum[i] += src[i];
            }
        }

        _count++;
    }

    /// <summary>Produces the combined frame.</summary>
    /// <exception cref="InvalidOperationException">No frames were added.</exception>
    public Frame Result()
    {
        if (_count == 0) throw new InvalidOperationException("no frames were added");

        var outSamples = new ushort[_sum.Length];
        switch (_method)
        {
            case StackMethod.Mean:
                // The engine's master-frame stacker (pipp_dll!stack_frames, FUN_180086110) divides its
                // per-pixel accumulator by the frame count with unsigned integer division, so a .5
                // average truncates: two frames of 10 and 11 average to 10, not 11. Match that exactly.
                for (int i = 0; i < outSamples.Length; i++)
                {
                    outSamples[i] = (ushort)(_sum[i] / _count);
                }
                break;

            case StackMethod.Sum:
                for (int i = 0; i < outSamples.Length; i++)
                {
                    long v = _sum[i];
                    outSamples[i] = (ushort)(v > _max ? _max : v);
                }
                break;

            case StackMethod.Median:
                outSamples = Median(_retained!, _sum.Length);
                break;
        }

        return new Frame(_width, _height, _channels, _bitDepth, outSamples) { Bayer = BayerPattern.None };
    }

    private static ushort[] Median(List<ushort[]> frames, int length)
    {
        var result = new ushort[length];
        var buffer = new ushort[frames.Count];
        for (int i = 0; i < length; i++)
        {
            for (int f = 0; f < frames.Count; f++)
            {
                buffer[f] = frames[f][i];
            }
            Array.Sort(buffer);
            int n = buffer.Length;
            result[i] = (n & 1) == 1
                ? buffer[n / 2]
                : (ushort)(((int)buffer[n / 2 - 1] + buffer[n / 2] + 1) / 2);
        }
        return result;
    }
}
