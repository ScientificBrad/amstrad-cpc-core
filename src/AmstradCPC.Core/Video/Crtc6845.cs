namespace AmstradCPC.Core.Video;

/// <summary>
/// Motorola 6845 Cathode Ray Tube Controller (CRTC Type 0/1).
/// Responsible for raster timing, HSYNC/VSYNC pulses, and video memory address generation.
/// </summary>
public sealed class Crtc6845
{
    // 18 internal registers
    public byte[] Registers { get; } = new byte[18];
    public byte SelectedRegister { get; set; } = 0;

    // Current raster counters
    public int HorizontalCounter { get; private set; } = 0; // 0 to R0
    public int RasterLine { get; private set; } = 0;        // 0 to R9
    public int VerticalCounter { get; private set; } = 0;   // 0 to R4
    public ushort MemoryAddress { get; private set; } = 0x3000;
    private ushort _rowStartAddress = 0x3000;

    // Signal flags
    public bool Hsync { get; private set; } = false;
    public bool Vsync { get; private set; } = false;
    public bool DisplayEnable { get; private set; } = false;

    // Event hooks for Gate Array synchronization
    public Action? OnHsyncStart { get; set; }
    public Action<bool>? OnVsyncChange { get; set; }
    public Action? OnFrameComplete { get; set; }

    public Crtc6845()
    {
        Reset();
    }

    public void Reset()
    {
        SelectedRegister = 0;
        Array.Clear(Registers, 0, Registers.Length);

        // Standard Amstrad CPC 464 PAL initial values
        Registers[0] = 63; // R0: Horizontal Total (64 chars)
        Registers[1] = 40; // R1: Horizontal Displayed (40 chars = 80 bytes)
        Registers[2] = 46; // R2: HSYNC Position
        Registers[3] = 14; // R3: HSYNC & VSYNC width (Sync width)
        Registers[4] = 38; // R4: Vertical Total (39 char rows)
        Registers[5] = 0;  // R5: Vertical Total Adjust
        Registers[6] = 25; // R6: Vertical Displayed (25 char rows = 200 scanlines)
        Registers[7] = 30; // R7: VSYNC Position
        Registers[8] = 0;  // R8: Interlace mode
        Registers[9] = 7;  // R9: Max Raster Address (8 scanlines/char)
        Registers[12] = 0x30; // R12: Screen Base ($C000 >> 8)
        Registers[13] = 0x00; // R13: Screen Base Low

        HorizontalCounter = 0;
        RasterLine = 0;
        VerticalCounter = 0;
        _rowStartAddress = (ushort)((Registers[12] << 8) | Registers[13]);
        MemoryAddress = _rowStartAddress;
    }

    public void WriteIndex(byte reg)
    {
        SelectedRegister = (byte)(reg & 0x1F);
    }

    public void WriteData(byte data)
    {
        if (SelectedRegister < 18)
        {
            Registers[SelectedRegister] = data;
        }
    }

    public byte ReadData()
    {
        if (SelectedRegister < 18)
        {
            return Registers[SelectedRegister];
        }
        return 0;
    }

    public byte ReadStatus()
    {
        return 0x00; // Standard CRTC status
    }

    /// <summary>
    /// Steps the CRTC by 1 character clock (1.0 µs = 4 Z80 cycles = 2 bytes video fetch).
    /// </summary>
    public void StepCharacterClock()
    {
        int r0 = Registers[0];
        int r1 = Registers[1];
        int r2 = Registers[2];
        int r3 = Registers[3] & 0x0F;
        int r4 = Registers[4];
        int r6 = Registers[6];
        int r7 = Registers[7];
        int r9 = Registers[9];

        // Horizontal Sync Generation
        bool oldHsync = Hsync;
        Hsync = (HorizontalCounter >= r2) && (HorizontalCounter < (r2 + (r3 != 0 ? r3 : 16)));
        if (Hsync && !oldHsync)
        {
            OnHsyncStart?.Invoke();
        }

        // Display Enable (horizontal & vertical within displayed limits)
        DisplayEnable = (HorizontalCounter < r1) && (VerticalCounter < r6);

        // Advance character counter
        HorizontalCounter++;
        MemoryAddress++;

        if (HorizontalCounter > r0)
        {
            HorizontalCounter = 0;

            // Advance raster line within character row
            RasterLine++;
            if (RasterLine > r9)
            {
                RasterLine = 0;
                _rowStartAddress += (ushort)r1;
                VerticalCounter++;

                // Vertical Sync Check
                bool oldVsync = Vsync;
                Vsync = (VerticalCounter >= r7) && (VerticalCounter < (r7 + 2)); // 2 char rows approx 16 lines
                if (Vsync != oldVsync)
                {
                    OnVsyncChange?.Invoke(Vsync);
                }

                if (VerticalCounter > r4)
                {
                    VerticalCounter = 0;
                    _rowStartAddress = (ushort)(((Registers[12] & 0x3F) << 8) | Registers[13]);
                    OnFrameComplete?.Invoke();
                }
            }
            MemoryAddress = _rowStartAddress;
        }
    }

    /// <summary>
    /// Computes the 16-bit video RAM fetch address from current MA and RA.
    /// Address = ((MA & 0x3000) << 2) | ((RA & 7) << 11) | ((MA & 0x03FF) << 1)
    /// </summary>
    public ushort GetCurrentVideoAddress()
    {
        ushort ma = MemoryAddress;
        int ra = RasterLine & 0x07;
        return (ushort)(((ma & 0x3000) << 2) | (ra << 11) | ((ma & 0x03FF) << 1));
    }
}
