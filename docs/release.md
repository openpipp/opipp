# Releasing OPIPP

Publication is a consequence of a green build, not a separate manual act. Pushing a tag that
matches `v*` runs [`.github/workflows/release.yml`](../.github/workflows/release.yml), which
builds and tests the tagged commit, packages the self-contained win-x64 App + CLI, hashes the zip,
extracts that zip into a fresh directory and smoke-tests the extracted CLI, and only then creates
and publishes the GitHub release.

The workflow file is the source of truth for the steps. This page explains the contract and the
operator path.

## What the workflow does

| # | Step | Gate |
|---|---|---|
| 1 | Check out the tag and resolve the version | Tag must match `v<major>.<minor>.<patch>[-suffix]` and equal `Version` in `Directory.Build.props`. A mismatch fails before anything is built. |
| 2 | `dotnet restore`, `build`, `test OPIPP.slnx -c Release` | Any failing test stops the run. |
| 3 | `dotnet publish` `Opipp.App` and `Opipp.Cli`, self-contained `win-x64`, into one folder | Both `Opipp.App.exe` and `Opipp.Cli.exe` must exist. |
| 4 | Write `README-ALPHA.txt` | Carries the not-affiliated, AI-disclosure and GPLv3 wording from the previous builds. |
| 5 | Zip the folder, compute SHA-256, write `release-notes.md` | The notes include `docs/release-notes/<version>.md` when present, then the checksum. |
| 6 | Extract the zip into `$RUNNER_TEMP` and run the extracted `Opipp.Cli.exe -tif -q=10 -od=... capture.ser` on a generated 4-frame SER | Nonzero exit, a missing or non-TIFF output, or a frame-count mismatch fails the run. |
| 7 | Upload the zip and notes as a workflow artifact | Kept 14 days, including on dry runs. |
| 8 | Create a **draft** release with the zip attached | Tag name is the release title and tag; `-alpha` tags are marked prerelease. |
| 9 | Read the draft back and check asset name, state `uploaded`, size, and prerelease flag | A bad upload leaves an unpublished draft. |
| 10 | `gh release edit --draft=false` and verify the release is no longer a draft | This is the only publication act. |

Permissions are `contents: write` and nothing else. The token is passed to `gh` through `GH_TOKEN`
and is never echoed. Concurrent runs are serialised by the `release-<ref>` concurrency group and are
not cancelled mid-flight.

Artifact name: `OPIPP-<version>-win-x64.zip`, for example `OPIPP-0.1.4-alpha-win-x64.zip`.
`dist/` is scratch space in CI; it is git-ignored.

Before the first tagged run, check two repository settings:

