# Changelog

All notable changes to the **Amstrad CPC Family Hardware Reconstruction & Emulation Core** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [v0.1.0] - 2026-11-09 — First Playable Release

### Added
- **Zilog Z80A CPU Core:** Cycle-accurate execution core running at 4.00 MHz supporting all official opcodes, prefixes (`CB`, `ED`, `DD`, `FD`, `DDCB`, `FDCB`), undocumented flags ($XF$, $YF$, $MEMPTR$), interrupt modes (IM 0, 1, 2), and non-maskable interrupts (NMI).
- **Firmware Support:** Authentic cold boot support directly from unmodified factory mask ROMs for CPC 464 (Firmware 1.0 & Locomotive BASIC 1.0), CPC 664 (Firmware 1.1 & Locomotive BASIC 1.1), and CPC 6128 (Firmware 1.2 & Locomotive BASIC 1.1 with AMSDOS 0.5).
- **Memory Architecture:** 64 KB base RAM, 128 KB extended banked RAM, lower ROM paging (`&0000-&3FFF`), upper ROM paging (`&C000-&FFFF`), RAM write-through, and 16-bit port decoded I/O arbitration.
- **Gate Array 40010 / 40007:** Strict 4-clock (1 MHz) memory access stretching to simulate genuine hardware contention arbitration, 52-scanline interrupt counters synchronized to CRTC VSYNC, and dynamic 27-colour hardware palette mapping.
- **Hitachi HD6845 CRTC Video Controller:** Pixel-perfect rendering across Mode 0 (160×200, 16 colours), Mode 1 (320×200, 4 colours), and Mode 2 (640×200, 2 colours) with standard `&800` scanline memory interleaving and hardware screen base scrolling.
- **General Instrument AY-3-8912 PSG Sound Synthesis:**
  - 3 programmable square-wave tone channels with 12-bit dividers.
  - Pseudo-random noise generator with 5-bit period divider.
  - 16-bit hardware envelope generator with authentic shapes.
  - Faithful 16-step logarithmic DAC.
  - Windows WaveOut audio streaming (`winmm.dll`) at 44.1 kHz with dynamic 16-slot ring buffer queue and latency pacing, eliminating crackle and buffer underruns.
- **NEC µPD765 Floppy Disk Controller & AMSDOS:** Complete MFM floppy disk engine supporting standard and extended `.DSK` disk images, geometry parsing, sector reading/writing, and AMSDOS firmware routines (`CAT`, `RUN"PROGRAM"`).
- **Cassette Tape Subsystem:** Authentic hardware pulse-width modulation for `.CDT` and `.TZX` tape images, feeding digital transitions directly into Intel 8255 PPI Port B under motor relay control.
- **Intel 8255 PPI & 10×8 Keyboard Matrix:** Bi-directional PSG bus protocol, CRTC VSYNC status monitoring, printer busy flags, and full keyboard matrix scanning with real-time graphical matrix debugging.
- **Commercial Game Compatibility:** Verified cold boot and retail execution of flagship commercial titles including *Barbarian: The Ultimate Warrior* (1987, Palace Software), *Chase H.Q.* (1989, Ocean), *Subsunk* (1987), and *Stunt Car Racer* (1989).
- **Native Windows GUI Emulator:** Real-time 50.0 Hz PAL frame timing, Direct GDI+ rendering, full keyboard matrix handling, audio volume and mute controls, and live keyboard matrix debugger.
- **Test Suite:** Comprehensive .NET 8 xUnit test harness covering 42 automated hardware tests (100% passing).
- **CI / CD Pipeline:** GitHub Actions workflow verifying clean build, test execution, and release artifact packaging.
