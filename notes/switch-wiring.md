# Switch wiring audit — 131 CLI switches vs `PipelineSettings`

**Scope.** Every case label accepted by `CommandLineParser.Parse`, traced through `PippOptions` →
`Program.Map` → `PipelineSettings` → `Processor`. Read-only audit: no source file, `docs/HANDOFF.md`
or `docs/journal.md` was touched.

## Method

1. Parsed the `switch (key)` block (`CommandLineParser.cs:96-675`) programmatically: **131 case labels
   in 130 case groups** (`-ctr`/`-centre` share one body at 350-353).
2. `PippOptions` declares **118** settable properties; every one is assigned by at least one switch.
3. `Program.Map` (`Program.cs:126-196`) plus `BuildCrop` (219), `ChooseExtension` (228) and `LoadMaster`
   (198) read **50 distinct** `PippOptions` properties and set **39 of the 46** `PipelineSettings`
   properties; `LogArguments` is set in `Main` (71), so **40 of 46** are set by the CLI path.
4. "Consumer" is a `_settings.<Prop>` read in `Processor.cs`, quoted with method and line.
   `CommandLineBuilder.cs` mentions ~90 option properties but only *emits argv*; it is not a consumer
   and is excluded.
5. `docs/switch-matrix.csv` was compared row by row against the code (see *Docs drift*).

## Counts

| Group | Meaning | Switches | Share |
|---|---|---:|---:|
| **a** | wired and implemented — reaches a `PipelineSettings` property that a pipeline stage reads | **57** | 43.5% |
| **b** | parsed, no `PipelineSettings` property | **68** | 51.9% |
| **c** | property exists but no consumer | **1** | 0.8% |
| **d** | inert / alias | **5** | 3.8% |
| | **total** | **131** | 100% |

Group (a) splits into **27 fully implemented** and **30 partial** — they reach a live consumer but are
conditional, collapse a value, or fire only in one output branch; the `notes` column says which.
Group (c) is one switch plus one switch *argument*: `-nlz` (`LeadingZeros` is dead) and the
`-db=<pattern>` form of `-db` (`DebayerPattern` is dead) — `-db` itself is counted in (a) because its
bare form works.

| CLI section | switches | a | b | c | d |
|---|---:|---:|---:|---:|---:|
| Global / mode | 9 | 1 | 7 | 0 | 1 |
| Calibration frames | 9 | 2 | 7 | 0 | 0 |
| Input options | 21 | 6 | 14 | 0 | 1 |
| Processing options | 40 | 28 | 11 | 0 | 1 |
| Animation options | 7 | 0 | 7 | 0 | 0 |
| Quality options | 10 | 5 | 4 | 0 | 1 |
| Output options | 30 | 12 | 16 | 1 | 1 |
| Engine paths | 5 | 3 | 2 | 0 | 0 |
| **total** | **131** | **57** | **68** | **1** | **5** |

## Findings

- **Half the vocabulary is decorative.** 68 of 131 switches never reach `PipelineSettings`. Entire
  feature groups sit there: frame range (`-sf`, `-f`, `-ctrf`, `-ifd`), detection size (`-ms`), the
  area of interest (`-aoi*`), animation repeat (`-anim_*`), AVI writer options (`-avi_codec`,
  `-avi_quality`, `-avi_old*`, `-avi_safe_width`), GIF options and all RAW options.
- **Only 27 switches are unambiguously complete.** The other 30 in (a) are conditional or partial:
  `-chk` never rejects a frame, `-db_type` only distinguishes smooth-hue, `-afb_*` needs all four
  values, `-cx/-cy/-ox/-oy` need a crop size, `-resize_*` needs both enable and width, `-dark`/`-flat`
  need a readable master, the six wired format switches collapse to one container.
- **Two `PipelineSettings` properties are dead:** `DebayerPattern` (set from `-db=<pattern>`, never
  read) and `LeadingZeros` (set from `-nlz`, never read; Processor 382 always pads).
- **Three switches cannot change behaviour at all:** `-qtotal` (no `TotalPixel` in
  `Pipp.Core.Quality.QualityAlgorithm`; Program collapses it to the default), `-gif` (no `.gif` branch
  in `ChooseExtension` and no GIF writer), `-bin_average` (writes the default value).
- **Six `PipelineSettings` properties have no producer:** `Stack`, `PippLayout`, `WriteLogFile`,
  `OverexposedThreshold`, `AlignmentSearchRadius`, `EngineVersion` — stacking and layout control are
  unreachable from the command line.
- **The CSV is out of date** (see *Docs drift*): 22 of its 126 rows disagree with the code.

## Full table

All 131 switches, ordered by CLI section. Reading the columns: **mapped** is `yes` (read in `Program.Map` or its helpers), `indirect` (the
switch gates `LoadMaster` rather than mapping a value) or `no`. A dash in *PipelineSettings property*
or *pipeline consumer* means the switch has no downstream wiring at all. `partial` at the start of a
note marks a group (a) switch that is wired but incomplete.


