# Centring (`-ctr`): the exact offset rule

**Status: solved.** The rule below reproduces the original engine exactly on the whole capture:

| Check | Result |
| --- | --- |
| Exact `(dx, dy)` against the 16 unclipped oracle frames | **16 / 16** |
| Whole-frame pixel equality against `samples/ctr/oracle/saturn30/*.tif` | **30 / 30** |

It replaces the previous best (9/16, binary threshold 13, rounded bounding box, target (320,318)).
There are **no mismatching frames**.

Evidence is `samples/saturn30.ser` (640x640, 8-bit bayer, ColorID 8, 30 frames) against the engine's
own `-tif -ctr` output, with the engine log giving `Calculated minpixel value: 18`.

## 1. Coordinate conventions (this is where the ±1 lives)

* The engine's frame buffers are **bottom-up**: the SER reader (`FUN_1400b05f0`) reads the file's
  rows into the buffer starting at index `H-1`, so row 0 of a buffer is the **bottom** row of the
  picture.
* The detection box lives in the frame object at byte offsets `0xDD0..0xDDC`
  (`param_1[0x374..0x377]`), in buffer rows:

  | field | offset | meaning |
  | --- | --- | --- |
  | `param_1[0x374]` | `+0xDD0` | left |
  | `param_1[0x375]` | `+0xDD4` | top (buffer row, i.e. from the bottom) |
  | `param_1[0x376]` | `+0xDD8` | right |
  | `param_1[0x377]` | `+0xDDC` | bottom (buffer row) |

* `Opipp.Core.Frames.Frame` is **top-down**. Either flip when building the detector's luma plane
  (recommended, the recipe below does it) or convert the finished box.

## 2. From the box to the offset

`FUN_14009aa20` dispatches to `FUN_140096960` (8-bit) / `FUN_140096ec0` (16-bit). PIPP implements
`-ctr` through this AOI-crop routine: with no `-crop`/AOI set the crop is the full frame, its origin
is the object box centre, and every pixel of the crop that falls outside the frame is filled with
the frame's background colour. That is why a shift leaves flat border bands rather than repeating
edges.

In buffer coordinates (`FUN_140096960`, `param_2 != 0`, `param_5 = param_6 = 0`):

```
xpos = left + (right - left) / 2          // C integer division (truncation)
ypos = top  + (bottom - top) / 2          // buffer rows, bottom-up

originX = xpos - (width  >> 1)
originY = ypos - (height >> 1)
```

and the copy is `dst[y][x] = src[y + originY][x + originX]` in buffer rows, with the background
colour outside the source. `@XPOS` / `@YPOS` in `-gui` mode print exactly the two centre expressions
above (`FUN_14007e8e0`, reading `0xDD0`/`0xDD8` and `0xDD4`/`0xDDC`), confirming the crop uses the
same values.

Translated to the **top-down** frame that `Opipp.Core` uses:

```
dx =  width/2 - xpos
dy =  ypos - height/2                 // ypos in buffer rows
out[y][x] = in[y - dy][x - dx]        // fill with the frame background outside
```

Equivalently, the object lands with its box centre at `(width/2, height-1-height/2)` in top-down
pixels — `(320, 319)` for 640x640, `(48, 47)` for the 96x96 fixture. This matches the previously
documented target, so that part of the old note was right; what was wrong was how the centre is
measured.

**Fill / background colour.** The crop pads with the frame object's background triplet
(`param_1[7..9]` = frame bytes `+0x1C`/`+0x20`/`+0x24`, B/G/R). On this capture every frame pads
with **3**, which equals the frame median (= mode = truncated border mean) in all 30 frames. The
exact statistic the loader uses is not pinned down (all candidates coincide at 3 here); using the
median reproduces all 30 outputs. Only the exposed border bands (`|dx|` columns, `|dy|` rows) are
affected.

