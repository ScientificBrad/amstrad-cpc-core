# Known Unknowns and Technical Investigation Ledger

**Project:** Amstrad CPC Computer Family Reconstruction  
**Researcher:** ScientificBrad

---

## Technical Uncertainties and Investigation Strategy

| ID | Topic | Description & Uncertainty | Investigation Path & Resolution Strategy | Priority |
|:---|:---|:---|:---|:---:|
| U001 | CRTC Variant Subtleties | Differences between CRTC Type 0 (HD6845), Type 1 (UM6845R), Type 2 (MC6845), Type 3 (ASIC), and Type 4 (Pre-ASIC). Some demo effects depend on specific register write timing during VSYNC. | Model baseline Type 0/1 first, verify with CPC 464 firmware boot, then add configurable CRTC types and validation tests. | Medium |
| U002 | Gate Array Interrupt Delay after VSYNC | Exact cycle timing between VSYNC rising edge and the 2-HSYNC interrupt counter resynchronization. | Test with firmware interrupt handler and frame-synced demos; confirm with logic analyzer traces. | High |
| U003 | Undocumented Z80 Flag Calculations | Behavior of undocumented flags $XF$ (bit 3) and $YF$ (bit 5) in instructions such as `BIT n, (HL)`, `CPI`, `CPD`, and 16-bit arithmetic. | Implement flag tables matching established Z80 clean-room behavior; verify with automated Z80 CPU test suites. | High |
| U004 | FDC Status Register Polling Edge Cases | Handling of non-standard track layouts and copy protections (speedlocks, weak bits) in extended DSK images. | Implement standard NEC µPD765 command set (Read Sector, Write Sector, Read ID, Recalibrate, Seek) first; extend with honest status codes. | Medium |
| U005 | Tape Loading Edge Jitter | Exact tolerance of firmware cassette loading routines to pulse duration variance and fast-forward tape hacks. | Implement direct bitstream injection from standard `.CDT` files and high-level firmware trap bypass options as user-selectable modes. | Medium |
| U006 | Plus ASIC Sprite Multiplexing | Hardware sprite scanline limits and priority over background inks in CPC Plus architecture. | Detailed inspection in Phase 11 milestone once classic CPC architecture is fully verified. | Deferred (Phase 11) |