### Global / mode (9 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 1 | `-help` | `-` | no | `—` | — | **d** | Prints `PippHelpText` and exits 0 (Program.cs:33-37); not an option. |
| 2 | `-gui` | `Gui` | yes | `GuiProtocol` | Report() 661 | **a** | Full: drives the `@TOKEN` progress protocol. |
| 3 | `-test_run` | `TestRun` | no | `—` | — | **b** | No PS property; `TestRun` is never read. |
| 4 | `-sdp` | `ShowDetectedPixels` | no | `—` | — | **b** | No PS property; there is no overlay stage. |
| 5 | `-sr` | `SaveRejectedFrames` | no | `—` | — | **b** | No PS property; rejected frames are dropped (Processor 278, 288). |
| 6 | `-no_ram_buff` | `NoRamBuffers` | no | `—` | — | **b** | No PS property; every frame is held in a `List` (Processor 244). |
| 7 | `-check` | `CheckOnly` | no | `—` | — | **b** | No PS property; validate-and-exit is a `Main` concern, not a stage. |
| 8 | `-debugavi` | `DebugAvi` | no | `—` | — | **b** | No PS property; `AviWriter` has no diagnostics switch. |
| 9 | `-join` | `Join` | no | `—` | — | **b** | Banner only (Program.cs:91); `Run()` loops per file independently. |

### Calibration frames (9 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 10 | `-dark` | `DarkSubtraction` | indirect | `MasterDark (gate)` | Transform() 581 | **a** | `partial` — gate for `LoadMaster` (Program 192); no effect unless -dark_tmp/-dark_pgm names a readable PGM. |
| 11 | `-save_dark` | `SaveMasterDark` | no | `—` | — | **b** | No PS property; no master-writing stage. |
| 12 | `-hot_pixel` | `HotPixelThreshold` | no | `—` | — | **b** | No PS property; no hot-pixel repair stage. |
| 13 | `-dark_bayer` | `DarkHasBayer` | no | `—` | — | **b** | No PS property. |
| 14 | `-flat` | `FlatCalibration` | indirect | `MasterFlat (gate)` | Transform() 585 | **a** | `partial` — gate for `LoadMaster` (Program 193); no effect unless -flat_tmp names a readable PGM. |
| 15 | `-save_flat` | `SaveMasterFlat` | no | `—` | — | **b** | No PS property. |
| 16 | `-flat_bayer` | `FlatHasBayer` | no | `—` | — | **b** | No PS property. |
| 17 | `-flat_dark` | `FlatDarkSubtraction` | no | `—` | — | **b** | No PS property. |
| 18 | `-save_flat_dark` | `SaveMasterFlatDark` | no | `—` | — | **b** | No PS property. |

### Input options (21 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 19 | `-raw_hpf` | `RawHotPixelFilter` | no | `—` | — | **b** | No PS property; the RAW path has no hot-pixel filter. |
| 20 | `-raw_db` | `RawDebayerAlgorithm` | no | `—` | — | **b** | No PS property. |
| 21 | `-raw_ocs` | `RawColourSpace` | no | `—` | — | **b** | No PS property. |
| 22 | `-raw_hl` | `RawHighlightRecovery` | no | `—` | — | **b** | No PS property (the PippOptions default 1 never reaches the pipeline). |
| 23 | `-y8` | `TrueY800` | no | `—` | — | **b** | No PS property. |
| 24 | `-wjpos_dtmask` | `WinJuposDateTimeMask` | no | `—` | — | **b** | No PS property; names are built at Processor 336/391. |
| 25 | `-wjpos_lt2ut` | `WinJuposLocalToUtc` | no | `—` | — | **b** | No PS property. |
| 26 | `-wjpos_ipfps` | `WinJuposInputFps` | no | `—` | — | **b** | No PS property. |
| 27 | `-ser_bpp` | `SerBitDepth` | no | `—` | — | **b** | No PS property; the SER reader always trusts the header. |
| 28 | `-sf` | `StartFrame` | no | `—` | — | **b** | No PS property; the read loop (Processor 249-325) has no start index. |
| 29 | `-f` | `FrameCount` | no | `—` | — | **b** | No PS property; the read loop runs to EOF. |
| 30 | `-ctrf` | `FramesFromCentre` | no | `—` | — | **b** | No PS property; the range origin is fixed at frame 1. |
| 31 | `-ifd` | `InputFrameDrop` | no | `—` | — | **b** | No PS property; the read loop cannot drop leading frames. |
| 32 | `-mono` | `ColourMode` | yes | `ForceMono/ForceColour` | Transform() 559/568 | **a** | Full. |
| 33 | `-colour` | `ColourMode` | yes | `ForceMono/ForceColour` | Transform() 559/568 | **a** | Full. |
| 34 | `-db` | `Debayer / DebayerPattern` | yes | `Debayer / DebayerPattern` | Transform() 564 / none | **a** | `partial` — bare `-db` works; `-db=<pattern>` only sets `DebayerPattern`, which Processor never reads. |
| 35 | `-pb` | `ProtectBayer` | no | `—` | — | **b** | No PS property. |
| 36 | `-db_type` | `DebayerAlgorithm` | yes | `SmoothHue` | Transform() 566/570 | **a** | `partial` — only value 2 (SmoothHue) survives; 0 and 1 both map to `false` (Program 139). |
| 37 | `-bin` | `Binning` | yes | `Binning` | Transform() 574 | **a** | Full. |
| 38 | `-bin_sum` | `BinningCalculation` | yes | `BinningSum` | Transform() 577 | **a** | Full. |
| 39 | `-bin_average` | `BinningCalculation` | yes | `BinningSum` | Transform() 577 | **d** | Writes the default value; only undoes an earlier -bin_sum. |

