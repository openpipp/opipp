# Journal

Chronological record of what each working session established. Newest last. Commit hashes are the
checkpoints to resume from.

## M0 - Recovery (`94f4b88`)

Copied the installed binaries read-only (`bin/`), decompiled `PIPP.exe` to 14,277 lines of C#
(`decompiled/`, ILSpy), dumped 4,229 native functions from `pipp_core.exe` (`ghidra/out/`), extracted
the 126-switch CLI contract from the GUI, and rebuilt the engine and UI as .NET 10 projects.
Result: build clean, 284 tests, first parity harness.

## M1 - Drop-in parity (`c66b5a8`)

Matched the engine's output layout (`<out>/<name>/<n>.<ext>`), numbering (from 1; from 0 after
quality selection; padded to frame count), `-keepimgname`, `logs/<name>_pipp_log.txt` and the quality
log, and exit code 127 for unknown switches. Harness extended to compare directory trees and log
text. 9/9 cases matched at that point.

## M2 - Formats (`055b884`, `01c4524`)

Verified BMP and Y8 (the latter is a no-op: mono AVI is already Y800). Added 16-bit and RGB fixtures.
Two findings:

- the original's 16-bit TIFF is byte-swapped under an `II` header (ffmpeg cannot read it either), so
  we write spec-correct files and record a deliberate divergence;
- our colour SER used ColorID 16, which PIPP treats as bayer/mono. The correct dialect is
  **100 = RGB, 101 = BGR**; after fixing the writer, both colour cases match exactly.

GIF remains unimplemented (needs LZW + palette); recorded as a known gap.

## M3 - Real data (`b1756a4`)

Obtained a real capture (Strata sample set: Saturn, 640x640, 8-bit bayer, 8984 frames) and tested a
30-frame subset. **Plain and debayer are pixel-identical on real data** - the strongest evidence so
far. The histogram stretch and centring diverge.

The stretch turned out to be image-dependent: gain 2.3043 on the real frame vs 2.04 on synthetic at
`-hs=204`. Sweeps across two `-hs` values and six frames fit `gain = (wp/100) * 255/(255-b)` with
`b` drifting 28.05-29.49. The user chose **option A**: chase exact parity rather than declare a
divergence. The Ghidra trace located the option field (`+0x1DC`, default `0xCC`, percent form
`x255/100`) but not yet the consumer.

## M4 - Real-data stretch parity (`093eea2`)

Found the consumer by following the option, not grepping for `0x1dc`: the parser wrapper
`FUN_1400aea40` is called from `FUN_14007e8e0` with a stack-local options struct, whose field
`+0x1DC` appears in the frame loop as `local_261c` and is passed to **`FUN_14009b4a0`**. That routine
is the whole answer:

- **white point = mean of the 25 brightest samples** (`0x19`), per channel, accumulated from the top of
  the histogram with a weighted boundary bin and integer division;
- **gain = `whitePoint * (max/100) / (white − black)`** with `max` = 255 or 65535 (the doubles `2.55`
  and `655.35` at `0x140137788`/`0x140137790`), `black` = 0 unless `-hs_bp` (mean of the 25 darkest);
- **map = `truncate((v − black) * gain)`**, clipped;
- on colour, the **smallest** channel gain is used unless `-hsrgb` (`options + 0x1E0`) asks for one per
  channel. No equalisation, despite the help text.

The old constant-gain model was the saturated-tail special case, and the "−1 bias" was double
precision: `204*2.55/255` sits just below 2.04.

Verification: a Python model of the rule reproduced **all 30 frames** of the real Saturn capture
exactly, then the C# port did too (`--real` in the harness). Gains at `-hs=204` are 2.2816-2.3432
(W = 222..228, median 2.3120) - wider than the earlier six-frame estimate, which had been derived
from rounded pixel pairs. `-hs_bp` and the two colour-stretch variants also match, so `-hsrgb` and
the black-point rule moved from "parsed" to "verified".

Two side findings: `-hs_bp` had never been wired into `PipelineSettings` (now it is), and the debayer
parity case had been comparing two **no-ops** - `-db=<n>` only sets the pattern; the bare `-db` flag
enables interpolation, and with it our output differs by ~1 LSB on 37% of pixels (recorded as a known
gap, not fixed here).

