# Development Log

**Project:** Amstrad CPC Computer Family Reconstruction  
**Researcher:** ScientificBrad

---

### Phase 0 & Milestone 1 — Foundation & Forensic Setup
- Initialized dedicated repository structure: `src/`, `tests/`, `REA/`, `docs/`, `roms/`, `tapes/`, `disks/`, `cartridges/`.
- Verified physical ROM dumps:
  - `OS464.ROM` (16 KB, MD5 `a175623be3162846c46eae2ca1723031`)
  - `BASIC1-0.ROM` (16 KB, MD5 `f7f3b668537b135dd42195e76c5d490e`)
  - `CPC464-combined.rom` (32 KB, MD5 `a993f85b88ac4350cf4d41554e87fe4f`)
- Established Clean-Room Reverse Engineering documentation (`REA.md`, `FINDINGS_LEDGER.md`, `HARDWARE_INVENTORY.md`, `FIRMWARE_INVENTORY.md`, `RESEARCH_SOURCES.md`, `KNOWN_UNKNOWNS.md`).
- Verified core architectural timings:
  - 16 MHz master clock $\to$ 4 MHz CPU clock.
  - Gate Array 1 MHz memory sync (stretching 3-cycle M-cycles by 1 cycle, making all instructions multiples of 4 T-states).
  - CRTC 6845 raster generation with 2048-byte `$800` scanline stride.
  - Video Modes 0, 1, and 2 bit interleaving rules.
  - 27-colour hardware palette matrix.
  - PPI 8255 and AY-3-8912 keyboard matrix scanning coupling.
- Creating .NET 8 solution and projects (`AmstradCPC.Core`, `AmstradCPC.Emulator`, `AmstradCPC.Debugger`, `AmstradCPC.Tests`).
