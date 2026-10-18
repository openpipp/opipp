# OPIPP status


Snapshot of the reconstruction. Regenerate the numbers, do not trust memory: see
[`parity.md`](parity.md) for the harness and [`switch-matrix.csv`](switch-matrix.csv) for the switch
inventory.

## Build and tests

| Item | Value |
| --- | --- |
| SDK | .NET 10.0.402, local only (`~/.dotnet-sdk`) |
| Solution | `OPIPP.slnx` (note: `.slnx`, not `.sln`) |
| Build | `dotnet build OPIPP.slnx` - clean |
| Tests | **553 passing, 0 failing** (7 projects) |
| Managed code | `Opipp.Core` 11.8k lines, `Opipp.Cli` 0.3k, `Opipp.App` 14.5k, tests 5.5k |
| Reference artefacts | `decompiled/` 14,277 lines C#, `ghidra/out/` 4,229 functions |

## Parity

`dotnet run --project tools/Opipp.Parity` → **ALL CASES MATCH** (18 cases: 15 verified, 2 deliberate
divergences, 1 known gap; pixel content, directory layout and log text compared). Adding `--real`
runs four more cases against the 30-frame Saturn capture in `samples/` (git-ignored), which also all
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
| gif-out | `-gif` | *deliberate divergence* (original emits an image-less GIF; we write a valid animation) |
| *(with `--real`)* real-plain | `-tif` | 30 real frames |
| *(with `--real`)* real-stretch | `-tif -hs=204` | 30 real frames |
| *(with `--real`)* real-stretch-bp | `-tif -hs=204 -hs_bp` | 30 real frames |
| *(with `--real`)* real-centre | `-tif -ctr` | 30 real frames, pixel-identical |

## Switch coverage

`docs/switch-matrix.csv` is regenerated from the wiring audit in
[`../notes/switch-wiring.md`](../notes/switch-wiring.md), which traces all 131 case labels in
`CommandLineParser.Parse` through `PippOptions` and `Program.Map` into `PipelineSettings` and the
stage that reads each value.

| Wiring | Switches |
| --- | ---: |
| Wired, fully implemented | 27 |
| Wired, partial (conditional, collapsed value, or one output branch) | 30 |
| Parsed, no `PipelineSettings` property | 68 |
| Property exists, no consumer | 1 |
| Inert / alias | 5 |
| **Total** | **131** |

**57 of 131 switches are wired** (27 full, 30 partial), 68 are parsed-only, one sets a property no
stage reads (`-nlz` → `LeadingZeros`; the `-db=<pattern>` argument has the same defect), and five
are inert or aliases. The matrix grades wiring: `verified` = fully wired, `executed` = wired but
partial, `parsed-only` = no downstream property, `inert` = no effect (the one dead property plus the
five aliases, six rows). Oracle parity is the separate table above.

Sharpest gaps: `-chk` never rejects a frame, `-ms` is not wired at all, `-qtotal` and `-bin_average`
collapse to defaults, and six `PipelineSettings` properties have no producer (`Stack`, `OpippLayout`,
`WriteLogFile`, `OverexposedThreshold`, `AlignmentSearchRadius`, `EngineVersion`).

## Work in flight

Nothing in flight. Phase 4a item 1 (histogram stretch) is **done and verified**: the gain is measured
per frame from the mean of the 25 brightest samples, and all 30 frames of the real capture match the
original pixel for pixel at `-hs=204`, `-hs=204 -hs_bp`, and on the synthetic colour fixture with and
without `-hsrgb`. Next is the rest of Phase 4a: the quality metric's integer stride loop
(`notes/quality-loop.md`) and the unwired switches listed in `notes/switch-wiring.md`.

## Behaviour verified against the original

- `-hs` measures a white point per frame (mean of the 25 brightest samples, integer division) and
  applies `whitePoint * 255/100 / white`; `-hs_bp` subtracts the mean of the 25 darkest samples first.
  The old "constant gain xxxx/100" model only held on fixtures whose bright tail saturates at 255.
- `-hs=xx%` and `-hs=xxp` are the same setting as `-hs=(xx*255/100)`; `-hsrgb` selects per-channel
  gains, without it the smallest channel gain is used for all three.
- FITS is written **bottom-up** and every FITS is **flipped on read** (an original quirk).
- FITS keeps sample depth (`BITPIX=8` for 8-bit) and emits `DATAMIN`/`DATAMAX`.
- `-ctr` run-detects the object box and centres its origin: `xpos = left + (right−left)/2`,
  `ypos = top + (bottom−top)/2` in bottom-up rows, then `dx = W/2 − xpos`, `dy = ypos − H/2`.
  Offsets are whole-pixel translations and the vacated area takes the frame median. **30/30 frames
  of the real capture are pixel-identical** to the original.
