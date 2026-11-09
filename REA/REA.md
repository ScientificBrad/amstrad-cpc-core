# Amstrad CPC Reverse Engineering and Reconstruction Assessment (REA)

**Researcher:** ScientificBrad  
**Project:** Amstrad CPC Hardware, Firmware and Emulator Reconstruction  
**Workspace:** `D:\Emulation\AmstradCPC` (linked to `D:\Emulation\Amstrad`)  
**Target Platform:** Windows 11, .NET 8 / C#  
**Methodology:** Clean-room forensic reverse engineering, timing analysis, hardware documentation cross-referencing, automated unit/integration testing against real firmware and software.

---

## 1. Executive Mission Statement

Following our successful clean-room reconstructions of the **Acorn BBC Micro Model B** and **Sinclair ZX Spectrum 48K**, this project undertakes the authentic reconstruction of the **Amstrad CPC computer family**:
- **CPC 464 (1984)**: 64 KB RAM, internal cassette deck, Firmware 1.0, Locomotive BASIC 1.0.
- **CPC 664 (1985)**: 64 KB RAM, 3-inch integrated Hitachi/Panasonic floppy drive, Firmware 1.1, Locomotive BASIC 1.1.
- **CPC 6128 (1985)**: 128 KB RAM (bank-switched), 3-inch floppy drive, Firmware 1.2, Locomotive BASIC 1.1.
- **CPC Plus Series (464+, 6128+) & GX4000 (1990)**: ASIC hardware, 4096-colour palette, hardware sprites, raster splits, cartridge subsystem.

**Core Directives:**
1. **Zero Shortcuts:** No hardcoded screenshots, fake startup screens, or simulated high-level traps substituting for hardware execution.
2. **Authentic Firmware Execution:** The system must boot and execute genuine Amstrad OS ROMs and Locomotive BASIC ROMs verbatim on an emulated Z80A CPU core with authentic Gate Array, CRTC 6845, Intel 8255 PPI, and GI AY-3-8912 PSG emulation.
3. **Motto:** *We don't stop. Everybody else stops. We keep going.*

---

## 2. Key Architecture Principles

1. **Z80A CPU at 4.0 MHz with Gate Array Wait-State Stretching:**
   - 16 MHz master crystal is divided by 4 to generate the 4 MHz CPU clock $\Phi$.
   - Video RAM is shared between CPU and CRTC/Gate Array without bus contention penalties ("snow").
   - The Gate Array achieves this by synchronizing memory cycles to 1 MHz (4 clock cycles) boundaries. Every 3-cycle machine cycle ($M$-cycle) is stretched with a wait state to 4 clock cycles.
   - Consequently, **every Z80 instruction takes an exact multiple of 4 T-states** (1 microsecond = "1 NOP").
2. **Memory Architecture & ROM Overlays:**
   - Flat 64 KB address space for Z80.
   - Lower ROM ($0000–$3FFF): 16 KB OS / Firmware.
   - Upper ROM ($C000–$FFFF): 16 KB BASIC, AMSDOS, or expansion sideways ROMs (selected via port `&DFxx`).
   - Gate Array controls Lower and Upper ROM enable lines.
   - **Critical Hardware Asymmetry:** Writes to $0000–$3FFF or $C000–$FFFF always write through to underlying RAM, regardless of whether ROM read overlay is enabled!
   - CPC 6128 adds 64 KB extension RAM (total 128 KB) switched in 16 KB blocks via Gate Array `&7Fxx` RAM banking register.
3. **CRTC 6845 & Gate Array Video Generation:**
   - Standard text-oriented CRTC (Motorola 6845 / Hitachi HD6845) generates 14-bit memory addresses `MA[13:0]` and 3-bit raster line addresses `RA[2:0]`.
   - Gate Array translates CRTC addresses to physical RAM:
     - `A15..A14` from `MA[13:12]` (video page base, usually `$C000`).
     - `A13..A11` from `RA[2:0]` (line 0–7 within character row, giving 2048-byte stride `$800`).
     - `A10..A0` from `MA[9:0]`.
   - Display modes:
     - **Mode 0:** 160 × 200, 16 colours (4 bpp, 2 pixels/byte, interleaved bits).
     - **Mode 1:** 320 × 200, 4 colours (2 bpp, 4 pixels/byte, interleaved bits).
     - **Mode 2:** 640 × 200, 2 colours (1 bpp, 8 pixels/byte).
   - 27-colour hardware palette generated via 3-state (Off, Half, Full) RGB outputs.
4. **Gate Array Interrupt System:**
   - Internal 6-bit counter increments on every HSYNC.
   - Reaching count 52 asserts `/INT` to Z80 CPU and resets counter.
   - CRTC VSYNC triggers resynchronization on the 2nd HSYNC after VSYNC edge (or triggers INT if counter >= 32).
   - Provides exactly 6 interrupts per 312-line PAL frame ($52 \times 6 = 312$).
5. **Intel 8255 PPI & GI AY-3-8912 Subsystem:**
   - PPI Port A: Bi-directional 8-bit bus tied to AY-3-8912 data bus.
   - PPI Port B: Inputs (Cassette in, VSYNC, Centronics busy, LK1–LK4 configuration jumpers).
   - PPI Port C: Lower nibble selects keyboard matrix row (0–9) via 74LS145 decoder; bits 4–5 cassette out and motor; bits 6–7 control AY PSG `BC1` and `BDIR`.
   - AY-3-8912 Register 14: Keyboard matrix columns (return lines 0–7).
