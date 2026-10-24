# OPIPP status

Current state of the reconstruction. Regenerate the numbers, do not trust memory: the commands below
produce every figure on this page.

| | |
|---|---|
| Version | `0.2.2-alpha` at the tagged commit |
| SDK | .NET 10.0.402 (local `~/.dotnet-sdk`) |
| Tests | **1324 tests, 0 failing** across nine projects (2 skip when the non-redistributable Saturn fixture is absent) |
| Parity | **27 compared, 27 matched, 2 deliberate differences, 0 gaps, 0 failed** |
| Release | `v0.2.2-alpha`, Windows x64 zip, self-contained |
| History | [`journal.md`](journal.md) — dated findings, superseded details |

```bash
export DOTNET_ROOT="$HOME/.dotnet-sdk"; export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet test OPIPP.slnx
dotnet run --project tools/Opipp.Parity -- --real
```

## What is verified

- SER, FITS, AVI (uncompressed), TIFF, PNG and BMP read/write; GIF write; PGM as calibration input
  only; DNG read-only (restricted uncompressed subset).
- Debayer, binning, median and mono modes, histogram stretch, gain/gamma, flip/rotate/crop/resize.
- Dark/flat calibration, applied to raw frames before debayering and binning; a master that does not
  match the capture geometry is a controlled error, not a crash or a silently empty result.
- Master creation (dark, flat-dark, flat) reads and writes the engine's `.tmp` masters, subtracts a
  flat-dark exactly once, keeps RGB masters three-channel, is cancellable, and publishes nothing when
  a build fails.
- Integration has been exercised on real captures: a 2011 Jupiter/Ganymede AVI (frames extracted), a
  2022 Saturn SER (8,984 frames), a 2023 Jupiter TIFF, and Moon/Mars stills. Raw CFA + centring is
  rejected as designed; `-db` first is the working path, and the error text now names it.
  See [validation.md](validation.md).
- Process memory is measured, not estimated: the integration probe reports peak working set for
  streaming mean/sum, in-memory median and tiled median. See [memory.md](memory.md).
- Integration staging lives in a run-owned `.opipp-staging/<guid>/` directory; stale run directories
  are removed on a later run, and nothing else in the output tree is swept.
- Final-image integration is available from the CLI (`-stack=mean|sum|median`, `-stack_coverage`,
  `-stack_subpixel`) and a GUI dialog: one output per batch input (one per joined unit), subpixel
  registration, coverage masks that never invent edge samples, a double-precision intermediate, and
  exact median (in memory below the caps, disk-backed above). If an input fails to decode, the accepted frames are still integrated and
  published as a labelled partial result (exit 1); final integration does not require every selected
  file to decode.
- A later FITS IMAGE header that the reader rejects fails the file with the HDU named; it is never
  silently skipped, and `-check` applies the same constraint set as the reader.
- Joined stacks report the identity of the source that actually contributed the selected frames,
  independently of input order.
- Raw SER output describes the frame it actually contains: the ColorID follows the transformed CFA
  phase (all four patterns, flips, rotations, odd crops and odd translations) and binning/resize write
  mono. A mixed-phase sequence fails with a controlled error instead of a lying header.
- Corrupt and truncated FITS, and mixed-CFA joins, produce controlled failures naming the input; no
  unhandled-exception exits. `-check` audits every image HDU structurally without decoding pixels.
