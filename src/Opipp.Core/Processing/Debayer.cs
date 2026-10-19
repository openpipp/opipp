using Opipp.Core.Frames;

namespace Opipp.Core.Processing;

/// <summary>
/// Bayer mosaic demosaicing.
/// PIPP exposes two algorithms: the default bi-linear interpolation and an optional
/// "smooth hue" pass (<c>-sh</c>). The bi-linear kernel matches the original engine exactly:
/// integer averages truncate (C++ division semantics, not round-half-up), and the outer one-pixel
/// ring is not interpolated at all - it is recomputed from the mosaic by a dcraw-style
/// <c>border_interpolate(1)</c> pass (mean of the same-colour samples in the clipped 3x3 window).
/// Verified against <c>bin/pipp_core.exe</c> by the parity harness case <c>debayer-interpolated</c>.
/// The smooth-hue refinement interpolates the colour <em>differences</em> (R-G, B-G) and re-adds
/// the green plane, which is the standard constant-hue method PIPP's help text describes as
/// "slower than the default bi-linear algorithm".
/// </summary>
public static class Debayer
{
    /// <summary>
    /// Maps PIPP's debayer pattern index to a <see cref="BayerPattern"/>.
    /// The engine prints its own mapping: 0 = GBRG, 1 = BGGR, 2 = RGGB, 3 = GRBG
    /// (pipp_core.exe help text, "0: GBRG  1: BGGR  2: RGGB  3: GRBG").
    /// </summary>
    public static BayerPattern FromPippIndex(int index) => index switch
    {
        0 => BayerPattern.GBRG,
        1 => BayerPattern.BGGR,
        2 => BayerPattern.RGGB,
        3 => BayerPattern.GRBG,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "PIPP debayer pattern must be 0..3"),
    };

    /// <summary>Demosaics a single-plane Bayer frame into a 3-channel frame of the same bit depth.</summary>
    /// <param name="frame">Single-channel Bayer frame; <see cref="Frame.Bayer"/> must not be <see cref="BayerPattern.None"/>.</param>
    /// <param name="smoothHue">Use the constant-hue refinement instead of plain bi-linear.</param>
    public static Frame ToColour(Frame frame, bool smoothHue = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Channels != 1) throw new ArgumentException("debayer requires a single-plane frame", nameof(frame));
        var pattern = frame.Bayer;
        if (pattern == BayerPattern.None) throw new ArgumentException("frame has no Bayer pattern", nameof(frame));

        int w = frame.Width, h = frame.Height;
        var src = frame.Samples;
        var dst = new Frame(w, h, 3, frame.BitDepth) { Index = frame.Index, SourceName = frame.SourceName };
        var o = dst.Samples;

        // Colour of each site in the 2x2 tile, expressed as channel index (0=R,1=G,2=B).
        (int c00, int c01, int c10, int c11) = pattern switch
        {
            BayerPattern.RGGB => (0, 1, 1, 2),
            BayerPattern.BGGR => (2, 1, 1, 0),
            BayerPattern.GRBG => (1, 0, 2, 1),
            BayerPattern.GBRG => (1, 2, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(frame)),
        };

        int SiteColor(int x, int y) => ((y & 1) == 0)
            ? (((x & 1) == 0) ? c00 : c01)
            : (((x & 1) == 0) ? c10 : c11);

        int At(int x, int y)
        {
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x >= w) x = w - 1;
            if (y >= h) y = h - 1;
            return src[y * w + x];
        }

        // 1) known samples first, 2) interpolate the two missing channels per site.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int c = SiteColor(x, y);
                int v = src[y * w + x];
                int idx = (y * w + x) * 3;
                o[idx + c] = (ushort)v;

                int diag = (At(x - 1, y - 1) + At(x + 1, y - 1) + At(x - 1, y + 1) + At(x + 1, y + 1)) / 4;
                int horiz = (At(x - 1, y) + At(x + 1, y)) / 2;
                int vert = (At(x, y - 1) + At(x, y + 1)) / 2;

                switch (c)
                {
                    case 1: // green site: red and blue from orthogonal neighbours
                        // The horizontal neighbour (x^1) is either the red or the blue site
                        // of the 2x2 tile; the vertical one carries the other colour.
                        if (SiteColor(x ^ 1, y) == 0)
                        {
                            o[idx + 0] = (ushort)horiz;
                            o[idx + 2] = (ushort)vert;
                        }
                        else
                        {
                            o[idx + 0] = (ushort)vert;
                            o[idx + 2] = (ushort)horiz;
                        }
                        break;
                    case 0: // red site: green average of 4, blue from diagonals
                        o[idx + 1] = (ushort)((At(x - 1, y) + At(x + 1, y) + At(x, y - 1) + At(x, y + 1)) / 4);
                        o[idx + 2] = (ushort)diag;
                        break;
                    default: // blue site
                        o[idx + 1] = (ushort)((At(x - 1, y) + At(x + 1, y) + At(x, y - 1) + At(x, y + 1)) / 4);
                        o[idx + 0] = (ushort)diag;
                        break;
                }
            }
        }

        if (smoothHue)
        {
            ApplySmoothHue(dst, pattern, w, h);
        }

        // The original fills the outer ring last, from the mosaic rather than from the interpolated
        // planes, so it overwrites both the bilinear and the smooth-hue result there.
        if (w > 2 && h > 2)
        {
            FillBorder(dst, frame, (c00, c01, c10, c11), w, h);
        }

        return dst;
    }

    /// <summary>
    /// dcraw's <c>border_interpolate(1)</c>: the outer one-pixel ring is not interpolated. Each
    /// channel there is the mean of the mosaic samples carrying that colour inside the clipped 3x3
    /// window around the pixel, using the same truncating integer division as the interior.
    /// </summary>
    private static void FillBorder(Frame rgb, Frame mosaic, (int c00, int c01, int c10, int c11) tile, int w, int h)
    {
        var o = rgb.Samples;
        var src = mosaic.Samples;

        int SiteColor(int x, int y) => ((y & 1) == 0)
            ? (((x & 1) == 0) ? tile.c00 : tile.c01)
            : (((x & 1) == 0) ? tile.c10 : tile.c11);

        Span<int> sum = stackalloc int[3];
        Span<int> count = stackalloc int[3];

        for (int y = 0; y < h; y++)
        {
            bool rowOnBorder = y == 0 || y == h - 1;
            for (int x = 0; x < w; x++)
            {
                if (!rowOnBorder && x != 0 && x != w - 1) continue;

                sum.Clear();
                count.Clear();
                int y0 = Math.Max(0, y - 1), y1 = Math.Min(h - 1, y + 1);
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1);
                for (int yy = y0; yy <= y1; yy++)
                {
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        int c = SiteColor(xx, yy);
                        sum[c] += src[yy * w + xx];
                        count[c]++;
                    }
                }

                int idx = (y * w + x) * 3;
                for (int c = 0; c < 3; c++)
                {
                    if (count[c] != 0) o[idx + c] = (ushort)(sum[c] / count[c]);
                }
            }
        }
    }

    /// <summary>
    /// Constant-hue refinement: the chroma differences R-G and B-G are slowly varying, so
    /// interpolating those and re-adding green removes the colour fringing of plain bilinear.
    /// </summary>
    private static void ApplySmoothHue(Frame rgb, BayerPattern pattern, int w, int h)
    {
        var o = rgb.Samples;
        var rMinusG = new float[w * h];
        var bMinusG = new float[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int idx = i * 3;
                int g = o[idx + 1];
                rMinusG[i] = o[idx + 0] - g;
                bMinusG[i] = o[idx + 2] - g;
            }
        }

        var rSmooth = Blur3(rMinusG, w, h);
        var bSmooth = Blur3(bMinusG, w, h);

        int max = rgb.BitDepth == 8 ? 255 : 65535;
        for (int i = 0; i < w * h; i++)
        {
            int idx = i * 3;
            int g = o[idx + 1];
            o[idx + 0] = (ushort)Math.Clamp((int)MathF.Round(g + rSmooth[i]), 0, max);
            o[idx + 2] = (ushort)Math.Clamp((int)MathF.Round(g + bSmooth[i]), 0, max);
        }
    }

    private static float[] Blur3(float[] src, int w, int h)
    {
        var dst = new float[src.Length];
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(h - 1, y + 1);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1);
                float sum = 0;
                for (int yy = y0; yy <= y1; yy++)
                    for (int xx = x0; xx <= x1; xx++)
                        sum += src[yy * w + xx];
                int count = (y1 - y0 + 1) * (x1 - x0 + 1);
                dst[y * w + x] = sum / count;
            }
        }
        return dst;
    }
}
