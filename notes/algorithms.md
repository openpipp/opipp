# Algorithms: what is observed, what is reconstructed

Each entry states the evidence and whether the implementation matches observed behaviour or is a
documented reconstruction. Evidence addresses are Ghidra image addresses in `bin/pipp_core.exe`
(sha256 `04a088af…`) unless stated otherwise.

## Observed (implemented to match)

### Image resize / "stretch"
- Routine: `FUN_14005ca60` (1,268 bytes), prints `"Stretching the image...\n"` (string at 0x140125488,
  xref 0x14005ca9e).
- Mapping: destination index runs `dVar19 += srcSize/dstSize` starting at 0, so destination pixel 0
  samples source pixel 0 (corner aligned, **not** pixel centre). Value =
  `next*(frac) + current*(1-frac) + 0.5`, truncated; the second index is clamped to the last
  row/column. Both upscale and downscale use the same expression.
- Implementation: `Processing/Resize.cs` (`Bilinear`), tests
  `Bilinear_2x2_To_4x4_MatchesEngineCornerAlignedGrid`, `Bilinear_2x2_To_1x1_SamplesTopLeftCorner`.

### Quality subsample / luma projection
- Routine: `FUN_1400af990`, includes `"SubSample: Unsupported depth %d"` (0x14013a280, xref 0x1400afc69).
- Depth codes: 1 = 8-bit mono, 2 = 16-bit mono, 3 = 8-bit colour via LUTs, 6 = 16-bit colour.
- Colour luma weights in the 16-bit path: `(R*299 + G*587 + B*114)/1000` reading BGR-ordered samples —
  Rec.601. Implementation: `Quality/QualityMetrics.ToLuma`.
- Returns the mean luma of the block, i.e. this routine is the measurement primitive, not the score.

### Colour buffer order
- Same routine indexes `puVar4[-1]`, `*puVar4`, `puVar4[1]` as B,G,R, so PIPP's internal colour frames
  are **BGR**. The managed `Frame` is RGB; writers/readers convert at the boundary
  (`SerWriter` handles SER ColorID 17 = BGR).

### Debayer pattern indices
- Engine help text at 0x140138490: `0: GBRG  1: BGGR  2: RGGB  3: GRBG`.
- Implementation: `Processing/Debayer.FromPippIndex`, used by the CLI mapping.

### Histogram stretch (measured against the oracle, not inferred)
- Help string at 0x138600 describes `-hs=xxxx` as "stretch to maximum pixel value 204 (80% of 255)",
  but the **observed behaviour is a gain of xxxx/100**: `-hs=204` multiplies by 2.04, it does not map
  204 to 255.
- Measured on a fixed ramp through the original binary: at 204, 38 -&gt; 77 and 123 -&gt; 250; at 153,
  38 -&gt; 58 and 123 -&gt; 188; at 102, 38 -&gt; 38 and 123 -&gt; 125. Exact-integer inputs land one
  lower than naive arithmetic suggests (50 -&gt; 101, 75 -&gt; 152, 100 -&gt; 203), which fits
  `truncate((v * whitePoint - 1) / 100)` — the engine carries a one-unit bias.
- Implementation: `Processing/HistogramStretch.StretchToWhitePoint`; tests
  `StretchToWhitePoint_204_AppliesTwoPointZeroFourGain`, `StretchToWhitePoint_204_UsesSinglePrecisionGain`.
  The percentage spelling (`-hs=xx%`) is a *different* mapping (white point at xx% of full scale,
  gain 100/xx) and remains inferred — no oracle case exercises it yet.

### SER v3 header (verified, corrects an earlier assumption)
- The header is **178 bytes**: a 42-byte integer prefix (FileID `LUCAM-RECORDER`, LuID, ColorID,
  LittleEndian, width, height, depth, frame count), three 40-byte ASCII strings at 42/82/122, then
  two `int64` timestamps at **162** (local) and **170** (UTC). There is **no UTC-offset field**; the
  offset exposed by `SerHeader.LocalTimeUtcOffsetMinutes` is derived from the two timestamps.
  (An earlier brief in this project described an extra `int32` offset at 162 and a 182-byte header;
  that was wrong and the implementation follows the layout above.)
- Validated two ways: structurally (`file size == 178 + frames × frameSize`, confirmed on a generated
  colour file: 32×32×3×8-bit × 4 frames = 12,466 bytes) and by the SER subagent against the real
  `pipp_dll.dll!ser_info` oracle, which accepted the generated mono-Bayer-8 and RGB-16 files with the
  expected dimensions, depth, frame count and endianness.
- Two quirks observed in that oracle: `ser_info` swaps the Instrument/Telescope labels, and it labels
  ColorID 16 as `BAYER_CYYM` even though 16 is RGB in the v3 specification.
- PIPP's own engine documents SER output as mono-only ("SER file format only supports mono images").
  This implementation writes colour SER (ColorID 16) as well, which is within the v3 spec and which
  PIPP's reader handles — a documented superset, not a parity claim.

### Engine invocation protocol
- The engine does **not** treat EOF as end-of-input: a stdin stream that ends without the literal line
  `DONE` makes it block forever (verified: CPU time stays at zero, FFmpeg DLLs loaded, no output,
  no files). The WinForms front-end writes `DONE` and then closes the stream (`Form1.do_processing_thread`).
- Options are honoured from **argv**; a stdin-only invocation with `DONE` produced no output in testing.
  The parity harness therefore passes switches and input paths both ways and always terminates stdin
  with `DONE`, which reproduces the GUI's behaviour exactly.

