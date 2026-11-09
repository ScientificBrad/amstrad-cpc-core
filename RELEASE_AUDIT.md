# Release Audit & Pre-Publication Checklist

**Amstrad CPC Family Hardware Reconstruction & Emulation Core**  
**Release Target:** v0.1.0 (First Playable Release)  
**Audit Date:** November 9, 2026  
**Auditor:** ScientificBrad  

---

## 1. Audit Scope & Verification Matrix

| Category | Item Checked | Verification Method | Status |
| :--- | :--- | :--- | :---: |
| **Intellectual Property** | No copyrighted ROMs in Git | Strict `.gitignore` audit (`*.rom`, `*.ROM`) | **PASS** |
| **Intellectual Property** | No commercial disk/tape dumps in Git | Strict `.gitignore` audit (`*.dsk`, `*.cdt`, `*.tzx`) | **PASS** |
| **Clean Distribution** | Legal notices present | `LICENSE`, `THIRD_PARTY_NOTICES.md`, `roms/README.md` | **PASS** |
| **Build Integrity** | C# Emulator Compilation | `dotnet build -c Release` (0 errors, 0 warnings) | **PASS** |
| **Self-Contained Executable**| Standalone win-x64 binary | `dotnet publish -r win-x64 --self-contained` | **PASS** |
| **Automated Testing** | C# xUnit Test Suite | 42/42 unit tests passed (net8.0-windows) | **PASS** |
| **CPU Emulation** | Zilog Z80A Microprocessor | Full instruction set, prefixes, undocumented flags, IM 0/1/2 | **PASS** |
| **Firmware Execution** | Amstrad OS & Locomotive BASIC | Clean cold boot into BASIC 1.0 (CPC 464) and 1.1 (CPC 6128) | **PASS** |
| **Memory Subsystem** | 64K / 128K Banking | Lower/Upper ROM paging, RAM write-through, 16-bit I/O | **PASS** |
| **Video Engine** | Gate Array & HD6845 CRTC | Mode 0/1/2 rendering, 27-color palette, R12/R13 scrolling | **PASS** |
| **Audio Synthesis** | AY-3-8912 PSG | 3 tone channels, noise generator, envelope, 44.1 kHz WinMM | **PASS** |
| **Storage Subsystem** | NEC µPD765 FDC & AMSDOS | DSK sector streaming, geometry parsing, CAT / RUN" | **PASS** |
| **Tape Subsystem** | CDT Pulse Modulation | High-fidelity pulse streaming to PPI Port B with motor relay | **PASS** |
| **Input Subsystem** | PPI 8255 & Keyboard Matrix | 10×8 hardware matrix, live viewer, Enter/Space/F7 mapped | **PASS** |
| **Flagship Game** | Barbarian (1987) | Title screen, combat gameplay, authentic audio, 50 FPS | **PASS** |
| **Commercial Suite** | Multi-Game Testing | Chase H.Q., Subsunk, Stunt Car Racer verified | **PASS** |
| **Visual Gallery** | Authenticated PNG Screenshots | 5 verified screenshots in `docs/images/` | **PASS** |
| **CI / CD Pipeline** | GitHub Actions Workflow | `.github/workflows/ci.yml` with clean-room tests | **PASS** |

---

## 2. Test Execution Ledger

```text
========================================================================================================
C# xUnit Test Project (tests/AmstradCPC.Tests/AmstradCPC.Tests.csproj)
Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 7.2s
========================================================================================================
TOTAL AUTOMATED TESTS: 42
TOTAL PASSED: 42 (100%)
TOTAL FAILED: 0
```
