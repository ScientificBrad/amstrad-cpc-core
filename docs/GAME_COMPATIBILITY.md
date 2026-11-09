# Amstrad CPC Game and Software Compatibility Ledger

**Project:** Amstrad CPC Computer Family Reconstruction  
**Researcher:** ScientificBrad

---

## Status Classification Criteria

- **NOT TESTED:** Candidate identified, media not yet evaluated.
- **BOOTS:** Machine executes loader; OS/firmware initializes media.
- **LOADS:** Media data loaded into memory without checksum errors.
- **TITLE SCREEN:** Displays graphic title/loading screen.
- **INTERACTIVE:** Menu navigation or control configuration responds to input.
- **PLAYABLE:** Complete core gameplay loop operational without fatal errors.
- **PLAYABLE WITH ISSUES:** Game is playable but exhibits minor graphical/audio discrepancies.
- **FAILED:** Execution crashes, freezes, or produces severe corruption.

---

## Software Compatibility Matrix

| Title | Model | Media | ROM / Hardware Req | Status | Notes & Verification Steps |
|:---|:---:|:---:|:---|:---:|:---|
| **Barbarian: The Ultimate Warrior** | CPC 6128 | Disk (.DSK) | Firmware 1.2, BASIC 1.1, AMSDOS | **PLAYABLE** | Palace Software 1987. Loaded via authentic AMSDOS FDC (`RUN"BARB1A`). Read 183 operations across 33 cylinders, Mode 0 16-color title screen with 14 active palette colors rendered, Space key input starts 1-player combat arena. Joystick and keyboard operational. |
| **Locomotive BASIC 1.0 Prompt** | CPC 464 | Internal ROM | Firmware 1.0, BASIC 1.0 | **PLAYABLE** | Cold reset, banner display, cursor flashing, command prompt ready. Authentic 50.0 FPS. |
| **Locomotive BASIC Graphics Demo**| CPC 464 | Interactive | Firmware 1.0 | **PLAYABLE** | `MODE 1`, `PLOT`, `DRAW`, `CIRCLE`, `INK` commands rendered cleanly. |
| **Roland in the Caves** | CPC 464 | Tape (.CDT) | 64 KB RAM | NOT TESTED | Classic Amsoft launch title. |
| **Roland on the Ropes** | CPC 464 | Tape (.CDT) | 64 KB RAM | NOT TESTED | Amsoft platformer. |
| **Harrier Attack** | CPC 464 | Tape (.CDT) | 64 KB RAM | NOT TESTED | Durell side-scrolling action. |
| **Manic Miner** | CPC 464 | Tape/Disk | 64 KB RAM | NOT TESTED | Software Projects platformer. |
| **Jet Set Willy** | CPC 464 | Tape/Disk | 64 KB RAM | NOT TESTED | Software Projects classic. |
| **Chuckie Egg** | CPC 464 | Tape/Disk | 64 KB RAM | NOT TESTED | A&F Software arcade port. |
| **Head Over Heels** | CPC 464/6128 | Disk (.DSK) | 64/128 KB RAM | NOT TESTED | Ocean isometric masterpiece. |
| **R-Type** | CPC 6128 | Disk (.DSK) | 128 KB RAM | NOT TESTED | Electric Dreams arcade port. |
