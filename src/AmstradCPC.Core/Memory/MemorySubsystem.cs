namespace AmstradCPC.Core.Memory;

/// <summary>
/// CPC Machine Model designation.
/// </summary>
public enum CpcModel
{
    CPC464,
    CPC664,
    CPC6128
}

/// <summary>
/// Amstrad CPC Memory Subsystem.
/// Reconstructs 64 KB / 128 KB RAM, lower firmware ROM overlay, upper ROM banking,
/// write-through behavior to underlying RAM, and CPC 6128 RAM bank switching.
/// </summary>
public sealed class MemorySubsystem
{
    public CpcModel Model { get; }

    // Physical RAM: 64 KB (4 banks of 16 KB) for CPC 464/664; 128 KB (8 banks of 16 KB) for CPC 6128.
    public byte[] Ram { get; }

    // Lower ROM (Firmware): 16 KB at $0000-$3FFF
    public byte[]? LowerRom { get; set; }

    // Upper ROMs: up to 256 banks of 16 KB mapped to $C000-$FFFF (Bank 0 = BASIC, Bank 7 = AMSDOS)
    public byte[][] UpperRoms { get; } = new byte[256][];

    // ROM enables (controlled by Gate Array)
    public bool LowerRomEnabled { get; set; } = true;
    public bool UpperRomEnabled { get; set; } = true;

    // Active Upper ROM bank (controlled by I/O write to &DFxx)
    public byte SelectedUpperRomBank { get; set; } = 0;

    // CPC 6128 RAM configuration register (0-7, &C0-&C7)
    public byte RamBankConfig { get; set; } = 0;

    // 16KB bank mapping for the 4 16KB regions (0: 0000-3FFF, 1: 4000-7FFF, 2: 8000-BFFF, 3: C000-FFFF)
    private readonly int[] _mappedRamBanks = new int[4];

    public MemorySubsystem(CpcModel model = CpcModel.CPC464)
    {
        Model = model;
        int ramSize = (model == CpcModel.CPC6128) ? 128 * 1024 : 64 * 1024;
        Ram = new byte[ramSize];
        UpdateBankPaging();
    }

    public void LoadLowerRom(byte[] romData)
    {
        LowerRom = new byte[16384];
        Array.Copy(romData, LowerRom, Math.Min(romData.Length, 16384));
    }

    public void LoadUpperRom(int bank, byte[] romData)
    {
        UpperRoms[bank] = new byte[16384];
        Array.Copy(romData, UpperRoms[bank], Math.Min(romData.Length, 16384));
    }

    /// <summary>
    /// Updates the 16KB RAM mapping based on the CPC 6128 RAM configuration register.
    /// </summary>
    public void SetRamBankConfig(byte config)
    {
        if (Model != CpcModel.CPC6128) return;
        RamBankConfig = (byte)(config & 0x07);
        UpdateBankPaging();
    }

    private void UpdateBankPaging()
    {
        if (Model != CpcModel.CPC6128)
        {
            _mappedRamBanks[0] = 0;
            _mappedRamBanks[1] = 1;
            _mappedRamBanks[2] = 2;
            _mappedRamBanks[3] = 3;
            return;
        }

        switch (RamBankConfig)
        {
            case 0: // C0: Standard 64K
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 1; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 3;
                break;
            case 1: // C1: Bank 4 in 4000-7FFF
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 4; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 3;
                break;
            case 2: // C2: All 64K in expansion RAM (4, 5, 6, 7)
                _mappedRamBanks[0] = 4; _mappedRamBanks[1] = 5; _mappedRamBanks[2] = 6; _mappedRamBanks[3] = 7;
                break;
            case 3: // C3: Bank 3 in 4000-7FFF, Bank 7 in C000-FFFF
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 3; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 7;
                break;
            case 4: // C4: Bank 4 in 4000-7FFF
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 4; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 3;
                break;
            case 5: // C5: Bank 5 in 4000-7FFF
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 5; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 3;
                break;
            case 6: // C6: Bank 6 in 4000-7FFF
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 6; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 3;
                break;
            case 7: // C7: Bank 7 in 4000-7FFF
                _mappedRamBanks[0] = 0; _mappedRamBanks[1] = 7; _mappedRamBanks[2] = 2; _mappedRamBanks[3] = 3;
                break;
        }
    }

    /// <summary>
    /// CPU Memory Read with Lower/Upper ROM overlay.
    /// </summary>
    public byte Read(ushort address)
    {
        if (address < 0x4000)
        {
            if (LowerRomEnabled && LowerRom != null)
            {
                return LowerRom[address];
            }
            return Ram[(_mappedRamBanks[0] * 16384) + address];
        }

        if (address >= 0xC000)
        {
            if (UpperRomEnabled && UpperRoms[SelectedUpperRomBank] != null)
            {
                return UpperRoms[SelectedUpperRomBank][address - 0xC000];
            }
            return Ram[(_mappedRamBanks[3] * 16384) + (address - 0xC000)];
        }

        int slot = address >> 14; // slot 1 (0x4000-0x7FFF) or slot 2 (0x8000-0xBFFF)
        int offset = address & 0x3FFF;
        return Ram[(_mappedRamBanks[slot] * 16384) + offset];
    }

    /// <summary>
    /// CPU Memory Write.
    /// CRITICAL HARDWARE RULE: Writes ALWAYS write to the underlying RAM, even if ROM read is active!
    /// </summary>
    public void Write(ushort address, byte value)
    {
        int slot = address >> 14;
        int offset = address & 0x3FFF;
        Ram[(_mappedRamBanks[slot] * 16384) + offset] = value;
    }

    /// <summary>
    /// Direct Video RAM access for Gate Array / CRTC rendering.
    /// Reads from base 64KB RAM (Bank 0-3).
    /// </summary>
    public byte ReadVideoRam(ushort address)
    {
        return Ram[address & 0xFFFF];
    }
}
