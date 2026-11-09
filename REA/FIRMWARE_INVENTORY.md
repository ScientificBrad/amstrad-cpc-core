# Amstrad CPC Firmware Inventory

**Project:** Amstrad CPC Reconstruction  
**Researcher:** ScientificBrad

---

## 1. Physical Verification of ROM Dumps

The following official ROM dumps have been forensically verified in the workspace:

### CPC 464 (Model 464)
- **File:** `roms/OS464.ROM`
  - **Size:** 16,384 bytes (16 KB)
  - **MD5:** `a175623be3162846c46eae2ca1723031`
  - **SHA256:** `edca8a0beddc281f2c72d74d783356fa670833cbdedda167c544264f1876c85d`
  - **Description:** Amstrad CPC 464 Operating System (Firmware v1.0). Contains cold reset entry, low jump pack, kernel, screen pack, cassette manager, sound manager, keyboard scanner, text VDU driver, graphics VDU driver.
  - **Target Address:** `$0000–$3FFF` (Lower ROM).

- **File:** `roms/BASIC1-0.ROM`
  - **Size:** 16,384 bytes (16 KB)
  - **MD5:** `f7f3b668537b135dd42195e76c5d490e`
  - **SHA256:** `4b650a78149b1c679224621473594c0e15b97a0113bbe73c0c8d51ef14bfb77e`
  - **Description:** Locomotive Software BASIC v1.0. Full floating-point math, tokenized interpreter, graphics commands (PLOT, DRAW, MOVE, CIRCLE), sound synthesis commands (SOUND, ENV, ENT), string manipulation.
  - **Target Address:** `$C000–$FFFF` (Upper ROM Bank 0).

- **File:** `roms/CPC464-combined.rom`
  - **Size:** 32,768 bytes (32 KB)
  - **MD5:** `a993f85b88ac4350cf4d41554e87fe4f`
  - **SHA256:** `00960d9bf75b2b90856c970f1aa078e1e2aa028b2c104f1dded0262f5d37b15e`
  - **Description:** Single 27C256 EPROM containing OS464 (bytes 0–16383) followed by BASIC1-0 (bytes 16384–32767). Used on cost-reduced single-ROM CPC 464 motherboards.

---

## 2. Firmware Version Mapping Matrix

| Model | Lower ROM | Upper ROM Bank 0 | Upper ROM Bank 7 | Notes |
|:---|:---|:---|:---|:---|
| **CPC 464** | Firmware 1.0 (16 KB) | Locomotive BASIC 1.0 (16 KB) | None (Cassette only) | Classic 464 |
| **CPC 664** | Firmware 1.1 (16 KB) | Locomotive BASIC 1.1 (16 KB) | AMSDOS 0.5 (16 KB) | First floppy CPC |
| **CPC 6128** | Firmware 1.2 (16 KB) | Locomotive BASIC 1.1 (16 KB) | AMSDOS 0.5 (16 KB) | 128 KB flagship |
| **CPC 464+** | Cartridge Page 0 (OS 2.0) | Cartridge Page 1 (BASIC 1.1) | Cartridge Page 3 (Burnin' Rubber) | ASIC Plus |
| **CPC 6128+**| Cartridge Page 0 (OS 2.0) | Cartridge Page 1 (BASIC 1.1) | Cartridge Page 7 (AMSDOS), Page 3 | ASIC Plus |
| **GX4000** | None (Boots from Cartridge) | Cartridge Page 0 | Cartridge Page 1..31 | Console |

---

## 3. Firmware Entry Points & Architecture

- **`$0000` (RST 0):** Hardware Cold / Warm Reset entry.
- **`$0008` (RST 1 / `LOW JUMP`):** Firmware indirect call handler.
- **`$0010` (RST 2 / `SIDE CALL`):** Inter-ROM bank call mechanism.
- **`$0018` (RST 3 / `FAR CALL`):** Far call handler across RAM/ROM banks.
- **`$0020` (RST 4 / `RAM LAM`):** Read byte from alternate bank.
- **`$0028` (RST 5 / `FIRM JUMP`):** Fast firmware routine call.
- **`$0030` (RST 6):** Unused / Reserved.
- **`$0038` (RST 7 / `INTERRUPT`):** Z80 Interrupt Mode 1 vector (Gate Array 300 Hz ticker).
- **`$B800–$B900`:** Central firmware jump block (`KM_READ_KEY`, `TXT_OUTPUT`, `GRA_PLOT`, `CAS_READ`, `SOUND_QUEUE`, etc.).