### Processing options (40 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 40 | `-input_gain` | `InputGain` | yes | `Gain` | Transform() 591 | **a** | Full. |
| 41 | `-gamma` | `Gamma` | yes | `Gamma` | Transform() 591 | **a** | Full. |
| 42 | `-median` | `MedianFilter` | yes | `MedianFilter` | Transform() 596 | **a** | Full. |
| 43 | `-convmono` | `ConvertToMono` | yes | `MonoMode` | Transform() 561/600 | **a** | Full. |
| 44 | `-hs` | `HistogramStretch` | yes | `StretchWhitePoint` | Transform() 604 | **a** | Full; the `%`/`p` spellings are normalised to 1..255 in the parser (CommandLineParser 290-303). |
| 45 | `-hs_bp` | `HistogramStretchNoBlackPoint` | yes | `StretchPreserveBlackPoint` | Transform() 609 | **a** | Full. |
| 46 | `-hsrgb` | `HistogramStretchRgb` | yes | `StretchPerChannelGains` | Transform() 610 | **a** | Full. |
| 47 | `-oe` | `OverexposureThreshold` | yes | `MaxOverexposedPixels` | Run() 283 | **a** | `partial` — the count gate works, but `OverexposedThreshold` (the pixel value) is never set, so it uses the frame maximum (Processor 548-551). |
| 48 | `-flip_x` | `FlipX` | yes | `FlipX` | Transform() 614 | **a** | Full. |
| 49 | `-flip_y` | `FlipY` | yes | `FlipY` | Transform() 615 | **a** | Full. |
| 50 | `-rotate90` | `Rotation` | yes | `Rotation` | Transform() 616 | **a** | Full. |
| 51 | `-rotate180` | `Rotation` | yes | `Rotation` | Transform() 616 | **a** | Full. |
| 52 | `-rotate270` | `Rotation` | yes | `Rotation` | Transform() 616 | **a** | Full. |
| 53 | `-chk` | `CheckForPlanet` | yes | `CheckForPlanet` | WriteLogFiles() 468 | **a** | `partial` — read only to choose a log header (Processor 468); frames are never rejected for a missing object. |
| 54 | `-ms` | `MinPlanetSize` | no | `—` | — | **b** | No PS property; `MinPlanetSize` is never mapped. |
| 55 | `-mp` | `MinPixelValue` | yes | `MinPixelValue` | Run() 303 / log 472 | **a** | Full (detection floor for centring and the log). |
| 56 | `-ctr` | `Centre` | yes | `Centre` | Run() 305 / log 468 | **a** | Full. |
| 57 | `-centre` | `Centre` | yes | `Centre` | same body as -ctr | **d** | Alias of -ctr: same `case` body (CommandLineParser 350-353). |
| 58 | `-shdw` | `ShadowEdge` | no | `—` | — | **b** | No PS property. |
| 59 | `-cx` | `CropWidth` | yes | `Crop` | Transform() 623 | **a** | `partial` — needs -cx or -cy (`BuildCrop`, Program 219-226); offsets alone are ignored. |
| 60 | `-cy` | `CropHeight` | yes | `Crop` | Transform() 623 | **a** | `partial` — needs a crop size. |
| 61 | `-ox` | `OffsetX` | yes | `Crop` | Transform() 623 | **a** | `partial` — read only when a crop size is present. |
| 62 | `-oy` | `OffsetY` | yes | `Crop` | Transform() 623 | **a** | `partial` — read only when a crop size is present. |
| 63 | `-en_surface_feature_tracking` | `SurfaceFeatureTracking` | yes | `SurfaceTracking` | Run() 311 | **a** | Full. |
| 64 | `-en_ss` | `EnableStabilisation` | no | `—` | — | **b** | No PS property. |
| 65 | `-afb_x` | `AnchorFeatureX` | yes | `AlignmentBox` | Run() 317 | **a** | `partial` — all four -afb_* are required (Program 181-183) and ignored without -en_surface_feature_tracking. |
| 66 | `-afb_y` | `AnchorFeatureY` | yes | `AlignmentBox` | Run() 317 | **a** | `partial` — all four required. |
| 67 | `-afb_width` | `AnchorFeatureWidth` | yes | `AlignmentBox` | Run() 317 | **a** | `partial` — all four required. |
| 68 | `-afb_height` | `AnchorFeatureHeight` | yes | `AlignmentBox` | Run() 317 | **a** | `partial` — all four required. |
| 69 | `-freject` | `AnchorFeatureReject` | no | `—` | — | **b** | No PS property. |
| 70 | `-aoi` | `AreaOfInterest` | no | `—` | — | **b** | No PS property. |
| 71 | `-aoi_x` | `AreaOfInterestX` | no | `—` | — | **b** | No PS property. |
| 72 | `-aoi_y` | `AreaOfInterestY` | no | `—` | — | **b** | No PS property. |
| 73 | `-aoi_width` | `AreaOfInterestWidth` | no | `—` | — | **b** | No PS property. |
| 74 | `-aoi_height` | `AreaOfInterestHeight` | no | `—` | — | **b** | No PS property. |
| 75 | `-aoi_reject` | `AreaOfInterestReject` | no | `—` | — | **b** | No PS property. |
| 76 | `-aoi_crop` | `AreaOfInterestCrop` | no | `—` | — | **b** | No PS property. |
| 77 | `-resize_en` | `ResizeFrames` | yes | `Resize` | Transform() 627 | **a** | `partial` — also needs -resize_width (Program 163-165). |
| 78 | `-resize_width` | `ResizeWidth` | yes | `Resize` | Transform() 627 | **a** | `partial` — only honoured with -resize_en. |
| 79 | `-resize_height` | `ResizeHeight` | yes | `Resize` | Transform() 627 | **a** | `partial` — only honoured with -resize_en. |

