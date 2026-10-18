# HANDOFF - start here

Cold-start guide for continuing this project in a fresh session. Read this first, then the two
documents it points at. Everything here is verified as of commit **b1756a4**.

## What the project is

A reverse-engineered, managed (.NET 10 / C# 13) reconstruction of **PIPP 2.5** (Planetary Imaging
PreProcessor, by Chris Garry), built to become an open-source, modernised replacement. The original
is closed-source; `bin/`, `decompiled/` and `ghidra/` are read-only reference material and must never
be published or committed. No `LICENSE` until the author's permission is obtained.

## State in one table

| Item | Value |
| --- | --- |
| Repo | `C:\Users\ilanp\Projects\PIPP-RE` (git, local only, 5 commits, HEAD `b1756a4`) |
| Build | `dotnet build PIPP.slnx` - clean |
| Tests | **291 passing, 0 failing** (7 projects) |
| Parity | **15 cases: 13 verified, 1 deliberate divergence, 1 known gap** |
| Managed code | Core 11.5k lines, Cli 0.3k, App 14.5k, tests 5.5k |
| Reference | `decompiled/` 14,277 lines C#; `ghidra/out/pipp_core.c` 4,229 functions |
| Real data | `samples/` 4.8 GB (git-ignored, evaluation-use third-party capture) |

Run everything with:

```bash
export DOTNET_ROOT="$HOME/.dotnet-sdk"; export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
cd "C:/Users/ilanp/Projects/PIPP-RE"
dotnet test PIPP.slnx --nologo
dotnet build src/Pipp.Cli/Pipp.Cli.csproj && dotnet build tools/Pipp.Parity/Pipp.Parity.csproj
dotnet run --project tools/Pipp.Parity --no-build      # expect: ALL CASES MATCH
```

Rebuild **both** consumers before a parity run - a stale `Pipp.Cli.exe` silently compares old output.

## The task in flight (Phase 4a, option A chosen by the user)

**Goal: make `-hs` (histogram stretch) match the original on real data, not just on synthetic fixtures.**

What is proven:

- On the synthetic fixture, `gain = wp/100` fits exactly (2.04 at `-hs=204`).
- On the real Saturn capture the gain is **image-dependent**: 2.2921-2.3068 at the same `-hs=204`,
  where the model predicts 2.04.
- Both fixtures fit `gain = (wp/100) * 255/(255 - b)` with `b = 0` (synthetic) and `b ~= 29.6` (real).
  Two independent `-hs` values per frame agree to 0.2%, so the *form* is likely right.
- `b` drifts across six real frames (28.05-29.49) while their background statistics are identical
  (mode 3, p90 6), so it keys off the bright tail, not the background.
- A battery of order statistics (mean, sd, p99, p99.9, top-1/5/10%, mean+3sd/5sd) does **not**
  produce 156 (synthetic) and 138 (real) simultaneously. It is not a simple percentile.

What is known about the code:

- The option lives at **`options + 0x1DC`**, default **`0xCC` (204)**, set in `FUN_1400ab9a0`.
- The percent spelling is converted **`x255/100`** before storing (corpus lines 114922-114924).
- `FUN_1400ab9a0` is only reached through a 152-byte wrapper `FUN_1400aea40`, called from a
  stack-local options struct - so the consumer is **not** reachable by text search.
- Text-grep hits on `0x1dc` at lines 21103 / 75496 / 62014 are zlib and FFmpeg structs, not this one.

**Next action (do this first):** trace to the consumer with cross-references, not grep.

1. `procedure_callers` on `FUN_1400aea40`, then on its caller, until a global options pointer appears.
2. `xrefs` to that global; the function reading `+0x1DC` and scaling samples is the target.
3. Read it: it should show the `255/(255-b)` computation and name the statistic behind `b`.
4. Implement in `src/Pipp.Core/Processing/HistogramStretch.cs`, keeping the synthetic result at 2.04
   and the bias rule `trunc((v*wp - 1)/100)` where it still applies.
5. Verify: re-run parity (13 cases must stay green) plus the real-data check below.

Real-data check (the one that exposed this):

```bash
cd samples
# oracle and managed on 30 real frames, four option sets: plain / hs / db / ctr
# outputs land in samples/par/<case>/<oracle|managed>/saturn30/*.tif
```

Compare decoded pixels with ffmpeg (`-pix_fmt gray`, or `rgb24` for debayer), not file bytes.

If the decompiled consumer proves unreadable, fall back to fitting `b` empirically over ~20 frames
with deliberately varied histograms.

## Gotchas that cost time before

- **Oracle invocation**: switches on **argv**, and stdin must end with a literal `DONE` line. EOF
  alone hangs it (zero CPU, no output). Stdin-only produces nothing.
- **Synthetic fixtures must satisfy the detector**: flat dark background, object not touching an
  edge, `-ms` relaxed (default 15) when the object is small.
- **FITS**: the original stores rows bottom-up, flips every FITS on read, and writes 16-bit samples
  byte-swapped under an `II` header (ffmpeg cannot read them either - we deliberately diverge).
- **SER colour** uses ColorID **100 = RGB, 101 = BGR**; 16/17 are bayer/mono ids in PIPP's dialect.
- **Quality selection** numbers outputs from 0, plain runs from 1, padded to the frame count.
- `PIPP.slnx`, not `.sln`.

## Where to read next

| Document | Why |
| --- | --- |
| [`roadmap.md`](roadmap.md) | phases, scoreboard, recorded decisions |
| [`status.md`](status.md) | verified behaviours, known divergences, environment |
| [`parity.md`](parity.md) | harness usage and fixture rules |
| [`journal.md`](journal.md) | what each session proved, with commit hashes |
| [`../notes/engine-behaviour.md`](../notes/engine-behaviour.md) | GUI token stream, real-data findings, protocol details |
