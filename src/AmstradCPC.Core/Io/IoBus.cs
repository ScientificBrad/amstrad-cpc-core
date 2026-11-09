using AmstradCPC.Core.Memory;
using AmstradCPC.Core.Storage;
using AmstradCPC.Core.Video;

namespace AmstradCPC.Core.Io;

/// <summary>
/// Amstrad CPC 16-bit I/O Bus Controller.
/// Dispatches I/O read and write transactions using partial address line decoding.
/// </summary>
public sealed class IoBus
{
    private readonly GateArray _gateArray;
    private readonly Crtc6845 _crtc;
    private readonly MemorySubsystem _memory;
    private readonly Ppi8255 _ppi;
    private readonly Upd765Fdc _fdc;

    public IoBus(GateArray gateArray, Crtc6845 crtc, MemorySubsystem memory, Ppi8255 ppi, Upd765Fdc fdc)
    {
        _gateArray = gateArray;
        _crtc = crtc;
        _memory = memory;
        _ppi = ppi;
        _fdc = fdc;
    }

    public byte ReadPort(ushort port)
    {
        // PPI 8255: A11 = 0
        if ((port & 0x0800) == 0)
        {
            int ppiReg = (port >> 8) & 0x03;
            return ppiReg switch
            {
                0 => _ppi.ReadPortA(),
                1 => _ppi.ReadPortB(),
                2 => _ppi.ReadPortC(),
                _ => 0xFF
            };
        }

        // CRTC 6845: A14 = 0
        if ((port & 0x4000) == 0)
        {
            if ((port & 0x0100) == 0)
            {
                return _crtc.ReadStatus(); // &BExx
            }
            return _crtc.ReadData(); // &BFxx
        }

        // Floppy Disk Controller (µPD765): A10 = 0 and A7 = 0 (&FB7E / &FB7F)
        if ((port & 0x0400) == 0 && (port & 0x0080) == 0)
        {
            if ((port & 0x0001) == 0)
            {
                return _fdc.ReadMainStatus(); // &FB7E: Main Status Register
            }
            return _fdc.ReadData(); // &FB7F: Data FIFO
        }

        return 0xFF; // Floating bus
    }

    public void WritePort(ushort port, byte value)
    {
        // Gate Array: A15 = 0 and A14 = 1 (&7Fxx)
        if ((port & 0xC000) == 0x4000)
        {
            _gateArray.WriteRegister(value);
            if ((value & 0xC0) == 0xC0)
            {
                _memory.SetRamBankConfig(value);
            }
        }

        // CRTC 6845: A14 = 0
        if ((port & 0x4000) == 0)
        {
            if ((port & 0x0100) == 0)
            {
                _crtc.WriteIndex(value); // &BCxx
            }
            else
            {
                _crtc.WriteData(value);  // &BDxx
            }
        }

        // Upper ROM Bank Select: A15 = 1 and A13 = 0 (&DFxx)
        if ((port & 0x2000) == 0 && (port & 0x8000) != 0)
        {
            _memory.SelectedUpperRomBank = value;
        }

        // PPI 8255: A11 = 0 (&F4xx - &F7xx)
        if ((port & 0x0800) == 0)
        {
            int ppiReg = (port >> 8) & 0x03;
            switch (ppiReg)
            {
                case 0: _ppi.WritePortA(value); break;
                case 1: break; // Port B is read-only on CPC
                case 2: _ppi.WritePortC(value); break;
                case 3: _ppi.WriteControl(value); break;
            }
        }

        // Floppy Drive Motor (&FA7E) and FDC Data (&FB7F): A10 = 0 and A7 = 0
        if ((port & 0x0400) == 0 && (port & 0x0080) == 0)
        {
            if ((port & 0x0100) == 0 && (port & 0x0001) == 0)
            {
                _fdc.MotorOn = (value & 0x01) != 0; // &FA7E
            }
            else if ((port & 0x0001) != 0)
            {
                _fdc.WriteData(value); // &FB7F
            }
        }
    }
}
