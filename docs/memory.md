# Memory model

## The problem

Every accepted frame used to stay resident until selection and output. Samples are 16-bit regardless
of source depth, so 10,000 frames at 1920x1080 is ~41 GB mono (~116 GB colour) before object overhead.
Selection does not help: it runs after the frames have accumulated.

## Design

- **`FrameStore`** (`src/Opipp.Core/Frames/FrameStore.cs`) — append-only scratch file holding
  transformed frames. Each record is a fixed metadata header (dimensions, channels, depth, Bayer
  pattern, source frame number, sample count) followed by raw little-endian samples. The file is
  opened with `FileOptions.DeleteOnClose`, so it cannot outlive the process even if disposal is
  skipped.
- **`RetainedFrame`** (`src/Opipp.Core/Frames/RetainedFrame.cs`) — a value-type handle that is either
  inline (the frame object itself) or stored (store + index). `Frame` materialises either
  representation, so the pipeline carries one type and call sites still read `.Frame`.
- **Budget** — `PipelineSettings.RetentionBudgetBytes`, default 512 MB of pixel data. Frames stay
  inline below it; the first frame that would cross it creates the store and **migrates the frames
  already accepted**, so a unit never has two homes for its frames. Small runs never touch the disk.
- **Lifetime** — one store per `Run`, disposed before the report is returned.

## What stays the same

- Output bytes: `ForcedSpill_ProducesByteIdenticalOutput` runs 24 frames with a 1-byte budget
  (everything spilled) and compares SHA-256 of every output file against the in-memory run.
- Quality log: the source frame number is serialised with each stored frame, so `Pos/Source Frame`
  columns survive a spill.
- `-nr` / `-no_ram_buff` ordering: the reorder key is the source frame number, not the store index.

## Still open

- **GIF output** collects every selected frame into a list before writing (`GifWriter` takes a
  list), so that path is still unbounded. Fixing it needs an incremental writer API.
- No CLI switch exposes the budget yet; it is a settings property only.
- No peak-RSS measurement — the budget caps retained pixel data, not total process memory
  (transform buffers, stacker accumulator and the reader add to it).
- `FrameStacker` accumulates a running sum rather than frames, so it is already bounded.
