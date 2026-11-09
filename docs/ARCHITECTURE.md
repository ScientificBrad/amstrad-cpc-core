# Amstrad CPC Core Architecture

**Author:** ScientificBrad  
**Implementation:** C# / .NET 8 (Core & Windows Forms Emulator)

---

## 1. Modular Subsystem Design

```
+---------------------------------------------------------------------------------+
|                                AmstradMachine                                   |
|                                                                                 |
|  +-------------------+        +---------------------+     +------------------+  |
|  |     CpuZ80        |<------>|     GateArray       |<--->|    Crtc6845      |  |
|  | (Zilog Z80A 4MHz) |        | (Wait states, Video,|     | (Raster/Address  |  |
|  |                   |        |  Palette, Interrupt)|     |  Generator)      |  |
|  +-------------------+        +---------------------+     +------------------+  |
|           |                              |                          |           |
|           v                              v                          v           |
|  +-------------------+        +---------------------+     +------------------+  |
|  |  MemorySubsystem  |        |      IoBus          |     | ScreenRenderer   |  |
|  | 64KB/128KB RAM,   |        | 16-bit port decode  |     | Mode 0/1/2       |  |
|  | Lower/Upper ROMs  |        |                     |     | Framebuffer      |  |
|  +-------------------+        +---------------------+     +------------------+  |
|           |                              |                                      |
|           |                   +----------+----------+                           |
|           |                   |                     |                           |
|           |                   v                     v                           |
|           |           +---------------+     +---------------+                   |
|           |           |    Ppi8255    |<--->|    Ay38912    |                   |
|           |           | (Ports A/B/C) |     | (PSG Sound &  |                   |
|           |           +---------------+     |  Keyboard In) |                   |
|           |                   |             +---------------+                   |
|           |                   v                     |                           |
|           |           +---------------+             v                           |
|           |           | KeyboardMatrix|     +---------------+                   |
|           |           +---------------+     | AudioBuffer   |                   |
|           |                                 +---------------+                   |
|           v                                                                     |
|  +--------------------------------------------------+                           |
|  | Storage: CassetteDeck (.CDT) & FloppyDrive (.DSK)|                           |
|  +--------------------------------------------------+                           |
+---------------------------------------------------------------------------------+
```

### 1.1 Instruction and Timing Pipeline
- **Clock Frequencies:**
  - Master Crystal: 16.0 MHz
  - CPU Clock ($\Phi$): 4.0 MHz (1 T-state = 0.25 µs)
  - Gate Array Video/Memory arbitration clock: 1.0 MHz (4 T-states = 1.0 µs = "1 NOP")
  - Sound Chip Clock: 1.0 MHz
- **Instruction Execution:**
  - Instructions are executed on `CpuZ80`.
  - Machine cycles are aligned to 4 T-states by the Gate Array.
  - Video memory fetches never produce wait-state pauses because video RAM is accessed during alternate phases of the 1 MHz sub-clock.

### 1.2 Video System
- CRTC 6845 increments character counter horizontally and scanlines vertically.
- Gate Array decodes CRTC registers and fetches 2 bytes per CRTC character clock from RAM.
- Display Modes:
  - Mode 0: 160 x 200, 16 colours.
  - Mode 1: 320 x 200, 4 colours.
  - Mode 2: 640 x 200, 2 colours.
- Framebuffer is rendered to a 32-bit ARGB buffer (default 768 x 272 including borders) and presented at 50 Hz.
