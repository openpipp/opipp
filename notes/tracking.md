# Surface-feature tracking in the engine (PIPP 2.5.9)

Addresses are image-base `0x140000000`. Call chain: `FUN_14009b410` (entry) → `FUN_1400a7470`
(context) → `FUN_1400a6e40` (coarse seed) → `FUN_1400a5f40` (fine refinement).

This note exists because a subagent produced a **false 30/30 parity claim** by deriving its "oracle"
shifts from our own output. Read the verification section before trusting any tracking result.

## Coarse stage — `FUN_1400a6e40` (1556 bytes, line 111448 of `ghidra/out/pipp_core.c`)

Downsamples the frame by the block factor (`iVar10 / param_4`), builds a 256-bin histogram per
candidate position on that reduced image, and returns a seed position. The seed is quantised to the
coarse step, so on a well-tracked planetary capture it is usually no offset at all; it matters only
when the feature moves far between frames.

## Fine stage — `FUN_1400a5f40` (2097 bytes, line 110915)

1. **Working buffer**: the *moving* frame averaged over a **5×5 box** — 24 neighbours plus the centre,
   integer sum divided by **25** (`0x19`). Built over the box grown by `e8 = boxWidth >> 4` and
   `f8 = boxHeight >> 4` on each side.
2. **Per-candidate LUT** (256 entries), from the *reference* block statistics:
   `mean = (sum*4 / *(u32*)(ctx+0x3c)) * *(u8*)(ctx+0x38)`, clamped to 255;
   `lut[i] = clamp((i - mean) * (u32(ctx+0x40) / (sum*4 - mean*N)), 0, DAT_140120fd8)`.
   This is a brightness/contrast normalisation applied to the candidate before scoring.
3. **Score**: sum of squared differences between `lut[blurred_candidate]` and the **raw** reference
   pixel, with an early exit once the partial sum exceeds `best >> 2`.
4. **Search window**: `dy` outer, `dx` inner, both over `[0, 2*e8)` and `[0, 2*f8)`, with
   `offset = index - e8`. For a 48 px box that is **−3 … +2** — asymmetric, not ±3. Strict `<` for a
   new best, so the first minimum in scan order wins.
5. **Reject**: if the final score exceeds the stored threshold at `ctx+0x1a0`, the frame is reported as
   not tracked (this is the path that `-aoi_reject` and the "no AOI found" counter sit behind).
6. **Result**: `(best_dx - e8) + box.X`, `(best_dy - f8) + box.Y`.

There is no sub-pixel fit and no ZNCC anywhere in this path. A tracker that searches ±32 px with ZNCC
finds *better* correlations than the engine ever considers, which is why it disagrees on ~11 of 30
frames — the mismatch is the search policy, not the image content.

## Verified oracle shift table

Derived unambiguously by brute-force shifting the oracle's plain `-tif` output until it equals the
oracle's tracked output (no ambiguity on any of the 30 frames):

```
frame :  1   2   3   4   5   6   7   8   9  10  11  12  13  14  15
dx    :  0   1   0  -1   0   1   1   0  -1   0   1   1   0  -1  -1
dy    :  0  -1  -3  -3  -2  -1  -1  -2  -2  -2  -1  -1   0   0  -1

frame : 16  17  18  19  20  21  22  23  24  25  26  27  28  29  30
dx    : -1  -1   0   0   1   1  -1  -1  -1  -1  -3  -2  -1  -2  -2
dy    : -1  -1  -1  -1  -2  -3  -5  -3  -1  -2   0  -1  -1  -1   1
```

Fixture: `samples/saturn30.ser`, `-afb_x=330 -afb_y=340 -afb_width=48 -afb_height=48`.

## Verification discipline

Never derive an "oracle" from a directory you also write to. Recover a reference shift by comparing
**the original's output against the original's plain output** (as above), or by running the original
into a fresh directory each time. A directory that is byte-identical to our own output is a red flag,
not a confirmation.

## Measured variants (2026-10-18)

| Variant | Exact shifts | L1 |
|---|---|---|
| ZNCC, +/-32 search (previous tracker) | 19/30 | - |
| Port: raw mosaic, fixed window | 20/30 | 17 |
| Port: debayer + luma, fixed window | 22/30 | 13 |
| Port: debayer + luma, coarse seed + fine | 22/30 | 12 |
| Port: debayer + luma, coarse seed without acceptance gates (C#) | 6 frames byte-identical | - |

The coarse stage does not improve on seeding at the box for this fixture, which is why the port
leaves it disabled until its percentile/brightness acceptance gates are reproduced. The residual
8 frames are off by exactly one pixel in every variant, so the difference is in the score
arithmetic, not in the search policy.
