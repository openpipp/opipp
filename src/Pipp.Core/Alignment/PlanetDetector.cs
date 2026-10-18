using Pipp.Core.Frames;

namespace Pipp.Core.Alignment;

/// <summary>Result code of the engine's object finder (<c>FUN_140077560</c> / <c>FUN_140076ae0</c>).</summary>
public enum PlanetDetectionStatus
{
    /// <summary>An object was found and its box written (engine return 0).</summary>
    Found = 0,

    /// <summary>No object: no qualifying run, or an object touching the top/bottom edge (engine return -1).</summary>
    NoObject = -1,

    /// <summary>The object touches the left or right border (engine return -2).</summary>
    PartialObject = -2,

    /// <summary>The object is smaller than the minimum size (engine return -3).</summary>
    TooSmall = -3,
}

/// <summary>
/// One detection result: the engine's return code plus the box it wrote. Coordinates are inclusive
/// and top-down (image) pixels; a failure carries the zeroed box the engine leaves behind.
/// </summary>
/// <param name="Status">Engine return code.</param>
/// <param name="Left">Box left edge, inclusive.</param>
/// <param name="Top">Box top edge, inclusive.</param>
/// <param name="Right">Box right edge, inclusive.</param>
/// <param name="Bottom">Box bottom edge, inclusive.</param>
public readonly record struct PlanetDetection(PlanetDetectionStatus Status, int Left, int Top, int Right, int Bottom)
{
    /// <summary>True when the status is <see cref="PlanetDetectionStatus.Found"/>.</summary>
    public bool Found => Status == PlanetDetectionStatus.Found;

    /// <summary>Box width in pixels (<c>right - left</c>).</summary>
    public int Width => Right - Left;

    /// <summary>Box height in pixels (<c>bottom - top</c>).</summary>
    public int Height => Bottom - Top;

    /// <summary>The failed result for a status, with the engine's zeroed box.</summary>
    public static PlanetDetection Failure(PlanetDetectionStatus status) => new(status, 0, 0, 0, 0);
}

/// <summary>
/// The engine's object finder, recovered from <c>pipp_core!FUN_140077560</c> (16-bit samples) and
/// <c>FUN_140076ae0</c> (8-bit samples, per-channel lookup tables).
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is a two-stage scan on an 8-bit luma image whose row 0 is the <b>bottom</b> of the
/// frame, as in the engine's work buffer: a four-tap running mean
/// (<c>sum of the last four luma samples ≥ 4·threshold</c>) turns each row into bright runs, the
/// longest run of at least ten columns seeds the object, and two vertical passes grow the seed up
/// and down while runs of at least ten columns overlap the previous row's interval.
/// </para>
/// <para>
/// The box the finder writes is deliberately asymmetric — <c>left = start - 1</c>,
/// <c>right = end - 3</c>, and the vertical edges sit one row past the object — so a saturated
/// rectangle of columns <c>L..R</c>, rows <c>T..B</c> yields <c>x1 = L-1</c>, <c>y1 = T+1</c>,
/// <c>x2 = R+1</c>, <c>y2 = B+1</c>. This type reports that box in top-down image coordinates.
/// </para>
/// <para>
/// The engine's four-box fields are also the quality metric's measurement window, so a caller that
/// scores frames must pass <see cref="PlanetDetection.Left"/>..<see cref="PlanetDetection.Bottom"/>
/// to <see cref="Quality.EngineQuality.SetDetectedBox"/> exactly as returned (which grows the box by
/// one pixel on each side before measuring).
/// </para>
/// </remarks>
public static class PlanetDetector
{
    /// <summary>Minimum length of a bright run; the engine's literal 10 (<c>9 &lt; run</c>).</summary>
    private const int MinimumRun = 10;

    /// <summary>Rows at the end of the scan (top of the image) the seed pass never visits.</summary>
    private const int TopMargin = 5;

    /// <summary>
    /// Detects the object in a frame using the 16-bit or the 8-bit sample path, whichever matches
    /// the frame's bit depth.
    /// </summary>
    /// <param name="frame">Frame to inspect.</param>
    /// <param name="threshold">Detection threshold (<c>-mp</c>, the logged "Calculated minpixel value"), 8-bit scale.</param>
    /// <param name="minSize">Minimum object size (<c>-ms</c>), compared with <c>min(width, height)</c>; 0 disables it.</param>
    public static PlanetDetection Detect(Frame frame, int threshold, int minSize)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        if (minSize < 0) throw new ArgumentOutOfRangeException(nameof(minSize));