- The engine requires a `DONE` line on stdin; EOF alone hangs it.
- Quality selection keeps the same frames as the original (numbering differs).
- Debayer `-db` alone enables interpolation; `-db=<n>` only selects the pattern. The pattern-only
  no-op matches; the interpolation itself is still ~1 LSB off (see below).

## Recently added

- **Baseline JPEG decoder** (`JpegBaselineDecoder`): SOF0 with 4:4:4, 4:2:2 and 4:2:0 sampling,
  restart markers and 16-bit DQT; cross-checked against libjpeg (mean error ≤ 0.15, max ≤ 3/255,
  15 tests). Not wired to an input path yet.
- **Surface tracker** (`SurfaceTracker`): the engine's own algorithm - 5x5 integer blur,
  percentile-stretched template, per-candidate brightness/contrast LUT, SSD over the even-even
  subsample, asymmetric `[-box/16, +box/16-1]` window. Wired to `-en_surface_feature_tracking`
  through `-en_ss`; 23/30 frames of the real Saturn capture are byte-identical, the rest off by one
  pixel (see `notes/tracking.md`).

## Known divergences (deliberate or open)

| Divergence | Reason / next step |
| --- | --- |
| 16-bit TIFF samples are byte-swapped by the original under an `II` header (unreadable in ffmpeg) | We write spec-correct files; replicating a data-corrupting bug is not acceptable |
| ~~Colour SER misread~~ | Fixed: PIPP uses ColorID **100 = RGB / 101 = BGR**, not 16/17. Writer now uses 100; reader still accepts the old ids |
| Debayer interpolation differs by 1 LSB on ~37% of interior pixels (up to 3 at edges) | Our `-db -db=2` result vs the original on a synthetic RGGB mosaic. The old parity case only ran `-db=2`, which the engine treats as pattern-only, so this was never exercised. Next: compare our interpolation kernel against `FUN_14009f4e0`/`FUN_14009e5f0`/`FUN_14009fc30` |
| Surface tracking: 23/30 frames byte-identical on the real Saturn fixture, 7 frames off by exactly 1 px | The engine's coarse stage is not reproduced faithfully (its reference capture and histogram scoring differ). Porting `FUN_1400a7ec0` and re-enabling the seed is the remaining work; see `notes/tracking.md` |
| GIF output | Implemented and wired (`GifWriter` plus the `.gif` branch in `Processor`). The original writes an image-less GIF (only a NETSCAPE extension, a comment and the trailer), so we deliberately emit a valid animation. The wiring audit predates this and still classes `-gif` inert |
| ~~Centring (`-ctr`) output differs~~ | Fixed: the engine centres the detected object box, not a weighted centroid (`notes/centring.md`). Whole-pixel translation with the vacated area filled by the frame median; 30/30 frames of the real capture are pixel-identical and the `real-centre` case matches |
| ~~Detection threshold uses Otsu~~ | Fixed: `FUN_14009ab40` averages 3x3 blocks into a 1/3-scale image, averages the five *smallest* row maxima, then `T = m + (meanAbove-m)/4`. Exact on real frames (18 vs 18); synthetic discs come out one unit low because the engine rounds inside its luma lookups |

## Environment notes

- `DOTNET_ROOT="$HOME/.dotnet-sdk"`, `PATH` must include it plus `$DOTNET_ROOT/tools`.
- Ghidra: `GHIDRA_INSTALL_DIR=C:\tools\ghidra_12.1.4_PUBLIC`, `JAVA_HOME=C:\Users\<you>\.jdks\jdk-21.0.12+8`.
- The oracle needs **argv for switches** and **stdin terminated by `DONE`**; stdin-only produces nothing.
- Synthetic fixtures must be accepted by the detector: flat dark background, object not touching an
  edge, and `-ms` relaxed (default 15) when the object is small.

## Provenance

`bin/`, `decompiled/` and `ghidra/` are reference copies of Chris Garry's PIPP and its output.
They are git-ignored and must never be published. The repository itself is GPLv3 (see `LICENSE` and
`NOTICE`); that covers this project's own code and artwork only, not PIPP. See
[`../notes/provenance.md`](../notes/provenance.md).

## Switch coverage (final)

131 switches: **76 verified against the original**, 37 executed (wired, not oracle-proven), 6 documented
skips and 12 inert (accepted by the engine with no observable effect). The six skips are `-raw_db`,
`-raw_hl`, `-raw_ocs`, `-raw_hpf` (need dcraw 9.27, which the engine statically links), `-aoi_reject`
(the discard rule could not be pinned down) and `-flat_bayer` (private master format).

## Deliberate divergences (current)

- 16-bit TIFF: original writes byte-swapped samples under an `II` header; we write spec-correct files.
- GIF: original emits an image-less file; we write a valid animation.
- Non-tracking `-aoi_crop`: original writes a quarter-filled out-of-bounds buffer; we clamp.
- **M25 - start-up paths**: the original aborts on a URI/colon argument during `Form_Load`; our port
  rejects the entry and reports it in the "could not be added" dialog.
