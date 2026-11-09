# Amstrad CPC Hardware Reference

**ScientificBrad — Historical Computer Reverse Engineering**

---

## 1. I/O Port Map and Decoding Rules

The Amstrad CPC uses partial decoding on the 16-bit address bus during I/O transactions:

| Port / Device | Address Bit Condition | Example Port | Direction | Function |
|:---|:---|:---:|:---:|:---|
| **Gate Array** | $A_{15}=0, A_{14}=1$ | `&7Fxx` | Write | Pen select, colour palette, video mode, ROM enable, RAM banking |
| **CRTC Index** | $A_{14}=0, A_9=1, A_8=0$ | `&BC00` | Write | Select CRTC internal register (0–17) |
| **CRTC Data** | $A_{14}=0, A_9=1, A_8=1$ | `&BD00` | Write | Write to selected CRTC register |
| **CRTC Status** | $A_{14}=0, A_9=1, A_8=0$ | `&BE00` | Read | Read CRTC status (where supported) |
| **CRTC Data In** | $A_{14}=0, A_9=1, A_8=1$ | `&BF00` | Read | Read from selected CRTC register |
| **Upper ROM Select** | $A_{15}=1, A_{13}=0$ | `&DF00` | Write | Select Upper ROM bank (0 = BASIC, 7 = AMSDOS) |
| **PPI Port A** | $A_{11}=0, A_9=0, A_8=0$ | `&F400` | Read/Write | AY-3-8912 PSG Data Bus |
| **PPI Port B** | $A_{11}=0, A_9=0, A_8=1$ | `&F500` | Read | Inputs: Cassette, VSYNC (bit 7), LK jumpers |
| **PPI Port C** | $A_{11}=0, A_9=1, A_8=0$ | `&F600` | Write/Read | Keyboard row (bits 0-3), Cassette (bits 4-5), PSG BC1/BDIR (bits 6-7) |
| **PPI Control** | $A_{11}=0, A_9=1, A_8=1$ | `&F700` | Write | 8255 Mode control register |
| **FDC Status** | $A_{10}=0, A_8=0, A_7=0$ | `&FB7E` | Read | µPD765 Main Status Register (MSR) |
| **FDC Data** | $A_{10}=0, A_8=0, A_7=0$ | `&FB7F` | Read/Write | µPD765 Data Register (FIFO) |
| **FDC Motor** | $A_{10}=0, A_8=0, A_7=0$ | `&FA7E` | Write | Floppy drive motor control (bit 0) |

---

## 2. Gate Array Register Layout (`&7Fxx`)

Gate Array operations are selected by bits 7 and 6 of the written byte:

### 2.1 Select Pen / Ink (`00xx xxxx`)
- Bits 7–6: `00`
- Bits 4–0: Pen number:
  - `0x00`–`0x0F` (0–15): Select Ink 0–15.
  - `0x10` (16): Select Border color.

### 2.2 Select Colour (`01xx xxxx`)
- Bits 7–6: `01`
- Bits 4–0: Hardware colour value (0–31, selecting from the 27 physical colours).

### 2.3 Mode and ROM Enable (`10xx xxxx`)
- Bits 7–6: `10`
- Bits 1–0: Video Mode:
  - `00`: Mode 0 (160x200, 16 colours)
  - `01`: Mode 1 (320x200, 4 colours)
  - `10`: Mode 2 (640x200, 2 colours)
  - `11`: Mode 3 (160x200, 4 colours - undocumented)
- Bit 2: Lower ROM Disable (`0` = Lower ROM enabled at `$0000–$3FFF`, `1` = Lower ROM disabled, RAM visible)
- Bit 3: Upper ROM Disable (`0` = Upper ROM enabled at `$C000–$FFFF`, `1` = Upper ROM disabled, RAM visible)
- Bit 4: Reset Interrupt Counter (`1` = Resets Gate Array 52-line counter and clears pending interrupt)

### 2.4 RAM Banking (`11xx xxxx` - CPC 6128)
- Bits 7–6: `11`
- Bits 2–0: Configuration mode (0–7):
  - `0` (C0): Bank 0 (Standard 64K RAM)
  - `1` (C1): Bank 1 (Ext RAM in `$4000-$7FFF`)
  - `2` (C2): Bank 2 (All 64K from expansion RAM)
  - `3` (C3): Bank 3 (Ext RAM in `$C000-$FFFF`, Base bank 3 in `$4000-$7FFF`)
  - `4` (C4): Bank 4 (Ext block 0 in `$4000-$7FFF`)
  - `5` (C5): Bank 5 (Ext block 1 in `$4000-$7FFF`)
  - `6` (C6): Bank 6 (Ext block 2 in `$4000-$7FFF`)
  - `7` (C7): Bank 7 (Ext block 3 in `$4000-$7FFF`)

---

## 3. Keyboard Matrix Layout (10 Rows x 8 Columns)

| Row \ Col | Bit 7 | Bit 6 | Bit 5 | Bit 4 | Bit 3 | Bit 2 | Bit 1 | Bit 0 |
|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **0** | F. (Numpad .) | ENTER (Numpad) | F3 | F6 | F9 | Cursor Down | Cursor Right | Cursor Up |
| **1** | F0 | F2 | F1 | F5 | F8 | F7 | COPY | Cursor Left |
| **2** | CONTROL | \ ` | SHIFT | F4 | ] } | RETURN | [ { | CLR |
| **3** | . > | / ? | : * | ; + | P | @ | - _ | ^ £ |
| **4** | , < | M | K | L | I | O | 9 ) | 0 _ |
| **5** | SPACE | N | J | H | Y | U | 7 ' | 8 ( |
| **6** | V | B | F | G | T | R | 5 % | 6 & |
| **7** | X | C | D | S | W | E | 3 # | 4 $ |
| **8** | Z | CAPSLOCK | A | TAB | Q | ESC | 2 " | 1 ! |
| **9** | DEL | Joy 0 Fire 3 | Joy 0 Fire 2 | Joy 0 Fire 1 | Joy 0 Right | Joy 0 Left | Joy 0 Down | Joy 0 Up |
