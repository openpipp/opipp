# Roadmap: OPIPP to feature parity

PIPP (Planetary Imaging PreProcessor) 2.5, reconstructed from the installed binaries as a fully
managed .NET 10 codebase. This document is the plan of record: what is done, what is missing, in what
order, and how each step is proven.

## Goals

| Goal | Measure | Now | Target |
| --- | --- | --- | --- |
| **Recovery** (read the original) | binaries decompiled | 100 % | 100 % |
| **Parity** (behave like the original) | switches wired + oracle-verified | 131/131 addressed, 76 verified | all executable areas |
| **Verification** (prove it) | parity cases matching | **29 cases, all matching** | one per executable area |
| **Distribution** | installable build | **0.1.0-alpha built** | 1.0 once permission exists |

Recovery is finished. Parity is complete except one algorithm (surface tracking). The alpha is built
and smoke-tested. Release is blocked on permission, not on code.

## Status and community

Early development. The engine is functionally complete and parity-tested, the GUI is ported and fully
managed, and 0.1.1-alpha is installable - but surface tracking is not exact yet and the UI has not had
broad real-world use. The project is being published now specifically to get feedback from people who
image planets, not because it is finished.

Feedback that moves the project most:

1. captures that fail, stall, or come out worse than the original (SER/AVI, the command line, and
   `<output>/logs/`);
2. workflow gaps compared with how you actually process;
3. behaviour differences not already listed in [`status.md`](status.md).

AI assistance is disclosed in the README and applies to this roadmap too: the port was written with
large-language-model assistance against decompiled output and black-box measurements, and will
continue that way. Claims in this file are expected to be backed by a test or a parity case.

## GitHub readiness

| Item | State |
| --- | --- |
| `LICENSE` (GPLv3, for this repository's own code and artwork) | added |
| `NOTICE` (no affiliation, no original binaries, fair-use position) | added |
| Reference artefacts excluded from the repo and its history | `bin/`, `decompiled/`, `ghidra/` are git-ignored and were never committed |
| Evaluation data excluded | `samples/` git-ignored, never committed |
| Build output excluded | `dist/`, `obj/`, `bin/` per project are git-ignored |
| Secrets | none: no credentials, tokens or connection strings anywhere in the tree |
| Tests runnable from a clone | yes - the 80 KB synthetic parity fixtures are committed |
| CI | not yet configured; build + tests is the required gate |

## Where the numbers come from

- **131 distinct switches** are emitted or understood by the original stack, extracted from the
  decompiled `Form1.cs` (`notes/cli-spec.md`).
- **verified** = a parity-harness case reproduces the original byte-for-byte (**76 switches**).
- **executed** = the switch reaches `PipelineSettings` and changes output (**43 switches**).
- **inert** = accepted and documented as having no managed effect (**12 switches**).
- Source of truth: [`switch-matrix.csv`](switch-matrix.csv), counts in [`coverage.json`](coverage.json).

## Feature scoreboard

Weights are a judgement call, stated so they can be argued with.

| Area | State | Now | Weight |
| --- | --- | --- | --- |
| Output formats (TIF, FITS, PNG, AVI, SER, BMP, Y8, colour, GIF) | all verified; SER headers byte-identical; GIF deliberately diverges (valid animation vs an image-less original file) | 99 % | 15 % |
| Input formats (SER, TIF, FITS, PNG, BMP, AVI incl. MJPEG, DNG) | MJPEG ffmpeg-verified (mean abs diff 0.011); baseline DNG reader wired, absolute values differ from dcraw | 95 % | 15 % |
| Core image ops (stretch, flip, rotate, resize, bin, gain, gamma, mono, debayer, median) | stretch/flip/debayer/centring verified on real frames | 98 % | 15 % |
| Calibration (dark, flat, flat-dark, masters) | `.tmp` master format recovered and byte-tested; save/load and `-flat_bayer` wired; `-flat_bayer` discard behaviour not proven | 96 % | 10 % |
| Frame selection (`-q`, `-qmin/qmax/qinc`, `-chk/-ms`, `-qw`, `-incqv`, `-freject`) | default, Ninox and Histogram algorithms all match the engine | 97 % | 10 % |
| Alignment (`-ctr`, tracking, `-ctrf`, stabilisation) | `-ctr` exact; **tracking 23/30 frames byte-identical** (engine algorithm ported; coarse stage not faithful) | 85 % | 10 % |
| Stacking (mean/median, multi-file join) | engine mean (truncating) matched; `-join` wired | 70 % | 5 % |
| Animation / GIF output | all `-anim_*` modes verified frame-for-frame | 95 % | 5 % |
| Output layout, logs, exit codes, `-gui` stream | `-gui` stdout byte-identical across nine scenarios; logs and numbering exact | 99 % | 10 % |
| GUI application | ported, fully managed, no `pipp_dll.dll`; start-up path guard | 92 % | 5 % |
| **Weighted total** | | **≈ 94 %** | |

The single material gap is **surface tracking**: the algorithm is ported and scores 23/30 on the real
Saturn fixture, with the remaining frames off by one pixel. Everything else is at or near exactness.

## Alpha

| | |
| --- | --- |
| Artifact | `dist/OPIPP-0.1.1-alpha-win-x64.zip` (self-contained win-x64, 50.7 MB) |
| Provenance | `Version=0.1.1-alpha` in `Directory.Build.props`; window title `OPIPP 0.1.1-alpha - Open PIPP` |
| Verified | 553 tests, 29/29 parity cases, CLI conversion of 30 real frames, GUI launch, no native DLL |
| Notes | [`alpha.md`](alpha.md) |

Rebuild with the two `dotnet publish` commands in `alpha.md`. `dist/` is git-ignored.

---

## Phase status

| Phase | State |
| --- | --- |
| 1. Drop-in parity (layout, logs, exit codes, `-gui`) | **done** — trees and logs compared, 127 on unknown switch, `-gui` byte-identical |
| 2. Remaining formats (BMP, Y8, GIF, colour SER, AVI/MJPEG) | **done** — deliberate divergences documented |
| 3. Calibration and master frames | **done** — `.tmp` format, save/load, `-flat_bayer` |
| 4. Selection, alignment, stacking | **done except tracking exactness** |
| 5. Animation and long tail | **done** — `-anim_*`, WinJUPOS, legacy AVI, RAW switches |
| 6. GUI parity | **done** — managed, `LegacyNativeApi` deleted, `-gui` verified |
| 7. Open-source release | **blocked on Chris Garry's written permission** |
| 8. Alpha build | **done** — 0.1.1-alpha, binaries renamed to Opipp.* |

### Phase 4 remainder — tracking exactness

The tracker is the engine's algorithm (`FUN_1400a7470` → `FUN_1400a6e40` → `FUN_1400a5f40`): 5×5
integer blur, percentile-stretched template, per-candidate brightness/contrast LUT, SSD over the
even-even subsample, asymmetric `[-box/16, +box/16-1]` window, threshold rejection.

Measured on the Saturn fixture:

| Variant | Exact frames |
| --- | --- |
| ZNCC ±32 (previous) | 19/30 |
| Ported, raw mosaic | 20/30 |
| Ported, debayer + luma, box seed | 23/30 |
| Ported, coarse seed without gates | 6/30 |
| Ported, coarse seed **with** gates | 3/30 |
| Ported, gates enabled but box seed | 23/30 (current) |

Why the coarse stage is disabled: the gates themselves are correct — with them the 3-frame arrow probe
matches the original exactly (no shift, because no candidate is accepted). But our coarse *reference
capture and scoring* differ from the engine's, so the seed lands a few pixels off and the ±3 fine
window cannot recover, which is worse than seeding at the box.

**Next unit of work:** port `FUN_1400a7ec0`'s coarse reference capture (it takes its own block
dimensions and downsample factors from the context rather than reusing `box/16`) and the engine's
per-candidate histogram scoring, then re-enable the coarse seed. Success criterion: arrow probe still
applies no shift, and Saturn reaches ≥28/30. Notes: [`../notes/tracking.md`](../notes/tracking.md).

