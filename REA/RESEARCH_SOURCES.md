# Research Sources and Reference Documentation

**Project:** Amstrad CPC Computer Family Reconstruction  
**Researcher:** ScientificBrad

---

## Primary Technical Documentation

1. **CPCWiki Documentation & Technical Compendium**
   - URL: `https://www.cpcwiki.eu/`
   - URL: `https://cpctech.cpcwiki.de/`
   - Key topics: Default I/O Port Summary, Gate Array internal logic, CRTC register behavior, Video RAM interleaving formulas, Hardware colour palette tables, Keyboard matrix layouts.

2. **Amstrad CPC 464 / 664 / 6128 Firmware Guide**
   - Author: Soft 968 / Locomotive Software
   - Comprehensive documentation of the lower ROM kernel, jump packs, entry points (`KM_READ_KEY`, `TXT_OUTPUT`, `GRA_PLOT`, `CAS_READ`, `MC_BOOT_PROGRAM`), low jump vector redirection (`RST 1` to `RST 5`), memory map, and interrupt ticker.

3. **Service & Hardware Manuals**
   - *Amstrad CPC 464 Service Manual* (Amstrad Consumer Electronics plc)
   - *Amstrad CPC 664 Service Manual*
   - *Amstrad CPC 6128 Service Manual*
   - Schematics covering 40007, 40010 Gate Array, Z80A, Intel 8255 PPI, General Instrument AY-3-8912, Motorola 6845, NEC µPD765A.

4. **Datasheets & Chip Specifications**
   - *Zilog Z80 / Z80A CPU User's Manual* (Zilog, Inc.) - Complete opcode matrix, timing diagrams, undocumented flags ($XF, YF, MEMPTR$), interrupts (IM 0, 1, 2).
   - *Motorola MC6845 Cathode Ray Tube Controller Specification* (Motorola Inc.) - Horizontal & vertical sync counters, raster addresses $RA[4:0]$, memory addresses $MA[13:0]$.
   - *General Instrument AY-3-8910 / AY-3-8912 Programmable Sound Generator Data Manual* - Tone generation, noise LFSR, envelope period & shapes, internal 8-bit I/O port.
   - *Intel 8255A / 8255A-5 Programmable Peripheral Interface* - Mode 0/1/2 control word formats, bit set/reset operations.
   - *NEC µPD765A / Intel 8272A Floppy Disk Controller Manual* - Command phases, execution phases, result phases, status registers ST0–ST3.

5. **Community Decaps & Modern Logic Analysis**
   - Bread80 Amstrad Hardware Analysis (`bread80.com`) - Gate Array / WAIT pin interactions, memory arbitration, 4-cycle stretch mechanism.
   - CPCWiki Gate Array Decap Project - Physical verification of silicon gates inside IC 40010.
