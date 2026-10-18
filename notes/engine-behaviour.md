# Engine behaviour (observed from pipp_core.exe / pipp_dll.dll)

All statements below are observations from the shipped artifact: string literals with file offsets
(`notes/native-strings.txt`), PE tables, and REA/Ghidra pseudocode. Offset column = raw file offset in
`bin/pipp_core.exe` unless marked DLL.

## 1. Process model

The GUI starts `pipp_core.exe` and communicates through **stdout tokens**, not IPC:

| Token | Meaning |
|---|---|
| `@PROGRESS=%d` | 0..100 progress |
| `@PFILE=%d` / `@PFILELAB=File %d of %d` | current input file |
| `@PFRAMELAB=%d` / `@FNUM=%d` / `@FCNT=%d` | frame counters |
| `@FNAMELAB=%s` | current file name label |
| `@XPOS` / `@YPOS` | detected planet position |
| `@QSORT=%d` / `@QSFRAMESLAB=%d` | quality-sort state |
| `@ANIM=%d` | animation state |
| `@DETECT_THRESHOLD=%d` | auto-detected threshold (test run) |
| `@MAX_PIXEL`, `@MAX_R_PIXEL`, `@MAX_G_PIXEL`, `@MAX_B_PIXEL` | measured maxima (test run) |
| `@Error: ...` | fatal error line |

`-test_run` runs detection only and prints the `@MAX_*`/`@DETECT_THRESHOLD` values used by the GUI's
"Test Options" dialog. `-gui` makes the engine emit GUI tokens; `-sdp` is set by the GUI as well.

## 2. Pipeline order (from help text order + processing messages)

1. argument parse (`-ini=` file is read first if given)
2. input discovery, extension/format probing, timestamp extraction, sorting (`-join` keeps files as one stream)
3. frame window: `-sf`, `-f`, `-ifd` (input frame drop), `-ctrf`
4. per frame: decode (AVI/SER/FITS/BMP/TIF/PNG/RAW via embedded dcraw 9.27) → optional `-ser_bpp` override
   → mono/colour override (`-mono`/`-colour`) → debayer (`-db`/`-db_type`/`-sh`) → binning (`-bin`, `-bin_sum`)
