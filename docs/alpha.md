# OPIPP 0.1.2-alpha

The first distributable build of the reconstruction. **Alpha** means: the engine is functionally
complete and parity-tested, but one algorithm (surface tracking) is still one pixel off on part of
the real-data fixture, and the project has no publication permission yet. Treat it as a working
preview, not a replacement for the original.

## Artifact

| | |
|---|---|
| File | `dist/OPIPP-0.1.2-alpha-win-x64.zip` (attached to the GitHub release) |
| Size | 50,772,549 bytes (48.4 MiB) |
| SHA-256 | `1c57aee5e7b942152e33f2c7853a794fd19705c89c497ac8ec859355160dba47` |
| Runtime | self-contained .NET 10, win-x64 — no .NET install needed |
| Contents | `Opipp.App.exe` (GUI, managed), `Opipp.Cli.exe` (drop-in engine), `README-ALPHA.txt` |

## How it was built

```
export DOTNET_ROOT="$HOME/.dotnet-sdk"; export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet publish src/Opipp.App -c Release -r win-x64 --self-contained true \
    -o dist/OPIPP-0.1.2-alpha-win-x64
dotnet publish src/Opipp.Cli -c Release -r win-x64 --self-contained true \
    -o dist/OPIPP-0.1.2-alpha-win-x64
```

Version identity lives in `Directory.Build.props` (`Version=0.1.2-alpha`).

## Smoke tests run on the published folder

| Check | Result |
|---|---|
| `Opipp.Cli.exe -help` | prints the switch reference |
| 30-frame conversion of `samples/saturn30.ser` | 30 TIFFs written |
| `Opipp.App.exe` start | window opens, title `OPIPP 0.1.0-alpha - Open PIPP` |
| Native DLL dependence | none — `pipp_dll.dll` is not used or shipped |
| Test suite | 553 passing, 0 failing |
| Parity harness | 29/29 cases, `ALL CASES MATCH` |

## What is known to be off

- **Surface-feature tracking**: 23 of 30 frames on the real Saturn fixture are byte-identical to
  the original; the other 8 differ by exactly one pixel (off by one pixel). The engine's fine tracker
  is ported (blur, percentile-stretched template, per-candidate LUT, SSE, asymmetric window), but
  the last bit of its arithmetic is not reproduced. See `docs/journal.md` (M27).
- **Deliberate divergences** carried over from the reconstruction: spec-correct 16-bit TIFF, a valid
  animated GIF instead of the original's image-less file, clamped `-aoi_crop`, and the start-up path
  guard.
- **Release**: no `LICENSE`, and the build must not be redistributed without Chris Garry's written
  permission (`notes/provenance.md`).

## Rebuilding

`dist/` is git-ignored; the zip is produced locally from a clean tree with the two `dotnet publish`
commands above, then:

```
powershell -NoProfile -Command "Compress-Archive -Path 'dist\OPIPP-0.1.2-alpha-win-x64\*' \
    -DestinationPath 'dist\OPIPP-0.1.2-alpha-win-x64.zip' -Force"
```