### Animation options (7 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 80 | `-anim_fwd` | `AnimateForward` | no | `—` | — | **b** | No PS property; there is no animation/repeat stage. |
| 81 | `-anim_fwd_lf_rep` | `RepeatForwardLast` | no | `—` | — | **b** | No PS property. |
| 82 | `-anim_rev` | `AnimateReverse` | no | `—` | — | **b** | No PS property. |
| 83 | `-anim_rev_lf_rep` | `RepeatReverseLast` | no | `—` | — | **b** | No PS property. |
| 84 | `-anim_all_rep` | `RepeatAll` | no | `—` | — | **b** | No PS property. |
| 85 | `-no_anim_rev_last_rep` | `NoReverseInLastRepeat` | no | `—` | — | **b** | No PS property. |
| 86 | `-anim_all_rep_always` | `RepeatAllForever` | no | `—` | — | **b** | No PS property. |

### Quality options (10 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 87 | `-q` | `QualityFrameCount / QualityAllFrames` | yes / no | `KeepFrames / -` | Select() 642 / none | **a** | `partial` — `-q=<n>` sets `QualityFrameCount` → `KeepFrames` (works); bare `-q` sets `QualityAllFrames`, which Program never maps. |
| 88 | `-qpcnt` | `QualityPercent` | yes | `KeepPercent` | Select() 643 | **a** | Full. |
| 89 | `-qold` | `QualityAlgorithm` | yes | `QualityAlgorithm` | Run() 298 / log 495 | **a** | Full (Ninox). |
| 90 | `-qhisto` | `QualityAlgorithm` | yes | `QualityAlgorithm` | Run() 298 / log 495 | **a** | Full (Histogram). |
| 91 | `-qtotal` | `QualityAlgorithm` | yes | `QualityAlgorithm` | none (default) | **d** | Collapsed to the default by Program.cs:167-172; `Pipp.Core.Quality.QualityAlgorithm` has no TotalPixel member. |
| 92 | `-qmin` | `QualityMinDownsample` | no | `—` | — | **b** | No PS property. |
| 93 | `-qmax` | `QualityMaxDownsample` | no | `—` | — | **b** | No PS property. |
| 94 | `-qinc` | `QualityIncrement` | no | `—` | — | **b** | No PS property. |
| 95 | `-qw` | `QualityWeighting` | no | `—` | — | **b** | No PS property, and no stacking path to apply it to. |
| 96 | `-nr` | `NoRenumber` | yes | `RenumberFrames` | Run() 388 | **a** | `partial` — `RenumberFrames` is read at 388, in the still-image branch only. |

