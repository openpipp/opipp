# Validation datasets

Real captures used to check that OPIPP works beyond the synthetic fixtures and the original Saturn
sample. **None of these files are committed or redistributed**: `samples/` is git-ignored and every set
below is used for evaluation only, under the terms of its source. Outputs derived from third-party
captures are not published without the attribution their licence requires.

## What is here

| Dataset | Object | Date | Format | Content | Source / terms |
|---|---|---|---|---|---|
| `saturn30.ser` | Saturn | — | SER, 8-bit | 30 × 640×640, Bayer-tagged RGGB | Third-party evaluation capture already used for native parity; not redistributable |
| `slooh/jupiter_ganymede_2011-09-08.avi` | Jupiter + Ganymede | 2011-09-08 | AVI, I420 | 620 × 640×480, 10 fps | Slooh Lucky Imaging Challenge. Slooh terms: derived images must carry the Slooh logo if published; used internally only |
| `slooh/jupiter_png/` | Jupiter + Ganymede | 2011-09-08 | 49 × PNG | frames extracted from the above | Derived for pipeline testing |
| `strata/2022-10-25-0055_2.ser` | Saturn | 2022-10-25 | SER, 8-bit | 8,984 × 640×640, Bayer-tagged RGGB, 3.5 GB | Mac Observatory "Strata" sample files, provided for app evaluation and testing |
| `strata/.../2023-11-02-1756_5-KL-IR685-Jup_lapl6_ap176.tif` | Jupiter | 2023-11-02 | TIFF | 4 MB pre-stacked image (IR685) | Same Strata sample set |
| `stills/moon.png` (from `.jpg`) | Full Moon | 2010 | PNG / TIFF | 1920×1825 | Wikimedia Commons, *FullMoon2010*, Gregory H. Revera, CC BY-SA 3.0 |
| `stills/mars.png` (from `.jpg`) | Mars | 2003 | PNG / TIFF | 1920×1920 | Wikimedia Commons, *OSIRIS Mars true colour*, ESA & MPS for OSIRIS Team, CC BY-SA 3.0 IGO |

## What each set exercised

| Check | Result |
|---|---|
| Jupiter: 49 real frames → joined mean integration | **SUCCESS**, one 640×480 colour SER; OPIPP's own PNG conversion shows a clean Jupiter disc with Ganymede |
| Jupiter: batch mode without `-join` | one output per input (49 files), as documented — join is required for a sequence |
| Saturn (`saturn30.ser`): raw CFA + `-ctr` | correctly **rejected** (odd centring shifts move the mosaic phase); no output |
| Saturn 2022 (Strata): 200-frame subset, raw CFA + `-ctr` | **rejected** with the phase message; no output |
| Saturn 2022 (Strata): 200-frame subset, `-db` + `-ctr` + mean | **SUCCESS** in 3.2 s, one 619×598 colour image; visually clean rings and globe |
| Jupiter 2023 (Strata TIFF) → PNG | **SUCCESS**; clean disc, expected IR685 green cast |
| Saturn (`saturn30.ser`): `-db` + `-ctr` + mean | **SUCCESS**, one colour image ~619×598 |
| Moon: PNG → FITS (8-bit colour) → PNG | round-trip correct, no channel swap or inversion |
| Mars: PNG → TIFF → view | correct orientation and colour |
| AVI input, Slooh original | **rejected with a clear error**: stream compression `I420` is not implemented by the managed AVI reader |
| AVI input, ffmpeg `rawvideo bgr24` | **rejected**: "frame 1 is truncated: 0 bytes available" — the reader does not handle this chunk layout |

## Findings that came out of real data

1. **I420 / planar YUV AVI is common and unsupported.** The error is controlled and names the codec,
   but a real capture cannot be opened. This is a genuine input-format gap, not a corruption.
2. **ffmpeg's rawvideo BGR AVI is not readable** even though the codec is `rawvideo`; the reader sees
   zero bytes for the first frame. Uncompressed DIB AVIs written by other tools may be fine; this
   layout is not.
3. **Multiple inputs are batch inputs.** Passing 49 PNGs without `-join` writes 49 outputs. This is
   correct but easy to misread; the fixture-gated regression now covers the joined form.
4. **Raw CFA + centring needs debayer.** The engine correctly refuses the phase-changing combination,
   and the working path is `-db` first. The 0.2.2 help/error changes point at it explicitly.

## Reproducing

```bash
# Jupiter: joined mean integration
Opipp.Cli.exe -join -ser -stack=mean -od=<out> samples/slooh/jupiter_png/*.png

# Saturn: raw rejection, then debayered success
Opipp.Cli.exe -ser -stack=mean -ctr -mp=40 -od=<out> samples/saturn30.ser      # exit 1
Opipp.Cli.exe -db -ser -stack=mean -ctr -mp=40 -od=<out> samples/saturn30.ser  # exit 0

# Still round-trips
Opipp.Cli.exe -fits -od=<out> samples/stills/moon.png
Opipp.Cli.exe -tif  -od=<out> samples/stills/mars.png
```

## Not yet covered

- A lunar or solar **sequence** (only stills so far), and Mars as a sequence.
- Compressed planetary video that needs an FFmpeg-backed decode path.
- Deep-sky FITS with non-integer or scaled data (the float-FITS caveat in `status.md` applies).