**Parity quirk (not active here).** If the object hanging off `frame+0x372` has a non-zero byte at
`+0x14`, `FUN_140096960` rounds odd origins down to even. All 30 shifts here are odd, so that flag
is off for this capture (it is most likely the bayer/colour-output flag). Do not enable it for mono
`-ctr`.

## 3. The detector

`FUN_140076ae0` is the 8-bit path, `FUN_140077560` the 16-bit path; the frame loop takes the 8-bit
one when `FUN_14007d840` returns 1. It is **not** a centroid and **not** a plain threshold bounding
box: it is a scanline run/extent tracker.

Luma per sample:

* 8-bit path: `luma = (LUT_R[r] + LUT_G[g] + LUT_B[b]) >> 15`, where the three 256-entry tables live
  at `frame+0xE`, `+0x10E`, `+0x20E`. Each entry is the Rec.601 coefficient in 15-bit fixed point, so
  the sum is `≈ (299r + 587g + 114b) / 1000`; for a mono/bayer frame (`r = g = b`) this is the
  sample value itself.
* 16-bit path: `luma = ((587*g + 299*r + 114*b) / 1000) >> 8` on the 16-bit samples, i.e. the high
  byte of the 16-bit luma.

Algorithm (all indices are buffer rows; `thr` = 18 here):

1. **Best run.** Scan pointer rows `1 .. H-6` (loop label `iVar24 = 2 .. H-5`, row pointer
   `iVar24-1`): compute a **4-sample moving sum** of luma; a pixel is "on" when that sum `>= 4*thr`.
   Track the longest run with `length > 9`: `(bestRow, bestStart, bestEnd, bestLen)`, where
   `bestEnd` is the first off pixel (or the last column when the run reaches the row end). Ties go
   to the first run seen.
2. **Extent towards increasing rows** (down the buffer = up the picture): starting at
   `row = bestRow` (the label, i.e. one row past the row phase 1 scanned — this is in the binary),
   scan each row for runs. A run qualifies when `runStart <= rightBound && leftBound <= x`; the
   first qualifying run of a row extends `left/right`; the row where no run qualifies ends the
   object, `bottom = row - 1`.
3. **Extent towards decreasing rows**: the same test from `bestRow` downwards, with `bestEnd` as the
   right bound instead of the running one; `top = row + 1`.
4. **Trims.** The written box is `left = minX - 1`, `right = maxX - 3`, `top`, `bottom` as above.
   The `-1` / `-3` are in the binary and are essential.

Gates (return code `-1` no object, `-2` partial, `-3` too small):

* `bottom - top <= 1` → no object;
* `left < 2 || right >= W-4 || top < 2 || bottom >= H-1` → partial;
* `minSize > right-left` or `minSize > bottom-top` → too small (`minSize` = `frame[5]`, PIPP's
  minimum-size (`-ms`) setting; the default passes this capture).

The threshold is computed **once**, from the first frame prepared, and reused for the file:
`DetectionThreshold.ComputeOrDefault` already reproduces it (18 on this capture, matching the engine
log). Threshold sensitivity is sharp: 17 → 8/16, 19 → 8/16.

## 4. Why the earlier candidates failed

* **Centroid / Otsu / binary threshold masks / (v−thr) weighting**: the engine never computes an
  intensity centroid; it measures run extents, which is why the answer is stable at ±1 px while
  centroid rules drift by several pixels as the threshold moves.
* **Plain bounding box at the engine's own threshold (18)**: close (centre within 0–2 px) but not
  exact — a bbox test picks up isolated stars/noise columns, and the engine's box carries the
  `-1` / `-3` trims and row extents decided by run continuity, not by which pixels are above
  threshold.
* **`bbox centre` rounding / different targets**: with the wrong box, no target/rounding combination
  can fix all frames; the previous best 9/16 is the ceiling of that family.

## 5. Measurements (buffer coordinates, `thr = 18`)

