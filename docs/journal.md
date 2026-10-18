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
