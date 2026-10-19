namespace Opipp.Core.Processing;

using Opipp.Core.Frames;

/// <summary>Shared helpers for the processing stages. Not part of the public API.</summary>
internal static class ProcessingUtil
{
    /// <summary>Largest sample value representable at the frame's bit depth (255 or 65535).</summary>
    internal static int MaxValue(Frame frame) => frame.BitDepth == 8 ? 255 : 65535;

    /// <summary>
    /// Creates an output frame with the same bit depth and provenance metadata as <paramref name="source"/>.
    /// The sample buffer is taken as-is. <paramref name="bayer"/> supplies the mosaic pattern the output
    /// deserves; pass <see cref="BayerPattern.None"/> for a stage that does not preserve the sampling
    /// lattice. Null copies the source's pattern.
    /// </summary>
    internal static Frame NewLike(
        Frame source, int width, int height, int channels, ushort[] samples, BayerPattern? bayer = null) =>
        new(width, height, channels, source.BitDepth, samples)
        {
            Bayer = bayer ?? source.Bayer,
            Index = source.Index,
            SourceName = source.SourceName,
        };

    /// <summary>Clamps a sample value to the frame's representable range.</summary>
    internal static ushort ClampSample(long value, int max) =>
        (ushort)Math.Clamp(value, 0L, max);

    // ------------------------------------------------------------------ CFA phase

    /// <summary>
    /// The 2x2 colour tile a pattern describes, as channel indices (0 = R, 1 = G, 2 = B) for the
    /// sites at (0,0), (1,0), (0,1) and (1,1) respectively.
    /// </summary>
    internal static (int C00, int C01, int C10, int C11) Tile(BayerPattern pattern) => pattern switch
    {
        BayerPattern.RGGB => (0, 1, 1, 2),
        BayerPattern.GRBG => (1, 0, 2, 1),
        BayerPattern.GBRG => (1, 2, 0, 1),
        BayerPattern.BGGR => (2, 1, 1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "not a Bayer pattern"),
    };

    /// <summary>
    /// Inverse of <see cref="Tile"/>: names the pattern whose 2x2 tile is the given colours.
    /// Only the four Bayer tiles have green on one diagonal and one red plus one blue site; anything
    /// else is a lattice that a single <see cref="BayerPattern"/> cannot describe.
    /// </summary>
    internal static BayerPattern PatternFromTile(int c00, int c01, int c10, int c11) => (c00, c01, c10, c11) switch
    {
        (0, 1, 1, 2) => BayerPattern.RGGB,
        (1, 0, 2, 1) => BayerPattern.GRBG,
        (1, 2, 0, 1) => BayerPattern.GBRG,
        (2, 1, 1, 0) => BayerPattern.BGGR,
        _ => throw new InvalidOperationException(
            $"the 2x2 tile ({c00},{c01},{c10},{c11}) is not a Bayer mosaic: a mosaic has green on one " +
            "diagonal and one red plus one blue site"),
    };

    /// <summary>
    /// Colour index (0 = R, 1 = G, 2 = B) of the mosaic site at a pixel position, or -1 when there
    /// is no pattern. Only the parity of the coordinates matters, and negative positions (a shift
    /// that reads left of or above the origin) wrap correctly.
    /// </summary>
    internal static int ColourAt(BayerPattern pattern, int x, int y)
    {
        if (pattern == BayerPattern.None) return -1;
        var (c00, c01, c10, c11) = Tile(pattern);
        int px = x & 1, py = y & 1;
        return py == 0 ? (px == 0 ? c00 : c01) : (px == 0 ? c10 : c11);
    }

    /// <summary>
    /// Recomputes the mosaic pattern for an output lattice, where output pixel (x, y) samples the
    /// source pixel returned by <paramref name="map"/>. This is the single rule every geometry stage
    /// uses: the output's first 2x2 tile is read back through the mapping and named. Reflections,
    /// quarter turns and crops are lattice isomorphisms, so a valid mosaic maps to a valid mosaic;
    /// an odd width/height or an odd crop origin moves the phase to another of the four patterns
    /// instead of leaving a stale marker behind.
    /// </summary>
    internal static BayerPattern Retag(BayerPattern pattern, Func<int, int, (int X, int Y)> map)
    {
        if (pattern == BayerPattern.None) return BayerPattern.None;

        (int X, int Y) p00 = map(0, 0);
        (int X, int Y) p01 = map(1, 0);
        (int X, int Y) p10 = map(0, 1);
        (int X, int Y) p11 = map(1, 1);

        return PatternFromTile(
            ColourAt(pattern, p00.X, p00.Y),
            ColourAt(pattern, p01.X, p01.Y),
            ColourAt(pattern, p10.X, p10.Y),
            ColourAt(pattern, p11.X, p11.Y));
    }

    /// <summary>
    /// Recomputes the pattern after a whole-pixel translation: output pixel (x, y) is the source
    /// pixel (x - dx, y - dy). An even translation keeps the phase, an odd one moves it.
    /// </summary>
    internal static BayerPattern RetagTranslated(BayerPattern pattern, int dx, int dy) =>
        Retag(pattern, (x, y) => (x - dx, y - dy));
}