### Output options (30 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 97 | `-osubdir` | `OutputSubdirectory` | no | `—` | — | **b** | No PS property; output is written straight into `OutputDirectory` (Processor 219). |
| 98 | `-od` | `OutputDirectory` | yes | `OutputDirectory` | Run() 219/340/357 | **a** | Full. |
| 99 | `-op` | `OutputPrefix` | yes | `Prefix` | Run() 336 | **a** | Full. |
| 100 | `-os` | `OutputSuffix` | yes | `Suffix` | Run() 336 | **a** | Full. |
| 101 | `-avi` | `Formats` | yes | `OutputExtension` | ChooseExtension 228 / Run 354 | **a** | `partial` — feeds `Formats` → `ChooseExtension` (Program 228-239): one container per run, priority ser > avi > fit > tif > png > bmp. |
| 102 | `-ser` | `Formats` | yes | `OutputExtension` | ChooseExtension 228 / Run 354 | **a** | `partial` — one container per run. |
| 103 | `-bmp` | `Formats` | yes | `OutputExtension` | ChooseExtension 228 / Run 354 | **a** | `partial` — one container per run. |
| 104 | `-tif` | `Formats` | yes | `OutputExtension` | ChooseExtension 228 / Run 354 | **a** | `partial` — one container per run. |
| 105 | `-png` | `Formats` | yes | `OutputExtension` | ChooseExtension 228 / Run 354 | **a** | `partial` — one container per run. |
| 106 | `-fits` | `Formats` | yes | `OutputExtension` | ChooseExtension 228 / Run 354 | **a** | `partial` — one container per run; extension is `.fit`. |
| 107 | `-gif` | `Formats` | yes | `OutputExtension` | none (.ser fallback) | **d** | `Formats = Gif` is mapped, but `ChooseExtension` has no `.gif` branch and `FrameSinkFactory.Extensions` no GIF writer; output falls back to `.ser`. |
| 108 | `-avi_codec` | `AviCodec` | no | `—` | — | **b** | No PS property; `AviWriterOptions` carries only PixelFormat/FpsRate/FpsScale (Formats/AviFormat.cs:26-45). |
| 109 | `-avi_quality` | `AviQuality` | no | `—` | — | **b** | No PS property; same writer-options gap. |
| 110 | `-avi_old` | `AviOldFormat` | no | `—` | — | **b** | No PS property. |
| 111 | `-avi_old4` | `AviOldFormat` | no | `—` | — | **b** | No PS property. |
| 112 | `-avi_safe_width` | `AviSafeWidth` | no | `—` | — | **b** | No PS property. |
| 113 | `-auto_fps` | `AutoFps` | no | `—` | — | **b** | No PS property; `Fps` is only ever -fps_rate/-fps_scale. |
| 114 | `-fps_rate` | `FpsRate` | yes | `Fps` | writers 348/359/395 | **a** | `partial` — `Fps` reaches only the AVI rate fields (FrameIo 104-111); SER and stills ignore it. |
| 115 | `-fps_scale` | `FpsScale` | yes | `Fps` | writers 348/359/395 | **a** | `partial` — AVI timing only. |
| 116 | `-gif_quant_type` | `GifQuantisation` | no | `—` | — | **b** | No PS property, and no GIF writer. |
| 117 | `-gif_border_tol` | `GifBorderTolerance` | no | `—` | — | **b** | No PS property, and no GIF writer. |
| 118 | `-gif_trns_pxl_tol` | `GifTransparentPixelTolerance` | no | `—` | — | **b** | No PS property, and no GIF writer. |
| 119 | `-gif_col_tbl_depth` | `GifColorTableDepth` | no | `—` | — | **b** | No PS property, and no GIF writer. |
| 120 | `-gif_lossy_comp` | `GifLossyCompression` | no | `—` | — | **b** | No PS property, and no GIF writer. |
| 121 | `-keepimgname` | `KeepImageFilenames` | yes | `KeepImageNames` | Run() 391 | **a** | Full. |
| 122 | `-nlz` | `NoLeadingZeros` | yes | `LeadingZeros` | none | **c** | `PipelineSettings.LeadingZeros` is never read; padding is always applied (Processor 382). |
| 123 | `-incqv` | `IncludeQualityValue` | no | `—` | — | **b** | No PS property. |
| 124 | `-rgb` | `SplitRgb` | no | `—` | — | **b** | No PS property. |
| 125 | `-drop` | `OutputFrameDrop` | no | `—` | — | **b** | No PS property. |
| 126 | `-wjpos_time` | `WinJuposTime` | no | `—` | — | **b** | No PS property. |

### Engine paths (5 switches)

| # | switch | PippOptions property | mapped in Program.cs? | PipelineSettings property | pipeline consumer | group | notes |
|---:|---|---|---|---|---|---|---|
| 127 | `-temp_dir` | `TempDirectory` | no | `—` | — | **b** | No PS property; the managed engine stages nothing on disk. |
| 128 | `-dark_tmp` | `MasterDarkTmpFile` | indirect | `MasterDark` | Transform() 581 | **a** | `partial` — loaded by `LoadMaster` into `MasterDark` (Program 192); needs -dark. |
| 129 | `-dark_pgm` | `MasterDarkPgmFile` | indirect | `MasterDark` | Transform() 581 | **a** | `partial` — preferred over -dark_tmp (Program 192); needs -dark. |
| 130 | `-flat_tmp` | `MasterFlatTmpFile` | indirect | `MasterFlat` | Transform() 585 | **a** | `partial` — loaded by `LoadMaster` into `MasterFlat` (Program 193); needs -flat. |
| 131 | `-flat_dark_tmp` | `MasterFlatDarkTmpFile` | no | `—` | — | **b** | `MasterFlatDarkTmpFile` is never read by Program or Processor. |

## Proposed `PipelineSettings` properties for group (b)

62 properties cover 67 of the 68 group (b) switches. The 68th, `-check`, is **not** a pipeline
setting: validate-then-exit is a `Program.Main` branch (like `-help`), so it should be handled before
`Map` is ever called.

