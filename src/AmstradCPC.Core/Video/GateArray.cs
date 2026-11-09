using AmstradCPC.Core.Memory;

namespace AmstradCPC.Core.Video;

/// <summary>
/// Amstrad CPC Gate Array (IC 40007 / 40010).
/// Reconstructs:
/// - 27-colour palette (16 pens + 1 border)
/// - Screen video mode selection (Mode 0, 1, 2)
/// - Lower and Upper ROM enable lines
/// - 52-line HSYNC interrupt counter with VSYNC resynchronization
/// - RAM banking register for CPC 6128
/// </summary>
public sealed class GateArray
{
    private readonly MemorySubsystem _memory;

    // Palette: 16 pens + border (index 16)
    public byte SelectedPen { get; set; } = 0;
    public byte[] PenHardwareColors { get; } = new byte[17];
    public uint[] PenRgbColors { get; } = new uint[17];

    // Current video mode (0, 1, or 2)
    public byte VideoMode { get; set; } = 1;

    // 52-line HSYNC Interrupt Generator
    public int HsyncCounter { get; private set; } = 0;
    public bool InterruptRequested { get; set; } = false;

    // VSYNC resynchronization state
    private int _vsyncHsyncCount = 0;
    private bool _inVsync = false;

    // 32 hardware colour values to 32-bit ARGB table
    public static readonly uint[] HardwareColorToArgb = new uint[32]
    {
        0xFF808080, // 0x00: White (Pastel)
        0xFF808080, // 0x01: White (Duplicate)
        0xFF00FF80, // 0x02: Sea Green
        0xFFFFFF80, // 0x03: Pastel Yellow
        0xFF000080, // 0x04: Blue
        0xFFFF0080, // 0x05: Purple
        0xFF008080, // 0x06: Cyan
        0xFFFF8080, // 0x07: Pink
        0xFF00FF80, // 0x08: Sea Green (Duplicate)
        0xFFFFFF80, // 0x09: Pastel Yellow (Duplicate)
        0xFFFFFF00, // 0x0A: Bright Yellow
        0xFFFFFFFF, // 0x0B: Bright White
        0xFFFF0000, // 0x0C: Bright Red
        0xFFFF00FF, // 0x0D: Bright Magenta
        0xFFFF8000, // 0x0E: Orange
        0xFFFF80FF, // 0x0F: Pastel Magenta
        0xFF000080, // 0x10: Blue (Duplicate)
        0xFFFF0080, // 0x11: Purple (Duplicate)
        0xFF00FF00, // 0x12: Bright Green
        0xFF00FFFF, // 0x13: Bright Cyan
        0xFF000000, // 0x14: Black
        0xFF0000FF, // 0x15: Bright Blue
        0xFF008000, // 0x16: Green
        0xFF0080FF, // 0x17: Sky Blue
        0xFF800080, // 0x18: Magenta
        0xFF80FF80, // 0x19: Pastel Green
        0xFF80FF00, // 0x1A: Lime Green
        0xFF80FFFF, // 0x1B: Pastel Cyan
        0xFF800000, // 0x1C: Red
        0xFF8000FF, // 0x1D: Mauve
        0xFF808000, // 0x1E: Yellow
        0xFF8080FF  // 0x1F: Pastel Blue
    };

    public GateArray(MemorySubsystem memory)
    {
        _memory = memory;
        Reset();
    }

    public void Reset()
    {
        SelectedPen = 0;
        VideoMode = 1;
        HsyncCounter = 0;
        InterruptRequested = false;
        _vsyncHsyncCount = 0;
        _inVsync = false;

        // Default startup colours: Pen 0 = Blue (0x04), Pen 1 = Bright Yellow (0x0A), Border = Blue (0x04)
        for (int i = 0; i < 17; i++)
        {
            SetPenColor(i, (byte)((i == 1) ? 0x0A : 0x04));
        }
    }

    /// <summary>
    /// Gate Array I/O Write transaction (&7Fxx).
    /// </summary>
    public void WriteRegister(byte data)
    {
        switch (data >> 6)
        {
            case 0: // 00: Select Pen
                if ((data & 0x10) != 0)
                {
                    SelectedPen = 16; // Border
                }
                else
                {
                    SelectedPen = (byte)(data & 0x0F);
                }
                break;

            case 1: // 01: Select Colour for currently selected pen
                byte color = (byte)(data & 0x1F);
                SetPenColor(SelectedPen, color);
                break;

            case 2: // 10: Mode and ROM Configuration
                VideoMode = (byte)(data & 0x03);
                _memory.LowerRomEnabled = (data & 0x04) == 0;
                _memory.UpperRomEnabled = (data & 0x08) == 0;

                if ((data & 0x10) != 0)
                {
                    // Reset 52-line interrupt counter and clear pending interrupt
                    HsyncCounter = 0;
                    InterruptRequested = false;
                }
                break;

            case 3: // 11: RAM Banking (CPC 6128)
                _memory.SetRamBankConfig(data);
                break;
        }
    }

    private void SetPenColor(int penIndex, byte hardwareColor)
    {
        if (penIndex >= 0 && penIndex < 17)
        {
            PenHardwareColors[penIndex] = hardwareColor;
            PenRgbColors[penIndex] = HardwareColorToArgb[hardwareColor & 0x1F];
        }
    }

    /// <summary>
    /// Called on each CRTC HSYNC pulse edge.
    /// Manages the 52-line counter and VSYNC resynchronization.
    /// </summary>
    public void OnHsync()
    {
        HsyncCounter++;

        if (_inVsync)
        {
            _vsyncHsyncCount++;
            if (_vsyncHsyncCount == 2)
            {
                // On the 2nd HSYNC after VSYNC start:
                // If counter >= 32, generate an interrupt
                if (HsyncCounter >= 32)
                {
                    InterruptRequested = true;
                }
                HsyncCounter = 0;
            }
        }

        if (HsyncCounter >= 52)
        {
            HsyncCounter = 0;
            InterruptRequested = true;
        }
    }

    /// <summary>
    /// Called when CRTC VSYNC state changes.
    /// </summary>
    public void SetVsync(bool vsyncActive)
    {
        if (vsyncActive && !_inVsync)
        {
            _inVsync = true;
            _vsyncHsyncCount = 0;
        }
        else if (!vsyncActive && _inVsync)
        {
            _inVsync = false;
        }
    }
}
