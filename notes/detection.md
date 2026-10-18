# Object detection: `FUN_140077560` (16-bit path) and `FUN_140076ae0` (8-bit path)

Recovered from `ghidra/out/pipp_core.c:77675-78257` (image address `0x140077560`, 2,903 bytes,
785 instructions, one `ret` at `0x1400780b6`), the sibling 8-bit routine at `77078-77673`, the call
site in the frame loop `FUN_14007e8e0` (`87350-87398`, `89272-89286`), and a disassembly of the
shipped `bin/pipp_core.exe` used to settle the places where the pseudo-C aliases one local onto
several roles. Section 7 lists the cases reproduced against the original engine with synthetic
fixtures.

The two routines are the same algorithm; they differ only in how a sample becomes an 8-bit luma.
`FUN_140077560` reads 16-bit samples, `FUN_140076ae0` reads bytes through per-channel lookup
tables. Pseudocode below is written once.

## 1. Contract

```
int detect(int *frame, int mode /*param_2*/, int draw /*param_3*/);
```

| return | meaning | caller action (`87370-87398`) |
| --- | --- | --- |
| `0` | object found, box written | frame accepted (`LAB_140082443`) |
| `-1` (`0xffffffff`) | no object | `local_2b44++`, filename prefix `no_object_` |
| `-2` (`0xfffffffe`) | object too close to the frame border (a box edge sits inside the margin) | `local_2b40++`, prefix `partial_object_` |
| `-3` (`0xfffffffd`) | object smaller than the minimum size | `local_2b48++`, prefix `too_small_` |

Those three counters are printed at `89272`, `89275` and `89284` as *"Frames discarded with no
planet detected"*, *"… partial planet detected"* and *"… planet diameter below minimum"*. So the
mapping the log asks for is `-1 → no planet`, `-2 → partial planet`, `-3 → diameter below minimum`.

On every failure path the four box fields are zeroed (`78220-78223`, `78239-78242`, `78245-78248`,
`78252-78255`). On success they are written and not zeroed (`78230-78236`). Callers depend on this:
the quality driver reads the same four fields, and the GUI reads them for `@XPOS`/`@YPOS`
(`86497-86508`, centre = `x1 + (x2-x1)/2`, `y1 + (y2-y1)/2`).

`draw` is 0 at the `-chk` call (`87368`). `mode` there is `local_27a0`, a stack slot Ghidra never
assigns, so its static value is unknown — but a model with `mode = 0` reproduces all 19 oracle
cases in section 7, so it is 0 in practice. `mode` 1..6 and `draw` 1 exist in the code but were
never observed (section 8).

## 2. Fields read and written

`param_1` is the frame object; offsets are bytes.

| offset | int index | read/write | meaning | evidence |
| --- | --- | --- | --- | --- |
| `+0x00` | `[0]` | read | width in pixels (row stride = width*3 samples) | `77723`, `77727` |
| `+0x04` | `[1]` | read | height in pixels | `77735`, `77738` |
| `+0x08` | `[2]` | read (dispatch only) | sample-format flag: 1 selects the byte/LUT path, anything else the 16-bit path | `87350-87356` via `FUN_14007d840` (`+8`) |
| `+0x10` | `[4]` | read | detection threshold — the logged "Calculated minpixel value", `-mp` | `77722`; getter `FUN_14007d890`, setter `FUN_14009b350`, printed `89251`/`87360` |
| `+0x14` | `[5]` | read | minimum object size in pixels, `-ms` | `78228-78229` |
| `+0x38` | `[0x0e]` | read (8-bit path) | 256-entry int LUT, blue → luma | `77085` etc. |
| `+0x438` | `[0x10e]` | read (8-bit path) | 256-entry int LUT, green | same |
| `+0x838` | `[0x20e]` | read (8-bit path) | 256-entry int LUT, red | same |
| `+0xca8` | `[0x32a]` | read | pointer to the image object; `[+8]` of that object is the pixel buffer | `77731-77732` |
| `+0xdd0` | `[0x374]` | **write** | box x1 (left) | `78236`, `78220` |
| `+0xdd4` | `[0x375]` | **write** | box y1 (top) | `78230`, `78221` |
| `+0xdd8` | `[0x376]` | **write** | box x2 (right) | `78231`, `78222` |
| `+0xddc` | `[0x377]` | **write** | box y2 (bottom) | `78235`, `78223` |