Defaults follow the GUI/PippOptions defaults (`PippOptions.cs` remarks), so a missing switch and an
explicit GUI default produce the same pipeline. Enum types should be pipeline-owned (the pipeline
namespace does not reference `Pipp.Core.Cli` today); mirror the CLI enum values rather than importing
`PippAviCodec`/`PippGifQuantisation` into `PipelineSettings`.

| property | type | default | consuming stage | switches covered |
|---|---|---|---|---|
| `TestRun` | bool | false | `Run()`: stop after the first accepted frame, force `.bmp` | `-test_run` |
| `ShowDetectedPixels` | bool | false | output stage: draw the detection box before writing | `-sdp` |
| `SaveRejectedFrames` | bool | false | `Run()`: rejection paths write to `<out>/rejected/` | `-sr` |
| `NoRamBuffers` | bool | false | `Run()`: frame-list buffering strategy | `-no_ram_buff` |
| `DebugAvi` | bool | false | `AviWriter`: container diagnostics | `-debugavi` |
| `JoinMode` | bool | false | `Run()`: concatenate all inputs into one output | `-join` |
| `TempDirectory` | string? | null | writers: scratch path for staged output | `-temp_dir` |
| `HotPixelThreshold` | int? | null | `Transform` §3, before dark subtraction | `-hot_pixel` |
| `DarkHasBayer` | bool | false | calibration master preparation | `-dark_bayer` |
| `FlatHasBayer` | bool | false | calibration master preparation | `-flat_bayer` |
| `FlatDarkSubtraction` | bool | false | `Transform` §3: subtract `MasterFlatDark` from the flat | `-flat_dark` |
| `MasterFlatDark` | Frame? | null | `Transform` §3 | `-flat_dark_tmp` |
| `SaveMasterDark` | bool | false | post-calibration: write the stacked master dark | `-save_dark` |
| `SaveMasterFlat` | bool | false | post-calibration: write the stacked master flat | `-save_flat` |
| `SaveMasterFlatDark` | bool | false | post-calibration: write the stacked flat-dark | `-save_flat_dark` |
| `StartFrame` | int | 1 | `Run()`: read-loop start (1-based) | `-sf` |
| `FrameCount` | int? | null | `Run()`: read-loop stop | `-f` |
| `FramesFromCentre` | bool | false | `Run()`: range origin = centre | `-ctrf` |
| `InputFrameDrop` | int | 0 | `Run()`: skip N leading frames | `-ifd` |
| `SerBitDepth` | int? | null | `FrameSourceFactory`: SER decode | `-ser_bpp` |
| `TrueY800` | bool | false | decode: 8-bit frame interpretation | `-y8` |
| `RawHotPixelFilter` | bool | false | RAW decode | `-raw_hpf` |
| `RawDebayerAlgorithm` | `PippRawDebayerAlgorithm?` | null | RAW decode | `-raw_db` |
| `RawColourSpace` | `PippRawColourSpace?` | null | RAW decode | `-raw_ocs` |
| `RawHighlightRecovery` | int | 1 | RAW decode | `-raw_hl` |
| `ProtectBayer` | bool | false | `Transform` §5 geometry (keep the mosaic through crop/rotate) | `-pb` |
| `MinPlanetSize` | int? | null | `Run()`: detection gate (with `-chk`) | `-ms` |
| `ShadowEdge` | int? | null | `FrameAligner.CentreOffset` | `-shdw` |
| `EnableStabilisation` | bool | false | `Run()`: alignment branch | `-en_ss` |
| `AnchorFeatureReject` | bool | false | `Run()`: reject when `EstimateTranslation` returns null | `-freject` |
| `AreaOfInterest` | `PixelRect?` | null | `Run()`: AOI gate; `Transform` §5 crop | `-aoi`, `-aoi_x`, `-aoi_y`, `-aoi_width`, `-aoi_height` |
| `AreaOfInterestReject` | bool | false | `Run()`: reject frames leaving the AOI | `-aoi_reject` |
| `AreaOfInterestCrop` | bool | false | `Transform` §5: crop to the AOI | `-aoi_crop` |
| `QualityMinDownsample` | int | 3 | `QualityMetrics.Compute`: smallest factor | `-qmin` |
| `QualityMaxDownsample` | int | 5 | `QualityMetrics.Compute`: largest factor | `-qmax` |
| `QualityIncrement` | int | 1 | `QualityMetrics.Compute`: step | `-qinc` |
| `QualityWeighting` | bool | false | `FrameStacker`: weight frames by score | `-qw` |
| `OutputSubdirectory` | string | "" | `Run()`: output path prefix | `-osubdir` |
| `IncludeQualityValue` | bool | false | `Run()`: filename stem | `-incqv` |
| `SplitRgb` | bool | false | output stage: one file per channel | `-rgb` |
| `OutputFrameDrop` | int | 0 | output loop: write one frame in N | `-drop` |
| `WinJuposTime` | `PippWinJuposTimeMode?` | null | naming: which timestamp to write | `-wjpos_time` |
| `WinJuposDateTimeMask` | string? | null | naming: timestamp format | `-wjpos_dtmask` |
| `WinJuposLocalToUtc` | bool | false | naming: timestamp conversion | `-wjpos_lt2ut` |
| `WinJuposInputFps` | int? | null | naming: frame timing | `-wjpos_ipfps` |
| `AviCodec` | pipeline `AviCodec` enum | Dib | `AviWriterOptions` | `-avi_codec` |
| `AviQuality` | int | 10 | `AviWriterOptions` | `-avi_quality` |
| `AviOldFormat` | pipeline `AviOldFormat` enum | None | `AviWriter` size handling | `-avi_old`, `-avi_old4` |
| `AviSafeWidth` | bool | false | `AviWriter`: pad the frame width | `-avi_safe_width` |
| `AutoFps` | bool | false | `Run()`: derive `Fps` from the input header | `-auto_fps` |
| `GifQuantisation` | pipeline `GifQuantisation` enum | NeuralNet | `GifWriter` (not yet written) | `-gif_quant_type` |
| `GifBorderTolerance` | int | 5 | `GifWriter` | `-gif_border_tol` |
| `GifTransparentPixelTolerance` | int | 5 | `GifWriter` | `-gif_trns_pxl_tol` |
| `GifColorTableDepth` | int | 8 | `GifWriter` | `-gif_col_tbl_depth` |
| `GifLossyCompression` | int | 0 | `GifWriter` | `-gif_lossy_comp` |
| `AnimateForward` | bool | true | output stage: sequence assembly | `-anim_fwd` |
| `RepeatForwardLast` | int? | null | sequence assembly | `-anim_fwd_lf_rep` |
| `AnimateReverse` | bool | false | sequence assembly | `-anim_rev` |
| `RepeatReverseLast` | int? | null | sequence assembly | `-anim_rev_lf_rep` |
| `RepeatAll` | int? | null | sequence assembly | `-anim_all_rep` |
| `NoReverseInLastRepeat` | bool | false | sequence assembly | `-no_anim_rev_last_rep` |
| `RepeatAllForever` | bool | false | sequence / GIF loop | `-anim_all_rep_always` |

