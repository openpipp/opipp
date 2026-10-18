# Quality score: the stride loop, the box life-cycle, and the "first frame boost"

Status: recovered from `bin/pipp_core.exe` (sha256 `04a088af…`) by disassembly, and modelled against the
oracle on `samples/ident.ser` / `samples/saturn30.ser`. Addresses are image addresses
(`base 0x140000000`); corpus line numbers refer to `ghidra/out/pipp_core.c`.

**Headline:** for "PIPP Default" (algorithm 0) the score is

```
score = sqrt( Σ_s  weight(s) · metric(s) )  ×  bright50SumSquares / 3251250.0
weight(s) = (qmin*qmin) / (s*s)          // 32-bit integer division
```

and because `qinc >= 1`, `weight(qmin) = 1` and **every larger stride contributes exactly 0**. The
stride loop is not a multi-scale average: strides after `qmin` only exist to scribble on a shared mask
buffer. That buffer is never cleared, which is what makes the first processed frame score ~1.3x higher
than the same frame in any other position.

## 1. Where the score comes from

- Producer: `FUN_1400af2d0(image, algorithm, isColour)` — 0x1400af2d0..0x1400af98e, 425 instructions,
  **one `ret` at 0x1400af98e**, result in `xmm0`.
- Caller: corpus 86598, inside the per-frame loop, guarded by the quality switch (`local_2670`).
  `FUN_1400b03e0(container, score, frameIndex)` (corpus 86599) inserts the value that the quality log
  prints with `%lf`.
- `algorithm`: `local_2650` from the CLI; 0 = "PIPP Default", 1 = "Old (As taken from Ninox)",
  2 = "Histogram", 3 = brightness sum. Of the documented values, only 0 and 1 reach the stride loop:
  - 2 returns `FUN_1400b00b0(image,flag) / (boxWidth*boxHeight)` (0x1400af333-0x1400af368);
  - 3 returns `FUN_1400afda0(image,flag)` (0x1400af372-0x1400af37d);
  - 0 and 1 run the loop, then `sqrt` (`FUN_1400bead0` = the CRT `sqrtsd` wrapper, 0x1400bebb9);
  - only 0 additionally multiplies by `FUN_1400b00b0(image,flag)/3251250.0` (0x1400af94c-0x1400af965);
  - 1 skips both the stride weight (0x1400af89c `cmp r14d,1 / je`) and the final scale.

Registers: `rbx` = image, `r14d` = algorithm, `edi` = the current stride. `[rsp+0x6c]` keeps
`isColour` for the final `FUN_1400b00b0` call.

## 2. The driver loop (exact accumulation semantics)

Image-struct fields used (uint indices / byte offsets):

| field | offset | meaning |
| --- | --- | --- |
| `image[0xb]` | 0x2c | `-qmin` |
| `image[0xc]` | 0x30 | `-qmax` |
| `image[0xd]` | 0x34 | `-qinc` |
| `image[2]` | 0x08 | bytes per sample (1 = 8-bit, 2 = 16-bit); pixel pitch is `3*image[2]` |
| `image[4]` | 0x10 | detection threshold ("Calculated minpixel value", 8-bit) |
| `image[0x374..0x377]` | 0xdd0/0xdd4/0xdd8/0xddc | box left, top, right, bottom (inclusive) |

Disassembly (0x1400af4ae onward):

```
edi = [rbx+0x2c]                      ; qmin  -> current stride
if (edi > [rbx+0x30]) goto epilogue   ; qmin > qmax: no loop
xmm6 = 0
do {
    nCols = boxWidth  / edi           ; signed idiv, 0x1400af4d0
    nRows = boxHeight / edi
    if (nCols < 2 || nRows < 2) break                    ; 0x1400af4e7
    buf = subsample(box, edi, nCols, nRows)
    reference = max interior sample < 0xfffa             ; see §3.2
    if (reference > 0) rescale buf by 60000.0/reference
    blurred = box3x3(buf, nCols, nRows) / 9
    m = metric(blurred, nCols, nRows, image[4] << 8)     ; FUN_1400aecf0
    xmm1 = m
    if (r14d != 1) {                                     ; 0x1400af89c
        eax = [rbx+0x2c] * [rbx+0x2c]                    ; qmin^2
        ecx = edi * edi                                  ; stride^2
        eax = eax / ecx                                  ; signed idiv -> int
        xmm0 = (double)eax * xmm1
    } else xmm0 = m
    xmm6 += xmm0                                         ; addsd, 0x1400af8c2
    do {                                                 ; skip duplicate geometries
        edi += [rbx+0x34]                                ; qinc
        if (boxWidth / edi != nCols) break
    } while (boxHeight / edi == nRows)
} while (edi <= [rbx+0x30])
```

