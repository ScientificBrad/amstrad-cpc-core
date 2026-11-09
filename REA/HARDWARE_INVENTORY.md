# Amstrad CPC Hardware Inventory

**Project:** Amstrad CPC Computer Family Reconstruction  
**Researcher:** ScientificBrad

---

## 1. Primary Silicon Components

### Central Processing Unit (CPU)
- **Part:** Zilog Z80A (or NEC D780C-1, Mostek MK3880)
- **Clock:** 4.000 MHz (derived from 16.000 MHz master crystal / 4)
- **Package:** 40-pin DIP
- **Architecture:** 8-bit accumulator/ALU, 16-bit address bus (64 KB addressable), 8-bit data bus.
- **Timing Contention:** Synchronized by Gate Array READY / WAIT to 1 MHz (4-cycle) boundaries.

### Amstrad Gate Array
- **Part Numbers:**
  - Amstrad 40007 / 40008 (early CPC 464)
  - Amstrad 40010 (CPC 464 later revisions, CPC 664, CPC 6128)
  - Amstrad 40226 (Pre-ASIC SMD)
  - ASIC 40015 (CPC Plus / GX4000)
- **Package:** 40-pin DIP (or 100-pin QFP on Plus ASIC)
- **Key Roles:**
  - 16 MHz master oscillator input and clock division (4 MHz CPU, 1 MHz/2 MHz CRTC/pixel clock).
  - Memory contention and WAIT state generation.
  - Video serialization: Mode 0 (16 colours), Mode 1 (4 colours), Mode 2 (2 colours).
  - 27-colour hardware palette selection (16 pens + 1 border).
  - Upper and Lower ROM enable/disable gating.
  - 52-line HSYNC interrupt counter and VSYNC resynchronization.
  - RAM banking control (CPC 6128).

### Cathode Ray Tube Controller (CRTC)
- **Part:** Motorola MC6845 / Hitachi HD6845 / UM6845R / CPC ASIC internal CRTC
- **Clock:** 1.0 MHz character clock (1 character = 16 pixels Mode 2, 8 pixels Mode 1, 4 pixels Mode 0 = 2 bytes)
- **Package:** 40-pin DIP
- **Registers:** 18 internal registers (R0 to R17) accessed via I/O index register (`&BCxx`) and data register (`&BDxx`).
- **CRTC Types:**
  - Type 0: HD6845 / UM6845
  - Type 1: UM6845R / AMS40489
  - Type 2: MC6845
  - Type 3: CPC+ ASIC internal CRTC
  - Type 4: Pre-ASIC cost-reduced CPC 6128

### Programmable Peripheral Interface (PPI)
- **Part:** Intel 8255A-5 / NEC µPD8255AC-5
- **Package:** 40-pin DIP
- **Ports:**
  - Port A (8-bit bidirectional, `&F4xx`): Connected to AY-3-8912 PSG data bus.
  - Port B (8-bit input, `&F5xx`): Cassette input, Centronics printer busy, LK jumpers, CRTC VSYNC.
  - Port C (8-bit output, `&F6xx`): Lower 4 bits row select (0-9) to 74LS145; bits 4-5 cassette; bits 6-7 AY BC1/BDIR.
  - Control Register (`&F7xx`): Mode 0 configured (Port A bidir/in/out, Port B input, Port C output).

### Programmable Sound Generator (PSG)
- **Part:** General Instrument AY-3-8912
- **Clock:** 1.000 MHz (16 MHz / 16)
- **Package:** 28-pin DIP
- **Features:** 3 square-wave tone channels (A, B, C), 1 noise generator, 1 envelope generator with 16 shapes, 1 8-bit general purpose I/O port (Port A).
- **Wiring on CPC:** Port A is tied directly to the 8 columns of the keyboard matrix.

### Floppy Disk Controller (FDC)
- **Part:** NEC µPD765A / Intel 8272A
- **Clock:** 4.0 MHz
- **Package:** 40-pin DIP
- **Support Chip:** SED9420 / FDC9229 data separator.
- **Drive Geometry:** 3-inch Compact Floppy Disc (CF-2), single-sided (SS/DD), 40 tracks, 9 sectors per track, 512 bytes per sector (178 KB formatted capacity per side).

---

## 2. Memory Subsystem Specifications

| Model | Total RAM | RAM Organization | Lower ROM | Upper ROM Banks | Max Upper Banks |
|:---|:---:|:---|:---:|:---:|:---:|
| **CPC 464** | 64 KB | 8x 64Kx1 DRAM (4164) | 16 KB (OS 1.0) | Bank 0 (BASIC 1.0) | Up to 16/256 via expansion |
| **CPC 664** | 64 KB | 8x 64Kx1 DRAM (4164) | 16 KB (OS 1.1) | Bank 0 (BASIC 1.1), Bank 7 (AMSDOS) | Up to 16/256 |
| **CPC 6128** | 128 KB | 16x 64Kx1 DRAM (4164) or 4x 64Kx4 | 16 KB (OS 1.2) | Bank 0 (BASIC 1.1), Bank 7 (AMSDOS) | Up to 16/256 |
| **CPC 464+** | 64 KB | 2x 64Kx4 or 8x 64Kx1 | Cartridge Page | Cartridge Pages (0..31) | 512 KB cartridge space |
| **CPC 6128+**| 128 KB | 4x 64Kx4 DRAM | Cartridge Page | Cartridge Pages (0..31) | 512 KB cartridge space |
| **GX4000** | 64 KB | 2x 64Kx4 DRAM | Cartridge Page | Cartridge Pages (0..31) | 512 KB cartridge space |