Two properties are prerequisites rather than pass-throughs:

- `MasterFlatDark` (from `-flat_dark_tmp`) needs a flat-dark decoder in `Program.LoadMaster`; today the
  flat path passes `null` for the PGM argument (Program 193).
- `AviCodec`/`AviQuality`/`AviOldFormat`/`AviSafeWidth` cannot take effect until `AviWriterOptions`
  grows codec/quality fields — it currently carries only `PixelFormat`, `FpsRate` and `FpsScale`
  (`Formats/AviFormat.cs:26-45`).

## Group (c) and (d) in detail

### (c) property exists, no consumer — 1 switch (+1 argument form)

| switch | property | why it is dead |
|---|---|---|
| `-nlz` | `LeadingZeros` | Never read. Processor 382 computes `digits` from the frame count and always pads; there is no branch on `LeadingZeros`. |
| `-db=<pattern>` (argument form) | `DebayerPattern` | Mapped at Program 138, never read. `Debayer.ToColour` uses the frame's own `Bayer` value, so the requested pattern is ignored. The bare `-db` form (which sets `Debayer`) does work. |

### (d) inert / alias — 5 switches

| switch | reason |
|---|---|
| `-help` | Handled by `Main`, prints `PippHelpText`, exits 0 (Program 33-37). No option, no settings. |
| `-centre` | Alias of `-ctr`; the two labels share one `case` body (CommandLineParser 350-353). |
| `-bin_average` | Sets `BinningCalculation = Average`, which is also the `null` default, so `BinningSum` stays false. Its only effect is to undo an earlier `-bin_sum`. |
| `-qtotal` | `PippQualityAlgorithm.TotalPixel` is parsed, but `Pipp.Core.Quality.QualityAlgorithm` has no `TotalPixel` member and Program's switch (`167-172`) sends it to `PippDefault`. It also resets an earlier `-qold`/`-qhisto`. |
| `-gif` | `Formats = Gif` is mapped, but `ChooseExtension` (Program 228-239) has no `.gif` branch and `FrameSinkFactory.Extensions` (FrameIo 84) has no GIF writer. Output silently becomes `.ser`. |

### PipelineSettings properties with no producer

| property | type | default | read at | note |
|---|---|---|---|---|
| `Stack` | `StackMethod?` | null | Run 338 | no CLI switch; stacking is unreachable |
| `PippLayout` | `bool` | true | Run 372 | no switch; `-osubdir` is the nearest concept |
| `WriteLogFile` | `bool` | true | Run 403 | no switch; logs cannot be disabled |
| `OverexposedThreshold` | `int` | 0 (→ frame max) | 548-551 | no switch; changes what `-oe` counts as overexposed |
| `AlignmentSearchRadius` | `int` | 32 | Run 317 | no switch; fixed at 32 px |
| `EngineVersion` | `string` | "v2.5.9" | 459, 482, 507 | constant, intentionally not CLI-driven |

`LogArguments` is not in this list: it is set in `Program.Main` (71) from `effectiveArgs`, not from a
switch.

