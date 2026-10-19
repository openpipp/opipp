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
/// Raised when <see cref="StackMethod.Median"/> mode is asked to retain more frames than its cap.
/// </summary>
/// <remarks>
/// Median mode keeps every accepted frame's samples in memory because a median cannot be accumulated
/// incrementally. The cap is a memory guard, not a property of the algorithm: a disk-backed tiled
/// median would lift it, and is not implemented. Callers that hit this should reduce the selection or
/// use mean/sum.
/// </remarks>
public sealed class MedianFrameLimitException : InvalidOperationException
{
    internal MedianFrameLimitException(int frameLimit, int width, int height, int channels)
        : base($"median stacking retains at most {frameLimit} frames; frame {frameLimit + 1} cannot be accepted " +
               $"({width}x{height}x{channels}, {width * height * channels * 2L:N0} bytes per frame, " +
               $"{frameLimit * (long)width * height * channels * 2:N0} bytes held). " +
               "Disk-backed median stacking is not implemented; reduce the selection or use mean/sum.")
    {
        FrameLimit = frameLimit;
        Width = width;
        Height = height;
        Channels = channels;
    }

    /// <summary>The configured cap, in frames.</summary>
    public int FrameLimit { get; }

    /// <summary>Frame width the stacker accepts.</summary>
    public int Width { get; }

    /// <summary>Frame height the stacker accepts.</summary>
    public int Height { get; }

    /// <summary>Channel count the stacker accepts.</summary>
    public int Channels { get; }

    /// <summary>Bytes retained per frame in median mode (width x height x channels x 2).</summary>
    public long BytesPerFrame => (long)Width * Height * Channels * 2;
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
/// <para>
/// <b>Sample ownership.</b> <see cref="Add"/> copies a frame's samples before retaining them, so the
/// caller keeps ownership of the buffer it passed and may mutate or reuse it as soon as
/// <see cref="Add"/> returns, including after a frame was accepted for a median. Mean and sum modes
/// consume the samples inside <see cref="Add"/> and retain nothing.
/// </para>
/// <para>
/// <b>Median frame cap.</b> Median mode must hold every frame: there is no incremental median.
/// The cap is <see cref="MaxMedianFrames"/> (default 64) and the next <see cref="Add"/> after it
/// throws <see cref="MedianFrameLimitException"/>. The memory that cap implies is
/// <c>MaxMedianFrames x width x height x channels x 2 bytes</c>; for 64 RGB frames at 1920x1080 that
/// is 64 x 1920 x 1080 x 3 x 2 = 796,262,400 bytes (759.375 MiB), so 64 is a deliberate ceiling
/// rather than a property of the median. A disk-backed tiled median would remove it; that is not
/// implemented here.
/// </para>
/// </remarks>
public sealed class FrameStacker
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly int _bitDepth;
    private readonly int _max;
    private readonly int _sampleCount;
    private readonly StackMethod _method;
    private readonly long[]? _sum;
    private readonly List<ushort[]>? _retained;
    private int _count;

    /// <summary>Creates a stacker for frames of the given geometry.</summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="channels">1 or 3.</param>
    /// <param name="bitDepth">8 or 16.</param>
    /// <param name="method">Combination rule.</param>
    /// <param name="maxMedianFrames">Refuse median mode beyond this many retained frames (memory guard).</param>
    /// <exception cref="ArgumentOutOfRangeException">A geometry, depth, method or limit value is invalid.</exception>
    public FrameStacker(int width, int height, int channels, int bitDepth, StackMethod method = StackMethod.Mean, int maxMedianFrames = 64)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (channels is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(channels));
        if (bitDepth is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(bitDepth));
        if (!Enum.IsDefined(method))
            throw new ArgumentOutOfRangeException(nameof(method), method, "unknown stack method");
        if (maxMedianFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(maxMedianFrames), maxMedianFrames, "median frame limit must be at least 1");