## M5 - Detection threshold (`<commit>`)

Phase 4 item 1. The engine's "Calculated minpixel value" is `FUN_14009ab40`:

- it averages 3x3 blocks into a 1/3-scale copy of the frame (integer mean of nine samples);
- takes each small-image row's maximum, keeps the five **smallest** (padding with zero), and averages
  them (`m`) - on a frame with a planet most rows are empty, which is why the value tracks the
  background rather than the object;
- averages every small-image pixel strictly above `m`, then returns `m + (meanAbove - m)/4`; a zero
  result makes the caller substitute 40 (`FUN_14009b350(x, 0x28)`).

Verified: real Saturn frames report 17-18 and the reconstruction now prints the same value (18 on
frame 1, log-for-log). Synthetic noiseless discs are one unit low, a rounding artefact inside the
engine's per-sample luma tables, recorded rather than papered over. Replaces `FrameAligner.OtsuThreshold`
in the pipeline and in the log; `-chk` now makes the log carry the value, as the engine does.

Centring was then measured properly: `-ctr` **is** a pure integer translation (the object bbox moves
without resizing), the vacated border is filled with the frame's background level, and shift
`(-19,+37)` with fill 3 reproduces oracle frame 1 byte-for-byte. The centroid rule itself is still
open: every weighting we tried (binary, v, v-thr, squared, 1/3-scale) lands 2-4 px away from the
offset the engine applied.

## M6 - Quality scores quantified (`<pending commit>`)

With `-q=10 -qmin=3 -qmax=5 -qinc=1` the engine writes `logs/<name>_pipp_quality.txt` with
`Pos / Source Frame / Output Frame / Quality Value / % of Best`. The format already matches ours, so
the log comparison is structural; the numbers do not.

On the real capture the engine ranks frame 1 first at **1065.667424** and frame 11 second at
**813.334568** - a 31% spread. Ours ranks frame 11 first at 19.711496 and frame 1 ninth at 18.870455,
a 4% spread. The ordering itself differs, so this is not a scale factor: the estimator behind "PIPP
Default" weighs something we do not. Next step is to find that routine (`FUN_1400af990` is the
downsample it calls; follow its callers), not to calibrate constants.

## M7 - Quality estimator: pipeline located, metric still open

Walked the call sites of the subsample routine (`FUN_1400af990`). The quality path is:

1. `FUN_1400af990` samples the frame at a stride (`-qmin`..`-qmax`..`-qinc` drive the loop), writing a
   16-bit subsampled buffer;
2. the buffer is **contrast-normalised** by dividing by the mean of three stored accumulators
   (`dVar20 = dVar1 / iVar18`, saturating at 65535) - so absolute brightness cannot influence the
   score;
3. `FUN_1400afc90` applies a **3x3 box blur** to the normalised buffer;
4. the score comes from **`FUN_1400aecf0(image, blurred, width, height)`**; the algorithm selector
   `param_2 == 3` uses `FUN_1400afda0` instead (a 781-byte routine);
5. the search box (`image[0x374..0x377]`) is tracked and expanded across iterations.

Negative results, worth not repeating: variance, Tenengrad, mean |gradient|, Laplacian variance,
max gradient, and top-5 brightness (at strides 3/5/7, and top-5/median ratios) all give a frame1/frame11
ratio of 0.87-1.07, while the engine reports **1.3102**. So the score is not a global image statistic -
with the box tracking in the path, it is most likely measured inside the detected object's box.

Next: read `FUN_1400aecf0` (and `FUN_1400afda0` for mode 3); that is the metric itself.

## M8 - Quality: mode 3 is a brightness sum, not the score

`FUN_1400afda0` (the `param_2 == 3` branch, returns `double`) walks the search box
(`image[0x374..0x377]`), and for every sample whose 8-bit value exceeds the threshold (`param_1[4]`,
i.e. the "Calculated minpixel value") accumulates the **16-bit sample**; it returns that sum as a
double. So it is a *detection* score - how much signal is above the floor - not a sharpness metric.

