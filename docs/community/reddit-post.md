# Reddit post draft

Suggested title:

> **OPIPP - an open, managed rebuild of PIPP for planetary stacking - early alpha, feedback wanted**

Suggested communities: r/astrophotography, r/AskAstrophotography, r/planetaryimaging. Check each
subreddit's self-promotion rules before posting; some require flair or restrict release posts to a
weekly thread.

---

**OPIPP (Open PIPP)** is an independent rebuild of PIPP, the Planetary Imaging PreProcessor, as a
fully managed .NET application. It reads the same SER/AVI/FITS/TIFF inputs, accepts the same
command-line switches, and produces the same output layout, so existing processing routes keep
working - but the code is new, readable C#, and the UI and algorithms can be modernized from here.

**This is not affiliated with PIPP.** I am not Chris Garry, this is not an official release, and bugs
in the original are not mine to fix. PIPP itself is closed-source freeware; OPIPP is an independent
reimplementation published in the belief that reproducing formats and behaviour for interoperability
is fair use. No original binaries are distributed, and the MIT licence covers only the new code.

**AI was used to build this, and will keep being used.** The port was written with large-language-model
assistance, working from decompiled output and black-box measurements of the original. That is a
deliberate choice: it is how a one-person project gets through 14k lines of decompiled UI and 4k native
functions. Every behaviour claim is checked against the original by a test or a parity case, and
anything unverified is listed as a known gap rather than described as working.

**Where it actually is (0.1.1-alpha):**

- 553 tests, 29/29 parity cases against the original binaries
- all 131 command-line switches addressed; 76 verified byte-for-byte
- GUI is fully managed - no native DLL required
- **known gap:** surface-feature tracking matches 23 of 30 frames exactly on a real Saturn capture;
  the rest are one pixel off. The coarse search stage is not faithful yet.

**What I'm asking for:**

1. Captures that fail, stall, or come out visibly worse than PIPP - SER or AVI plus the command line.
   The log in `<output>/logs/` is the useful part.
2. Workflow gaps: what do you do in PIPP that this makes awkward or impossible?
3. Anyone willing to run a job and compare output frame-by-frame against the original on Windows.

Repo: https://github.com/<your-user>/OPIPP

It is early. If it breaks on your data, that is exactly the report I need - "didn't work on my capture"
with the log is more useful than silence.
