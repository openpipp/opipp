# Provenance & licensing

This repository is a **reverse-engineered reconstruction** of PIPP (Planetary Imaging PreProcessor)
by Chris Garry (http://sites.google.com/site/astropipp/ — original site, now offline).

- Original binaries analysed: `PIPP.exe`, `pipp_core.exe`, `pipp_dll.dll`, build timestamp 2017-01-06.
  SHA-256 recorded in `notes/evidence.md`. The installed copy under `C:\Program Files\PIPP` was never modified.
- The `decompiled/` and `ghidra/` directories are **reference artifacts**, not source to be compiled or
  redistributed. They remain the property of the original author.
- New code under `src/` is written for this reconstruction, informed by the observed behaviour of the
  original. It is published under the **GNU GPL v3 or later** (see `LICENSE`), which covers this
  project's own code and artwork only — it grants no rights in PIPP itself.

The project is published in the belief that reproducing file formats and a command-line vocabulary
for interoperability, research and modernization is **fair use**. That is the project's stated
position, not a legal determination.

Safeguards that remain in force:

- no original binaries are distributed with this repository;
- `decompiled/` and `ghidra/` are git-ignored and never shipped, vendored or committed;
- `NOTICE` records the lack of affiliation and the scope of the licence;
- a rights-holder request to change or remove material will be actioned promptly.

