# PIPP Reconstruction & Modernization Plan

Status: active work in progress. Reference = installed PIPP 2.5.x (2017-01-06 build), copied read-only to `bin/`.

## 1. What PIPP actually is (observed, not guessed)

| Artifact | Role | Evidence |
|---|---|---|
| `PIPP.exe` 1.46 MB, x64, .NET 4.0 WinForms | GUI **front-end**: builds a command line, starts `pipp_core.exe`, parses its stdout for progress | COR20 header present; `[DllImport]`s; `Process.Start` in `do_processing_thread`; `generate_cmdline_options*` methods |
| `pipp_core.exe` 1.5 MB, x64 native console | the **engine**: reads AVI/SER/FITS/image/RAW, debayers, calibrates, aligns, stacks, sorts by quality, writes outputs | console subsystem; FFmpeg + libpng use; CLI switches |
| `pipp_dll.dll` 1.35 MB, x64 native | **helper**: probe (`avi_info`, `ser_info`, `fits_info`, `video_check`, `ser_check`, `raw_decode_check`, `date_time_check`) + master-dark/flat **stacking** (`stack_frames`) + progress/gui callbacks. Statically links libpng 1.6.x (240 `png_*` exports) | PE export table, `c_dll.cs` call sites |
| `av*.dll`, `sw*.dll` | FFmpeg 3.x (avformat-57 / avcodec-57 / avutil-55 / swscale-4) | import tables |

The GUI never implements the pipeline; every processing switch in `Form1.generate_cmdline_options_*`
became a switch on the `pipp_core.exe` command line (152 emissions, full table in `notes/cli-spec.md`).

## 2. Deliverables

1. `decompiled/pipp-ui/` — complete IL decompilation of `PIPP.exe` (ilspycmd 11.1, net40 project, 15 files / 14,277 lines).
2. `ghidra/out/` — complete native decompilation corpus (all analyzed functions, C pseudocode + CSV index).
3. `notes/` — evidence ledger, CLI spec, algorithm notes, provenance.
4. `src/` — the modern replacement, **fully managed C# 13 / .NET 10**:
   - `Pipp.Core` — formats + pipeline + algorithms (no native DLL).
   - `Pipp.Cli` — headless engine, same switch vocabulary as `pipp_core.exe`.
   - `Pipp.App` — WinForms parity UI (`net10.0-windows`), generated from the decompiled Designer code, calling the core in-process instead of spawning a process.
5. `tests/` — xUnit golden/round-trip tests.

## 3. Modernization decisions (agreed with user)

- Shell: **.NET 10 WinForms**, reusing the decompiled layout for feature parity.
- Engine: **managed C# only**. No `pipp_dll.dll`, no `pipp_core.exe`.
- Video (AVI in/out) is the one place where an external native dependency is unavoidable for
  compressed codecs; plan is `FFmpeg.AutoGen` (or `ffmpeg` CLI fallback) behind `Pipp.Core.Video`,
  with pure-managed paths for uncompressed AVI. SER/FITS/BMP/TIFF/PNG are written managed.
- Provenance: reconstruction of a closed-source freeware app. Repository is marked
  "reconstruction — publish only with the original author's permission" (see `notes/provenance.md`).

## 4. Module map for `Pipp.Core`

```
Formats/     SerFile (v3/v4? header), FitsFile, AviFile, ImageFiles, RawDecode (later)
Frames/      Frame (16-bit mono/colour), PixelFormat, Debayer (Bilinear? + others per -db_type)
Calibration/ MasterDark, MasterFlat, hot-pixel map, optimise, subtract/divide
Processing/  Median, MonoConvert (-convmono 0..6), HistogramStretch (-hs/-hs_bp/-hsrgb),
             Overexposure reject (-oe), Flip/Rotate, PlanetCheck (-chk), Crop (-cx/-cy/-ox/-oy),
             Resize (bilinear), SurfaceFeatureTracking (stabilisation)
Quality/     Metrics (-qold/-qhisto/-qtotal/-qw), downsampling search (-qmin/-qmax/-qinc),
             frame selection (-q/-qpcnt), weights
Stacking/    Align (translation, sub-pixel), Stack (sum/average/median, Bayer-aware), MasterFrames
Output/      Bmp/Tiff/Png/Fits/Ser writers, Avi writer (codec matrix), Gif animation, WinJUPOS naming
Pipeline/    Processor (orchestration), PippOptions, Progress reporting, Cancellation
```

## 5. Verification strategy

- Round-trip tests for every container we write (write → read → byte/値 compare).
- Golden tests for algorithms against values observed in the native pseudocode (documented per test).
- End-to-end: synthetic SER/AVI (generated in tests) → process → assert on output file structure and
  pixel statistics. Native engine used only as an oracle where static evidence is ambiguous.
