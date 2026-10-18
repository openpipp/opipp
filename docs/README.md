# Project documentation

Living documents live here; the research trail stays in [`../notes`](../notes).

| Document | Read it when |
| --- | --- |
| [`HANDOFF.md`](HANDOFF.md) | **starting a new session** - state, current task, next action |
| [`journal.md`](journal.md) | what each session proved, with commit hashes |
| [`roadmap.md`](roadmap.md) | deciding what to work on next, or what "done" means |
| [`status.md`](status.md) | you need the current build/test/parity numbers and known gaps |
| [`parity.md`](parity.md) | running or extending the oracle comparison |
| [`switch-matrix.csv`](switch-matrix.csv) | checking whether a specific switch is implemented |
| [`coverage.json`](coverage.json) | machine-readable totals behind the roadmap percentages |

## Repository layout

```
PIPP-RE/
├── src/
│   ├── Pipp.Core/        formats, processing, pipeline, CLI model (the engine)
│   ├── Pipp.Cli/         drop-in replacement for pipp_core.exe
│   └── Pipp.App/         WinForms front-end (ported from the decompiled PIPP.exe)
├── tests/                7 projects, 289 tests - formats, processing, CLI, end-to-end
├── tools/
│   ├── Pipp.Parity/      oracle comparison harness
│   └── *.py              synthetic SER fixture generators
├── docs/                 this folder: roadmap, status, parity, switch matrix
├── notes/                research trail: CLI spec, algorithms, engine behaviour, provenance
├── bin/                  original binaries (read-only reference, NEVER committed)
├── decompiled/           ILSpy output from PIPP.exe (reference, NEVER committed)
├── ghidra/               decompiled native engine + function index (reference, NEVER committed)
└── parity/               harness working directories and outputs (generated)
```

The three reference folders are inputs to the reconstruction, not part of it. They are git-ignored:
the repository must be publishable without carrying anyone else's binaries or reversed source.

## Conventions

- **Verified beats plausible.** A behaviour is "done" when a test or a parity case proves it.
- **Numbers are generated.** Recompute the coverage figures rather than editing them by hand.
- **One divergence, one line.** Any deliberate difference from the original is listed in `status.md`.
- **Reference artefacts are read-only.** Never modify `bin/`, and never commit anything under it.

## Session protocol

Work arrives in sessions with limited context. To make hand-off cheap:

1. **Start** by reading [`HANDOFF.md`](HANDOFF.md) - it carries the current state and the exact next
   action, so no context has to be reconstructed from scratch.
2. **Work** in the order the handoff specifies; verify with tests and the parity harness, not by
   inspection.
3. **End** by updating, in this order:
   - [`journal.md`](journal.md): append what the session proved, with the commit hash;
   - [`HANDOFF.md`](HANDOFF.md): refresh the state table, the task in flight and the next action;
   - [`roadmap.md`](roadmap.md): record any decision in the decision log.
4. **Commit** the documentation with the code, never separately.

A session that ends without updating `HANDOFF.md` costs the next one an hour of rediscovery.