- Actions are enabled, and **Settings -> Actions -> General -> Workflow permissions** allows
  read and write (or at least does not cap the job's `contents: write`). Without it,
  `gh release create` fails with HTTP 403 and nothing is published.
- The tagged commit contains `.github/workflows/release.yml`; a tag pointing at an older commit
  cannot run it.

## Cutting a release

### 1. Bump the version and commit

`Directory.Build.props` is the single version identity:

```xml
<Version>0.1.5-alpha</Version>
<AssemblyVersion>0.1.5.0</AssemblyVersion>
<FileVersion>0.1.5.0</FileVersion>
```

Update any other version-bearing text in the same commit (window title, README/roadmap artifact
rows, status page). Then run the suite locally once:

```powershell
dotnet build OPIPP.slnx -c Release --nologo
dotnet test OPIPP.slnx -c Release --no-build --nologo
```

Commit the bump. The tag must point at a commit whose `Directory.Build.props` matches the tag; the
workflow refuses to publish otherwise.

### 2. Tag and push the tag

```powershell
git tag v0.1.5-alpha
git push origin v0.1.5-alpha
```

A single tag push is enough. Do not also publish the release by hand.

### 3. Watch the run

```powershell
gh run list --workflow=release.yml --limit 5
gh run watch
```

Or open the Actions tab. The run takes roughly the same time as a CI build plus two self-contained
publishes and a ~50 MB zip; the job timeout is 45 minutes. If the version check, tests or smoke
test fail, no release exists (or only an unpublished draft exists, if the failure was during
upload).

### 4. Verify the published release

```powershell
gh release view v0.1.5-alpha --json tagName,isPrerelease,assets
gh release download v0.1.5-alpha --pattern "OPIPP-0.1.5-alpha-win-x64.zip"
Get-FileHash -Algorithm SHA256 .\OPIPP-0.1.5-alpha-win-x64.zip
```

The hash must equal the checksum in the release notes. Confirm the prerelease flag for `-alpha`
tags, then spot-check the download by running `Opipp.Cli.exe -help` and a small conversion.

### If a run fails

- **Version mismatch**: bump `Directory.Build.props`, commit, and re-tag. If the failed run did not
  create a release or draft, deleting and re-creating the tag is safe. Never move a tag after a
  release is published; cut a new version instead.
- **Tests or smoke test fail**: fix the code, bump the version, and tag again. There is no override
  input and no way to skip the gate, by design.
- **Draft left behind after an upload/verification failure**: inspect it and delete it before
  re-running:
  ```powershell
  gh release list --include-drafts
  gh release delete v0.1.5-alpha --yes
  ```
  Do not use `--cleanup-tag` unless you also intend to delete the tag. Do not publish the draft by
  hand without checking the asset size and hash; the workflow will not overwrite an existing
  release, so a rerun with the draft still present fails at `gh release create`.
- **Release already exists**: the workflow does not clobber published assets. Bump the version, or
  deliberately delete the old release first.

## Dry run (no publication)

`workflow_dispatch` builds, packages and smoke-tests but skips every release step. Use it to
validate a change to the workflow or packaging without creating a release:

```powershell
gh workflow run release.yml -f tag=v0.1.5-alpha
gh run watch
```

Or Actions -> **release** -> **Run workflow**, leaving the tag empty to derive `v<Version>` from
`Directory.Build.props`. The dispatch runs the workflow and code from the selected branch, not from
the tag, so it is a packaging/test dry run, not release provenance. A tag that disagrees with
`Directory.Build.props` only produces a warning on dispatch; on a real tag push it is an error.

The zip and `release-notes.md` are available as the run's workflow artifact.

## Local fallback

This is what was done for 0.1.2 through 0.1.4, and it remains available when Actions is unavailable
or a release must be reproduced offline. It is no longer the default.

```powershell
$version = '0.1.5-alpha'
$stage = "dist\OPIPP-$version-win-x64"

dotnet publish src/Opipp.App -c Release -r win-x64 --self-contained true -o $stage
dotnet publish src/Opipp.Cli -c Release -r win-x64 --self-contained true -o $stage

# Write README-ALPHA.txt by hand, modelled on the previous release's copy.
Compress-Archive -Path "$stage\*" -DestinationPath "dist\OPIPP-$version-win-x64.zip" -Force
Get-FileHash -Algorithm SHA256 "dist\OPIPP-$version-win-x64.zip"
```

Smoke-test the zip, not the staging folder:

```powershell
$tmp = Join-Path $env:TEMP "opipp-smoke-$version"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive "dist\OPIPP-$version-win-x64.zip" $tmp
python tools\make_test_ser.py "$tmp\capture.ser"
& "$tmp\Opipp.Cli.exe" -tif '-q=10' "-od=$tmp\out" "$tmp\capture.ser"
if ($LASTEXITCODE -ne 0) { throw "CLI smoke test failed: $LASTEXITCODE" }
Get-ChildItem "$tmp\out" -Recurse -Filter *.tif
```

Then publish. Prefer the draft-first sequence the workflow uses:

```powershell
gh release create "v$version" "dist\OPIPP-$version-win-x64.zip" `
    --title "v$version" --prerelease --notes-file release-notes.md --verify-tag --draft
gh release view "v$version" --json tagName,isPrerelease,assets
gh release edit "v$version" --draft=false
```

The 0.1.2-0.1.4 releases used a direct `gh release create --prerelease`; the upload took about
seven minutes and timed out twice in the foreground, which is why the workflow now uploads from a
runner in the same job and verifies the asset before publishing.

## Why manual publication is no longer the default

The 0.1.4 release was published at **05:55:57 UTC**, while the CI test step for the same tag
finished at **05:57:09 UTC**. The release was live before the tagged commit's tests had finished:
publication did not depend on CI, so a human had to remember to check afterwards. The downloadable
zip was never built or smoke-tested by CI at all; it was whatever the operator had produced locally.
That makes the artifact's provenance a matter of trust, and it allowed the tag, the binaries and the
release notes to drift apart.

The workflow removes the ordering choice. Building, testing, packaging and smoke-testing happen in
the same job that creates the release, so a published release necessarily came from the tagged
commit and passed the suite. It also removes the slow, flaky manual upload from the critical path:
the asset is uploaded from the runner, and the release stays a draft until the uploaded asset's
name, state and size have been read back.

## What CI still does not verify

- **Parity against the original PIPP engine.** The harness needs the original, non-redistributable
  binaries; it stays a local check (see `docs/parity.md`).
- **The GUI actually opening a window.** The runner has no interactive desktop. The workflow checks
  that `Opipp.App.exe` is published and extracted, but does not launch it.
- **Code signing, installers and other architectures.** There are none; the artifact is an unsigned,
  self-contained `win-x64` zip.
- **The first real tag run.** The YAML is not executable locally; a `workflow_dispatch` dry run
  exercises every packaging step but deliberately skips the release steps.
