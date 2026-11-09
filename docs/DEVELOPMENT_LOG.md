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

---

### Milestone 2 — Real Game Engine Repair Mission (Palace Software Barbarian Achieved)

#### 1. Performance and Graphics Timing (Workstream 1)
- Replaced naive `Thread.Sleep` with Windows multimedia timer `timeBeginPeriod(1)` and high-precision `Stopwatch` spin-yield loop, locking execution at an authentic **50.0 FPS** (20.0 ms per frame).
- Replaced 1 MHz raw audio push with accurate linear downsampling to 44,100 Hz (882 16-bit samples per 80,000-cycle frame), completely eliminating buffer starvation, stutter, and silence.
- Created precomputed 256-entry lookup tables for video modes 0, 1, and 2 in `ScreenRenderer.cs`, dynamically centering display to eliminate border/overscan clipping.
- Overrode `ProcessDialogKey` in `AmstradWindow.cs` to prevent Windows Forms controls from eating cursor and tab keys, delivering them directly to the Z80 keyboard matrix.

#### 2. Cassette Engine (Workstream 2)
- Reconstructed authentic `.CDT` tape streaming engine (`CdtTape.cs`) supporting standard speed (0x10), turbo speed (0x11), pure tone (0x12), pulse sequence (0x13), and pause (0x20) blocks.
- Connected pulse train to Intel 8255 PPI Port B bit 0 (`CassetteDataIn`) gated by Port C bit 5 (`CassetteMotor`).
- Implemented optional fast tape loading.

#### 3. CPC 664/6128 Floppy Controller & AMSDOS (Workstream 3)
- Reconstructed NEC µPD765A / Intel 8272 Floppy Disk Controller (`Upd765Fdc.cs`) with full state machine handling Specify (`0x03`), Sense Drive Status (`0x04`), Recalibrate (`0x07`), Sense Interrupt Status (`0x08`), Seek (`0x0F`), Read Data (`0x46`/`0x66`), Write Data (`0x45`), and Read ID (`0x4A`).
- Reconstructed `.DSK` (Standard and Extended CPC DSK) image parser (`DskImage.cs`) with multi-track, variable sector size, and geometry decoding.
- Integrated floppy drive motor register at `&FA7E` and FDC MSR/Data ports at `&FB7E`/`&FB7F` in `IoBus.cs`.
- Integrated AMSDOS ROM in Upper ROM Bank 7 with Gate Array CPC 6128 RAM bank switching (`&C0`–`&C7`).

#### 4. Acceptance Milestone: Commercial Game Booted, Verified & Playable
- Booted genuine Amstrad CPC 6128 with Firmware 1.2, Locomotive BASIC 1.1, and AMSDOS ROM.
- Mounted authentic commercial release: `Barbarian: The Ultimate Warrior (1987, Palace Software)` Disk 1.
- Executed `CAT` command via emulated keyboard matrix; AMSDOS spun up floppy motor, read directory sectors `&C1`–`&C4` from track 0, and listed files on screen.
- Executed `RUN"BARB1A` command; AMSDOS and FDC executed 183 sector reads across 33 cylinders into RAM, switched Gate Array to Mode 0, disabled ROMs, and passed execution to the game entry point at `&6F7C`.
- Reached the multicolor 16-color Palace Software title screen with 14 active hardware palette colors.
- Delivered Space key press via keyboard matrix to select 1-Player mode; game transitioned into the in-game combat arena (`PC=&7D44`), responding to authentic keyboard and Joystick 0 controls.
- Verified by automated xUnit suite (25/25 passing tests).

---

### Milestone 3 — Keyboard Authenticity & Audio Prescaler Refinement

#### 1. Hardware-Accurate Keyboard Matrix & Elimination of Synthetic Injections
- **Root Cause Analysis:** Synthetic F7 injection on Enter and Space caused Locomotive BASIC to register `f7` (keypad 7) concurrently with Return, mutating `CAT` into `CAT7` (Syntax error).
- **Corrected Hardware Mapping:**
  - `Enter`/`Return`: strictly asserted at keyboard Row 2, Column 2 only.
  - `Space`: strictly asserted at keyboard Row 5, Column 7 (with existing Joy0 Fire 1 at Row 9, Column 4).
  - `F7`/`NumPad7`: strictly asserted at keyboard Row 1, Column 2 only.
  - All keys strictly assert on KeyDown and release matching matrix positions on KeyUp.
- **Menu/Dialog Bypass:** Overrode `ProcessDialogKey` and `ProcessCmdKey` in `AmstradWindow.cs` to prevent WinForms from swallowing Enter, Space, Escape, Tab, Arrows, and Function keys (`F1`..`F12`), while preserving `Ctrl+R` (Reset) and `Ctrl+P` (Pause/Resume) menu accelerators.
- **Regression Verification:** Added `KeyboardRegressionTests.cs` (5 tests) verifying isolated assertions for Enter, Space, F7, and key releases. Verified that `CAT` in BASIC executes directory sector reads without appending 7 or producing Syntax error, followed by `RUN"BARB1A"` and actual `F7` on DEMO screen to start combat.

#### 2. General Instrument AY-3-8912 Audio Engine Dialed In
- Implemented authentic AY-3-8910/8912 hardware prescalers:
  - Tone counters countdown toggling every $8 \times TP$ master clocks ($f_T = f_{\text{CLOCK}} / (16 \times TP)$).
  - Noise LFSR shifting every $16 \times NP$ master clocks.
  - Envelope generator stepping volume every $16 \times EP$ master clocks.
- Replaced point-sampling with oversampled box-filter integration (multi-sample averaging across each 44.1 kHz window), eliminating aliasing distortion.
- Added 1st-order High-Pass DC-blocking filter ($R=0.995$, $f_c \approx 35\text{ Hz}$), removing DAC unipolar DC offset pops.
