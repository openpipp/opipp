# Roadmap: PIPP-RE to feature parity

PIPP (Planetary Imaging PreProcessor) 2.5, reconstructed from the installed binaries.
This document is the plan of record: what is done, what is missing, in what order, and how we
know each step is finished.

Two different goals are tracked separately, because they finish at different times:

| Goal | Measure | Now | Target |
| --- | --- | --- | --- |
| **Recovery** (read the original) | binaries decompiled | 100 % | 100 % |
| **Parity** (behave like the original) | switches executed + oracle-verified | ~32 % | 100 % |
| **Verification** (prove it) | parity cases matching | 9 cases / 11 switches | all executable areas |

Recovery is finished. Everything left is porting and proof.

## Where the numbers come from

- **126 distinct switches** are emitted or understood by the original stack, extracted from the
  decompiled `Form1.cs` (`notes/cli-spec.md`). The engine itself adds more (`-temp_dir`, `-gui`,
  `-test_data`, …).
- **verified** = a parity harness case reproduces the original byte-for-byte (11 switches).
- **executed** = the switch reaches `PipelineSettings` and changes output, but has no oracle case yet.
- **parsed-only** = accepted on the command line, no effect (37+ switches: GIF, animation, RAW,
  AOI, master-saving, quality weighting, hot-pixel, WinJUPOS).
- Last computed matrix: [`switch-matrix.csv`](switch-matrix.csv), counts in [`coverage.json`](coverage.json).

## Feature scoreboard

Weights are a judgement call, stated so they can be argued with.

| Area | State | Now | Weight |
| --- | --- | --- | --- |
| Output formats (TIF, FITS, PNG, AVI, SER, BMP, Y8, colour) | all but GIF verified against the original | 92 % | 15 % |
| Input formats (SER, TIF, FITS, PNG, BMP, AVI) | executed; no GIF/RAW/dcraw/WinJUPOS | 65 % | 15 % |
| Core image ops (stretch, flip, rotate, resize, bin, gain, gamma, mono, debayer, median) | executed, stretch/flip/debayer verified | 85 % | 15 % |
| Calibration (dark, flat, flat-dark, masters) | subtraction wired; master save absent | 55 % | 10 % |
| Frame selection (-q, -qm, -qw, -qmin/qmax/qinc, -incqv, -freject) | -q verified, rest parsed-only | 40 % | 10 % |
| Alignment (-ctr, -st, -ctrf, stabilisation) | -ctr verified; rest partial/absent | 50 % | 10 % |
| Stacking (mean/median, multi-file join) | stacker exists, not wired to CLI | 35 % | 5 % |
| Animation / GIF output | absent | 0 % | 5 % |
| Output layout, logs, exit codes | layout, numbering, log and quality-log structure verified | 95 % | 10 % |
| GUI application | ported, some paths still native | 80 % | 5 % |
| **Weighted total** | | **≈ 60 %** | |

The weighted total is deliberately ahead of the raw switch count: the switches still inert are
mostly parameters of one missing feature each (GIF, RAW, animation), not separate capabilities.

---

## Phase 1 — Drop-in parity for the current surface — **DONE except item 4**

Status: items 1–3 are implemented and verified. Output layout, numbering, padding, `-keepimgname`
and both log files now match the original byte for byte on all nine parity cases, and an unknown
switch exits 127. Item 4 (the `-gui` token stream) turned out to be a GUI-integration problem and
moved to Phase 6; the observed sequence is recorded in `notes/engine-behaviour.md`.

Work:
1. Output layout: `<out>/<basename>/<n>.<ext>` instead of flat `capture_00000.tif`; `n` starts at 1
   (0 when quality selection runs — confirm on more cases). Honour `-keepimgname`.
2. `logs/<basename>_pipp_log.txt` with the original's sections: command line, source files,
   processing details, summary counts (`no planet detected`, `diameter below minimum`, …).
3. Exit codes and stderr text for bad switches (the original exits 127 on an unknown switch).
4. Progress/`@` token ordering under `-gui`, verified against a recorded GUI run.

Exit criteria: met - every case now compares directory trees and log text as well as pixels, and all
nine match. The `-gui` item is tracked in Phase 6.

Risk: layout changes break existing tests — do it behind the same settings object, update tests in
the same commit.

## Phase 2 — Remaining formats — **partially done**

Done and verified: **BMP** (`-bmp` matches pixels, layout and log) and **Y8** (`-avi -y8` matches; the
switch is a no-op on the default mono AVI, which is already Y800). Fixtures for 16-bit and RGB input
now exist.

Outstanding:
1. **GIF writer** — needs LZW compression plus the palette quantiser; nothing exists yet.
2. ~~Colour SER interop~~ — **done**: the original uses ColorID 100 (RGB) / 101 (BGR), not 16/17.
   Our writer was non-conformant; it now writes 100 and both colour cases (TIFF and PNG) match.
