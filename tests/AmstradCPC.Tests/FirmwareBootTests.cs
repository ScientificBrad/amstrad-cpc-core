using AmstradCPC.Core.Cpu;
using AmstradCPC.Core.Machine;
using AmstradCPC.Core.Video;
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

    [Fact]
    public void PpiPortB_VsyncBitMapping_And_FirmwareFlybackWait_Succeeds()
    {
        string osPath = FindRomPath("OS6128.ROM");
        string basicPath = FindRomPath("BASIC1-1.ROM");

        var machine = new AmstradMachine(AmstradCPC.Core.Memory.CpcModel.CPC6128);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));

        // 1. Direct hardware check: PPI Port B bit 0 is CRTC VSYNC
        Assert.False((machine.Ppi.ReadPortB() & 0x01) != 0, "VSYNC should not be active initially");
        // Step character clocks until VSYNC occurs
        bool vsyncSeen = false;
        for (int i = 0; i < 20000; i++)
        {
            machine.Crtc.StepCharacterClock();
            if ((machine.Ppi.ReadPortB() & 0x01) != 0)
            {
                vsyncSeen = true;
                break;
            }
        }
        Assert.True(vsyncSeen, "PPI Port B bit 0 must assert high during CRTC VSYNC");

        // 2. Firmware routine 0x07B4: MC WAIT FLYBACK
        // Setup CPU to call 0x07B4 and verify it returns promptly when CRTC generates VSYNC
        machine.Cpu.Reg.PC = 0x07B4;
        machine.Memory.Write(0xC000, 0x00); // NOP for return address
        machine.Cpu.Reg.SP = 0xBFFE;
        machine.Memory.Write(0xBFFE, 0x00); // Return address low
        machine.Memory.Write(0xBFFF, 0xC0); // Return address high

        int maxInstructions = 100000;
        int instructionsRun = 0;
        int crtcAccum = 0;
        while (machine.Cpu.Reg.PC != 0xC000 && instructionsRun < maxInstructions)
        {
            int cycles = machine.Cpu.Step();
            crtcAccum += cycles;
            while (crtcAccum >= 4)
            {
                crtcAccum -= 4;
                machine.Crtc.StepCharacterClock();
            }
            instructionsRun++;
        }

        Assert.Equal(0xC000, machine.Cpu.Reg.PC);
        _output.WriteLine($"MC WAIT FLYBACK (0x07B4) successfully completed in {instructionsRun} instructions without freezing.");
    }

    [Fact]
    public void AuthenticFirmware_BootToPrompt_SavesScreenshots()
    {
        string osPath = FindRomPath("OS464.ROM");
        string basicPath = FindRomPath("BASIC1-0.ROM");

        var machine = new AmstradMachine(AmstradCPC.Core.Memory.CpcModel.CPC464);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));

        // Step 40 frames to reach BASIC prompt
        for (int f = 0; f < 40; f++) machine.StepFrame();
        machine.Renderer.RenderFullFrame();

        string imgDir = Path.Combine(@"D:\Emulation\Amstrad", "docs", "images");
        Directory.CreateDirectory(imgDir);
        SaveFramebufferAsPng(Path.Combine(imgDir, "01_cpc464_boot.png"), machine.Renderer.Framebuffer, ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight);

        // CPC 6128
        string os6128Path = FindRomPath("OS6128.ROM");
        string basic6128Path = FindRomPath("BASIC1-1.ROM");
        string amsdosPath = FindRomPath("AMSDOS.ROM");

        var m6128 = new AmstradMachine(AmstradCPC.Core.Memory.CpcModel.CPC6128);
        m6128.Memory.LoadLowerRom(File.ReadAllBytes(os6128Path));
        m6128.Memory.LoadUpperRom(0, File.ReadAllBytes(basic6128Path));
        m6128.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));

        for (int f = 0; f < 50; f++) m6128.StepFrame();
        m6128.Renderer.RenderFullFrame();
        SaveFramebufferAsPng(Path.Combine(imgDir, "05_cpc6128_boot.png"), m6128.Renderer.Framebuffer, ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight);
    }

    private static void SaveFramebufferAsPng(string path, uint[] fb, int width, int height)
    {
        using var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        int[] intData = new int[fb.Length];
        for (int i = 0; i < fb.Length; i++) intData[i] = unchecked((int)fb[i]);
        System.Runtime.InteropServices.Marshal.Copy(intData, 0, data.Scan0, intData.Length);
        bmp.UnlockBits(data);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
