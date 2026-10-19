# Parity harness

`tools/Opipp.Parity` runs the original engine (`bin/pipp_core.exe`) and the managed engine
(`src/Opipp.Cli`) over the same fixtures with the same switches, then compares the produced frames by
SHA-256 of their samples.

```bash
export DOTNET_ROOT="$HOME/.dotnet-sdk"; export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet build src/Opipp.Cli/Opipp.Cli.csproj            # rebuild BOTH consumers
dotnet build tools/Opipp.Parity/Opipp.Parity.csproj
dotnet run --project tools/Opipp.Parity --no-build    # prints ALL CASES MATCH or divergences
```

Rebuilding only the harness leaves the CLI running a stale copy of the library, which looks exactly
like a regression. Rebuild both.

## Which options each case covers

See the case table in [`status.md`](status.md). A case covers only the switches it passes - "15/15"
means fifteen option sets, not "the engine matches".

## Real data (`--real`)

`samples/` holds a 4.8 GB third-party capture and is git-ignored, so the real-data cases are opt-in:

```bash
dotnet run --project tools/Opipp.Parity --no-build -- --real
dotnet run --project tools/Opipp.Parity --no-build -- --real --real-input path/to/capture.ser
```

They run the three stretch-sensitive option sets (`-tif`, `-hs=204`, `-hs=204 -hs_bp`) over the
30-frame Saturn capture and compare all 30 outputs the same way as the synthetic cases. This is the
check that exposed the image-dependent stretch: a synthetic disc whose bright tail saturates at 255
cannot tell a constant gain apart from a measured white point.

## Quantifying a known gap (`--measure`)

Acknowledged cases (`KnownGap`, `KnownDivergence`) normally skip both engines, so nothing is learned
about the size of the difference. `--measure` runs them anyway and prints pixel statistics:

```bash
dotnet run --project tools/Opipp.Parity --no-build -- --measure
dotnet run --project tools/Opipp.Parity --no-build -- --measure --only debayer-interpolated
```

For each differing frame it reports differing pixels and samples (per channel), max/mean absolute
delta, a delta histogram, and whether the difference sits on local edges or in flat areas. The
verdict does not change: a measured gap still counts as a gap, never as a match. `--only <name>`
runs a single case.

## Debayer

`debayer-interpolated` (`-tif -db -db=2 -db_type=0`) is a real comparison as of this harness. The
original's bi-linear demosaic uses two rules the port initially missed:

- **Truncating integer division.** Averages are C++ `/` (floor), not round-half-up. Green at an
  R/B site is one `(N+E+S+W)/4`, not `((E+W)/2 + (N+S)/2)/2`.
- **A dcraw-style `border_interpolate(1)` pass.** The outer one-pixel ring is not interpolated;
  each channel there is the mean of the mosaic's samples of that colour inside the clipped 3x3
  window, computed after (and overriding) the interpolation.

Measured before the fix: 1147-1161 of 3072 pixels differed (37.3-37.8%), max |delta| 3, mean 1.04
over differing samples (0.21 over all samples), and the error was spread through flat areas - 1046
of 1147 differing pixels were not on a local edge, so it was never an edge-adaptive-kernel problem.
After the fix all four frames are byte-identical, and the same mosaic decoded as patterns 0-3 also
matches the oracle. 16-bit Bayer input is not covered by this case.

## Adding a case

1. Add a `ParityCase("name", ["-switches"], Input: "fixture")` entry.
2. If it needs new input, add a fixture writer (see `WriteSyntheticSer`, `WriteBayerSer`) and register
   it in the input switch.
3. For selection/ordering features use `Mode: CompareMode.SetEquality`.
4. Run it, and expect the first run to fail: that is the point.

## Fixture rules (learned the hard way)

- The detector silently discards frames (exit 0, no files) when the object touches an edge, when the
  background is a ramp, or when the measured diameter is below `-ms` (default 15). Use a flat dark
  field and relax `-ms` to reach the transform under test.
- Read the oracle's own log (`<out>/logs/<name>_pipp_log.txt`) when it produces nothing - it names
  the reason: "no planet detected", "planet diameter below minimum", "Calculated minpixel value".
- Invocation: switches on **argv**, input paths too, and stdin closed with a literal `DONE` line.
  EOF alone hangs the engine with zero CPU.

## Comparing correctly

- Hash pixel content, not file bytes: headers legitimately differ (comments, `DATE`, `SOFTWARE`).
- Normalise format families before comparing (`.fit`/`.fits`, `.tif`/`.tiff`), or identical pixels
  report as a divergence.
- Include container extensions in the output filter (`.avi`, `.ser`), otherwise a working case looks
  like it produced nothing.
- On mismatch the harness prints the first five differing samples with coordinates, plus a count.

## What "verified" means

A switch is `verified` only when a case compares its output against the original and matches.
`executed` means it changes our output but has no oracle case. The distinction lives in
[`switch-matrix.csv`](switch-matrix.csv) - keep it current when adding cases.
