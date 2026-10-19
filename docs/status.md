# OPIPP status

Current state of the reconstruction. Regenerate the numbers, do not trust memory: the commands below
produce every figure on this page.

| | |
|---|---|
| Version | `0.1.5-alpha` at the tagged commit |
| SDK | .NET 10.0.402 (local `~/.dotnet-sdk`) |
| Tests | **615 passing, 0 failing, 0 skipped** across eight projects |
| Parity | **27 compared, 27 matched, 2 deliberate differences, 0 gaps, 0 failed** |
| Release | `v0.1.5-alpha`, Windows x64 zip, self-contained |
| History | [`journal.md`](journal.md) — dated findings, superseded details |

```bash
export DOTNET_ROOT="$HOME/.dotnet-sdk"; export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet test OPIPP.slnx
dotnet run --project tools/Opipp.Parity -- --real
```

## What is verified

- SER, FITS, AVI (uncompressed), TIFF, PNG, BMP, PGM, DNG read/write, and GIF write.
- Debayer, binning, median and mono modes, histogram stretch, gain/gamma, flip/rotate/crop/resize.
- Dark/flat calibration, applied to raw frames before debayering and binning; a master that does not
  match the capture geometry is a controlled error, not a crash or a silently empty result.
- Quality sort/limit, centroid + NCC alignment, AFB box, mean/sum/median stacking.
- Animation: forward, reverse, holds and repetitions, for AVI, GIF and still sequences.
- The CLI is a drop-in engine: normal command line and the GUI's stdin protocol, with the original
  `@PROGRESS/@PFILE/...` tokens under `-gui`.

## Parity

`tools/Opipp.Parity` runs `bin/pipp_core.exe` and `Opipp.Cli.exe` on the same capture with the same
switches and compares sample data by SHA-256, plus directory layout and log text. `--real` adds the
30-frame Saturn capture from `samples/` (git-ignored). See [`parity.md`](parity.md).

The summary separates four outcomes, and only real mismatches fail the run:

- **matched** — compared and byte-identical;
- **expected difference** — declared, with a reason (16-bit TIFF, GIF);
- **gap** — not compared yet; currently none;
- **failed** — compared and different.

`0 gaps` describes the registered cases, not every feature: see the gaps below.

## Switch coverage

131 switch labels. **76 verified against the original**, 37 wired but not oracle-proven, 6 documented
skips, 12 accepted as inert. The per-switch trace is `switch-matrix.csv`, generated from
[`../notes/switch-wiring.md`](../notes/switch-wiring.md).

Documented skips: `-raw_db`, `-raw_hl`, `-raw_ocs`, `-raw_hpf` (need the statically linked dcraw
9.27), `-aoi_reject` (discard rule not pinned down), `-flat_bayer` (private master format).

## Known gaps

| Gap | State |
|---|---|
| Surface-feature tracking | 23/30 frames byte-identical on the real Saturn capture; the rest are 1 px off (coarse stage; `notes/tracking.md`) |
| 16-bit Bayer debayer | 8-bit is byte-identical and a normal parity case; the 16-bit quantisation path is unverified |
| RAW/dcraw absolute values | `-raw_*` skips above; decoding exists but is not value-parity-proven |
| Colour GIF palette | Bounded by design (262,144-sample reservoir); large-capture RGB output intentionally differs from the pre-0.1.5 encoder |

## Deliberate divergences

- **16-bit TIFF**: the original writes byte-swapped samples under an `II` header; we write spec-correct files.
- **GIF**: the original emits an image-less file; we write a valid animation.
- **Non-tracking `-aoi_crop`**: the original writes a quarter-filled out-of-bounds buffer; we clamp.
- **M25 start-up paths**: the original aborts on a URI/colon argument during `Form_Load`; we reject it
  and report it in the "could not be added" dialog.
- **Large-capture RGB GIF**: from 0.1.5 the palette is sampled with a fixed global budget, so output
  for very long animations is intentionally not byte-identical to earlier OPIPP versions.

## Provenance

`bin/`, `decompiled/` and `ghidra/` are reference copies of Chris Garry's PIPP and its output. They are
git-ignored and never published. GPLv3 covers this project's own code and artwork only, not PIPP; see
[`../NOTICE`](../NOTICE) and [`../notes/provenance.md`](../notes/provenance.md).

## Environment notes

- `DOTNET_ROOT="$HOME/.dotnet-sdk"`, with `$DOTNET_ROOT/tools` on `PATH`.
- Ghidra: `GHIDRA_INSTALL_DIR=C:\tools\ghidra_12.1.4_PUBLIC`, `JAVA_HOME=C:\Users\<you>\.jdks\jdk-21.0.12+8`.
- The oracle needs **argv for switches** and **stdin terminated by `DONE`**; stdin alone hangs it.
