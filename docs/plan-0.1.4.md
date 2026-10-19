# 0.1.4 — plan and action items

Status: planned. Baseline is `440a285` (0.1.3-alpha published): 566 tests, parity 26 compared /
26 matched / 2 expected differences / 1 gap, CI green.

## Definition of done

- [ ] AVI/container animation no longer materialises the whole sequence.
- [ ] The 512 MB retention budget has a measured memory number behind it, recorded in `docs/memory.md`.
- [ ] Debayer's parity status is either fixed or quantified (max channel delta), not left as an unmeasured "gap".
- [ ] Every published number (README, roadmap, status, site) matches CI and the harness output.
- [ ] `v0.1.4-alpha` published with a verified asset and checksum; CI green on the tagged commit.

Non-goals for 0.1.4: surface-tracking accuracy (23/30), RAW/dcraw parity, GUI redesign. Those stay
documented as known gaps.

---

## A. Finish P2 — stream AVI/container animation

Why: `BuildAviAnimation` calls `BuildGeometryFrames`, which materialises every selected frame. It is
the last path whose memory grows with the capture.

- **A1. Baseline fixture (1 h).** Add a test that runs `-avi -anim_fwd -anim_rev` on a synthetic
  capture and stores the output hash. This is the safety net for A2–A3.
  *Accept:* test fails if any byte of the AVI changes.
- **A2. Count without materialising (1 h).** Compute the container frame count arithmetically from
  `queue.Count`, `Forward`, `Reverse`, both `LastFrameRepeats` and `AllRepeats`; assert it equals the
  current `BuildAviAnimation(...).Count` for a grid of option combinations (test, no behaviour change).
  *Accept:* property test over ~12 option combinations.
- **A3. Lazy sequence (3 h).** Replace `List<Frame> animated` with an `IEnumerable<Frame>` produced
  by frame index (same pattern as GIF animation: index order → materialise on demand). Keep
  `BuildGeometryFrames` for callers that genuinely need a list.
  *Accept:* A1 hash unchanged; no `List<Frame>` of the whole run on the AVI path.
- **A4. -rgb / split-channel path (1 h).** It extracts channels from `template`; confirm it does not
  re-materialise the sequence per channel, or document why it must.
  *Accept:* parity `-rgb` cases unchanged.
- **A5. Regression sweep (1 h).** Full suite + `--real` parity.
  *Accept:* 0 failures, no unexpected divergences.

Risk: AVI headers carry the frame count; if the count is wrong the file is corrupt. A1 catches it.

## B. Measure the memory claim

- **B1. Diagnostic budget override (1 h).** Read `OPIPP_RETENTION_BUDGET_MB` in `PipelineSettings`
  only when the variable is present. No CLI switch, so `-gui` transcripts stay engine-identical.
  *Accept:* env var changes the spill point; unset behaviour unchanged (test).
- **B2. Probe tool (2 h).** `tools/Opipp.MemProbe`: generate a synthetic SER of N frames at a given
  geometry, run `Opipp.Cli` as a child process at two budgets, record `PeakWorkingSet64` and elapsed.
  *Accept:* prints a table; runs unattended.
- **B3. Record (1 h).** Add a "Measured" section to `docs/memory.md` with the host, frame count,
  geometry, both peaks and the ratio. State the caveat that peak includes the runtime.
  *Accept:* numbers are reproducible with the documented command.

## C. Close the debayer parity gap

- **C1. Measure (2 h).** Run the oracle and port on the bayer fixture; report max/mean per-channel
  delta and the count of differing pixels.
  *Accept:* a number, not "differs".
- **C2. Decide (1 h).** If the delta comes from one identifiable rule, fix it and promote the case to
  a real comparison. Otherwise keep it as `KnownDivergence` with the measured delta in the note.
  *Accept:* harness prints the delta; roadmap wording matches.
- **C3. Follow-through (30 m).** Update the site's gaps list and `docs/roadmap.md`.

## D. Documentation truth pass

- **D1. Numbers (1 h).** README: 555 → current test count; roadmap: 553/29-29 → 566/26-2-1 in the two
  remaining places (`:86`, `:139`). Add a "last verified at commit X" line to `docs/status.md`.
  *Accept:* `grep -rn "29/29\|553 tests\|555 tests"` returns nothing.
- **D2. Status record (1 h).** Make `docs/status.md` the single authoritative status page: tests,
  parity accounting, coverage counts, known gaps, and the release it describes.
- **D3. Site (30 m).** Version pill, test count, gaps list, download link.

## E. Release 0.1.4-alpha

- **E1. Version (15 m).** `Directory.Build.props`, window title, README, roadmap artifact rows.
- **E2. Build and package (30 m).** Publish App + CLI win-x64 self-contained; write `README-ALPHA.txt`;
  zip; record size and SHA-256.
- **E3. Publish (up to 30 m).** `gh release create --prerelease`; **upload in the background** — the
  last upload took ~7 minutes and two foreground timeouts; then publish the draft and verify the
  asset state is `uploaded`.
- **E4. Verify (15 m).** HEAD request on the asset reaches 200; site shows 0.1.4; CI green on the
  tagged commit; `git status` clean.

## Stretch (not blocking 0.1.4)

- **S1. Tracking coarse stage.** Port `FUN_1400a7ec0` and re-run the Saturn fixture; target ≥ 28/30
  exact frames (currently 23/30).
- **S2. RAW absolute values.** Bring `-raw_hl`/`-raw_ocs`/`-raw_hpf` output in line with dcraw.

---

## Sequencing

| Order | Item | Estimate | Depends on |
|---|---|---|---|
| 1 | D1 numbers | 1 h | — |
| 2 | B1 env override | 1 h | — |
| 3 | A1 baseline | 1 h | — |
| 4 | A2 count | 1 h | A1 |
| 5 | A3 lazy sequence | 3 h | A2 |
| 6 | A4 -rgb check | 1 h | A3 |
| 7 | C1–C2 debayer | 3 h | — |
| 8 | B2–B3 probe | 3 h | B1 |
| 9 | D2–D3 status/site | 1.5 h | D1, C2, B3 |
| 10 | E1–E4 release | 1.5 h | all |

Total ≈ 3 working days, of which the risky item is A3 (touches an AVI writer that parity compares).

## Risks

- **AVI frame count** — a wrong count corrupts the file; A1's hash is the gate.
- **Upload time** — 0.1.3's asset took ~7 minutes; always background the upload and publish the draft
  only after the asset reports `uploaded`.
- **Doc drift** — the numbers changed three times during 0.1.3. After this release, update counts only
  from a fresh test run, never from memory.