`0x374..0x377` and `0xdd0..0xddc` are the same four `int`s written twice in the pseudo-C — the
index form and the byte form. There is one box, not two.

Pixels are 3 samples per pixel in **B,G,R** order, 16-bit words in the 16-bit path (`puVar14[0]`,
`[1]`, `[2]` weighted 114/587/299 at `77759-77760`), bytes in the 8-bit path. A mono frame stores
the same value in all three samples.

## 3. Algorithm

### 3.1 Setup (`77722-77738`)

```
W, H   = frame[0], frame[1]
T      = frame[4]                     // 8-bit threshold
L      = T << 2                       // threshold for the 4-sample sum
base   = *(*(frame + 0xca8) + 8)      // first pixel of row 0
stride = W * 3                        // samples per row
```

### 3.2 Luma (exact)

16-bit path (`77759-77760`, assembly `0x140077630-0x140077666`):

```
luma(x,y) = ((B*114 + G*587 + R*299) / 1000) >> 8        // 0..255
```

The division is a magic multiply by `0x10624dd3` (`/1000`), then `shr 8`. This is the same
Rec.601 projection the rest of the engine uses, applied to raw 16-bit samples, so the threshold
lives on an 8-bit scale. It is **not** the `threshold << 8 <= sample` comparison used by the
quality marker `FUN_1400aecf0`.

8-bit path (`77085-77095`): `luma = (LUT_R[r] + LUT_B[b] + LUT_G[g]) >> 15`, the three 256-entry
tables being the same weights in 15-bit fixed point (for a mono frame this is the identity, which
is why the 8-bit fixtures below match a model built on raw values).

### 3.3 Horizontal detector: a 4-tap running mean

Every scan in the routine uses the same primitive (`77746-77762`, assembly `0x140077610-0x140077675`).
A 4-entry ring buffer holds the last four luma bytes; `sum` is their sum, reset to 0 at the start of
each row, so the first three columns of a row are averaged against implicit zeros.

```
sum += luma(x,y) - ring[x & 3]; ring[x & 3] = luma(x,y)
bright(x) := sum >= L        // i.e. mean of last 4 luma values >= T
```

Consequence: for a saturated object the bright run extends up to **3 columns past** the right edge
and starts **at** its left edge whenever `4*T <= 255` (T <= 63) — a single bright sample already
fills the sum. Higher thresholds need 2, 3 or 4 bright samples in the window, which moves both
edges inward by up to 3 (verified with `-mp=200`, where an 11-wide bar produces no run at all).

### 3.4 Seed scan (`77739-77793`)

Scan rows `1 .. H-6` inclusive (row counter `iVar11` runs 2..H-5 and the buffer is indexed
`row = iVar11-1`; the guard `if (2 < H-4)` at `77739` means frames of height <= 6 never detect).
For each row:

- split the row into maximal runs of `bright` columns;
- a run qualifies only if its length is **>= 10** (`9 < uVar17`);
- keep the longest qualifying run seen so far; ties keep the earlier row (strict `>`);
- the run's end is recorded as the **first dark column** after it, or `W-1` if the run is still
  open when the row ends (`77783-77790`).

If no run qualifies anywhere, return `-1` (`78252-78256`). Otherwise the seed is
`(row, start, end, length)`; `start`/`end` are column indices of the 4-tap test, not of the object.

### 3.5 Grow down, then up (`77794-78000`)

Both passes start at the seed row and walk one row at a time (down: `r+1`, up: `r-1`) until a row
fails. A row *matches* when it contains a run of >= 10 bright columns that overlaps the previous
row's interval:

```
runStart <= prevEnd  &&  prevStart <= col
```

where `col` is the column at which the run has just reached length 10. On a match the interval
becomes `[min(runStart, prevStart), max(col, prevEnd)]` (per-row extremes are also folded into the
global extremes). A run shorter than 10 stops the row scan as soon as the column counter passes
`prevEnd` (`77835-77839`; the up-pass test is one of the places where the pseudo-C aliases two
locals, and assembly `0x1400779e2-0x1400779e7` shows the same `col > prevEnd` condition).

Two quirks matter for reimplementation:

- **The run test only fires every 10 columns.** The overlap branch is guarded by `runLen >= 10`
  while the counter is reset to 0 afterwards, so a row updates the interval at most once per 10
  bright samples, and the recorded "column" is 9..10 past the run start, not the true run end.
- **A row that ends in an open run marks the object as touching the right edge.** If the last run
  of the row is >= 10 and starts before `prevEnd`, the code sets the global right edge to `W-1`
  (`77887-77893`, `77988-77994`), which later becomes `x2 = W-4` and forces the `-2` "partial"
  result. A busy row boundary (a second bright patch at the row end) can therefore reject a
  perfectly centred object.

The passes terminate on the first non-matching row:

```
bottom = r_bad              // down pass: last good row + 1
top    = r_bad + 2          // up pass: first good row + 1 (r_bad is the row above it)
```

If a pass reaches the frame edge without a non-matching row, its edge keeps the initial value
(`bottom = 0`, `top = H-1`) and the height check at `78219` makes the result `-1`. **An object that
touches the top or bottom edge is reported as "no planet detected", not as "partial".** (Verified:
a bar covering rows 0..60 gives `-1`; a bar whose bottom row is 94 of 96 gives `-2`.)

### 3.6 Box adjustment (`78001-78002`, assembly `0x140077a9b-0x140077aa1`)

```
x1 = minStart - 1
x2 = maxEnd   - 3
```

`-1` and `-3` are literal (`dec ebx` / `sub r11d, 3`) and are applied before the mode blocks, so
they are part of the normal result. The `-3` compensates the 4-tap lag at the right edge; the `-1`
on the left does not, which makes the box asymmetric (see 6).

### 3.7 Optional mode refinement (`78003-78158`) — not exercised by `-chk`

`mode` 1..3 rescans a band of rows `[y1-5, y2+5]` clamped to the frame, from the middle of the box
outwards to the right, and then overwrites `x2` with the last column it accepted (0 = keep the old
value). `mode` 4..6 mirrors it leftwards and overwrites `x1` the same way. Both use the same 4-tap
test with 10-sample runs and stop at the first column without an overlapping run. The decompiled
column origin `(x1+x2)*3/4` looks like a decompiler artefact of `(x1+x2)/2 * 3`; the exact intent is
not recovered, and both blocks are dead for the `-chk` call site.

### 3.8 Optional overlay (`78159-78218`) — not exercised by `-chk`

With `draw == 1` the box is outlined **inside the image** with a 5-on/5-off dash: rows `y1-1` and
`y2+1`, columns `x1..x2`, all three samples set to `0xc000` (49,152 = 192<<8; `0xc0` in the 8-bit
sibling). This is the debug/preview outline. The `-sdp` switch is a *different* routine,
`FUN_14009b400` (`14009b400`), called right after detection at `87357`: it repaints every pixel as
`R = (luma*191)/255 + 64`, and sets `B = G = R` when `luma < T`, `B = G = 0` when `luma >= T`
(detected pixels go red).

### 3.9 Validation and result (`78219-78256`)

```
if (y2 - y1) < 2                                    -> -1, box zeroed
if (x1 <= 1 || x2 >= W-4 || y1 <= 1 || y2 >= H-1)   -> -2, box zeroed
if (x2-x1) < frame[5] || (y2-y1) < frame[5]         -> -3, box zeroed
if (x1 < 0) x1 = 0                                  // unreachable after the x1 > 1 test
store x1,y1,x2,y2; return 0
```

Order matters: the height check comes first, so an object two rows tall is `-1` even when it is
also below `-ms` (verified: a 2-row bar is "no planet", a 3-row bar is "diameter below minimum").
The border tests are asymmetric: `x1 > 1`, `x2 < W-4`, but `y1 > 1`, `y2 < H-1`.

## 4. Reimplementation pseudocode

