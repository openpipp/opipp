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