Consequences that matter:

- **Only `qmin` contributes.** `(qmin*qmin)/(s*s) < 1` for every `s > qmin`, and the division is 32-bit
  integer, so the weight is 0. Verified: `qmin=qmax=3` frame 1 = 1065.667424 and `qmin=3,qmax=5` frame 1
  = 1065.667424 — adding strides 4 and 5 changes the first frame by nothing at all.
- The weights are computed in *integer* arithmetic and converted to double (`cvtdq2pd`); a stride of 0
  would divide by zero, so `qmin >= 1` is assumed by the CLI.
- The de-duplication means each distinct `(boxWidth/s, boxHeight/s)` pair is measured once, with the
  smallest stride that produces it.
- **`edi` is the current stride and `[rbx+0x2c]` is `qmin`** (loaded once at 0x1400af4ae, before the
  loop; `edi` is then advanced by `[rbx+0x34]` = `qinc` at 0x1400af8d0). The multiplier at 0x1400af8a2 is
  therefore `qmin²/stride²`, *not* a per-pixel or per-area factor.
- Accumulation is a single double (`xmm6`), in stride order; the final `sqrt` and scale are applied once,
  after the loop (0x1400af941-0x1400af965).

Post-processing of the total:

```
if (algorithm <= 1) total = sqrt(total)              ; FUN_1400bead0
if (algorithm == 0) score = total * bright50/3251250.0
```

`FUN_1400b00b0` builds a 256-bin histogram of the 8-bit sample (depth 1: the first byte of the 3-byte
pixel; depth 2: the high byte of the 16-bit sample; colour uses the LUT / Rec.601 luma), walks bins from
255 downward taking at most **50** samples, and returns `Σ v²` as a double. `3251250 = 50 * 255²`, so the
factor is 1.0 when the 50 brightest samples are saturated. It is content-dependent (0.7508 on
`ident.ser`, 0.7640 on saturn frame 11) and is applied only to algorithm 0.

## 3. The per-stride measurement

### 3.1 Subsampling — `FUN_1400af990(image, src, imageWidth, depth, stride, stride)`

A top-left-aligned box average: the `stride x stride` block whose top-left corner is the box's
`(left, top)` is averaged, then the block is stepped by `stride` in both axes. `nCols = boxWidth/stride`,
`nRows = boxHeight/stride` (integer division), buffer layout is row-major with row length `nCols`.

- depth 1 (8-bit): reads one byte per sample at 3-byte pixel pitch, `sum(byte) * 256 / stride²`;
- depth 2 (16-bit): `sum(ushort) / stride²`;
- depth 3: 8-bit colour through the three `image+0x38/0x438/0x838` LUTs;
- depth 6: `(R*299 + G*587 + B*114)/1000` on BGR-ordered 16-bit samples.

Integer division, truncating.

### 3.2 Reference value and rescaling — 0x1400af5f2-0x1400af865

The code looks like a "top 3 accumulator" (`local_68/60/58` in the pseudo-C) but it degenerates:

- the first comparison slot (`[rsp+0x80]`) is zeroed and never written (the insertion loop runs
  `for (i = 5; i > rank; i--)`, and `rank` is always 0 because it compares against that unwritten slot);
- every insert therefore writes the *same* value into slots 1..5, including the three dwords that are
  later summed (`[rsp+0x8c] + [rsp+0x90] + [rsp+0x94]`);
- the gate (`[rsp+0x88]`) only accepts values strictly larger than the previous insert.

Net effect: the three "accumulators" always hold the same number and their mean is it, i.e.

```
reference = max{ v : v is an interior sample of the subsampled image, v < 0xFFFA }
gain      = reference > 0 ? 60000.0 / reference : 1.0
buf[i]    = (ushort)min(65535, (int)(buf[i] * gain))     // cvttsd2si, truncate
```