| frame | left | right | top | bottom | xpos | ypos | dx | dy | oracle (dx,dy) | |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 282 | 397 | 254 | 461 | 339 | 357 | -19 | 37 | -19, +37 | ✓ |
| 2 | 283 | 397 | 256 | 464 | 340 | 360 | -20 | 40 | -20, +40 | ✓ |
| 3 | 282 | 398 | 258 | 465 | 340 | 361 | -20 | 41 | – | |
| 4 | 284 | 398 | 256 | 463 | 341 | 359 | -21 | 39 | -21, +39 | ✓ |
| 5 | 283 | 398 | 256 | 463 | 340 | 359 | -20 | 39 | – | |
| 6 | 283 | 398 | 254 | 464 | 340 | 359 | -20 | 39 | – | |
| 7 | 282 | 397 | 256 | 463 | 339 | 359 | -19 | 39 | -19, +39 | ✓ |
| 8 | 283 | 399 | 257 | 462 | 341 | 359 | -21 | 39 | -21, +39 | ✓ |
| 9 | 283 | 398 | 257 | 462 | 340 | 359 | -20 | 39 | – | |
| 10 | 284 | 397 | 257 | 463 | 340 | 360 | -20 | 40 | – | |
| 11 | 282 | 397 | 256 | 463 | 339 | 359 | -19 | 39 | -19, +39 | ✓ |
| 12 | 284 | 399 | 256 | 464 | 341 | 360 | -21 | 40 | -21, +40 | ✓ |
| 13 | 282 | 399 | 254 | 463 | 340 | 358 | -20 | 38 | -20, +38 | ✓ |
| 14 | 282 | 398 | 254 | 463 | 340 | 358 | -20 | 38 | -20, +38 | ✓ |
| 15 | 282 | 398 | 256 | 463 | 340 | 359 | -20 | 39 | -20, +39 | ✓ |
| 16 | 282 | 399 | 255 | 464 | 340 | 359 | -20 | 39 | – | |
| 17 | 283 | 397 | 256 | 463 | 340 | 359 | -20 | 39 | – | |
| 18 | 282 | 398 | 256 | 464 | 340 | 360 | -20 | 40 | – | |
| 19 | 282 | 399 | 257 | 463 | 340 | 360 | -20 | 40 | -20, +40 | ✓ |
| 20 | 282 | 398 | 257 | 463 | 340 | 360 | -20 | 40 | -20, +40 | ✓ |
| 21 | 282 | 398 | 258 | 465 | 340 | 361 | -20 | 41 | -20, +41 | ✓ |
| 22 | 281 | 399 | 259 | 466 | 340 | 362 | -20 | 42 | – | |
| 23 | 282 | 398 | 258 | 464 | 340 | 361 | -20 | 41 | – | |
| 24 | 282 | 398 | 258 | 463 | 340 | 360 | -20 | 40 | – | |
| 25 | 281 | 397 | 256 | 464 | 339 | 360 | -19 | 40 | – | |
| 26 | 280 | 395 | 256 | 463 | 337 | 359 | -17 | 39 | -17, +39 | ✓ |
| 27 | 280 | 395 | 255 | 465 | 337 | 360 | -17 | 40 | – | |
| 28 | 281 | 395 | 255 | 466 | 338 | 360 | -18 | 40 | -18, +40 | ✓ |
| 29 | 280 | 397 | 256 | 465 | 338 | 360 | -18 | 40 | – | |
| 30 | 282 | 398 | 252 | 462 | 340 | 357 | -20 | 37 | -20, +37 | ✓ |

The “–” rows are the frames the parent excluded because their bounding box is not preserved after
the shift (content clips the frame edge); they are nevertheless reproduced **pixel-exactly** by the
same rule (section 6), which is a stronger check than the shift alone.

## 6. C# recipe

Drop-in for `Opipp.Core.Alignment`. `Detect` expects an 8-bit luma plane in the engine's bottom-up
order; `CentreOffset` returns the top-down offset the rest of the pipeline already uses
(`AlignmentOffset`, positive X right, positive Y down).