        _width = width;
        _height = height;
        _channels = channels;
        _bitDepth = bitDepth;
        _max = bitDepth == 8 ? 255 : 65535;
        _method = method;
        _sampleCount = checked(width * height * channels);
        MaxMedianFrames = maxMedianFrames;
        if (method == StackMethod.Median)
        {
            _retained = new List<ushort[]>(Math.Min(maxMedianFrames, 16));
        }
        else
        {
            _sum = new long[_sampleCount];
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

    /// <summary>
    /// Configured cap on retained frames. It is only enforced in <see cref="StackMethod.Median"/> mode,
    /// which retains every frame; mean and sum accumulate incrementally and are unbounded.
    /// </summary>
    public int MaxMedianFrames { get; }

    /// <summary>Frames accumulated so far.</summary>
    public int FrameCount => _count;

    /// <summary>Adds one frame, optionally subtracting a reference frame first (flat-dark correction).</summary>
    /// <remarks>
    /// The stacker copies samples before retaining them, so the caller may reuse or mutate
    /// <paramref name="frame"/> after this call without affecting the stack. The caller's buffers are
    /// never written to.
    /// </remarks>
    /// <exception cref="ArgumentException">Geometry or depth does not match the stacker.</exception>
    /// <exception cref="MedianFrameLimitException">Median mode exceeded its frame cap.</exception>
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
            src = tmp; // private already, so no second copy is needed below
        }

        if (_method == StackMethod.Median)
        {
            if (_retained!.Count >= MaxMedianFrames)
                throw new MedianFrameLimitException(MaxMedianFrames, _width, _height, _channels);
            // Snapshot: the caller owns frame.Samples and may reuse one buffer for every Add. A median
            // has to read the samples at Result() time, so retaining the reference would let a later
            // mutation rewrite frames that were already accepted (10, 30, 100 in one buffer would all
            // read 100). Subtract mode already produced a private buffer above.
            _retained.Add(subtract is null ? (ushort[])src.Clone() : src);
        }
        else
        {
            var sum = _sum!;
            for (int i = 0; i < src.Length; i++)
            {
                sum[i] += src[i];
            }
        }

        _count++;
    }

    /// <summary>Produces the combined frame.</summary>
    /// <param name="cancellationToken">Checked periodically while the result is computed.</param>
    /// <exception cref="InvalidOperationException">No frames were added.</exception>
    public Frame Result(CancellationToken cancellationToken = default)
    {
        if (_count == 0) throw new InvalidOperationException("no frames were added");
        cancellationToken.ThrowIfCancellationRequested();

        ushort[] outSamples = _method switch
        {
            StackMethod.Mean => Mean(cancellationToken),
            StackMethod.Sum => Sum(cancellationToken),
            StackMethod.Median => Median(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(_method), _method, "unknown stack method"),
        };

        return new Frame(_width, _height, _channels, _bitDepth, outSamples) { Bayer = BayerPattern.None };
    }

    private ushort[] Mean(CancellationToken cancellationToken)
    {
        // The engine's master-frame stacker (pipp_dll!stack_frames, FUN_180086110) divides its
        // per-pixel accumulator by the frame count with unsigned integer division, so a .5
        // average truncates: two frames of 10 and 11 average to 10, not 11. Match that exactly.
        var sum = _sum!;
        var result = new ushort[sum.Length];
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            result[i] = (ushort)(sum[i] / _count);
        }
        return result;
    }

    private ushort[] Sum(CancellationToken cancellationToken)
    {
        var sum = _sum!;
        var result = new ushort[sum.Length];
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            long v = sum[i];
            result[i] = (ushort)(v > _max ? _max : v);
        }
        return result;
    }

    private ushort[] Median(CancellationToken cancellationToken)
    {
        var frames = _retained!;
        var result = new ushort[_sampleCount];
        var buffer = new ushort[frames.Count];
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int f = 0; f < frames.Count; f++)
            {
                buffer[f] = frames[f][i];
            }
            Array.Sort(buffer);
            int n = buffer.Length;
            // Even counts round halves up ((a + b + 1) / 2), unlike the mean, which truncates.
            // Contract: [0, 1] stacks to mean 0 but median 1.
            result[i] = (n & 1) == 1
                ? buffer[n / 2]
                : (ushort)(((int)buffer[n / 2 - 1] + buffer[n / 2] + 1) / 2);
        }
        return result;
    }
}
