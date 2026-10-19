# HANDOFF - start here

Cold-start guide for continuing this project in a fresh session. Read this first, then the two
documents it points at. Everything here is verified as of the **v0.1.10-alpha** release.

## What the project is

A reverse-engineered, managed (.NET 10 / C# 13) reconstruction of **PIPP 2.5** (Planetary Imaging
PreProcessor, by Chris Garry), built to become an open-source, modernised replacement. The original
is closed-source; `bin/`, `decompiled/` and `ghidra/` are read-only reference material and are
git-ignored, never published or committed. The repository is GPLv3 for its own code and artwork (see
`LICENSE` and `NOTICE`).

## State in one table

| Item | Value |
| --- | --- |
| Repo | <https://github.com/openpipp/opipp> (public, GitHub organization `openpipp`) |
| Build | `dotnet build OPIPP.slnx` - clean |
| Tests | **926 tests, 0 failing** (9 projects; 2 skip without `samples/saturn30.ser`) |
| Parity | **27 compared, 27 matched**, 2 deliberate differences, 0 gaps (`--real`) |
| Switches | **131 parse; 76 verified / 43 wired / 12 inert** (see `docs/status.md`) |
| Managed code | Core 11.8k lines, Cli 0.3k, App 14.5k, tests 5.5k |
| Reference | `decompiled/` 14,277 lines C#; `ghidra/out/pipp_core.c` 4,229 functions |
| Real data | `samples/` 4.8 GB (git-ignored, evaluation-use third-party capture) |

Run everything with:

```bash
export DOTNET_ROOT="$HOME/.dotnet-sdk"; export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
cd "<repo>"
dotnet test OPIPP.slnx --nologo
dotnet build src/Opipp.Cli/Opipp.Cli.csproj && dotnet build tools/Opipp.Parity/Opipp.Parity.csproj
dotnet run --project tools/Opipp.Parity --no-build              # expect: ALL CASES MATCH
dotnet run --project tools/Opipp.Parity --no-build -- --real    # + 30 real Saturn frames
```

Rebuild **both** consumers before a parity run - a stale `Opipp.Cli.exe` silently compares old output.

## Phase 4a item 1 - histogram stretch: DONE

**`-hs` now matches the original on real data as well as on synthetic fixtures.**

The recovered rule (all from `FUN_14009b4a0`, the stretch routine; option at `options + 0x1DC`):

- The engine measures a **white point** per channel: the mean of the frame's **25 brightest samples**
  (`0x19`), accumulated from the top of the histogram, weighted boundary bin, integer division.
- **Gain** = `whitePoint * (max/100) / (white - black)`, with `max` = 255 or 65535 (the constants
  `2.55` and `655.35` at `0x140137788` / `0x140137790`). `black` is 0 unless `-hs_bp`, which uses the
  mean of the 25 *darkest* samples.
- **Mapping** = `truncate((v - black) * gain)`, clipped, below-black → 0.
- `-hs=xx%` / `-hs=xxp` → `xx*255/100` in the same field. `-hsrgb` = per-channel gains; without it
  the **smallest** channel gain is used for all channels. No channel equalisation happens.

The old "constant gain `xxxx/100` with a −1 bias" model was a special case: fixtures whose bright tail
saturates at 255 give exactly `wp/100`, and the "bias" is just the double product `204*2.55/255`
landing a hair below 2.04.

Evidence (all pixel-exact against `bin/pipp_core.exe`):

- 30 real frames, `-tif -hs=204` - gains 2.2816-2.3432, one per frame (W = 222..228, median 225 →
  2.3120). The earlier six-frame estimate (2.2921-2.3068) came from rounded pixel pairs; the six
  frames it sampled actually give 2.2916-2.3120.
- 30 real frames, `-tif -hs=204 -hs_bp`.
- Synthetic colour fixture, `-hs=204` and `-hs=204 -hsrgb` (min-gain vs per-channel differ by up to 52).
- Synthetic 32x24 disc keeps the constant 2.04, so the old measurement stays valid.

## Phase 4 status

Done: detection threshold (item 1), centring (item 2b, 30/30 pixel-exact), quality scoring (item 3,
six decimals), detection and `-chk`/`-ms` (item 2). All committed and covered by parity cases.

Also done: surface tracking (`-en_surface_feature_tracking` + `-en_ss` + `-afb_*`, not `-st`) wired
to `SurfaceTracker` with a documented +-1 px residual, and stacking arithmetic corrected to the
engine's truncating integer division (`-meanmed` does not exist in 2.5.9).

Open, in the order I would take them:

2. **Quality estimator** - historical note, now resolved: engine frame 1 = 1065.7 vs frame 11 = 813.3
   was a mask-buffer reuse artefact, reproduced exactly (see M18).
   Pipeline located: subsample `FUN_1400af990` -> normalise by mean -> 3x3 blur `FUN_1400afc90` ->
   metric `FUN_1400aecf0` (mode 3: `FUN_1400afda0`). Global statistics are ruled out (see journal M7).
   Producer found: `FUN_1400af2d0` returns the score, inserted at 86600 via
   `FUN_1400b03e0(container, score, frameIndex)`. **Ghidra lost the return value** - it types the
   function `void` with three bare `return;` while the caller reads a `double`. Next step is
   disassembled (journal M12 - M11 was decoded from misparsed section headers and is void). The
   metric is: crop a 10% margin, accumulate `(a-c)^2 + (b-c)^2` over neighbouring samples of the
   blurred subsampled image, return `sum / count / 10.0`, or -1.0 when nothing exceeds the threshold;
   the outer driver scales by `FUN_1400b00b0(...) / 3251250.0`. Constants all live in `.rdata`
   (10.0 at RVA 0x120fd0, -1.0 at 0x132950, 3251250.0 at 0x13a2b0, 60000.0 at 0x13a2a8, -0.1 at
   0x13a2b8). A Python model of that metric does **not** reproduce the engine's ranking (whole frame
   and object bbox both give ~0.95-1.0 where the engine gives 1.3102), so two parts are still
   missing: how `xmm6` accumulates across the `-qmin..-qmax` stride loop, and the content-dependent
   final scale `FUN_1400b00b0(image, flag) / 3251250.0` (that function builds a 0x400-byte histogram,
   it is not a getter). Read `FUN_1400b00b0`'s tail next.
3. **Detection rejection reasons** (item 2) - `-chk` now logs the threshold; the object finder
   `FUN_140077560` returns -1 for "no object" and the per-reason counters are not reproduced.
4. **Weighting and bounds** (item 3) - `-qw`, `-incqv`, `-freject`.
5. **Surface tracking / `-ctrf` / `-en_ss`** (item 4).
6. **`-meanmed`, `-join`** (item 5).

## Next task - Phase 4a item 2b: the centring centroid

The threshold is solved (see the commit before this one): `FUN_14009ab40`, 1/3-scale image, five
smallest row maxima, `m + (meanAbove-m)/4`; exact on real frames, one low on synthetic discs.

`-ctr` still diverges, but it is now bounded: the transform is a **pure integer translation**, the
vacated border is filled with the frame's background level (3 on the Saturn capture), and
`shift(-19,+37)` with fill 3 reproduces oracle frame 1 **exactly**. What is missing is the centroid
rule: binary, v, v-thr, v^2 and 1/3-scale centroids all land 2-4 px from the engine's implied
measurement (339,282) for target (320,319 or 320,318).

Current best hypothesis (journal M9): the offset is a **binary centroid at a threshold around 8**
(not the detection threshold of 18), truncated toward zero against the target `(320,319)`. That
reproduces frame 1's shift `(-19,+37)` exactly and misses x by one on frames 2 and 4. Next: sweep the
threshold over frames with unclipped objects, and test whether the mask is built on the 3x3-blurred
1/3-scale image (which would quantise the centroid in thirds). Then extend `--real` with a
`real-centre` case (30 frames), which should go from 0/30 to 30/30 identical.

Two smaller items found while doing the stretch, both recorded in `status.md`:

- **Debayer was never really verified.** `-db=<n>` only selects the pattern; the bare `-db` flag
  enables interpolation. With both, our output differs from the original by 1 LSB on ~37% of interior
  pixels (up to 3 at edges). The old `debayer` case only ran `-db=2`, i.e. it compared two no-ops; it
  is now renamed `db-pattern-only` and a `debayer-interpolated` known-gap case exists.
- `-hs_bp` was parsed but never wired to the pipeline; it is now wired and verified.

Real-data check (repeatable):

```bash
dotnet run --project tools/Opipp.Parity --no-build -- --real
# real-plain / real-stretch / real-stretch-bp over samples/saturn30.ser, 30 frames each
```


## Gotchas that cost time before

- **Oracle invocation**: switches on **argv**, and stdin must end with a literal `DONE` line. EOF
  alone hangs it (zero CPU, no output). Stdin-only produces nothing.
- **`-db=<n>` is not "debayer"**: it only sets the Bayer pattern. Only the bare `-db` flag enables
  interpolation, so a parity case needs both (`-db -db=2`).
- **The stretch statistic is 25 samples**, not a percentage: a fixture must have at least 25 pixels in
  its bright tail before it exercises the white-point measurement, and a frame with fewer than 25
  samples above 0 reports a white point below its maximum (that is the engine's own behaviour).
- **Synthetic fixtures must satisfy the detector**: flat dark background, object not touching an
  edge, `-ms` relaxed (default 15) when the object is small.
- **FITS**: the original stores rows bottom-up, flips every FITS on read, and writes 16-bit samples
  byte-swapped under an `II` header (ffmpeg cannot read them either - we deliberately diverge).
- **SER colour** uses ColorID **100 = RGB, 101 = BGR**; 16/17 are bayer/mono ids in PIPP's dialect.
- **Quality selection** numbers outputs from 0, plain runs from 1, padded to the frame count.
- `OPIPP.slnx`, not `.sln`.

## Where to read next

| Document | Why |
| --- | --- |
| [`roadmap.md`](roadmap.md) | phases, scoreboard, recorded decisions |
| [`status.md`](status.md) | verified behaviours, known divergences, environment |
| [`parity.md`](parity.md) | harness usage and fixture rules |
| [`journal.md`](journal.md) | what each session proved, with commit hashes |
| [`../notes/engine-behaviour.md`](../notes/engine-behaviour.md) | GUI token stream, real-data findings, protocol details |