- Quality weighting is attached to frame identity, so `-nr` and `-no_ram_buff` no longer change which
  ranks receive extra copies (the review's 95-vs-44 case is now 95 in every mode).
- A final stack keeps the first accepted frame's observer/telescope/instrument metadata.
- The file picker offers only formats the engine can open; JPEG, video containers and non-DNG RAW are
  rejected at add time.
- A run that wrote some frames and then failed reports **PARTIAL FAILURE** and exits non-zero; the
  written output is labelled incomplete and the failing input is named.
- `-check` validates the command line, inputs, masters and geometry, then exits without writing output.
- 8-bit FITS image units are padded to the 2880-byte boundary, including multi-image files.
- Raw Bayer geometry maintains its CFA phase; debayered frames carry none, and an explicit pattern
  selection overrides container metadata.
- Joining reads every image in a multi-HDU FITS file, not one per file, and opens one reader at a time.
- A cached flat master remembers whether its flat-dark was already applied, so toggling flat correction
  off and on cannot subtract it twice; a declined Quit no longer deletes cached masters.
- An explicitly named calibration master that does not exist stops the run with a diagnostic instead
  of silently disabling calibration.
- Mean, sum and median stacking are covered by exact numerical tests for mono/RGB at 8 and 16 bits,
  including saturation, fractional truncation, even-median rounding, reference subtraction and a
  32-bit overflow guard.
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

## How to read the test count

The suite count is what the runner executed, not a count of independent behavioural checks. Keep the
evidence types apart:

| Evidence | What it establishes |
|---|---|
| Unit and pipeline tests | the managed implementation follows the contracts written down here |
| Golden comparisons | output matches committed native fixtures for registered cases |
| Independently decoded output | a file is readable and numerically correct outside OPIPP's own readers |
| CI | the build, suite, docs consistency and packaged smoke convert pass on a clean runner |
| Live native parity (27/27) | maintainer-recorded result against the original engine, not run in CI |

Tests that need a non-redistributable fixture must skip explicitly when it is absent; a test that
cannot run must never report a pass.

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
| Integration rejection | No sigma-clipped or robust rejection; every selected frame contributes |
| Integration precision | Accumulation is double, but the delivered image is quantised to the input depth; there is no float FITS output yet |
| Real-capture coverage | Saturn/Jupiter captures and Moon/Mars stills are validated; no lunar or solar **sequence** yet, and no deep-sky FITS with scaled/float data |
| AVI input gaps | I420/planar-YUV AVI and ffmpeg `rawvideo bgr24` AVI are rejected; PNG-frame extraction is the workaround |
| Unequal master goldens | Native master files are pinned from identical inputs; unequal-frame native fixtures that would distinguish averaging rules are still missing |
| Bayer-marked stacking | Raw stacking requires one consistent CFA pattern after geometry; debayer before combining different phases. Persisted calibration `.tmp` masters do not carry a Bayer tag |

## 0.2.1 correctness changes

- FITS scanning rejects a nonzero incomplete later header, including a tail shorter than one card.
  A truncated payload in an HDU that would otherwise be skipped also fails instead of reporting a
  successful shorter capture. Valid different-sized images remain excluded by the same-shape rule.
  Supported images with a short payload retain the existing labelled partial-failure behavior.
- The reader and `-check` reject image sizes that cannot fit the decoder's sample or byte buffers,
  before attempting a pixel allocation.
- Mean, sum and median preserve the accepted frames' common Bayer pattern. An incompatible pattern,
  a mixture of raw Bayer and plain mono, or a Bayer tag on RGB is rejected before altering the stack.
  Calibration-master creation reports this as a controlled failure without publishing a master.
  This does not add final-image stacking controls to the CLI or GUI.

## Format boundaries

The engine is deliberately narrower than its file pickers suggest. These limits are enforced or
documented before a run rather than discovered in the output:

| Input or output | Supported | Not supported |
|---|---|---|
| AVI output | uncompressed 8-bit DIB/Y800; AVI 1.0 size limit around 4 GiB | any compressed encoder (MJPEG encode, ULRG, FMP4, YUY2) |
| TIFF input | classic uncompressed strips | compressed, tiled, planar, palette TIFF, BigTIFF |
| PNG input | non-interlaced grayscale/RGB at 8 or 16 bits | indexed palettes, Adam7 interlacing |
| Camera RAW | a restricted uncompressed DNG subset | general CR2/NEF/ARW support |
| Floating-point FITS | read as stored values converted to the unsigned integer model | automatic min/max scaling; normalized 0..1 data collapses (0.1, 0.4, 0.6, 0.9 becomes 0, 0, 1, 1) |

Floating-point FITS is therefore unsuitable for preserving precision; convert it to 16-bit integers
before processing if the values matter.

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