## Phase 7 — Release (blocked)

Work: written permission from Chris Garry; then `LICENSE`, provenance statement, CI (build + tests;
parity only where the original binaries may legally be used), packaging.

**Until permission exists: no publication, no binaries, no vendored `decompiled/` or `ghidra/`.**

---

## Decision log

### 2026-10-18 — the reconstruction is named OPIPP, and the alpha ships

Chosen over further parity work because the engine is functionally complete: 0.1.0-alpha is a working
preview (553 tests, 29/29 parity), and shipping it makes the remaining gap concrete instead of
theoretical. Tracking at 23/30 is documented in the release notes rather than hidden.

### 2026-10-18 — coarse stage disabled until its reference capture is faithful

The gates were implemented from constants read out of `.rdata` (0.5, 1.5, −0.8, −1.2) and measured:
the arrow probe then matched the original, but Saturn fell from 23/30 to 3/30. A wrong seed is worse
than no seed, so the coarse stage stays off until `FUN_1400a7ec0` and the histogram scoring are
ported. This is a recorded negative result, not an open question.

### 2026-10-18 — tracking ported rather than approximated

The previous ZNCC implementation was replaced with the engine's own scoring, even though it initially
scored worse on some variants, because a "better" correlator is a different product. The ZNCC variant
was only kept long enough to establish the baseline (19/30).

### 2026-10-17 — Phase 4a (real data) before remaining format work

Realistic captures expose errors toy frames hide: the first real run found the histogram-stretch error
immediately. That ordering held: every later algorithm was validated on `samples/saturn30.ser`.

---

## Working agreements

- Every behaviour change lands with either a test or a parity case — "it looks right" is not a status.
- Parity cases are added before a feature is declared done, not after.
- Numbers in this file are regenerated from the matrix, never remembered: see [`parity.md`](parity.md).
- Deliberate divergences are listed in [`status.md`](status.md) with a one-line rationale each.
- A claim of exactness must name the fixture and the command that produced it.