"Interior" here means rows 1..nRows-2, all columns. Samples >= 0xFFFA (65530) are skipped, so a fully
saturated image leaves `reference = 0` and no rescaling happens.

Verified indirectly: the model using `max` reproduces the clean single-stride scores to 0.006-0.008%
(`qmin=qmax=4`: engine 1304.686884, model 1304.577; `qmin=qmax=5`: 1487.801304 vs 1487.706). A top-3
*mean* would be ~0.5-1% lower on the gain and does not fit.

### 3.3 Blur — `FUN_1400afc90`

3x3 box blur, `sum/9` integer (`0x38e38e39` magic), written as `ushort`, interior pixels only
(rows 1..nRows-2, cols 1..nCols-2). The destination buffer (`image+0xc58`) is allocated with
`FUN_14008dd20`, which zeroes it, so the untouched border is 0.

### 3.4 Metric — `FUN_1400aecf0(blurred, nCols, nRows, threshold8 << 8, algorithm)`

```
ct = 1 + (int)(0.1 * nRows)          // cvttsd2si of nRows * -0.1, negated, +1
cl = 1 + (int)(0.1 * nCols)
thr = image[4] << 8

// pass 1: mark
count = 0; sumAbove = 0
for y in [ct, nRows-ct), x in [cl, nCols-cl):
    if blurred[y*nCols+x] >= thr:
        count++; sumAbove += value
        mask[p-1] = mask[p] = mask[p+1] = 1  for p = index-bw, index, index+bw  // full 3x3

if count == 0: return -1.0

// pass 2: mean squared neighbour difference over marked pixels
sum = 0; marked = 0
for y in [ct, nRows-ct), x in [cl, nCols-cl):
    if mask[y*nCols+x] != 0:
        c = blurred[p]; a = blurred[p + nCols]   // one row DOWN
        b = blurred[p + 1]                       // one column RIGHT
        sum += (c-a)^2 + (c-b)^2; marked++

return sum / marked / 10.0
```

Notes:

- The denominator is the number of **marked** pixels (the 3x3 dilation of the above-threshold set), not
  the crop area and not `count`; the crop is only the iteration window.
- The threshold comparison is `>=`, against `image[4] << 8` (e.g. 18 -> 4608).
- Algorithm 1 takes the other branch: `sum += |c-a| + |c-b|`, then
  `m = (sum/marked) * 50.0 / meanAboveThreshold`, where `meanAboveThreshold` is the mean of the samples
  that passed the threshold (0x1400af05b-0x1400af074); algorithm 0 uses the squared/`10.0` form.
- `-1.0` (constant at 0x140132950) means "no pixel above threshold in the crop". With weight 1 this makes
  the total negative and `sqrt` returns NaN — a black frame produces a NaN score, not 0.
- The `mask` buffer is `image+0xc48`, allocated by `FUN_14008dcc0(nCols*nRows)`. That allocator only
  grows (power-of-two capacity) and **never clears** — see §5.

## 4. Box life-cycle (`image[0x374..0x377]`, corpus 116959-116988)

Mapping: `0x374` = left/x0 (0xdd0), `0x375` = top/y0 (0xdd4), `0x376` = right/x1 (0xdd8),
`0x377` = bottom/y1 (0xddc); inclusive on both ends (the init stores `width-1`, `height-1`).

At the top of `FUN_1400af2d0`, before the stride loop (0x1400af3b8-0x1400af44e):

```
if (x1 == 0 && y1 == 0 && y0 == 0 && x0 == 0) {     // all four zero
    x0 = 0; y0 = 0; x1 = width-1; y1 = height-1;    // whole frame
} else {
    x0--; y0--; x1++; y1++;                          // grow 1 px on each side
    x0 = max(x0, 0); y0 = max(y0, 0);
    x1 = min(x1, width-1); y1 = min(y1, height-1);
}
boxWidth  = x1 - x0 + 1;
boxHeight = y1 - y0 + 1;
```

Who else writes those fields: only the object finder `FUN_140077560` (corpus 77650-77656 and
78230-78256). On success it writes the detected object bbox; on failure it writes four zeros. The
quality routine is the only other writer (the init/expand above).

So the life-cycle is:

1. **Before any detection** the struct is zeroed -> first quality call measures the **whole frame**.
2. **With detection** (`-chk` / the AOI path) the finder rewrites the box every frame, so the quality
   call measures **bbox + 1 px on each side**. Frames 2..N of `ident.ser` with `-chk` are identical
   (991.156442 x3), which proves the box is rewritten to the same bbox each frame rather than growing.
3. **Without detection** nothing rewrites the box, so after the first call it is the whole frame and the
   clamp keeps it there: every frame is measured over the whole frame.
4. **Detection failure mid-run** zeroes the box, so the next quality call jumps back to the whole frame.
   Because the +1 expansion is stored, a run that toggles detection can accumulate a few expansion
   steps before the clamp saturates.

Empirical check that the no-`-chk` box really is the whole frame: with a bright 9x9 probe patch moved
around a 640x640 textured frame, patches at the corners and edge midpoints all scored exactly
1.750718, while patches at (320,320)/(320,160)/(320,480)/(160,320)/(480,320) scored 96-113. That is
the **10% crop** of the whole frame (interior 66..573 px), not a smaller box: those outer patches are
outside the crop but still inside the reference-max window, hence the identical background-only score.

## 5. Why the first processed frame scores 1.3255x higher

It is not the box and not the image. It is the **mask buffer at `image+0xc48` being reused without
being cleared**.

- `FUN_14008dcc0` (0x1400af4a9, 0x1400aed61) grows a power-of-two buffer and returns it; it does not
  zero it. `FUN_14008dd20` (used for the blur buffer at 0x1400afcbf) does zero. The mask goes through
  the non-clearing one.
- The mask is a *union* of 3x3 dilations in the buffer's own index space (`y*nCols + x`). Stride 3
  produces a 213-column layout, stride 4 a 160-column layout, stride 5 a 128-column layout. Bits left
  by the previous frame's later strides land on unrelated pixels of the current frame's stride-3 mask,
  so `marked` is inflated with low-contrast background pixels and the mean squared difference drops.
- The first call in a process gets a fresh (zero) block, so it is the only *clean* measurement. Every
  later frame inherits the previous frame's scribbles. Frame 1 is not boosted; frames 2..N are depressed.

Evidence (all `samples/ident.ser`, four byte-identical frames, `-q=4`):

| qmin | qmax | qinc | frame 1 | frames 2-4 |
| --- | --- | --- | --- | --- |
| 3 | 3 | 1 | 1065.667424 | 1065.667424 |
| 4 | 4 | 1 | 1304.686884 | 1304.686884 |
| 5 | 5 | 1 | 1487.801304 | 1487.801304 |
| 4 | 5 | 1 | 1304.686884 | 1063.252846 |
| 3 | 5 | 2 | 1065.667424 | 925.189942 |
| 3 | 5 | 1 | 1065.667424 | 803.964973 |

When only one geometry is measured the stale bits coincide with the bits the current pass writes, so
identical frames score identically. As soon as a second geometry is measured the later frames fall.

A Python model of the whole chain (subsample -> max/60000 rescale -> 3x3 /9 -> 10% crop -> 3x3 mask ->
marked-pixel mean, with the mask buffer never cleared) reproduces it:

| case | engine | model |
| --- | --- | --- |
| ident `qmin=qmax=4`, frame 1 | 1304.686884 | 1304.577 |
| ident `qmin=qmax=5`, frame 1 | 1487.801304 | 1487.706 |
| ident `qmin=qmax=3`, frame 1 | 1065.667424 | 1063.994 |
| ident `qmin=3,qmax=5`, frames 2-4 | 803.964973 | 810.035 |
| ident `qmin=4,qmax=5`, frames 2-4 | 1063.252846 | 1068.304 |
| ident `qmin=3,qmax=5,qinc=2`, frames 2-4 | 925.189942 | 926.908 |
| saturn frame 1 clean | 1065.667424 | 1063.994 |
| saturn frame 11 clean (reordered first) | 1100.569423 | 1089.524 |
| saturn frame 11 second | 813.334568 | 812.623 |

Residual 0.006-1.0%, attributable to the per-frame threshold (`image[4]` is 17 or 18 depending on the
frame; the model uses 18) and to the exact reference sample.