## Docs drift: `docs/switch-matrix.csv`

The CSV has **126 rows** and is stale in both directions (it was not edited — out of scope here).

- **Missing switches (5):** `-help`, `-centre`, `-check`, `-debugavi`, `-bin_average`.
- **Marked `parsed-only` but actually wired (19):** `-afb_x`, `-afb_y`, `-afb_width`, `-afb_height`,
  `-chk`, `-convmono`, `-cx`, `-cy`, `-dark_tmp`, `-dark_pgm`, `-flat_tmp`, `-input_gain`,
  `-keepimgname`, `-mp`, `-ox`, `-oy`, `-qpcnt`, `-resize_width`, `-resize_height`.
- **Marked `executed`/`verified` but not wired (3):** `-ms` ("verified" — no `Program` mapping at all),
  `-nlz` ("executed" — `LeadingZeros` is dead), `-qtotal` ("executed" — collapses to the default).

That is 22 wrong status rows out of 126. `-ms` is the most misleading: it is the only switch the CSV
calls *verified* that never reaches the pipeline.

## Ten highest-value switches to wire first

Ordered by impact ÷ effort. "Cost" is S (a few lines in one stage), M (new stage logic), L
(structural change).

| # | switch | today | why it matters | first wiring step | cost |
|---:|---|---|---|---|---|
| 1 | `-f` | Read loop runs to EOF (Processor 249-325). | The GUI emits `-f` for every run with an end frame; today those runs process the whole capture. | `FrameCount` → `Run()` stop condition | S |
| 2 | `-sf` | No start index; every run begins at frame 1. | A range is meaningless without a start; same loop as `-f`. | `StartFrame` → `Run()` skip to N | S |
| 3 | `-ctrf` | Range origin fixed at frame 1. | The GUI emits `-ctrf` together with `-f` whenever an end frame is set (notes/cli-spec.md:67), so `-f` alone picks the **first** N frames instead of the centre N. | `FramesFromCentre` → `Run()` computes the start from `source.FrameCount` | S |
| 4 | `-ifd` | Leading frames cannot be dropped. | Only way to skip a corrupt preamble; identical loop change. | `InputFrameDrop` → `Run()` skip | S |
| 5 | `-osubdir` | Ignored; output lands directly in `-od`. | GUI runs expect `<out>/pipp_<stamp>/`; every run currently writes one level too high and later runs collide. | `OutputSubdirectory` → `Path.Combine` at Processor 219 | S |
| 6 | `-ms` | Not mapped; `-chk` only selects a log header (Processor 468). | “Reject frames without a planet” is a headline feature and is missing end-to-end; `MinPlanetSize` is the gate. | `MinPlanetSize` + a real `CheckForPlanet` rejection in `Run()` | M |
| 7 | `-qmin` / `-qmax` / `-qinc` | Ignored; quality is scored at full resolution. | Every `-q` run ranks differently from the oracle because the downsample search is absent. | `QualityMin/MaxDownsample`, `QualityIncrement` → `QualityMetrics.Compute` | M |
| 8 | `-en_ss` | Ignored, while `-en_surface_feature_tracking` works. | Tracked runs skip the stabilisation the GUI user asked for; `-afb_*` is emitted under the same guard. | `EnableStabilisation` → `Run()` alignment branch | S |
| 9 | `-sr` | Ignored; rejected frames are silently dropped. | Without saved rejects, detection/quality failures are undiagnosable; cheap once rejection paths exist. | `SaveRejectedFrames` → `Run()` rejection paths | S |
| 10 | `-join` | Printed in the banner only (Program 91). | Join mode is a top-level PIPP feature; N inputs currently produce N outputs. | `JoinMode` → `Run()`: one sink across inputs | L |

Why these ten: the first five live in `Run()`'s read loop or path construction, so they are cheap and
they fix the most common GUI invocations (any run with an end frame, any subdirectory run). `-ms` plus
a real `-chk` rejection closes the only *verified*-but-absent feature in the CSV. `-qmin/-qmax/-qinc`
is the difference between plausible and matching quality rankings. `-join` is last only because it is
the only structural change in the list.

**Deliberately not in the top ten:** `-avi_codec`/`-avi_quality` (blocked on `AviWriterOptions`),
`-gif_*` (no GIF writer), `-qw` (needs `Stack`, which no switch can reach), `-wjpos_*` (naming niche),
`-raw_*` (still-image RAW path, not the planetary-video mainline). Next tier after this list:
`-incqv`, `-rgb`, `-drop`, `-save_dark`/`-save_flat`, `-freject`.

## Caveats

- Line numbers are from the working tree at audit time; `Processor.cs` and `Program.cs` shift easily.
- "Implemented" means *the switch changes a value a live stage reads*, not that the reconstructed
  algorithm matches the native engine bit-for-bit — parity status is a separate question tracked in
  `docs/status.md`.
- `-db_type`, `-qold`, `-qhisto` select algorithms whose numerical fidelity is still being verified;
  they are counted as implemented because the selection does reach the stage.
