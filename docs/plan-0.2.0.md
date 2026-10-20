# 0.2.0 — Final image

Scope: make integration (stacking of selected science frames) a real, user-facing operation with a
defined contract. Join and calibration-master creation stay separate operations.

## Contract

| Decision | Value |
|---|---|
| Methods | mean, sum, median |
| Precision | accumulate in `double`; expose `IntegrationResult.PrecisionSamples`; quantise on output with half-away-from-zero rounding and clamping |
| Registration | integer part always applied; fractional part applied by bilinear resampling when `Subpixel` is on |
| Borders | `CropToOverlap` (default) removes pixels any contributor cannot fill; `KeepCanvas` keeps the first frame's canvas and reports coverage, never inventing edge samples |
| Median scale | in-memory up to 64 frames; above that, disk-backed bands with an exact result and a declared scratch budget |
| Metadata | method, contributors (source, weight, shift), coverage per pixel |
| CFA | one consistent pattern only; mixed phases rejected before any sample or provenance change |

## Acceptance gates

1. A constant frame remains constant over the valid overlap after a fractional shift (no edge bias, no resampling ripple beyond one sample).
2. An integer shift moves content exactly.
3. `[100, 101]` mean is `100.5` in `PrecisionSamples`; the quantised frame follows the documented rounding rule.
4. Coverage counts equal the number of contributors for every pixel in the output rect, and zero outside it.
5. Sum clamps at the output depth; median returns the exact middle sample for odd and the documented even rule.
6. Median at 65 and 1,000 small synthetic frames matches an independent per-pixel reference and stays within the declared budget.
7. Cancellation leaves no partial output and no staging files.
8. CLI and GUI select the method, coverage policy and subpixel registration, and produce exactly one file.
9. Mixed CFA phases, mixed geometry and mixed depth fail with a diagnostic naming the reason.

## Phases

1. Core integrator (mean/sum, subpixel, coverage, metadata) and disk-backed median.
2. Pipeline wiring, CLI surface, GUI controls, output naming, progress and cancellation.
3. Documentation, roadmap/site, release notes, 0.2.0-alpha release.
