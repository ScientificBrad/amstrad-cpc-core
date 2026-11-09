# Changelog

All notable changes to the Amstrad CPC Reconstruction project will be documented in this file.

## [0.1.0] - 2026-11-09
### Added
- Phase 0: Forensic hardware inventory, findings ledger, firmware inventory, and reverse engineering assessment.
- Phase 1: Clean-room Z80A CPU core with full instruction decoding, prefix support (CB, ED, DD, FD, DDCB, FDCB), cycle tracking, and disassembler.
- Phase 2: Amstrad CPC memory architecture with 64 KB/128 KB RAM, lower/upper ROM paging, write-through to RAM, and 16-bit I/O port decoding.
- Phase 3: Gate Array with wait-state stretch, Mode 0/1/2 video serializers, 27-colour palette, and 52-line interrupt counter.
- Phase 5 & 6: Intel 8255 PPI and General Instrument AY-3-8912 PSG with 10x8 keyboard matrix scanning.
- Verified physical ROM dumps: `OS464.ROM` and `BASIC1-0.ROM`.
