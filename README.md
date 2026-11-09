# Amstrad CPC Hardware Core & Real-Time Emulator

**Master Antigravity Engineering Mission**  
**Lead Engineer:** ScientificBrad  
**Development Platform:** Windows 11 / .NET 8 / C#  
**Motto:** *We don't stop. Everybody else stops. We keep going.*

---

## Overview

A clean-room, cycle-accurate reconstruction of the **Amstrad CPC 8-bit personal computer family**:
- **CPC 464 (1984)** — 64 KB RAM, Cassette, Firmware 1.0, Locomotive BASIC 1.0
- **CPC 664 (1985)** — 64 KB RAM, 3-inch Floppy Drive, Firmware 1.1, Locomotive BASIC 1.1
- **CPC 6128 (1985)** — 128 KB RAM, 3-inch Floppy Drive, Firmware 1.2, Locomotive BASIC 1.1
- **CPC 464 Plus / 6128 Plus / GX4000 (1990)** — Enhanced ASIC, 4096-colour palette, hardware sprites, cartridge system

## Architectural Highlights

- **Zilog Z80A Core:** Authentic cycle-accurate processor running at 4.0 MHz with full support for all standard instructions, prefixes (`CB`, `ED`, `DD`, `FD`, `DDCB`, `FDCB`), undocumented flags ($XF, YF, MEMPTR$), interrupts (IM 0, 1, 2), and NMI.
- **Gate Array Contention Arbitration:** Gate Array stretches memory accesses to 1 MHz boundaries (4 T-states), ensuring zero-contention video RAM sharing and strict 4-clock execution granularity.
- **CRTC 6845 & Video Subsystem:** Accurate horizontal and vertical timing, character row memory addressing with 2048-byte `$800` scanline stride, Mode 0 (160x200 16-colour), Mode 1 (320x200 4-colour), Mode 2 (640x200 2-colour), and 27-colour hardware palette.
- **Gate Array Interrupt Ticker:** 52-line HSYNC counter generating PAL frame-synced interrupts (approx 300 Hz) with VSYNC resynchronization.
- **Intel 8255 PPI & AY-3-8912 PSG:** Fully coupled audio synthesis and 10x8 matrix keyboard scanning.
- **Authentic Firmware Execution:** Cold boots genuine unmodified Amstrad firmware ROMs directly to the Locomotive BASIC prompt.

## Project Structure

```
AmstradCPC/
  src/
    AmstradCPC.Core/        # Hardware emulation core (CPU, Gate Array, CRTC, PPI, PSG, Memory)
    AmstradCPC.Emulator/    # Real-time Windows GUI application (Direct GDI+ renderer, sound buffer)
    AmstradCPC.Debugger/    # CPU disassembly, memory inspector, trace analysis
  tests/
    AmstradCPC.Tests/       # Automated xUnit test suite (CPU, memory, video, firmware boot)
  REA/                      # Forensic reverse engineering artifacts & findings ledger
  docs/                     # Architecture, hardware reference, compatibility logs
  roms/                     # User-supplied firmware ROM dumps
  tapes/                    # User-supplied .CDT cassette images
  disks/                    # User-supplied .DSK floppy disk images
  cartridges/               # User-supplied .CPR Plus cartridges
```

## Building and Running

Prerequisites: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# Build entire solution
dotnet build

# Run automated tests
dotnet test

# Launch Windows emulator
dotnet run --project src/AmstradCPC.Emulator
```
