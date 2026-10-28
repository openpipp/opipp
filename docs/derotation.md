# Field rotation

PIPP aligns frames by translation only. A capture whose field rotates — a handheld camera, camera
roll, or an alt-az mount without a derotator — therefore smears as the radius grows, and the limb is
where it shows first. OPIPP adds optional per-frame derotation to final-image integration.

## Verification: the original has no derotation

Checked against the recovered source and the switch surface, not assumed:

- The only rotation in the original engine is the fixed 90/180/270 family (`-rotate90`, `-rotate180`,
  `-rotate270`). There is no arbitrary-angle rotation, no `derotate`, and no field-rotation string.
- Every alignment path — `-ctr`, surface-feature tracking, `-ctrf`, stabilisation — produces integer
  or subpixel **translation**. No trigonometric term appears in the alignment path.
- 131 switches are documented; none corrects rotation.

The symptom reproduces exactly. A 30-frame, 512 px lunar sequence rotated 0.05°/frame (1.45° total)
stacked translation-only retains **26%** of the single-frame edge sharpness; at 4°/frame (116° total)
it retains **2.6%**. Displacement is θ·r, so the centre stays sharp while the limb blurs — the
behaviour reported after a minute of handheld lunar video.

## What OPIPP does

`-derotate=on` (default off) with `-derotate_search=<degrees>` (default 5, range 0.1–45). It is an
OPIPP extension, valid only with `-stack=`; ordinary conversion is untouched and `-derotate=off`
produces byte-identical output to omitting the switch.

1. **Estimate.** Each frame's angle is measured against frame 0 by normalised cross-correlation on
   luma: both images are rotated by ±θ/2 so neither side is favoured by resampling, the full
   surviving rectangle is scored (the limb carries the signal), the working side is capped at 256 px,
   and translation is sub-pixel. The previous frame's angle predicts the next search window, which
   widens once if the peak lands on its edge. Angles do not accumulate.
2. **Rotate.** Bicubic Keys convolution (`a = -1.0`, border-zero), about the image centre. The
   coefficient is the sharpest whose ringing stays around one level; bilinear retains far less.
3. **Crop.** Every frame is cropped to one centred rectangle that all rotations can fill, so the
   frames stay rectangular and mean, sum and median all work unchanged.
4. **Translate.** A correlation offset (`MatchOffset`) finishes alignment. Offsets below 0.05 px are
   passed as zero, because applying them costs sharpness for no alignment gain.

## Measured on real lunar texture

Outer-annulus Laplacian variance (r 0.35–0.48 of half-width), same command line with and without
`-derotate=on`; single frame = 1567.4.

| Sequence | Translation-only | Derotated | Gain | Retained |
|---|---:|---:|---:|---:|
| 30 frames, 0.05°/frame (1.45° total) | 411.7 | **994.4** | **2.42×** | 63.4% |
| 30 frames, 4°/frame (116° total) | 40.3 | **866.0** | **21.5×** | 55.3% |
| 40 frames, 0.06°/frame + handheld drift (2.34°) | 34.2 (translation-only `-ctr`: 268.4) | **813.3** mean, **828.0** median | **23.8×** plain; 3.0× centred | 53.1% |
| 5 identical stills | 1567.4 | 1567.4 | — | 100% (mean abs diff 0.0000) |

Angle accuracy against brute-force truth averages 0.039° (max 0.13°). The mild 1.45° case is the
weakest result and is reported as such: an internal 2.5× target was missed by 3%. Realistic handheld
rotation is larger, where the gain is an order of magnitude.

## Limits

- In-plane rotation only. Perspective, tilt and shear are not modelled.
- Rotation must be observable: a featureless or perfectly rotationally symmetric disc estimates 0°.
  Lunar and planetary surfaces normally carry enough texture.
- `-derotate_search` bounds the search. Rotation beyond it is clipped, not extrapolated.
- Selections longer than 120 frames refine a subset and interpolate the rest; shorter selections
  refine every frame.
- Cropping reduces the canvas: 512² → 499² at 1.45°, 640×480 → 575×431 at 5°.
- Raw Bayer frames are refused with the usual `-db` guidance; derotating a mosaic has no meaning.
- Median integration re-reads each frame per band and re-rotates it, so derotated median costs more
  than derotated mean.
- The sharpness numbers are relative comparisons on one texture, not a scientific image-quality score.

## Usage

```bash
# mean stack with derotation; search defaults to 5 degrees
Opipp.Cli.exe -join -ser -stack=mean -derotate=on -od=out frames/*.png

# widen the search for a long handheld capture
Opipp.Cli.exe -join -ser -stack=median -derotate=on -derotate_search=15 -od=out frames/*
```

GUI: **Processing → Final image integration… → Correct field rotation (derotate)**, with the search
range beside it. The setting persists as `FINAL_IMAGE_DEROTATE` and
`FINAL_IMAGE_DEROTATE_SEARCH`.
