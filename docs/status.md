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
| Tests | **295 passing, 0 failing** (7 projects) |
| Managed code | `Pipp.Core` 11.8k lines, `Pipp.Cli` 0.3k, `Pipp.App` 14.5k, tests 5.5k |
| Reference artefacts | `decompiled/` 14,277 lines C#, `ghidra/out/` 4,229 functions |

## Parity

`dotnet run --project tools/Pipp.Parity` → **ALL CASES MATCH** (18 cases: 15 verified, 1 deliberate
divergence, 2 known gaps; pixel content, directory layout and log text compared). Adding `--real`
runs three more cases against the 30-frame Saturn capture in `samples/` (git-ignored), which also all
match.

| Case | Switches | Compared |
| --- | --- | --- |
| plain-tif | `-tif` | pixels |
| stretch | `-tif -hs=204` | pixels |
| flip | `-tif -flip_x` | pixels |
| fits-out | `-fits` | pixels |
| png-out | `-png` | pixels |
| avi-out | `-avi` | frames via container reader |
| db-pattern-only | `-tif -db=2 -db_type=0` | pixels (the pattern-only **no-op**) |
| debayer-interpolated | `-tif -db -db=2 -db_type=0` | *known gap* |
| stretch-colour | `-tif -hs=204` (RGB input) | pixels: smallest channel gain |
| stretch-colour-rgb | `-tif -hs=204 -hsrgb` (RGB input) | pixels: per-channel gains |
| centre | `-tif -ctr -ms=5` | pixels |
| quality | `-tif -q=2` | frame multiset |
| bmp-out | `-bmp` | pixels, layout, log |
| y8-out | `-avi -y8` | AVI frames |
| tif-16bit | `-tif` (16-bit input) | *deliberate divergence* |
| tif-colour, png-colour | `-tif`, `-png` (RGB input) | pixels, layout, log |
| gif-out | `-gif` | *known gap* |
| *(with `--real`)* real-plain | `-tif` | 30 real frames |
| *(with `--real`)* real-stretch | `-tif -hs=204` | 30 real frames |
| *(with `--real`)* real-stretch-bp | `-tif -hs=204 -hs_bp` | 30 real frames |

## Work in flight

Nothing in flight. Phase 4a item 1 (histogram stretch) is **done and verified**: the gain is measured
per frame from the mean of the 25 brightest samples, and all 30 frames of the real capture match the
original pixel for pixel at `-hs=204`, `-hs=204 -hs_bp`, and on the synthetic colour fixture with and
without `-hsrgb`. Next is the rest of Phase 4a: the engine's "Calculated minpixel value" (we use Otsu)
and therefore centring on real data.

## Behaviour verified against the original

- `-hs` measures a white point per frame (mean of the 25 brightest samples, integer division) and
  applies `whitePoint * 255/100 / white`; `-hs_bp` subtracts the mean of the 25 darkest samples first.
  The old "constant gain xxxx/100" model only held on fixtures whose bright tail saturates at 255.
- `-hs=xx%` and `-hs=xxp` are the same setting as `-hs=(xx*255/100)`; `-hsrgb` selects per-channel
  gains, without it the smallest channel gain is used for all three.
- FITS is written **bottom-up** and every FITS is **flipped on read** (an original quirk).
- FITS keeps sample depth (`BITPIX=8` for 8-bit) and emits `DATAMIN`/`DATAMAX`.
- `-ctr` targets `(w/2, h−1−h/2)` with integer shifts; edge rows repeat.
- The engine requires a `DONE` line on stdin; EOF alone hangs it.
- Quality selection keeps the same frames as the original (numbering differs).
- Debayer `-db` alone enables interpolation; `-db=<n>` only selects the pattern. The pattern-only
  no-op matches; the interpolation itself is still ~1 LSB off (see below).

## Known divergences (deliberate or open)

| Divergence | Reason / next step |
| --- | --- |
| `-gui` token stream differs (`@PFILELAB` 0- vs 1-based, `@ANIM`/`@QSORT`/`@FNAMELAB`, ordering) | Phase 6, blocks dropping the native fallback |
| Quality log scores differ (structure matches) | Phase 4, quality algorithm not parity-proven |
| 16-bit TIFF samples are byte-swapped by the original under an `II` header (unreadable in ffmpeg) | We write spec-correct files; replicating a data-corrupting bug is not acceptable |
| ~~Colour SER misread~~ | Fixed: PIPP uses ColorID **100 = RGB / 101 = BGR**, not 16/17. Writer now uses 100; reader still accepts the old ids |
| Debayer interpolation differs by 1 LSB on ~37% of interior pixels (up to 3 at edges) | Our `-db -db=2` result vs the original on a synthetic RGGB mosaic. The old parity case only ran `-db=2`, which the engine treats as pattern-only, so this was never exercised. Next: compare our interpolation kernel against `FUN_14009f4e0`/`FUN_14009e5f0`/`FUN_14009fc30` |
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