Corollaries:

- `ident.ser` is four copies of `saturn30.ser` frame 1 (verified byte-identical), and the reordered
  run is `saturn30.ser` with frame 11 first (also verified) — which is why the "1065.667424" appears in
  both logs.
- The 1.3102 ratio in the original Saturn log and the 1.369 in the reordered log are both this artefact,
  which is why no content metric could reproduce them: they are not in the pixels.
- The whole top-10 ordering after position 1 is polluted by whatever each frame inherited, so parity
  requires reproducing the buffer reuse (per-frame quality scores are not intrinsic frame properties).

## 6. C# implementation recipe

```csharp
// One instance per work image, kept for the whole run: this is the engine's image struct.
sealed class QualityScratch
{
    public byte[] Mask = Array.Empty<byte>();   // image+0xc48: grow-only, NEVER cleared
}

static double PippDefault(ushort[] frame, int width, int height,
                          int qmin, int qmax, int qinc, int threshold8,
                          QualityScratch scratch)
{
    double total = 0.0;
    int stride = qmin;

    while (stride <= qmax)
    {
        int bw = width / stride, bh = height / stride;
        if (bw < 2 || bh < 2) break;

        // 1. box-average subsample (top-left aligned stride x stride blocks)
        var buf = new int[bw * bh];
        for (int y = 0; y < bh; y++)
        for (int x = 0; x < bw; x++)
        {
            long acc = 0;
            for (int dy = 0; dy < stride; dy++)
            {
                int rowBase = (y * stride + dy) * width + x * stride;
                for (int dx = 0; dx < stride; dx++) acc += frame[rowBase + dx];
            }
            // `frame` holds the engine's 16-bit sample scale (8-bit input already shifted left 8)
            buf[y * bw + x] = (int)(acc / (stride * stride));
        }

        // 2. reference = brightest interior sample below 0xFFFA; rescale to 60000
        int reference = 0;
        for (int y = 1; y < bh - 1; y++)
        for (int x = 0; x < bw; x++)
        {
            int v = buf[y * bw + x];
            if (v > reference && v < 0xFFFA) reference = v;
        }
        if (reference > 0)
        {
            double gain = 60000.0 / reference;
            for (int i = 0; i < buf.Length; i++)
            {
                int v = (int)(buf[i] * gain);                   // truncate toward zero
                buf[i] = v > 65535 ? 65535 : v;
            }
        }

        // 3. 3x3 box blur, integer /9, interior only (border stays 0)
        var blurred = new int[bw * bh];
        for (int y = 1; y < bh - 1; y++)
        for (int x = 1; x < bw - 1; x++)
        {
            int p = y * bw + x;
            blurred[p] = (buf[p-bw-1] + buf[p-bw] + buf[p-bw+1]
                        + buf[p-1]    + buf[p]    + buf[p+1]
                        + buf[p+bw-1] + buf[p+bw] + buf[p+bw+1]) / 9;
        }

        // 4. 10% crop, 3x3 dilation of samples >= threshold, then mean squared neighbour diff
        int ct = 1 + (int)(0.1 * bh), cl = 1 + (int)(0.1 * bw);
        int thr = threshold8 << 8;

        int need = bw * bh;
        if (scratch.Mask.Length < need)                 // power-of-two grow, contents kept
        {
            var bigger = new byte[Math.Max(need, scratch.Mask.Length * 2)];
            Array.Copy(scratch.Mask, bigger, scratch.Mask.Length);
            scratch.Mask = bigger;
        }
        byte[] mask = scratch.Mask;                     // note: no Array.Clear

        for (int y = ct; y < bh - ct; y++)
        for (int x = cl; x < bw - cl; x++)
        {
            int p = y * bw + x;
            if (blurred[p] < thr) continue;
            for (int dy = -1; dy <= 1; dy++)
            {
                int q = p + dy * bw;
                mask[q - 1] = 1; mask[q] = 1; mask[q + 1] = 1;
            }
        }

        double sum = 0.0; int marked = 0;
        for (int y = ct; y < bh - ct; y++)
        for (int x = cl; x < bw - cl; x++)
        {
            int p = y * bw + x;
            if (mask[p] == 0) continue;
            int c = blurred[p];
            int dDown  = c - blurred[p + bw];
            int dRight = c - blurred[p + 1];
            sum += (double)dDown * dDown + (double)dRight * dRight;
            marked++;
        }

        double metric = marked > 0 ? sum / marked / 10.0 : -1.0;
        total += ((qmin * qmin) / (stride * stride)) * metric;   // integer division: 1,0,0,...

        int next = stride + qinc;                                // skip duplicate geometries
        while (next <= qmax && width / next == bw && height / next == bh) next += qinc;
        stride = next;
    }

    return Math.Sqrt(total) * Bright50Squares(frame, width, height) / 3251250.0;
}
```

