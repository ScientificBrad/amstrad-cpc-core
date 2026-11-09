using AmstradCPC.Core.Memory;
using AmstradCPC.Core.Video;
using Xunit;

namespace AmstradCPC.Tests;

public class GateArrayAndVideoTests
{
    [Fact]
    public void GateArray_PenAndColorSelection_MapsTo27Colors()
    {
        var mem = new MemorySubsystem(CpcModel.CPC464);
        var ga = new GateArray(mem);

        // Select Pen 1: bits 7-6 = 00, data = 0x01
        ga.WriteRegister(0x01);
        Assert.Equal(1, ga.SelectedPen);

        // Select Colour for Pen 1: Bright Yellow (Hardware 10 / 0x0A): bits 7-6 = 01, data = 0x40 | 0x0A = 0x4A
        ga.WriteRegister(0x4A);
        Assert.Equal(0x0A, ga.PenHardwareColors[1]);
        Assert.Equal(0xFFFFFF00, ga.PenRgbColors[1]); // Bright Yellow ARGB

        // Select Border (Pen 16): bits 7-6 = 00, bit 4 = 1 -> data = 0x10
        ga.WriteRegister(0x10);
        Assert.Equal(16, ga.SelectedPen);

        // Set Border to Red (Hardware 28 / 0x1C): bits 7-6 = 01, data = 0x40 | 0x1C = 0x5C
        ga.WriteRegister(0x5C);
        Assert.Equal(0x1C, ga.PenHardwareColors[16]);
        Assert.Equal(0xFF800000, ga.PenRgbColors[16]); // Red ARGB
    }

    [Fact]
    public void GateArray_ModeAndRomSelect_UpdatesHardware()
    {
        var mem = new MemorySubsystem(CpcModel.CPC464);
        var ga = new GateArray(mem);

        // Write Mode 0 (bits 1-0 = 00), disable Lower ROM (bit 2 = 1), enable Upper ROM (bit 3 = 0):
        // data: bits 7-6 = 10 -> 0x80 | 0x04 = 0x84
        ga.WriteRegister(0x84);

        Assert.Equal(0, ga.VideoMode);
        Assert.False(mem.LowerRomEnabled);
        Assert.True(mem.UpperRomEnabled);
    }

    [Fact]
    public void GateArray_HsyncCounter_GeneratesInterruptAt52()
    {
        var mem = new MemorySubsystem(CpcModel.CPC464);
        var ga = new GateArray(mem);

        for (int i = 0; i < 51; i++)
        {
            ga.OnHsync();
            Assert.False(ga.InterruptRequested);
        }

        // 52nd Hsync triggers interrupt!
        ga.OnHsync();
        Assert.True(ga.InterruptRequested);
        Assert.Equal(0, ga.HsyncCounter);
    }
}
