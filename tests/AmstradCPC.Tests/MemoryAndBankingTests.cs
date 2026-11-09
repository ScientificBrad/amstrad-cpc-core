using AmstradCPC.Core.Memory;
using Xunit;

namespace AmstradCPC.Tests;

public class MemoryAndBankingTests
{
    [Fact]
    public void LowerRomOverlay_ControlsReads_WhileWritesGoToRam()
    {
        var mem = new MemorySubsystem(CpcModel.CPC464);
        var romData = new byte[16384];
        romData[0x0100] = 0xAA;
        mem.LoadLowerRom(romData);

        // When lower ROM is enabled:
        mem.LowerRomEnabled = true;
        Assert.Equal(0xAA, mem.Read(0x0100));

        // CRITICAL HARDWARE RULE: Writes to $0000-$3FFF ALWAYS go to underlying RAM!
        mem.Write(0x0100, 0x55);
        // Reading while ROM enabled still reads ROM
        Assert.Equal(0xAA, mem.Read(0x0100));

        // When lower ROM is disabled:
        mem.LowerRomEnabled = false;
        // Reading now sees the underlying RAM value that was written!
        Assert.Equal(0x55, mem.Read(0x0100));
    }

    [Fact]
    public void UpperRomOverlay_SupportsMultipleBanks()
    {
        var mem = new MemorySubsystem(CpcModel.CPC464);

        var basicRom = new byte[16384];
        basicRom[0x0000] = 0xB0; // Bank 0: BASIC
        mem.LoadUpperRom(0, basicRom);

        var amsdosRom = new byte[16384];
        amsdosRom[0x0000] = 0xD7; // Bank 7: AMSDOS
        mem.LoadUpperRom(7, amsdosRom);

        mem.UpperRomEnabled = true;

        // Select Bank 0
        mem.SelectedUpperRomBank = 0;
        Assert.Equal(0xB0, mem.Read(0xC000));

        // Select Bank 7
        mem.SelectedUpperRomBank = 7;
        Assert.Equal(0xD7, mem.Read(0xC000));

        // Disable Upper ROM: reads RAM
        mem.UpperRomEnabled = false;
        mem.Write(0xC000, 0x42);
        Assert.Equal(0x42, mem.Read(0xC000));
    }

    [Fact]
    public void Cpc6128_RamBanking_Switches128KbRamBlocks()
    {
        var mem = new MemorySubsystem(CpcModel.CPC6128);

        // Fill all 8 16KB banks with unique marker bytes
        for (int bank = 0; bank < 8; bank++)
        {
            for (int i = 0; i < 16384; i++)
            {
                mem.Ram[(bank * 16384) + i] = (byte)(0x10 + bank);
            }
        }

        // Config C0: Normal 64K (Banks 0, 1, 2, 3)
        mem.SetRamBankConfig(0);
        Assert.Equal(0x10, mem.Ram[0 * 16384]);
        Assert.Equal(0x11, mem.Read(0x4000));
        Assert.Equal(0x12, mem.Read(0x8000));

        // Config C1: Bank 4 mapped to $4000-$7FFF
        mem.SetRamBankConfig(1);
        Assert.Equal(0x14, mem.Read(0x4000));

        // Config C2: All 64K in expansion RAM (Banks 4, 5, 6, 7)
        mem.SetRamBankConfig(2);
        Assert.Equal(0x14, mem.Read(0x0000));
        Assert.Equal(0x15, mem.Read(0x4000));
        Assert.Equal(0x16, mem.Read(0x8000));
    }
}