The quality log itself reads its numbers from a container: `FUN_1400b03b0(container, i)` for the
score and `FUN_1400b0350(container)` for the best, written with `%lf` and a `%d%%` ratio
(corpus 89130-89176). The scores are pushed into that container earlier, by whatever computes them,
so the next step is to find the insert call (the push counterpart of `FUN_1400b03b0`) and read its
caller - that is where the `1065.667424`-style decimal is produced.

Also noted: `FUN_1400aecf0` (called with the blurred buffer) compares `threshold << 8` against
samples and marks a 3x3 neighbourhood - it is the overexposed/no-signal marker, not the metric.

## M9 - Centring: the offset looks like a binary centroid at a low threshold

Measured the oracle's exact translation per frame from the object bbox (a rigid move keeps bbox size
constant; frames where the object is clipped by the shift are unusable and were excluded). Frame 1
moves `(-19,+37)`.

Candidate rule: **binary centroid of the mask at a threshold well below the detection threshold, then
truncation toward zero**, `shift = (320 - cx, 319 - cy)`.

- threshold 8 on frame 1: centroid `(339.38, 281.26)` -> `(320-339.38, 319-281.26)` = `(-19.38, 37.74)`
  -> truncates to `(-19,+37)` - **exactly the observed shift**;
- frames 2 and 4 agree on y and miss x by one (`-19` predicted vs `-20`/`-21` observed), so the
  threshold or the target is not quite right yet.