5. calibration: dark subtract (`-dark*`, master from GUI's `stack_frames`), flat divide (`-flat*`)
6. processing: `-median`, mono conversion (`-convmono` 0..6), input gain/gamma, histogram stretch (`-hs`),
   overexposure reject (`-oe`), flip/rotate, planet check (`-chk`, `-ms`, `-mp`), centre (`-ctr`),
   surface-feature stabilisation (`-en_surface_feature_tracking`, `-afb_*`, `-en_ss`), AOI crop/reject,
   crop (`-cx/-cy/-ox/-oy`), resize (`-resize_*`)
7. quality: metric + downsample search (`-qmin` 3, `-qmax` 5, `-qinc` 1), sort, select (`-q`), weights (`-qw`)
8. output: write frames (`-bmp/-tif/-png/-fits/-ser/-avi`), optional animation (`-gif`, `-anim_*`),
   rename/renumber (`-nr`, `-nlz`), drop (`-drop`), WinJUPOS names (`-wjpos_*`)
9. summary/log file (`"%s (%s) Logfile"`, `"%s (%s) Quality Log"`)

## 3. Algorithms / constants

- **Debayer**: default bi-linear; `-sh` = smooth-hue. Pattern index mapping is PIPP-specific:
  **0 = GBRG, 1 = BGGR, 2 = RGGB, 3 = GRBG** (offset 0x138490). `-db_type` selects the Bayer layout variant
  used for reading the raw file; `-db` is the debayer pattern index.
- **Histogram stretch**: default white point 204 (80% of 255) (0x138600); `-hs=xxxx` sets 1..255;
  `-hs=xx%` sets 1..100 %; on colour it equalises R, G and B, optionally per channel (`-hsrgb`), with
  black-point preservation (`-hs_bp`).
- **Quality**: three algorithms — PIPP default, `-qold` (original Ninox), `-qhisto` (histogram).
  `-qw` duplicates the top 25 % quality frames twice and the next 25 % once (documented in help,
  0x136b90). Quality estimation downsamples between `-qmin` and `-qmax` in steps of `-qinc`.
- **Stacking (master dark/flat)**: `pipp_dll.stack_frames(files[], frameToSubtract, outTiff, outPgm, frames)`
  accumulates into a stacker object, prints `"  Stacking %d (estimated) frames"`, `"  %d frames stacked"`,
  rejects size/sample-size mismatches with `"  Error: Frame size (%d x %d) does not match dark frame size ..."`,
  writes TIFF (mono or colour variant) plus optional PGM for the engine, and calls the registered progress
  callback. Returns negative on abort (abort flag set by `set_abort_request`).
- **Planet detection**: `-mp` minimum pixel value (default computed, reported as "Calculated minpixel value"),
  `-ms` minimum planet size, `-oe` max overexposed pixel count, `-shdw` edge-in-shadow handling.
- **Resize**: `-resize_width` / `-resize_height`, `-1` = keep aspect ratio.
- **Binning**: `-bin=1..8`, average by default, `-bin_sum` for sum.
- **Mono conversion modes**: `-convmono=0..6` (seven distinct modes; GUI labels them).
- **WinJUPOS naming**: mask validated with strict rules (2 or 4 year chars, exactly 2 month/day/hour/minute,
  2 second chars or one fraction-of-minute char, at most one `N`); errors at 0x136...: "Require 2 or 4 year characters".

## 4. Format support (from error strings)

- **BMP**: 8, 24 and 32 bpp only; validates signature, offset and header size. Writes mono/colour.
- **TIFF**: 8 and 16 bits per sample; rejects unsupported compression values.
- **FITS**: requires SIMPLE, BITPIX, NAXIS, NAXIS1, NAXIS2 (NAXIS3 for colour), honours BZERO/BSCALE,
  16-bit output. Colour stored as NAXIS3=3 (planes).
- **SER**: validates 178-byte header, frame count > 0, enough bytes for all frames, bpp range; writes
  **mono only** (help text: "SER file format only supports mono images").
- **AVI**: own RIFF muxer (`c_pipp_avi`) with `idx1`, `strh`/`strf`, `avih`; YUY2 path has its own error
  ("Error: Error writing to AVI file (YUY2) ret = %d"); `-avi_old`, `-avi_old4` write legacy header
  variants, `-avi_safe_width` pads width to a codec-safe multiple, `-auto_fps` derives fps from timestamps.
  `-debugavi` dumps every header field.
- **PNG**: libpng 1.6.21 statically linked (also exported by pipp_dll.dll).
- **PGM**: master dark/flat handoff format, validated ("%s is not a valid PGM file!", "%s has the wrong dimensions!").
- **RAW**: dcraw 9.27 embedded in pipp_core.exe; `-raw_db`, `-raw_ocs`, `-raw_hl`, `-raw_hpf`.

## 5. Temp/aux files

`-temp_dir=` directory holds `pipp_master_dark.tmp`, `pipp_master_dark.pgm`, `pipp_master_flat_dark.tmp`,
`pipp_master_flat.tmp`, plus AVI temp buffers (`c_pipp_avi::create_temp_buffer`). The GUI passes them in
via `-dark_tmp`, `-dark_pgm`, `-flat_dark_tmp`, `-flat_tmp`.

## 6. Summary/log output

Engine prints a summary block ("Processing Details:", "Summary:", counts of files not found, unexpected
format, total input frames, frames discarded per reason, total/unique/duplicated output frames, quality
algorithm, frame type/format, output directory) and writes a log file and quality log file when
`-gui`/`-sdp` is not used, using the documented headers.

## GUI stdout protocol (observed with -gui, single input, 4 frames)

The original emits these on stdout in this order. Tokens are `@NAME=value`; the plain text between
them is not suppressed in `-gui` mode.

```
@PFILE=0
@PFILELAB=File 0 of 1
@FNAMELAB=none
@ANIM=0
@QSORT=0
Processing: (Batch mode)
  Input file:
    <path>
  Estimating input frames:
    Total input frames: 4
@FNAMELAB=capture.ser
@PFILE=100
```

Notes:
- `@PFILELAB` counts from **0** (`File 0 of 1`), while our engine emits `File 1 of 1`.
- `@FNAMELAB=none` precedes processing and is replaced by the actual file name later.
- `@ANIM` and `@QSORT` are emitted up front with `0`.
- Our engine currently emits `@PROGRESS`, `@FNUM`, `@FCNT` and orders the tokens differently.
  Matching this stream is a prerequisite for dropping the `EngineLocator` fallback in the GUI.