        byte[] luma = BuildLuma(frame);

        PlanetDetection bufferSpace = DetectCore(luma, frame.Width, frame.Height, threshold, minSize);
        if (!bufferSpace.Found)
        {
            return bufferSpace;
        }

        // The engine's rows run bottom-up; quality and the log use top-down image rows.
        int height = frame.Height;
        return new PlanetDetection(
            PlanetDetectionStatus.Found,
            bufferSpace.Left,
            height - 1 - bufferSpace.Bottom,
            bufferSpace.Right,
            height - 1 - bufferSpace.Top);
    }

    /// <summary>
    /// Detects an object in an 8-bit luma plane whose row 0 is the <b>bottom</b> of the image (the
    /// engine's work-buffer order). Returns the box in that same buffer space.
    /// </summary>
    /// <param name="luma">Width × height 8-bit luma samples, row 0 = bottom image row.</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="threshold">Detection threshold on the 8-bit luma scale.</param>
    /// <param name="minSize">Minimum object size, compared with <c>min(width, height)</c>; 0 disables it.</param>
    public static PlanetDetection DetectBufferOrder(ReadOnlySpan<byte> luma, int width, int height, int threshold, int minSize)
    {
        if (luma.Length < width * height)
        {
            throw new ArgumentException("luma plane is smaller than width × height", nameof(luma));
        }

        return DetectCore(luma, width, height, threshold, minSize);
    }

    /// <summary>The recovered algorithm, in the engine's bottom-up row coordinates.</summary>
    private static PlanetDetection DetectCore(ReadOnlySpan<byte> luma, int width, int height, int threshold, int minSize)
    {
        // Frames of height <= 6 have no row left for the seed scan.
        if (height - 4 <= 2 || width < 2)
        {
            return PlanetDetection.Failure(PlanetDetectionStatus.NoObject);
        }

        int limit = threshold * 4;
        int lastSeedRow = height - 1 - TopMargin;

        // ---- seed: the longest bright run on rows 1 .. height-6 -------------------------------
        int bestLength = 0;
        int seedRow = 0, seedStart = 0, seedEnd = 0;
        var ring = new int[4];
        for (int row = 1; row <= lastSeedRow; row++)
        {
            Array.Clear(ring);
            int sum = 0, runLength = 0, runStart = 0;
            int offset = row * width;
            for (int x = 0; x < width; x++)
            {
                int value = luma[offset + x];
                int slot = x & 3;
                sum += value - ring[slot];
                ring[slot] = value;

                if (sum < limit)
                {
                    if (runLength >= MinimumRun && runLength > bestLength)
                    {
                        bestLength = runLength;
                        seedRow = row;
                        seedStart = runStart;
                        seedEnd = x;                  // first dark column after the run
                    }

                    runLength = 0;
                }
                else
                {
                    if (runLength == 0)
                    {
                        runStart = x;
                    }

                    runLength++;
                }
            }

            if (runLength >= MinimumRun && runLength > bestLength)
            {
                bestLength = runLength;
                seedRow = row;
                seedStart = runStart;
                seedEnd = width - 1;                  // run still open at the row end
            }
        }

        if (bestLength == 0)
        {
            return PlanetDetection.Failure(PlanetDetectionStatus.NoObject);
        }

        int globalStart = seedStart;
        int globalEnd = seedEnd;

        int bottom = Grow(luma, width, height, limit, seedRow, +1, seedStart, seedEnd, ref globalStart, ref globalEnd);
        int top = Grow(luma, width, height, limit, seedRow, -1, seedStart, seedEnd, ref globalStart, ref globalEnd);

        // ---- the finder's asymmetric box adjustment ------------------------------------------
        int left = globalStart - 1;
        int right = globalEnd - 3;

        if (bottom - top < 2)
        {
            return PlanetDetection.Failure(PlanetDetectionStatus.NoObject);
        }

        if (left <= 1 || right >= width - 4 || top <= 1 || bottom >= height - 1)
        {
            return PlanetDetection.Failure(PlanetDetectionStatus.PartialObject);
        }

        // -ms is compared with both dimensions, i.e. with min(width, height).
        if (right - left < minSize || bottom - top < minSize)
        {
            return PlanetDetection.Failure(PlanetDetectionStatus.TooSmall);
        }

        if (left < 0)
        {
            left = 0;                                 // unreachable after the left > 1 test
        }

        return new PlanetDetection(PlanetDetectionStatus.Found, left, top, right, bottom);
    }

    /// <summary>
    /// Grows the seed interval one row at a time. Returns the terminating row the engine stores as
    /// the box edge: the first failing row for the downward pass, and that row plus two for the
    /// upward pass. Running off the frame returns the edge sentinel (0 or height-1).
    /// </summary>
    private static int Grow(
        ReadOnlySpan<byte> luma,
        int width,
        int height,
        int limit,
        int seedRow,
        int direction,
        int seedStart,
        int seedEnd,
        ref int globalStart,
        ref int globalEnd)
    {
        int previousStart = seedStart;
        int previousEnd = seedEnd;
        int row = seedRow + direction;
        var ring = new int[4];

        while (row >= 0 && row < height)
        {
            Array.Clear(ring);
            int sum = 0, runLength = 0, runStart = 0;
            bool found = false;

            int offset = row * width;
            for (int x = 0; x < width; x++)
            {
                int value = luma[offset + x];
                int slot = x & 3;
                sum += value - ring[slot];
                ring[slot] = value;

                if (sum < limit)
                {
                    if (runLength < MinimumRun)
                    {
                        // Nothing of this run can overlap any more; the engine stops the row here.
                        if (x > previousEnd)
                        {
                            break;
                        }
                    }
                    else if (runStart <= previousEnd && previousStart <= x)
                    {
                        found = true;
                        if (runStart < globalStart)
                        {
                            globalStart = runStart;
                        }

                        if (x > globalEnd)
                        {
                            globalEnd = x;
                        }

                        if (runStart < previousStart)
                        {
                            previousStart = runStart;
                        }

                        if (x > previousEnd)
                        {
                            previousEnd = x;
                        }
                    }

                    runLength = 0;
                }
                else
                {
                    if (runLength == 0)
                    {
                        runStart = x;
                    }

                    runLength++;
                }
            }

            // A row that ends in an open run that began before the interval means the object reaches
            // the right border; the engine widens the box and continues instead of stopping.
            if (runLength >= MinimumRun && runStart <= previousEnd)
            {
                previousEnd = width - 1;
                if (runStart < globalStart)
                {
                    globalStart = runStart;
                }

                if (width - 1 > globalEnd)
                {
                    globalEnd = width - 1;
                }
            }
            else if (!found)
            {
                // The engine's pass counter runs one row behind its buffer pointer: the downward
                // pass stores the last good row and the upward pass the first good row.
                return direction > 0 ? row - 1 : row + 1;
            }

            row += direction;
        }

        return direction > 0 ? 0 : height - 1;
    }

    /// <summary>
    /// Builds the 8-bit luma plane the finder reads, with row 0 at the bottom of the image.
    /// The 16-bit path projects raw 16-bit samples and keeps the high byte; the 8-bit path uses the
    /// bytes directly (the engine's lookup tables are the identity for mono data).
    /// </summary>
    private static byte[] BuildLuma(Frame frame)
    {
        int width = frame.Width;
        int height = frame.Height;
        int pixels = width * height;
        var luma = new byte[pixels];
        var samples = frame.Samples;

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * width;
            int destinationRow = (height - 1 - y) * width;

            if (frame.Channels == 1)
            {
                if (frame.BitDepth == 8)
                {
                    for (int x = 0; x < width; x++)
                    {
                        luma[destinationRow + x] = (byte)samples[sourceRow + x];
                    }
                }
                else
                {
                    for (int x = 0; x < width; x++)
                    {
                        luma[destinationRow + x] = (byte)(samples[sourceRow + x] >> 8);
                    }
                }
            }
            else if (frame.BitDepth == 8)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (sourceRow + x) * 3;
                    luma[destinationRow + x] = (byte)(((299 * samples[i]) + (587 * samples[i + 1]) + (114 * samples[i + 2])) / 1000);
                }
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (sourceRow + x) * 3;
                    int word = ((299 * samples[i]) + (587 * samples[i + 1]) + (114 * samples[i + 2])) / 1000;
                    luma[destinationRow + x] = (byte)(word >> 8);
                }
            }
        }

        return luma;
    }
}
