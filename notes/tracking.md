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

## Probe: what the engine does when its gates reject (2026-10-18)

A 3-frame synthetic SER (hard-edged asymmetric arrow; frame 2 moved +2,-1; frame 3 moved +4,-2) with
`-en_ss -en_surface_feature_tracking -afb 56,56,48x48` produced **no shift at all** from the original:
frames 2 and 3 are byte-identical to their inputs. Our port shifted frame 2 by exactly the injected
amount, which is "correct" by content but diverges from the engine.

Conclusion: the engine's coarse stage (`FUN_1400a6e40`) refuses this content and the caller then
leaves the frame unshifted; the fine stage is never reached. The acceptance gates in the coarse loop
are therefore part of the observable behaviour, not an optimisation:

- low percentile of the candidate must fall inside
  `[max(0, -10 - mean*DAT_140137938), 10 - (refLow+1)*ratio*DAT_140137940]`
- brightness ratio `candidateSum / refSum` must lie in `[DAT_140120fb0, DAT_140137930]`
- `highPercentile - lowPercentile > 24`

Our port currently seeds at the box and skips these gates, so it tracks content the original rejects
and can land elsewhere on content the original accepts. Porting the gates (and reading those four
constants from `.rdata` rather than guessing them) is the next step; it is also the likely reason the
Saturn fixture differs on 8 of 30 frames, because the gated seed moves the fine window.

## Coarse-gate constants, read from .rdata (2026-10-18)

Extracted from `bin/pipp_core.exe` (VA base 0x140000000; all four live in `.rdata`):

| Address | File offset | Value | Role in the gate |
|---|---|---|---|
| `DAT_140120fb0` | 0x11f5b0 | **0.5** | minimum candidate/reference brightness ratio |
| `DAT_140137930` | 0x135f30 | **1.5** | maximum brightness ratio |
| `DAT_140137938` | 0x135f38 | **-0.8** | scales the mean into the lower percentile bound |
| `DAT_140137940` | 0x135f40 | **-1.2** | scales (refLow+1)*ratio into the upper bound |

With those, the acceptance test in `FUN_1400a6e40` becomes, for a candidate block:

```
ratio   = candidateSum / referenceSum            // must be 0.5 .. 1.5
mean    = refLow * ratio
lowMin  = max(0, -10 - mean * (-0.8))            = max(0, 0.8*mean - 10)
lowMax  = 10 - (refLow + 1) * ratio * (-1.2)     = 10 + 1.2*(refLow+1)*ratio
accept  = lowPct in [lowMin, lowMax]  and  highPct - lowPct > 24
```

For a planetary frame `refLow` is 2-3, so the low percentile must sit at roughly 0-15: **the candidate
must have a near-black background and more than 24 levels of contrast**. That is why the arrow probe
(a 30-level background, low percentile ~30) is rejected outright while Saturn's limb against space is
accepted. It also means the gate is what stops the tracker locking onto texture in a bright sky.

Not yet ported: `CoarseSeed` still runs without these gates and is disabled in favour of seeding at
the box. Porting the gate is the step that should make the probe match the original (no shift) and
may move the gated seed enough to close the 8 Saturn frames.

## Negative result: gates alone make Saturn worse (2026-10-18)

The coarse gates were implemented from the extracted constants and measured:

- arrow probe: **matches the original** - no shift applied, because no candidate passes the gates;
- Saturn fixture: **3/30 identical frames**, down from 23/30 with box seeding.

So the gates are necessary but not sufficient: our coarse *scoring* (block-downsampled LUT-SSD over the
whole frame) accepts candidates at the wrong places on real data, and a seed a few pixels off cannot be
recovered by the +/-3 fine window. The change was reverted; box seeding restores 23/30.

The engine's coarse stage therefore differs from ours in more than its gates - most likely the coarse
reference capture (`FUN_1400a7ec0`, which takes its own block dimensions and downsample factors from
the context rather than reusing box/16) and the per-candidate histogram scoring. Porting those is the
prerequisite for enabling any coarse seed.
