# PIPP — decompiled, reconstructed, modernized

A reverse-engineered reconstruction of **PIPP (Planetary Imaging PreProcessor)** by Chris Garry,
rebuilt as a managed **.NET 10 / C# 13** codebase.

> **Provenance:** PIPP is closed-source freeware. This tree is a reconstruction for study and
> modernization. Do **not** publish or distribute it without the original author's permission —
> see [provenance](notes/provenance.md).

## What the original is

| Artifact | Role |
|---|---|
| `PIPP.exe` | WinForms front-end: builds a command line, spawns the engine, parses progress |
| `pipp_core.exe` | the engine: decodes AVI/SER/FITS/images/RAW, debayers, calibrates, aligns, scores, stacks, writes |
| `pipp_dll.dll` | probe + calibration-master stacking helper (statically links libpng) |
| `av*.dll`, `sw*.dll` | FFmpeg 3.x |

The GUI is not the product — the switch vocabulary is. All 152 argument emissions the GUI produces
are documented in [notes/cli-spec.md](notes/cli-spec.md).

## Layout

```
src/Pipp.Core   managed engine: formats, processing, quality, alignment, stacking, pipeline
src/Pipp.Cli    drop-in replacement for pipp_core.exe
src/Pipp.App    .NET 10 WinForms front-end ported from the decompiled UI
tests/          xUnit suites per subsystem + end-to-end pipeline tests
tools/          parity harness against the original engine + fixture generators
docs/           roadmap to feature parity, current status, parity guide, switch matrix
notes/          research trail: CLI spec, algorithms, engine behaviour, provenance
bin/            original binaries, copied read-only (reference only, git-ignored)
decompiled/     ILSpy project for PIPP.exe (14,277 lines of C#, reference only, git-ignored)
ghidra/         4,229 decompiled native functions + CSV index + string-xref tooling (git-ignored)
```

Progress and what comes next: [docs/roadmap.md](docs/roadmap.md) · current numbers:
[docs/status.md](docs/status.md) · switch-by-switch state: [docs/switch-matrix.csv](docs/switch-matrix.csv).


## Build and run

Requires the .NET 10 SDK (`dotnet --version` ≥ 10.0).

```bash
dotnet build PIPP.slnx
dotnet test PIPP.slnx

# headless engine (same switches as pipp_core.exe)
dotnet run --project src/Pipp.Cli -- -help
dotnet run --project src/Pipp.Cli -- -tif -q=50 -od=out capture.ser

# WinForms front-end
dotnet run --project src/Pipp.App
```

## Status

Working end to end (verified by tests): SER → pipeline → FITS/TIFF/SER output, calibration
subtract/divide, histogram stretch, quality selection, stacking. **289 tests pass** across seven
projects (formats 127, processing 80, CLI 77, pipeline 7).

The CLI is a drop-in engine: it accepts the GUI's stdin protocol (switches and file paths, one per
line) as well as a normal command line, and emits the original `@PROGRESS/@PFILE/...` tokens with
`-gui`. `Pipp.App` now prefers `Pipp.Cli.exe` via `EngineLocator` and only falls back to the legacy
`pipp_core.exe` when the managed engine is absent.

| Area | State |
|---|---|
| SER v3 read/write | complete |
| FITS read/write | complete (16-bit, Bayer keywords, BZERO) |
| AVI read/write | uncompressed DIB/Y800 only; compressed codecs rejected with a clear error |
| BMP / TIFF / PNG / PGM | complete; BMP writes 8/24bpp only (PIPP's reader rejects 16bpp), so 16-bit frames are downshifted and documented |
| Debayer (bilinear, smooth hue) | complete; pattern indices match the engine |
| Binning, median, mono modes, stretch, gain/gamma, flip/rotate/crop/resize | complete |
| Dark/flat calibration | complete (master frames via PGM handoff) |
| Quality sort/limit | complete contract; score formulas are reconstructions |
| Alignment | centroid + NCC translation + AFB box |
| Stacking | mean / sum / median |
| CLI | full 152-switch parse/build; executed subset listed in notes/algorithms.md |
| GIF animation, RAW/dcraw, planet detection, AOI, WinJUPOS naming, `-join`, `-qw` | parsed, not executed |
| WinForms UI | builds on .NET 10; drives the managed CLI through the original stdin/stdout protocol (legacy binary as fallback) and builds master darks/flats in-process when `pipp_dll.dll` is absent |

### Parity against the original engine

`tools/Pipp.Parity` runs `bin/pipp_core.exe` and `Pipp.Cli.exe` on the same synthetic capture with the
same switches and compares the produced frames by SHA-256 of their sample data:

```
dotnet run --project tools/Pipp.Parity
```

Current result: **9 of 9 cases match** — plain TIFF, `-hs=204` stretch, `-flip_x`, `-fits`, `-png`,
`-avi`, `-db=2` debayer, `-ctr` centring and `-q=2` quality selection all reproduce the original's
frame content (quality selection is compared as a multiset because it may reorder frames).

Two deliberate differences remain: this engine writes flat numbered files (`capture_00000.tif`) where
the original creates a per-input subdirectory (`capture/1.tif`) plus a `logs/` directory, and it
writes colour SER where the original's help text claims mono only.

Details of what is observed versus reconstructed: [notes/algorithms.md](notes/algorithms.md).

## Documentation

- [notes/00-plan.md](notes/00-plan.md) — scope, architecture, modernization decisions
- [notes/engine-behaviour.md](notes/engine-behaviour.md) — pipeline order, constants, stdout protocol
- [notes/cli-spec.md](notes/cli-spec.md) — every switch with the GUI guard that emits it
- [notes/algorithms.md](notes/algorithms.md) — evidence per algorithm
- [notes/provenance.md](notes/provenance.md) — licensing status
