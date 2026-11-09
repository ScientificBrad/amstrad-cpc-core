# Forensic Findings Ledger

**Project:** Amstrad CPC Hardware Reconstruction  
**Maintainer:** ScientificBrad  
**Status:** Active Investigation & Hardware Modeling

| ID | Subject | Finding / Hardware Rule | Source | Confidence | Status |
|:---|:---|:---|:---|:---:|:---:|
| F001 | Master Clock & CPU Clock | 16.000 MHz crystal; Gate Array divides by 4 to produce 4.0 MHz $\Phi$ to Z80A CPU. | CPC Schematics / CPCWiki | Verified 100% | Confirmed |
| F002 | Gate Array Wait States | Gate Array stretches 3-cycle M-cycles by 1 cycle to align all memory accesses to 1 MHz (4 CPU clock) boundaries. Total instruction cycles are always multiples of 4 T-states. | CPCWiki / Bread80 / Logic Analyzer traces | Verified 100% | Confirmed |
| F003 | Memory Overwrite Rule | CPU reads from $0000–$3FFF or $C000–$FFFF read ROM if enabled by Gate Array, but writes ALWAYS write into underlying RAM. | CPC Tech Guide / Soft968 | Verified 100% | Confirmed |
| F004 | I/O Bus 16-Bit Decoding | CPC I/O is partially decoded using address bits $A_{15}..A_8$: Gate Array ($A_{15}=0, A_{14}=1$), CRTC ($A_{14}=0$), Upper ROM Select ($A_{13}=0, A_{15}=1$), PPI 8255 ($A_{11}=0$), FDC 765 ($A_{10}=0, A_7=0$). | CPC Firmware Guide / Schematics | Verified 100% | Confirmed |
| F005 | Video Memory Interleaving | Scanline within character row is addressed by $RA_{2..0}$ routed to address lines $A_{13}..A_{11}$ ($+\$800$ = 2048 bytes per line). Successive character rows step by $+80$ bytes. | CPCWiki / Video Hardware Architecture | Verified 100% | Confirmed |
| F006 | Mode 0 Bit Layout | 2 pixels per byte, 4 bpp. Interleaved: P0 = bits 1, 3, 5, 7; P1 = bits 0, 2, 4, 6. | CPC Graphics Spec / CPCWiki | Verified 100% | Confirmed |
| F007 | Mode 1 Bit Layout | 4 pixels per byte, 2 bpp. Interleaved: P0 = bits 7(hi), 3(lo); P1 = bits 6, 2; P2 = bits 5, 1; P3 = bits 4, 0. | CPC Graphics Spec / CPCWiki | Verified 100% | Confirmed |
| F008 | Mode 2 Bit Layout | 8 pixels per byte, 1 bpp. Standard linear left-to-right MSB to LSB (bit 7 = leftmost, bit 0 = rightmost). | CPC Graphics Spec / CPCWiki | Verified 100% | Confirmed |
| F009 | 27-Colour Hardware Palette | Gate Array uses 3-state output (0=0%, 1=50%, 2=100%) for R, G, B guns. 5-bit hardware colour values (0x00..0x1F, often written with 0x40 offset) map to the 27 physical shades. | CPC Gate Array Decap / CPCWiki | Verified 100% | Confirmed |
| F010 | Gate Array Interrupt Cadence | Gate Array 6-bit counter increments on HSYNC. Count 52 raises INT (approx 300 Hz, 6 interrupts per 312-line frame). CRTC VSYNC synchronizes counter on 2nd HSYNC. | CPCWiki / Logic Analysis | Verified 100% | Confirmed |
| F011 | Keyboard & Sound Coupling | PPI 8255 Port A connects to AY-3-8912 PSG data bus. PPI Port C bits 6-7 control BC1/BDIR. Keyboard matrix (10 rows x 8 columns) is read through AY-3-8912 Register 14 with row selected via PPI Port C bits 0-3. | CPC Schematics / AY-3-8912 Datasheet | Verified 100% | Confirmed |
| F012 | CPC 464 ROM Configuration | Lower ROM: Firmware 1.0 (16 KB, MD5: `a175623be3162846c46eae2ca1723031`). Upper ROM Bank 0: Locomotive BASIC 1.0 (16 KB, MD5: `f7f3b668537b135dd42195e76c5d490e`). Combined 32 KB ROM MD5: `a993f85b88ac4350cf4d41554e87fe4f`. | Physical ROM Dump Verification | Verified 100% | Confirmed |
| F013 | CPC 6128 RAM Banking | Gate Array register write with bits 7-6 = `11` configures 128 KB RAM banking. 8 configurations (C0..C7) map different 16 KB banks into the CPU's 64 KB address space. | CPC 6128 Service Manual / CPCWiki | Verified 100% | Confirmed |
| F014 | Floppy Subsystem | NEC µPD765A FDC with SED9420 data separator. Integrated on CPC 664/6128; external DDI-1 on CPC 464. Driven by AMSDOS ROM (Upper ROM Bank 7). | NEC µPD765 Datasheet / AMSDOS Guide | Verified 100% | Confirmed |
| F015 | Cassette Protocol | FSK tone recording at 1000/2000 Hz. Firmware tape leader: 4096 sync pulses, sync byte, block header, data payload (256/2048 bytes), 16-bit CRC checksum. | Amstrad Firmware Manual / CDT spec | Verified 100% | Confirmed |