### Master dark/flat stacking
- `pipp_dll!stack_frames` (REA decompilation, evidence `ev_fcbae287…`): accumulates frames into a
  stacker, optional frame-to-subtract per input, rejects geometry/sample-size mismatches, writes TIFF
  plus PGM, reports through `set_progress_fn`, returns negative on abort.
- Implementation: `Stacking/FrameStacker`, `Processing/Calibration.BuildMaster`.
- The front-end's native call site is replaced too: `LegacyNativeApi.stack_frames` routes to
  `Pipeline/MasterFrameBuilder` when `pipp_dll.dll` is unavailable, so dark/flat masters can be built
  with no native code at all. It reproduces the same contract (mean, optional per-input subtract,
  geometry refusal returning -1, progress callback, TIFF + PGM outputs).

### Still-frame output naming
- The engine writes one file per frame for still formats and renumbers by default (`-nr` disables
  renumbering, `-nlz` disables zero padding). Implementation: `Pipeline/Processor` still-image branch,
  `_00000.tif` style names.

## Reconstructed (behaviour plausible, not verified against output)

- **Quality scores** (`PippDefault`, `Ninox`, `Histogram`): the ranking contract and "% of best"
  reporting are observed; the scalar formulas (Tenengrad, Laplacian variance, histogram entropy) are
  reconstructions. No labelled frame set was available to compare against the original scores.
- **Alignment**: centroid and normalised cross-correlation with parabola sub-pixel refinement. PIPP's
  exact scoring kernel and its "no AFB found"/"no AOI found" rejection thresholds are not reproduced.
- **Planet detection** (`-chk`, `-ms`, `-mp`, `-shdw`) and **AOI** handling: not implemented; the
  strings confirm the contract (`"  Calculated minpixel value: %d"`, `"no planet detected: %d"`).
- **Binning**: average `(sum + n/2)/n`, sum clamped to the sample maximum; the engine's exact rounding
  is unverified.
- **Mono conversion modes**: channel selection confirmed by the GUI combo mapping
  (0 = blue only … 3 = all three, 4 = R+G, 5 = R+B, 6 = G+B); the averaging arithmetic is reconstructed.
- **AVI**: managed RIFF writer for uncompressed DIB/Y800 only. The engine's ULRG (Ut Video) and FMP4
  (MPEG-4) paths require FFmpeg and are not implemented; the reader refuses compressed streams with a
  clear error rather than misdecoding.
  *Externally validated*: files written by this implementation were decoded by `ffprobe`/`ffmpeg` and
  compared byte-for-byte with the source frames (mono 8bpp pal8, colour 24bpp bgr24, Y800), and
  ffmpeg-authored `rawvideo` AVIs (bottom-up 24bpp, top-down Y800) were read back correctly. The
  pipeline now propagates frame rate and channel count into the writer (a colour frame must be stored
  as 24bpp, otherwise the writer's mono default would corrupt it), which `ffprobe` confirms as
  `pix_fmt=bgr24, r_frame_rate=30/1`. Index offsets are relative to the `movi` FourCC (first chunk at
  4), matching ffmpeg.
- **GIF animation**, **RAW/dcraw 9.27**, **WinJUPOS filename masks**, **AVI header legacy variants**
  (`-avi_old`, `-avi_old4`, `-avi_safe_width`), **`-join`** and **quality weighting** (`-qw`):
  parsed by the CLI model, not executed by the pipeline.

## Verified against the original engine (parity harness)

`tools/Pipp.Parity` runs `bin/pipp_core.exe` and `Pipp.Cli.exe` over the same fixtures with the same
switches and compares frame content by SHA-256. Current status: **9 of 9 cases match**.

| Case | Switches | Result |
| --- | --- | --- |
| plain-tif | `-tif` | match |
| stretch | `-tif -hs=204` | match |
| flip | `-tif -flip_x` | match |
| fits-out | `-fits` | match |
| png-out | `-png` | match |
| avi-out | `-avi` | match |
| debayer | `-tif -db=2 -db_type=0` | match |
| centre | `-tif -ctr -ms=5` | match |
| quality | `-tif -q=2` | same frame multiset (selection may reorder) |

Findings that came out of these runs, beyond the stretch formula above:

- **FITS is stored bottom-up and flipped on read.** Feeding the original a top-down FITS returns an
  upside-down frame, while its own files round-trip correctly - so the flip is unconditional, not a
  marker in the file. `FitsWriter` writes rows in reverse and `FitsReader` flips everything it reads.
  Any third-party FITS therefore appears vertically inverted in PIPP; that is original behaviour.
- **FITS keeps the sample depth**: 8-bit frames are `BITPIX=8` with `BZERO=0`, 16-bit frames are
  `BITPIX=16` with `BZERO=32768`. The original also emits `DATAMIN`/`DATAMAX`.
- **`-ctr` centres the object, it does not align frames to each other.** The target is
  `x = width/2`, `y = height - 1 - height/2` - i.e. `height/2` in the engine's bottom-up buffers, so a
  disc centred at (48,48) in a 96x96 frame comes out at (48,47). Sub-pixel positions are not used:
  the shift is integral and edge rows repeat rather than filling with zero.
- **The detector needs a flat field.** Synthetic frames with a diagonal background ramp are silently
  discarded; the same disc on a flat dark background is accepted. Frames are also rejected with
  "planet diameter below minimum" unless `-ms` is relaxed, so centring cases pass `-ms=5`.
- **Quality selection** (`-q=2`) keeps the same frames as the original, though the output numbering
  starts at `0` for the original and at `00000` for this engine.
- **AVI output** is byte-comparable only through the container reader: the original's AVI is read by
  `AviReader` and hashed frame-by-frame, so codec/timing differences are visible but not fatal.
- **Debayer** (`-db=2`, RGGB) reproduces the original exactly on a synthetic Bayer mosaic.