```csharp
using Opipp.Core.Frames;

namespace Opipp.Core.Alignment;

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
```

Usage: replace the body of `FrameAligner.CentreOffset` with
`PlanetCentring.CentreOffset(frame, detectionThreshold)`; the existing `FrameAligner.Shift` already
applies the integer offset — but note it does **not** reproduce the engine's padding (it clamps
source coordinates). To match the original bit-for-bit, shift with an explicit constant fill:

```csharp
// sketch: dst[y][x] = src[y - dy][x - dx], background outside the source rectangle
int fill = MedianOfSamples(frame);                   // 3 on saturn30; equals the engine's pad value
for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        long sy = y - dy, sx = x - dx;
        bool inside = sy >= 0 && sy < h && sx >= 0 && sx < w;
        for (int c = 0; c < frame.Channels; c++)
            dst.Samples[(y * w + x) * frame.Channels + c] =
                inside ? frame.Samples[((int)sy * w + (int)sx) * frame.Channels + c]
                       : (ushort)fill;
    }
```

(`MedianOfSamples` is a small helper: histogram the samples, walk to the halfway count. On 8-bit
data it returns ≤ 255; the engine stores the same value in the frame's background B/G/R fields.)

Run the detector on the frame at the same pipeline point as the engine: after debayer/mono, gain,
stretch and flip/rotate, before quality selection and the user crop (`notes/engine-behaviour.md` §2,
step 6). Feeding it a different processing stage changes the luma and therefore the box.

## 7. Verification performed

* Python reference `centring_ref3.py` (exact transliteration, Ghidra locals as names) → `verify_ctr3.py`:
  **16/16** exact, **30/30** pixel-identical, threshold 17/19 → 8/16 (sharpness check).
* C# port compiled and run (`dotnet run -c Release`, net10.0) against the same SER:
  **16/16** exact. The code block in section 6 is the compiled source, not a paraphrase.
* `Opipp.Core.Alignment.DetectionThreshold.ComputeOrDefault` run against the same frames returns
  **18** (matching the engine log), so the existing threshold code plugs straight into the recipe.
* Fill check: every out-of-frame pixel equals 3 and `median(frame) == 3` on all 30 frames.

## 8. Open points

* The padding statistic (median vs mode vs truncated border mean) is not distinguishable on this
  capture; all three equal 3 for all 30 frames. Median is what the recipe uses.
* The 16-bit path (`FUN_140077560`, `FUN_140096ec0`) is transcribed above but was not exercised: the
  only oracle set is 8-bit. The 8-bit and 16-bit detectors are structurally identical apart from the
  luma calculation, so the same rule should hold.
* The `frame+0x372/+0x14` parity rounding is inactive here; it needs an oracle with even shifts
  (i.e. a frame whose box centre is exactly on the target) to identify.

## 9. Commands run

```
# read the oracle log (engine threshold for this capture)
cat samples/ctr/oracle/logs/saturn30_pipp_log.txt          # "Calculated minpixel value: 18"

# Python reference: detection, shifts, and the full pixel comparison
python C:/Users/ilanp/AppData/Local/hermes/cache/scratch/verify_ctr3.py
#   exact shifts: 16 / 16
#   pixel-exact frames: 30 / 30

# threshold sharpness (same script, 17 and 19)
#   threshold 17 -> 8 / 16
#   threshold 19 -> 8 / 16

# C# recipe: same numbers from the exact source listed above
cd C:/Users/ilanp/AppData/Local/hermes/cache/scratch/recipecheck && dotnet run -c Release
#   recipe: 16/16

# repo threshold code against the same frames
cd C:/Users/ilanp/AppData/Local/hermes/cache/scratch/thrcheck && dotnet run -c Release
#   frame 1..5: threshold 18
```

**Best match: 16 / 16 exact shifts and 30 / 30 pixel-identical frames (no mismatching frames).**