Why 8: unknown. It is not the detection threshold (18) and not half of it (9 predicts frame 2's y but
not frame 1's). Next: sweep the threshold on clean frames only, and check whether the mask is built
on the 3x3-blurred 1/3-scale image (which would move the centroid in 1/3-pixel steps and explain the
one-unit misses).

## M10 - Quality producer identified: `FUN_1400af2d0`, return value lost by the decompiler

The container mystery is solved. `FUN_1400b03e0(container, double score, int frameIndex)` is the
insert (a sorted insert into `(double, index)` records); its only call site is 86600:

```
uVar14 = FUN_1400af2d0(lVar23, local_2650, local_2bfc);   // image, algorithm, flag
uVar10 = FUN_1400b03e0(local_2a90, uVar14, local_2c30 + 1);
```

So the quality score *is* the return value of `FUN_1400af2d0` - the 1727-byte search driver that
subsamples, contrast-normalises by the mean of three accumulators, blurs 3x3 (`FUN_1400afc90`), marks
overexposed pixels (`FUN_1400aecf0`) and dispatches `param_2 == 3` to `FUN_1400afda0`.

**Blocking detail:** Ghidra types that function `void` and shows three bare `return;` statements
(lines 58, 62 and 268 of its body), while the caller clearly reads a `double` from it. The pseudo-C has
lost the returned expression. Do **not** keep reading the C for it - disassemble the function's tails
(or run REA `analyze_function` / `procedure_pseudo_code` on `0x1400af2d0`) and read what lands in
`XMM0` at each `ret`. That is the remaining unknown for the quality score.

## M11 - Quality return path disassembled (capstone)

Installed capstone in a scratch venv (`uv venv` + `uv pip install capstone`) and disassembled
`0x1400af2d0` straight from `bin/pipp_core.exe` (RVA 0xaf2d0, 1727 bytes, 441 instructions; script at
`%LOCALAPPDATA%\hermes\cache\scratch\dis_quality.py`).

Facts recovered:

- there is exactly **one `ret`** (0x1400af6c9), so the three bare `return;` in the pseudo-C are jumps
  to a shared epilogue - the decompiler simply dropped the value;
- the return value is **`xmm0 = (xmm3 / xmm1) / K`**, or on the alternate branch
  `xmm0 = <constant at 0x140192d50>`;
- the accumulator is built as `xmm3 += (a - c)^2 + (b - c)^2` (`subsd`/`mulsd` pairs at
  0x1400af58b-0x1400af5ab, repeated at 0x1400af5d4 and 0x1400af644), i.e. **squared deviations from a
  reference point**, not a gradient or variance of pixels.

Open detail: the rip-relative divisor (`[rip+0x71d30]` at 0x1400af698 -> 0x1401813d0) and the alternate
constant (0x140192d50) decode as nonsense doubles through the current RVA mapping, so either the mapping
for those sections is wrong or the divisor is written at runtime. Verify with a section-aware read
before trusting any formula: `K` and the branch constant are the last unknowns.

## M12 - CORRECTION: M11 was decoded from the wrong bytes; real constants recovered

**M11 is void.** The script behind it unpacked the PE section header at `+12` instead of `+8`, so
`VirtualAddress`/`SizeOfRawData`/`PointerToRawData` were shifted by one field and it disassembled
garbage that happened to decode into a plausible-looking FP sequence. Lesson for the next session:
after any PE parse, verify the prologue of a known function (`push rbx; push r14; sub rsp,0xd8` at
RVA 0xaf2d0) before trusting a single instruction.

Corrected results (section parse fixed, prologue verified):

- `FUN_1400af2d0` - 425 instructions, `0x1400af2d0..0x1400af98e`, **one `ret` at 0x1400af98e**,
  returns a `double` in `xmm0` taken from `xmm6`; if `r14d == 0` it is scaled by
  `FUN_1400b00b0(...) / 3251250.0` (the constant at RVA 0x13a2b0; 3251250 = 255^2 * 50).
- `FUN_1400aecf0` - the per-scale metric (382+ instructions, `ret` at 0x1400af2c9):
  - crops a 10% margin on each side (`iVar12 = 1 - (int)(param_4 * -0.1)`);
  - counts samples with `threshold << 8 <= sample` and marks their 3x3 neighbourhoods;
  - accumulates `(a - c)^2 + (b - c)^2` over neighbouring 16-bit samples
    (loads at 0x1400af0c8/0x1400af0d3/0x1400af0e7), five such blocks per iteration;
  - returns `sum / count / 10.0` (divisor at RVA 0x120fd0 = **10.0**), or **-1.0** when the window
    holds no above-threshold sample (constant at RVA 0x132950 = -1.0).
- Neighbouring constants in `.rdata`: 0x13a2a8 = 60000.0, 0x13a2b8 = -0.1 (the crop fraction).

So the quality score is a **mean squared neighbour difference**, scaled by 10 and then by the outer
factor - not any of the global statistics ruled out in M7, which is consistent with the 1.31 vs ~1.0
gap measured there.

## M13 - Quality: metric implemented in a model, ranking still unexplained

Modelled the recovered metric in Python (stride-average to a small image, normalise to 60000/mean,
3x3 integer blur, crop 10%, mean squared 4-neighbour difference / 10) and compared frame 1 against
frame 11 on the real capture:

| region | stride 3 | stride 4 | stride 5 | engine |
| --- | --- | --- | --- | --- |
| whole frame | 0.9905 | 1.0089 | 0.9996 | 1.3102 |
| object bbox | 0.9531 | 0.9577 | 0.9551 | 1.3102 |

So the metric as modelled does not produce the engine's 31% advantage for frame 1, even restricted to
the object box. The remaining difference must come from the parts of the driver not yet modelled:

- the search box `image[0x374..0x377]` is **expanded by one pixel per iteration** and clamped
  (pseudo-C 116959-116988), so later iterations measure a larger region;
- `xmm6` accumulates across the whole stride loop (`-qmin`..`-qmax` by `-qinc`), and how the
  per-stride values combine is not yet known;
- the final scale is `FUN_1400b00b0(image, flag) / 3251250.0`, and `FUN_1400b00b0` is **not** a
  getter - it allocates a 0x400-byte stack buffer and walks the pixels (a histogram/statistic), so the
  factor is content-dependent and can differ per frame.

That last point is the most likely explanation for frame 1 being special: its score may be scaled by a
different content statistic than the others. Next: read `FUN_1400b00b0`'s tail (the return value) and
the stride-loop accumulation, then re-fit the model.

## M14 - The engine's quality ranking is order-dependent (first frame boosted 1.3x)

Reordering the capture settles it. With frames reordered so that frame 11 is processed first:

| run | first frame | score | next best | score | ratio |
| --- | --- | --- | --- | --- | --- |
| original order | frame 1 | 1065.667424 | frame 11 | 813.334568 | 1.310 |
| reordered (11 first) | frame 11 | 1100.569423 | frame 3 | 803.749462 | 1.369 |

Frame 11 is the *same pixels* in both runs, yet scores 1100.6 when processed first and 813.3 when
second. So the ~1.3x advantage is **not image content** - it is a structural artefact of being the
first frame through the quality path, and it explains why no content-based metric (variance,
Tenengrad, Laplacian, top-5 brightness, the recovered squared-difference metric at any stride, whole
frame or object bbox) could reproduce the 1.31 ratio: they were all ~0.95-1.0 because the effect is
not in the pixels.

Likely cause, from the pseudo-C: the search box `image[0x374..0x377]` is zero-initialised and, when
all four fields are zero, is set to the **whole frame** (116959-116964); only afterwards is it carried
over and expanded per frame. The first frame is therefore measured over a much larger region than
every later frame, which changes the accumulated value.

Implication for parity: matching the quality log requires reproducing this initialisation and the
carry-over of the box between frames, not a better sharpness formula. The next session should model
the box life-cycle (full frame on the first frame, then per-frame detection + one-pixel expansion)
and check the *ratios* rather than absolute scores.

## M15 - GIF: the original writes an image-less file; ours writes a valid one

Ran the engine with `-gif` on the 4-frame parity fixture:

```
bin/pipp_core.exe -gif -od=<dir> parity/capture.ser
```

It produces a single `capture.gif` at the output root (not in a per-name folder, unlike TIFF/PNG),
820 bytes, header `GIF89a`, logical screen 32x24, 256-entry global colour table - and a block walk
finds **no image descriptor at all**: only a NETSCAPE2.0 application extension, a comment extension,
and the trailer. `ffprobe` reports the dimensions but the stream has no decodable frames.

So the "GIF output" item is not a missing feature on our side; the shipped engine's GIF path is
broken for this input class, in the same family as the 16-bit TIFF byte-swap. We now have a working
writer (`GifWriter`, commit 8b6578a) verified against ffmpeg, so the sensible resolution is: write
**valid** GIFs and record the divergence, rather than reproduce an empty file.

Naming to match if we integrate it: one animated file at `<out>/<name>.gif` (no subfolder), 256-colour
GCT, NETSCAPE2.0 loop extension, no per-frame graphic control extension.

## M16 - Centring solved and implemented (`86112c6`)

`-ctr` is **not** a centroid. The engine run-detects an object box (`FUN_140076ae0` for the 8-bit
path, `FUN_140077560` for 16-bit) and centres the box origin:

```
xpos = left + (right - left) / 2      // bottom-up buffer rows
ypos = top  + (bottom - top) / 2
dx = W/2 - xpos;  dy = ypos - H/2
```

The detector is a scanline tracker: a 4-sample moving sum `>= 4 * threshold`, runs longer than 9
pixels, a best-row scan followed by extent scans in both directions, with the binary's own trims
(`left = minX - 1`, `right = maxX - 3`). The box is `(left-1, top+1, right+1, bottom+1)`, so its
width is the true width plus one and its height one less. Threshold 18 (the logged minpixel value) is
essential: 17 or 19 gives only 8/16 correct shifts.

Implemented as `PlanetCentring` (ported from `notes/centring.md`), with `FrameAligner.CentreOffset`
delegating to it and a new `ShiftWithBackground` that translates by whole pixels and fills vacated
area with the frame median - the old `Shift` resampled sub-pixel and clamped edges, which is right for
stacking but wrong here.

Verified: **30/30 frames of the real capture pixel-identical**, plus a new `real-centre` parity case.
This also retires the earlier "centring is a pure translation with fill 3" note: 3 was the median, and
the offset was never a centroid.

## M17 - Delegation round: detection, quality, audit, JPEG, tracking

Six parallel subagents produced (all verified, written up in `notes/`):

- **`notes/detection.md`** (369 lines) - `FUN_140077560` decoded: seed scan (rows 1..H-6, runs >= 10),
  interval-overlap growth, the `x1 -= 1 / x2 -= 3` trims, and the rejection codes `-1` no planet,
  `-2` partial, `-3` diameter below minimum, each mapped to its log counter. Corrects the earlier
  assumption that the 16-bit path compares `threshold << 8 <= sample`: it projects BGR to 8-bit luma
  and tests a 4-tap horizontal sum against `4 * threshold`. 19/19 fixtures agree with the binary.
- **`notes/quality-loop.md`** (456 lines) - the score is
  `sqrt(sum_s w(s) * m(s)) * bright50/3251250` with `w(s) = (qmin*qmin)/(s*s)` in **integer**
  division, so only the `qmin` stride contributes (verified: qmax=qmin and qmax>qmin give identical
  scores). The first-frame 1.3255x boost is a **bug**: the mask buffer at `image+0xc48` is allocated
  with the non-clearing allocator and reused across frames, so stale marks from earlier strides
  dilute the mean. Reproduced to 0.006-0.2% (clean) and 0.2-0.8% (polluted).
- **`notes/switch-wiring.md`** (414 lines) - 57 wired (27 full, 30 partial), 68 parsed-only, 1 property
  with no consumer, 5 inert. Sharpest: `-chk` never rejects a frame, `-ms` is not wired at all,
  `-nlz`/`-debayer_pattern` are dead, and `docs/switch-matrix.csv` was stale (22 wrong statuses).
- **`JpegBaselineDecoder.cs`** (~1.2k lines) + 15 tests - baseline SOF0 with 4:4:4/4:2:2/4:2:0,
  restart markers, 16-bit DQT; cross-checked against libjpeg (mean error <= 0.15, max <= 3/255).
- **`SurfaceTracker.cs`** + 16 tests - zero-mean normalised cross-correlation with parabolic
  sub-pixel refinement, gain/pedestal invariant, returns null below 0.5 correlation.
- **`notes/centring.md`** (459 lines) - the rule implemented in M16.

## M18 - Quality and detection: exact parity (`d3aaa0a`)

**Quality (`EngineQuality`, stateful).** The score now matches the engine to six decimals. Three
corrections were needed beyond the first reading of the recovered loop:

1. the stencil neighbour is the row **above** in image coordinates (the engine's buffer is bottom-up);
2. the subsample grid and the reused mask are indexed bottom-up, which is what reproduces the
   cross-frame scribbles - the mask buffer is allocated non-clearing and reused, the bug that makes
   the first frame score 1.3255x higher;
3. the box finder returns last-good/first-good rows, not one past.

Verified: `saturn30 -q=10` quality log is byte-identical across all ten rows (1065.667424,
813.334568, 808.547235, ...), the four-identical-frames fixture reproduces 1065.667424 /
803.964973 x3, and `-chk -q=30` matches all 30 rows.

**Detection (`PlanetDetector`).** Implements `FUN_140077560`: 4-tap horizontal running sum against
`4 * threshold`, runs longer than 9 pixels, seed row then interval-overlap growth, bottom-up order,
8-bit and 16-bit luma paths. Returns `Found` / `NoObject` / `PartialObject` / `TooSmall`; `-chk` now
rejects and counts all three reasons and `-ms` is wired.

The engine's **default minimum size is 50**, not the GUI's 15 (bisected: a 49-pixel object is
rejected, a 50-pixel one accepted). Oracle parity: `-chk` keeps 30/30 with no discards, `-mp=200`
gives 30 "no planet", `-ms=999` gives 30 "diameter below minimum", and a synthetic partial fixture
reports exactly one partial.

**MJPEG input (`22bc802`).** `AviReader` recognises MJPG and decodes chunks with the baseline JPEG
decoder; a 3-frame 64x48 MJPG AVI matches ffmpeg to mean 0.011 / max 2 of 255.

**Switch matrix refreshed** to 131 rows from the audit: 28 verified, 30 executed, 68 parsed-only,
5 inert. The `-gif` row was corrected after GIF output was wired.

372 tests, 22 parity cases (18 synthetic + 4 real), all matching.
