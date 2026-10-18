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
| Output formats (TIF, FITS, PNG, AVI, SER, BMP) | executed, TIF/FITS/PNG/AVI verified | 75 % | 15 % |
| Input formats (SER, TIF, FITS, PNG, BMP, AVI) | executed; no GIF/RAW/dcraw/WinJUPOS | 65 % | 15 % |
| Core image ops (stretch, flip, rotate, resize, bin, gain, gamma, mono, debayer, median) | executed, stretch/flip/debayer verified | 85 % | 15 % |
| Calibration (dark, flat, flat-dark, masters) | subtraction wired; master save absent | 55 % | 10 % |
| Frame selection (-q, -qm, -qw, -qmin/qmax/qinc, -incqv, -freject) | -q verified, rest parsed-only | 40 % | 10 % |
| Alignment (-ctr, -st, -ctrf, stabilisation) | -ctr verified; rest partial/absent | 50 % | 10 % |
| Stacking (mean/median, multi-file join) | stacker exists, not wired to CLI | 35 % | 5 % |
| Animation / GIF output | absent | 0 % | 5 % |
| Output layout, logs, exit codes, progress protocol | protocol good, layout/log differ | 60 % | 10 % |
| GUI application | ported, some paths still native | 80 % | 5 % |
| **Weighted total** | | **≈ 60 %** | |

The weighted total is deliberately ahead of the raw switch count: the switches still inert are
mostly parameters of one missing feature each (GIF, RAW, animation), not separate capabilities.

---

## Phase 1 — Drop-in parity for the current surface

**Goal:** a run produces the same files, in the same places, with the same log text.

Work:
1. Output layout: `<out>/<basename>/<n>.<ext>` instead of flat `capture_00000.tif`; `n` starts at 1
   (0 when quality selection runs — confirm on more cases). Honour `-keepimgname`.
2. `logs/<basename>_pipp_log.txt` with the original's sections: command line, source files,
   processing details, summary counts (`no planet detected`, `diameter below minimum`, …).
3. Exit codes and stderr text for bad switches (the original exits 127 on an unknown switch).
4. Progress/`@` token ordering under `-gui`, verified against a recorded GUI run.

Exit criteria: a parity case compares **directory trees and log text** (not just pixels) and matches;
pixel cases still 9/9.

Risk: layout changes break existing tests — do it behind the same settings object, update tests in
the same commit.

## Phase 2 — Remaining formats

Work: GIF animation writer (neuron quantiser, tolerance/lossy switches), Y8/TrueY AVI, BMP verify,
16-bit and colour fixtures across every writer, AVI input with multiple codecs, SER colour.

Exit criteria: format matrix (writer × depth × channels) plus oracle cases for `-bmp`, `-y8`, `-gif`.

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

Work: replace the remaining `LegacyNativeApi` call sites (`Test Options`, calibration) with managed
code; drop the `EngineLocator` fallback so the app always uses `Pipp.Cli`; golden test that
`CommandLineBuilder` emits the same argument list as the decompiled `Form1` for a matrix of settings.

Exit criteria: a full job runs from the GUI with no native DLL present; golden command-line test green.

## Phase 7 — Open-source release (blocked on permission)

Work: written permission from Chris Garry (author of PIPP); then `LICENSE`, provenance statement,
removal of `decompiled/` and `ghidra/` from history (they must never be committed), CI
(build + tests; parity only where the original binaries may legally be used), packaging
(self-contained win-x64 + portable zip).

Exit criteria: repository publishable without any reversed artefacts; CI green from a clean clone.

**Until permission exists: no publication, no binaries, no vendored decompiled output.**

---

## Working agreements

- Every behaviour change lands with either a test or a parity case — "it looks right" is not a status.
- Parity cases are added before a feature is declared done, not after.
- Numbers in this file are regenerated, never remembered: see `docs/parity.md`.
- Deliberate divergences are listed in `docs/status.md` with a one-line rationale each.