```
detect(frame, mode, draw):
    W, H, T = frame.width, frame.height, frame.threshold
    if H - 4 <= 2: return -1

    # --- seed: longest horizontal bright run, rows 1..H-6
    best = 0
    for r in 1 .. H-6:
        reset ring, sum, run
        for x in 0 .. W-1:
            update ring/sum with luma(x,r)
            if sum < T*4:
                if run >= 10 and run > best: seed = (r, runStart, x); best = run
                run = 0
            else:
                if run == 0: runStart = x
                run += 1
        if run >= 10 and run > best: seed = (r, runStart, W-1); best = run
    if best == 0: return -1

    # --- grow: prev = seed interval, r walks down then up
    gmin, gmax = seed.start, seed.end
    bottom = grow(down,  from seed.row)      # returns lastGood+1, or 0 if it ran off the frame
    top    = grow(up,    from seed.row)      # returns firstGood+1, or H-1 if it ran off the frame

    x1, x2 = gmin - 1, gmax - 3
    if mode != 0: apply refinement (modes 1-3 rewrite x2, 4-6 rewrite x1)
    if draw == 1: outline box in the image

    if bottom - top < 2: return -1
    if x1 <= 1 or x2 >= W-4 or top <= 1 or bottom >= H-1: return -2
    if x2-x1 < ms or bottom-top < ms: return -3
    frame.box = (x1, top, x2, bottom)
    return 0

grow(direction, row):
    prevStart, prevEnd = seed.start, seed.end
    while 0 <= row < H:
        found = 0; run = 0
        reset ring, sum
        for x in 0 .. W-1:
            update ring/sum with luma(x,row)
            if sum < T*4:
                if run < 10:
                    if x > prevEnd: break            # walked past the interval, nothing overlapped
                elif runStart <= prevEnd and prevStart <= x:
                    found = 1
                    prevStart = min(prevStart, runStart)
                    prevEnd   = max(prevEnd, x)      # x, not the run end
                run = 0
            else:
                if run == 0: runStart = x
                run += 1
        if not found: return row + (2 if direction == up else 0)
        if run >= 10 and runStart <= prevEnd:        # row ended in an open run
            prevEnd = W-1; gmax = W-1; gmin = min(gmin, runStart)
        row += (-1 if direction == up else 1)
    return H-1 if direction == up else 0
```

`gmin`/`gmax` are updated inside `grow` with every matching run (`min(runStart)`, `max(x)`).

## 5. What the box means geometrically

For a saturated rectangle spanning columns `L..R` and rows `T..B`, with `2 <= T` and `B <= H-2`:

| field | value | vs object |
| --- | --- | --- |
| `x1` | `L-1` | one pixel left of the object |
| `y1` | `T+1` | one row below the object's top |
| `x2` | `R+1` | one pixel right of the object |
| `y2` | `B+1` | one row below the object's bottom |

so `width = trueWidth + 1` and `height = trueHeight - 1` (vbar: 11 → 12; hbar: 11 → 10). For a disc
of radius `r` the growth stops where the chord falls below 10 pixels: rows `|dy| <= sqrt(r^2-25)`,
giving `height = 2*floor(sqrt(r^2-25))` (r=15 → 28, verified).

`-ms` is compared against both `x2-x1` and `y2-y1`, i.e. the *minimum* of the two, so the effective
"diameter" is `min(width, height)`.

## 6. Rejection conditions → log counter

| condition | code | log counter | filename prefix |
| --- | --- | --- | --- |
| `H <= 6`, or no >= 10-pixel bright run in rows 1..H-6 | `-1` | no planet detected | `no_object_` |
| height < 2 rows (including any object that runs off the top/bottom edge) | `-1` | no planet detected | `no_object_` |
| `x1 <= 1` (object at the left edge) | `-2` | partial planet detected | `partial_object_` |
| `x2 >= W-4` (object at the right edge, or a row ending in an open run) | `-2` | partial planet detected | `partial_object_` |
| `y1 <= 1` / `y2 >= H-1` | `-2` | partial planet detected | `partial_object_` |
| `min(x2-x1, y2-y1) < -ms` | `-3` | diameter below minimum | `too_small_` |

## 7. Verification against the shipped engine

Model in `%LOCALAPPDATA%\hermes\cache\scratch\detmodel.py` (Python transliteration of section 4)
run against `bin/pipp_core.exe` on synthetic 8-bit SER fixtures, `-chk -mp=40`, box dimension read
out by bisecting `-ms` (accepted at `-ms = d`, rejected at `d+1` ⇒ `min(width,height) = d`) and by
the accept/reject reason in the summary log. All 19 fixtures below agree (the `-ms` boundary was
bisected in each case, so each row is 2-7 oracle runs):

