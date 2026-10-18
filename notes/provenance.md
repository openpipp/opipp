# Provenance & licensing

This repository is a **reverse-engineered reconstruction** of PIPP (Planetary Imaging PreProcessor)
by Chris Garry (http://sites.google.com/site/astropipp/ — original site, now offline).

- Original binaries analysed: `PIPP.exe`, `pipp_core.exe`, `pipp_dll.dll`, build timestamp 2017-01-06.
  SHA-256 recorded in `notes/evidence.md`. The installed copy under `C:\Program Files\PIPP` was never modified.
- The `decompiled/` and `ghidra/` directories are **reference artifacts**, not source to be compiled or
  redistributed. They remain the property of the original author.
- New code under `src/` is written for this reconstruction, informed by the observed behaviour of the
  original. It is intended to be released under an OSI licence **only with the original author's
  permission** (or if the author publishes their own source).

Until that permission exists, treat this tree as private research material:

- do not publish the repository or release binaries;
- do not vendor `decompiled/` or `ghidra/out/` anywhere;
- `LICENSE` intentionally absent until provenance is cleared.

If permission is granted, add the chosen licence here plus a NOTICE crediting Chris Garry for the
original PIPP design, formats and algorithms.