`Bright50Squares`: 256-bin histogram of the 8-bit sample (high byte for 16-bit mono; LUT luma for 8-bit
colour; `(R*299+G*587+B*114)/1000 >> 8` for 16-bit colour), walk bins 255..0 taking at most 50 samples,
return `Σ v²` as a double.

Implementation notes:

- **Parity mode** keeps `QualityScratch.Mask` alive across frames and never clears it; a clean score is
  what you get with a zeroed mask, i.e. only the first frame of a run.
- For a *single* frame (a unit test) always start from a zeroed mask and compare against PIPP's frame 1
  of that image.
- Default `qmin=3, qmax=5, qinc=1` means only stride 3 affects the value; strides 4 and 5 only corrupt
  the mask for the following frame. If the goal is a sane quality metric rather than log parity, drop
  the mask reuse (`Array.Clear`) and the score becomes a stable single-scale contrast measure.
- `-1.0` for an all-dark crop makes `sqrt` produce NaN; the engine prints that as a very low value.
  Guard it if the log must not contain NaN.

## 7. Verified vs inferred

**Verified against the binary and/or the oracle**

- `FUN_1400af2d0` is 0x1400af2d0..0x1400af98e with one `ret`; the score is in `xmm0`; algorithm 0
  returns `sqrt(Σ w·m) * bright50/3251250`.
- `weight(stride) = floor(qmin²/stride²)`; only `qmin` contributes (proved by the `qmax=qmin` runs:
  identical scores; `qmax>qmin`: frame 1 identical to the `qmax=qmin` run).
- `edi` = current stride, `[rbx+0x2c]` = `qmin`, `[rbx+0x34]` = `qinc`, `[rbx+0x30]` = `qmax`.
- Metric: 10%-plus-one crop; `>= image[4]<<8`; 3x3 dilation mask; denominator = marked-pixel count;
  `(c-down)² + (c-right)²`; `/10.0`; `-1.0` when nothing is above threshold.
- Rescaling reference is the maximum interior subsample value below 0xFFFA, gain `60000/reference`
  (the three "accumulator" slots always receive the same value; the model matches to 0.006%).
- Blur is a 3x3 integer `/9` box filter, interior only; its buffer is zeroed each call.
- Box fields and the all-zero -> whole-frame init / else +1 expand-and-clamp logic (0x1400af3b8-0x1400af44e).
- The only other writer of the box is the object finder `FUN_140077560` (bbox on success, zeros on
  failure).
- The mask buffer `image+0xc48` is allocated with the non-clearing allocator `FUN_14008dcc0`; the blur
  buffer uses the clearing `FUN_14008dd20`.
- The first-frame anomaly is the mask reuse: the six-row qmin/qmax table above, plus the model's
  reproduction of all six cases and of saturn frames 1/11.

**Inferred, not directly exercised**

- The per-frame value of `image[4]` (the model uses 18; the engine reports 17-18 on real frames and 18
  on `ident.ser`). This is the most likely source of the remaining 0.1-1% model residual.
- The exact detected bbox with `-chk`: the model with the naive `>=18` bbox + 1 gives 1044/1001 vs the
  engine's 1020/991, i.e. the shape of the effect is right but the finder's bbox is a few pixels
  different.
- The colour paths inside the subsampler and `FUN_1400b00b0` (depth 3/6) were read from the code but
  only the mono 8-bit path was exercised on the oracle.
- That the first mask allocation is zero-filled (fresh heap pages). The match of the first-frame model
  supports it; the code does not explicitly zero it.
- `FUN_1400bead0` is the CRT `sqrt` wrapper (the `sqrtsd` at 0x1400bebb9 makes this near-certain).