| fixture | oracle | model |
| --- | --- | --- |
| vbar x40..50 y20..75 | min-dim 12 | 12 |
| hbar x10..85 y40..50 | min-dim 10 | 10 |
| disc r=15 at (48,48) | min-dim 28 | 28 |
| bars y1..60, y2..60, y3..60 | heights 59, 58, 57 | 59, 58, 57 |
| bar y30..93 | height 63 | 63 |
| bar y40..50 | height 10 | 10 |
| bar y0..60 | `-1` | `-1` |
| bar y91..95 (outside the seed scan) | `-1` | `-1` |
| 2-row bar | `-1` | `-1` |
| 3-row bar, `-ms=5` | `-3` | `-3` |
| bar x0=2 / x0=3 | `-2` / `0` | `-2` / `0` |
| bar ending at x=91 / x=90 | `-2` / `0` | `-2` / `0` |
| bar bottom row 94 / 93 | `-2` / `0` | `-2` / `0` |
| vbar at `-mp=200` | discarded | `-1` (run < 10 columns) |

The `-mp=200` case is the sharpest check of the 4-tap rule: with 255-valued pixels a run needs four
saturated samples, so an 11-wide bar yields only 9 qualifying columns and no seed.

## 8. Uncertain or not recovered

- **`mode` 1..6** (`78003-78158`): not reached by the `-chk` path in any run made here (a mode-0
  model matches all 19 fixtures). The two blocks are documented by shape only; the column origin
  and the `uVar13` accumulator in the pseudo-C are aliased and were not disambiguated.
- **`draw == 1`** (`78159-78218`): no CLI switch was found that reaches it; the outline geometry is
  read from the pseudo-C, not observed. `-sdp` is a separate overlay (`FUN_14009b400`).
- **Why the vertical edges are `+1`**: pass 1 stores `iVar11 = row+1` while the buffer pointer is
  one row behind, and the vertical passes then use that counter both as row index and as edge
  value. The net effect (top and bottom one past the object) is verified on every vertical fixture
  in section 7; the internal indexing is not fully reconciled, so the pseudocode states the effect,
  not the offset.
- **Seed tie-breaking**: the first row with the longest run wins (strict `>` at `77764`), inferred
  from the comparison; not isolated with a fixture.
- **`-1` vs `-2` for edge-touching objects**: objects touching the left/right edge are `-2`,
  objects touching the top/bottom are `-1` because the grow pass runs off the frame and leaves the
  edge at its initial value. Verified, but it reads like an oversight in the original.
- **The `-1` / `-3` x adjustment** makes the box asymmetric and is applied even in `draw` mode; both
  detector variants do it (`FUN_140076ae0` has the same `dec`/`sub 3`), so it is engine-wide
  behaviour rather than a misprint, but its intent is unknown.
- **Pass-1 bottom margin**: rows `0` and `H-5 .. H-1` are never visited by the seed scan. The 5-row
  margin is exact from the code; why 5 is not explained by anything in the routine.
- **Threshold scale on 16-bit input**: the 16-bit path thresholds *8-bit* luma, while
  `-mp` is documented as a sample value. On 16-bit data the effective threshold is
  `(T << 8)` on the 16-bit sample scale, which is consistent, but it means `-mp` values above 255
  can never be met (the luma byte saturates at 255).

## 9. Files

- Written: `notes/detection.md` (this file). No other repository file was modified.
- Scratch (outside the repo): `detmodel.py` (validated model), `mkser.py`, `mk8.py`, `mkbar.py`,
  `dumpser.py`, `sweep.sh`, `borders.sh`, `vmeasure.sh`, `dis_det.py` + `det_dis.txt` (disassembly
  of `0x140077560`) under `%LOCALAPPDATA%\hermes\cache\scratch`.
- Verified: field offsets and the box writers; the luma formula and the `>= 4*T` test; the seed
  scan limits and the >= 10 run rule; both growth passes including the open-run/right-edge quirk;
  the `-1`/`-3` adjustment; all four border tests; the `-1`/`-2`/`-3` classification; the
  `min(width,height)` meaning of `-ms`.
- Inferred: `mode`/`draw` semantics; the internal row-index off-by-one; seed tie-breaking; the
  purpose of the 5-row top margin and of the `-1`/`-3` pair.
