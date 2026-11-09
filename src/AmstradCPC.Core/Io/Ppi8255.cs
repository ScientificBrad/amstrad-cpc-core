using AmstradCPC.Core.Audio;
using AmstradCPC.Core.Video;

namespace AmstradCPC.Core.Io;

/// <summary>
/// Intel 8255 Programmable Peripheral Interface (PPI).
/// Coordinates keyboard row selection, AY-3-8912 PSG bus protocol,
/// cassette tape deck signals, and CRTC VSYNC input.
/// </summary>
public sealed class Ppi8255
{
    private readonly Ay38912 _ay;
    private readonly Crtc6845 _crtc;

    public byte PortA { get; set; } = 0xFF;
    public byte PortB { get; set; } = 0xFF;
    public byte PortC { get; set; } = 0x00;
    public byte Control { get; set; } = 0x92; // Mode 0: Port A In/Out, Port B In, Port C Out

    // Cassette state
    public bool CassetteDataIn { get; set; } = false;
    public bool CassetteDataOut { get; private set; } = false;
    public bool CassetteMotor { get; private set; } = false;

    public Ppi8255(Ay38912 ay, Crtc6845 crtc)
    {
        _ay = ay;
        _crtc = crtc;
        Reset();
    }

    public void Reset()
    {
        PortA = 0xFF;
        PortB = 0xFF;
        PortC = 0x00;
        Control = 0x92;
        CassetteDataIn = false;
        CassetteDataOut = false;
        CassetteMotor = false;
    }

    public byte ReadPortA()
    {
        // If AY bus control is in 'Read' mode (BC1=1, BDIR=0, bit 6 of Port C)
        if ((PortC & 0xC0) == 0x40)
        {
            PortA = _ay.ReadData();
        }
        return PortA;
    }

    public void WritePortA(byte value)
    {
        PortA = value;
    }

    public byte ReadPortB()
    {
        // Port B Inputs:
        // Bit 0: CRTC VSYNC signal (1 = VSYNC active, 0 = inactive)
        // Bit 1: Centronics BUSY (0 = ready)
        // Bit 2: LK1 distributor link (1 = Amstrad)
        // Bit 3: LK4 50Hz/60Hz (1 = 50 Hz PAL)
        // Bit 4: LK2 (1)
        // Bit 5: LK3 (1)
        // Bit 6: LK1 (1)
        // Bit 7: Cassette data in
        byte b = 0x7E; // default links (Amstrad, 50 Hz)
        if (_crtc.Vsync) b |= 0x01;
        if (CassetteDataIn) b |= 0x80;
        return b;
    }

    public byte ReadPortC()
    {
        return PortC;
    }

    public void WritePortC(byte value)
    {
        PortC = value;

        // Lower 4 bits select keyboard row (0 to 9)
        int row = value & 0x0F;
        _ay.SelectedKeyboardRow = row;

        // Cassette signals
        CassetteDataOut = (value & 0x10) != 0;
        CassetteMotor = (value & 0x20) != 0;

        // Top 2 bits control AY-3-8912 PSG
        byte ayControl = (byte)(value & 0xC0);
        switch (ayControl)
        {
            case 0xC0: // BC1=1, BDIR=1: Select Register
                _ay.SelectRegister(PortA);
                break;
            case 0x80: // BC1=0, BDIR=1: Write Data
                _ay.WriteData(PortA);
                break;
            case 0x40: // BC1=1, BDIR=0: Read Data
                PortA = _ay.ReadData();
                break;
            case 0x00: // Inactive
                break;
        }
    }

    public void WriteControl(byte value)
    {
        if ((value & 0x80) != 0)
        {
            Control = value;
        }
        else
        {
            // Bit set/reset format
            int bit = (value >> 1) & 0x07;
            bool set = (value & 0x01) != 0;
            byte mask = (byte)(1 << bit);
            if (set) PortC |= mask;
            else PortC &= (byte)~mask;
            WritePortC(PortC);
        }
    }
}
