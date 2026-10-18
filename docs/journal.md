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
