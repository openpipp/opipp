# Status

Snapshot of the reconstruction. Regenerate the numbers, do not trust memory: see
[`parity.md`](parity.md) for the harness and [`switch-matrix.csv`](switch-matrix.csv) for the switch
inventory.

## Build and tests

| Item | Value |
| --- | --- |
| SDK | .NET 10.0.402, local only (`~/.dotnet-sdk`) |
| Solution | `PIPP.slnx` (note: `.slnx`, not `.sln`) |
| Build | `dotnet build PIPP.slnx` - clean |
| Tests | **291 passing, 0 failing** (7 projects) |
| Managed code | `Pipp.Core` 11.5k lines, `Pipp.Cli` 0.3k, `Pipp.App` 14.5k, tests 5.5k |
| Reference artefacts | `decompiled/` 14,277 lines C#, `ghidra/out/` 4,229 functions |

## Parity

`dotnet run --project tools/Pipp.Parity` → **ALL CASES MATCH** (15 cases: 13 verified, 1 deliberate
divergence, 1 known gap; pixel content, directory layout and log text compared).

| Case | Switches | Compared |
| --- | --- | --- |
| plain-tif | `-tif` | pixels |
| stretch | `-tif -hs=204` | pixels |
| flip | `-tif -flip_x` | pixels |
| fits-out | `-fits` | pixels |
| png-out | `-png` | pixels |
| avi-out | `-avi` | frames via container reader |
| debayer | `-tif -db=2 -db_type=0` | pixels |
| centre | `-tif -ctr -ms=5` | pixels |
| quality | `-tif -q=2` | frame multiset |
| bmp-out | `-bmp` | pixels, layout, log |
| y8-out | `-avi -y8` | AVI frames |
| tif-16bit | `-tif` (16-bit input) | *deliberate divergence* |
| tif-colour, png-colour | `-tif`, `-png` (RGB input) | pixels, layout, log |
| gif-out | `-gif` | *known gap* |

## Work in flight

`-hs` (histogram stretch) matches on synthetic fixtures but **not on real data**: the original's gain
is image-dependent (2.30 vs our 2.04 at `-hs=204` on the Saturn capture). The user chose exact parity
(option A). See [`HANDOFF.md`](HANDOFF.md) for the trace target and next action.

## Behaviour verified against the original

- `-hs=xxxx` is a **gain of xxxx/100** with a one-unit bias: `trunc((v*xxxx − 1)/100)`.
- FITS is written **bottom-up** and every FITS is **flipped on read** (an original quirk).
- FITS keeps sample depth (`BITPIX=8` for 8-bit) and emits `DATAMIN`/`DATAMAX`.
- `-ctr` targets `(w/2, h−1−h/2)` with integer shifts; edge rows repeat.
- The engine requires a `DONE` line on stdin; EOF alone hangs it.
- Quality selection keeps the same frames as the original (numbering differs).
- Debayer `-db=2` (RGGB) matches on a synthetic mosaic.

## Known divergences (deliberate or open)

| Divergence | Reason / next step |
| --- | --- |
| `-gui` token stream differs (`@PFILELAB` 0- vs 1-based, `@ANIM`/`@QSORT`/`@FNAMELAB`, ordering) | Phase 6, blocks dropping the native fallback |
| Quality log scores differ (structure matches) | Phase 4, quality algorithm not parity-proven |
| 16-bit TIFF samples are byte-swapped by the original under an `II` header (unreadable in ffmpeg) | We write spec-correct files; replicating a data-corrupting bug is not acceptable |
| ~~Colour SER misread~~ | Fixed: PIPP uses ColorID **100 = RGB / 101 = BGR**, not 16/17. Writer now uses 100; reader still accepts the old ids |
| GIF output | Not implemented: needs LZW + palette quantiser |
| Detection threshold uses Otsu, not the engine's "Calculated minpixel value" | Phase 4 |
| GIF, RAW/dcraw, animation, AOI, master saving, quality weighting | Parsed, not executed — Phases 2–5 |
| `Pipp.App` still calls the native API for `Test Options` and calibration | Phase 6 |

## Environment notes

- `DOTNET_ROOT="$HOME/.dotnet-sdk"`, `PATH` must include it plus `$DOTNET_ROOT/tools`.
- Ghidra: `GHIDRA_INSTALL_DIR=C:\tools\ghidra_12.1.4_PUBLIC`, `JAVA_HOME=C:\Users\ilanp\.jdks\jdk-21.0.12+8`.
- The oracle needs **argv for switches** and **stdin terminated by `DONE`**; stdin-only produces nothing.
- Synthetic fixtures must be accepted by the detector: flat dark background, object not touching an
  edge, and `-ms` relaxed (default 15) when the object is small.

## Provenance

`bin/`, `decompiled/` and `ghidra/` are reference copies of Chris Garry's PIPP and its output.
They are git-ignored and must never be published. No `LICENSE` file exists on purpose until the
author's permission is obtained. See [`../notes/provenance.md`](../notes/provenance.md).
