using AmstradCPC.Core.Cpu;
using AmstradCPC.Core.Machine;
using Xunit;
using Xunit.Abstractions;

namespace AmstradCPC.Tests;

public class FirmwareBootTests
{
    private readonly ITestOutputHelper _output;

    public FirmwareBootTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindRomPath(string filename)
    {
        string[] candidates = new[]
        {
            Path.Combine("roms", filename),
            Path.Combine("..", "..", "..", "..", "..", "roms", filename),
            Path.Combine(AppContext.BaseDirectory, "roms", filename),
            Path.Combine(@"D:\Emulation\Amstrad\roms", filename)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return Path.Combine(@"D:\Emulation\Amstrad\roms", filename);
    }

    [Fact]
    public void AuthenticFirmware_BootsAndExecutesInstructions()
    {
        string osPath = FindRomPath("OS464.ROM");
        string basicPath = FindRomPath("BASIC1-0.ROM");

        Assert.True(File.Exists(osPath), $"OS464.ROM not found at {osPath}");
        Assert.True(File.Exists(basicPath), $"BASIC1-0.ROM not found at {basicPath}");

        byte[] osRom = File.ReadAllBytes(osPath);
        byte[] basicRom = File.ReadAllBytes(basicPath);

        var machine = new AmstradMachine(AmstradCPC.Core.Memory.CpcModel.CPC464);
        machine.Memory.LoadLowerRom(osRom);
        machine.Memory.LoadUpperRom(0, basicRom);

        // Verify entry point opcode at $0000: LD BC, $7F89 (0x01)
        Assert.Equal(0x01, machine.Memory.Read(0x0000));

        // Execute several instructions and verify execution progress
        int initialSteps = 50;
        for (int i = 0; i < initialSteps; i++)
        {
            ushort pc = machine.Cpu.Reg.PC;
            var (mnemonic, _) = Z80Disassembler.Disassemble(machine.Memory.Read, pc);
            _output.WriteLine($"[{i,3}] PC:{pc:X4} | {mnemonic,-20} | AF:{machine.Cpu.Reg.AF:X4} BC:{machine.Cpu.Reg.BC:X4} DE:{machine.Cpu.Reg.DE:X4} HL:{machine.Cpu.Reg.HL:X4} SP:{machine.Cpu.Reg.SP:X4}");
            machine.Cpu.Step();
        }

        // The CPU must have stepped past 0x0000 and accumulated cycles
        Assert.True(machine.Cpu.TotalCycles > 0);
        Assert.True(machine.Cpu.Reg.PC > 0x0000);

        // Run for 5 complete PAL video frames (~400,000 cycles)
        for (int f = 0; f < 5; f++)
        {
            machine.StepFrame();
        }

        Assert.Equal(5, machine.TotalFrames);
        Assert.True(machine.Cpu.TotalCycles >= 5 * AmstradMachine.CyclesPerFrame);
    }

    [Fact]
    public void AuthenticFirmware_InitializesGateArrayAndCrtc()
    {
        string osPath = FindRomPath("OS464.ROM");
        string basicPath = FindRomPath("BASIC1-0.ROM");

        byte[] osRom = File.ReadAllBytes(osPath);
        byte[] basicRom = File.ReadAllBytes(basicPath);

        var machine = new AmstradMachine(AmstradCPC.Core.Memory.CpcModel.CPC464);
        machine.Memory.LoadLowerRom(osRom);
        machine.Memory.LoadUpperRom(0, basicRom);

        // Run for 15 video frames (~1.2 million cycles) to allow full OS initialization
        for (int f = 0; f < 15; f++)
        {
            machine.StepFrame();
        }

        // Firmware 1.0 sets CRTC registers R0..R13 to setup the 40x25 character display
        _output.WriteLine($"CRTC R0 (Horiz Total): {machine.Crtc.Registers[0]}");
        _output.WriteLine($"CRTC R1 (Horiz Displayed): {machine.Crtc.Registers[1]}");
        _output.WriteLine($"CRTC R4 (Vert Total): {machine.Crtc.Registers[4]}");
        _output.WriteLine($"CRTC R6 (Vert Displayed): {machine.Crtc.Registers[6]}");
        _output.WriteLine($"CRTC R7 (VSYNC Pos): {machine.Crtc.Registers[7]}");
        _output.WriteLine($"CRTC R12 (Screen Base Hi): {machine.Crtc.Registers[12]:X2}");

        Assert.Equal(63, machine.Crtc.Registers[0]);
        Assert.Equal(40, machine.Crtc.Registers[1]);
        Assert.Equal(25, machine.Crtc.Registers[6]);
        Assert.Equal(0x30, machine.Crtc.Registers[12]); // $C000 >> 8 / 4 = 0x30
    }
}