3. **AVI input with multiple codecs** and a writer × depth × channels matrix.

Recorded decision: the original writes 16-bit TIFF samples byte-swapped under an `II` header, which
ffmpeg also cannot read. We write spec-correct files and treat this as a deliberate divergence.

Decision needed: RAW/dcraw support. It is a whole decoder dependency and cannot ship as managed-only
without a large port; propose deferring to Phase 6 or declaring it out of scope.

## Phase 3 — Calibration and master frames

Work: wire `-save_dark`, `-save_flat`, `-save_flat_dark` to `MasterFrameBuilder`; Bayer-aware
masters (`-dark_bayer`, `-flat_bayer`); flat-dark chain; verify master output numerically.

Exit criteria: master frames produced by both engines match within 0 LSB on a 20-frame fixture;
subtraction results verified.

## Phase 4 — Selection, alignment, stacking

Work:
1. Reverse-engineer the engine's "Calculated minpixel value" rule (currently Otsu) — the remaining
   blind spot in centring and detection.
2. `-chk` planet detection with rejection reasons matching the log.
3. Quality weighting (`-qw`, `-incqv`), search bounds (`-qmin/-qmax/-qinc`), `-freject`.
4. Surface tracking (`-st`), `-ctrf`, stabilisation (`-en_ss`).
5. Wire `-meanmed`/stacking and `-join` into the CLI.

Exit criteria: on a fixture with deliberately varied quality, the kept-frame set **and order** match;
stacked output matches; tracking offsets match frame by frame.

## Phase 5 — Animation and long tail

Work: `-anim_*` (forward/reverse/repeat), `-sdp`, `-pb`, `-raw_*`, `-wjpos_*`, `-avi_old*`,
`-hot_pixel`, `-no_ram_buff`, `-auto_fps`.

Exit criteria: every switch in `switch-matrix.csv` is `executed` or explicitly documented as
out of scope with a reason in `docs/status.md`.

## Phase 6 — GUI parity

Work:
1. Match the `-gui` stdout token stream exactly (`@PFILELAB` is 0-based, `@FNAMELAB` transitions from
   `none` to the file name, `@ANIM`/`@QSORT` first, text lines interleaved) - see
   `notes/engine-behaviour.md` for the observed sequence.
2. Replace the remaining `LegacyNativeApi` call sites (`Test Options`, calibration) with managed code.
3. Drop the `EngineLocator` fallback so the app always uses `Pipp.Cli`.
4. Golden test that `CommandLineBuilder` emits the same argument list as the decompiled `Form1`.

Exit criteria: a full job runs from the GUI with no native DLL present; golden command-line test green.

## Phase 7 — Open-source release (blocked on permission)

Work: written permission from Chris Garry (author of PIPP); then `LICENSE`, provenance statement,
removal of `decompiled/` and `ghidra/` from history (they must never be committed), CI
(build + tests; parity only where the original binaries may legally be used), packaging
(self-contained win-x64 + portable zip).

Exit criteria: repository publishable without any reversed artefacts; CI green from a clean clone.

**Until permission exists: no publication, no binaries, no vendored decompiled output.**

---

## Decision log

**Next step: Phase 4a — real-data parity, then the detection threshold.** Chosen over GIF and over
the remaining format work, for three reasons:

1. **Every verification so far runs on toy frames** (32x24 discs on flat fields). Nothing proves the
   engine agrees with the original on a realistic capture: 640x480, sensor noise, gradients, a
   partially visible or off-centre planet, hundreds of frames. A tool that matches on synthetic data
   and drifts on real data is worse than one with a missing feature.
2. **The detection threshold is the largest unproven behaviour.** Centring, `-chk` rejection, quality
   ranking and the `-ctr` target all depend on the engine's "Calculated minpixel value"; we use Otsu.
   The original prints that value into its log, so it can be characterised black-box across many
   fixtures - no decompiler needed - and only then matched.
3. **GIF is contained and visible, not risky.** It needs a new encoder (LZW + palette), but nothing
   else in the engine depends on it, and its output is quantised so byte parity was never the target.

Order: (a) realistic fixture and rerun all 12 cases, fixing what breaks; (b) characterise the
threshold from logged values, implement, verify on the same fixtures; (c) return to GIF, then AVI
input codecs.

Quick win to take alongside: Phase 3's master saving (`-save_dark`, `-save_flat`) is a small wiring
job against `MasterFrameBuilder` and unlocks an everyday workflow.

---

## Working agreements

- Every behaviour change lands with either a test or a parity case — "it looks right" is not a status.
- Parity cases are added before a feature is declared done, not after.
- Numbers in this file are regenerated, never remembered: see `docs/parity.md`.
- Deliberate divergences are listed in `docs/status.md` with a one-line rationale each.
