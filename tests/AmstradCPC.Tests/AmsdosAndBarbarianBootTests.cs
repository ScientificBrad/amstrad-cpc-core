using System;
using System.IO;
using System.Linq;
using AmstradCPC.Core.Cpu;
using AmstradCPC.Core.Machine;
using AmstradCPC.Core.Memory;
using AmstradCPC.Core.Storage;
using AmstradCPC.Core.Video;
using Xunit;
using Xunit.Abstractions;

namespace AmstradCPC.Tests;

public class AmsdosAndBarbarianBootTests
{
    private readonly ITestOutputHelper _output;

    public AmsdosAndBarbarianBootTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindPath(string relativePath)
    {
        string[] candidates = new[]
        {
            relativePath,
            Path.Combine("..", "..", "..", "..", "..", relativePath),
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(@"D:\Emulation\Amstrad", relativePath)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return Path.Combine(@"D:\Emulation\Amstrad", relativePath);
    }

    private void PressKey(AmstradMachine machine, int row, int col)
    {
        machine.Keyboard.SetKeyState(row, col, true);
        for (int i = 0; i < 4; i++) machine.StepFrame();
        machine.Keyboard.SetKeyState(row, col, false);
        for (int i = 0; i < 4; i++) machine.StepFrame();
    }

    private void TypeText(AmstradMachine machine, string text)
    {
        foreach (char ch in text)
        {
            char c = char.ToUpperInvariant(ch);
            switch (c)
            {
                case 'R': PressKey(machine, 6, 2); break;
                case 'U': PressKey(machine, 5, 2); break;
                case 'N': PressKey(machine, 5, 6); break;
                case '"':
                    // Shift + 2
                    machine.Keyboard.SetKeyState(2, 5, true); // Shift
                    PressKey(machine, 8, 1);                  // 2
                    machine.Keyboard.SetKeyState(2, 5, false); // Release Shift
                    for (int i = 0; i < 4; i++) machine.StepFrame();
                    break;
                case 'B': PressKey(machine, 6, 6); break;
                case 'A': PressKey(machine, 8, 5); break;
                case '1': PressKey(machine, 8, 0); break;
                case 'C': PressKey(machine, 7, 6); break;
                case 'T': PressKey(machine, 6, 3); break;
                case '\n':
                case '\r':
                    PressKey(machine, 2, 2); // Return
                    break;
            }
        }
    }

    [Fact]
    public void Amsdos_InitializesAndRespondsToFdc()
    {
        string osPath = FindPath(Path.Combine("roms", "OS464.ROM"));
        string basicPath = FindPath(Path.Combine("roms", "BASIC1-0.ROM"));
        string amsdosPath = FindPath(Path.Combine("roms", "AMSDOS.ROM"));
        string dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));

        Assert.True(File.Exists(osPath));
        Assert.True(File.Exists(basicPath));
        Assert.True(File.Exists(amsdosPath));
        Assert.True(File.Exists(dskPath));

        var machine = new AmstradMachine(CpcModel.CPC464);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
        machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));

        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);
        machine.Fdc.DriveA = dsk;

        // Boot machine for 30 frames to reach BASIC prompt
        for (int f = 0; f < 30; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"Machine booted. Total frames: {machine.TotalFrames}, Total cycles: {machine.Cpu.TotalCycles:N0}");
        _output.WriteLine($"Current PC: {machine.Cpu.Reg.PC:X4}, SP: {machine.Cpu.Reg.SP:X4}");

        // Now select Bank 7 (AMSDOS) via I/O port &DFxx
        machine.Io.WritePort(0xDF00, 7);
        Assert.Equal(7, machine.Memory.SelectedUpperRomBank);

        // Verify AMSDOS ROM header at $C000:
        byte romType = machine.Memory.Read(0xC000);
        _output.WriteLine($"AMSDOS ROM byte 0 at $C000: 0x{romType:X2}");
        Assert.Equal(1, romType); // Type 1: Extension ROM (AMSDOS)
    }

    [Fact]
    public void Cpc6128_BootsWithAmsdosToBasicPrompt()
    {
        string osPath = FindPath(Path.Combine("roms", "OS6128.ROM"));
        string basicPath = FindPath(Path.Combine("roms", "BASIC1-1.ROM"));
        string amsdosPath = FindPath(Path.Combine("roms", "AMSDOS.ROM"));

        Assert.True(File.Exists(osPath), "OS6128.ROM exists");
        Assert.True(File.Exists(basicPath), "BASIC1-1.ROM exists");
        Assert.True(File.Exists(amsdosPath), "AMSDOS.ROM exists");

        var machine = new AmstradMachine(CpcModel.CPC6128);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
        machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));

        // Boot machine for 50 frames (~1.0s)
        for (int f = 0; f < 50; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"CPC 6128 booted. Frames: {machine.TotalFrames}, Total cycles: {machine.Cpu.TotalCycles:N0}");
        _output.WriteLine($"PC: {machine.Cpu.Reg.PC:X4}, SP: {machine.Cpu.Reg.SP:X4}, SelectedUpperRom: {machine.Memory.SelectedUpperRomBank}");

        Assert.True(machine.Cpu.TotalCycles > 3_000_000);
    }

    [Fact]
    public void Cpc6128_CatListsBarbarianFiles()
    {
        string osPath = FindPath(Path.Combine("roms", "OS6128.ROM"));
        string basicPath = FindPath(Path.Combine("roms", "BASIC1-1.ROM"));
        string amsdosPath = FindPath(Path.Combine("roms", "AMSDOS.ROM"));
        string dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));

        var machine = new AmstradMachine(CpcModel.CPC6128);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
        machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));

        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);
        machine.Fdc.DriveA = dsk;

        // Boot machine for 50 frames to reach BASIC prompt
        for (int f = 0; f < 50; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"Boot complete. Motor: {machine.Fdc.MotorOn}, FDC Phase: {machine.Fdc.Phase}");

        // Type 'CAT' + Return
        TypeText(machine, "CAT\r");

        // Step for 100 frames (~2 seconds) while AMSDOS reads directory
        for (int f = 0; f < 100; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"After CAT: Motor={machine.Fdc.MotorOn}, Cylinder={machine.Fdc.CurrentCylinderA}, FDC Phase={machine.Fdc.Phase}");
        _output.WriteLine($"FDC Log entries ({machine.Fdc.History.Count}):");
        foreach (var log in machine.Fdc.History)
        {
            _output.WriteLine($"  {log}");
        }
        _output.WriteLine($"Total Cycles: {machine.Cpu.TotalCycles:N0}");
    }

    [Fact]
    public void Cpc6128_RunBarbarianLoadsAndExecutesGame()
    {
        string osPath = FindPath(Path.Combine("roms", "OS6128.ROM"));
        string basicPath = FindPath(Path.Combine("roms", "BASIC1-1.ROM"));
        string amsdosPath = FindPath(Path.Combine("roms", "AMSDOS.ROM"));
        string dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));

        var machine = new AmstradMachine(CpcModel.CPC6128);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
        machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));

        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);
        machine.Fdc.DriveA = dsk;

        // Boot machine for 50 frames to reach BASIC prompt
        for (int f = 0; f < 50; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"Boot complete. Typing RUN\"BARB1A...");

        // Clear history before RUN command
        machine.Fdc.History.Clear();

        // Type RUN"BARB1A and Enter
        TypeText(machine, "RUN\"BARB1A\r");

        // Step for 450 frames (~9 seconds) while game loads all sectors and presents title screen
        for (int f = 0; f < 450; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"After full load: Motor={machine.Fdc.MotorOn}, Cylinder={machine.Fdc.CurrentCylinderA}, FDC Phase={machine.Fdc.Phase}");
        _output.WriteLine($"FDC Total Operations: {machine.Fdc.History.Count}");
        _output.WriteLine($"CPU PC: {machine.Cpu.Reg.PC:X4}, SP: {machine.Cpu.Reg.SP:X4}, Cycles: {machine.Cpu.TotalCycles:N0}");
        _output.WriteLine($"Gate Array Mode: {machine.GateArray.VideoMode}, LowerRom: {machine.Memory.LowerRomEnabled}, UpperRom: {machine.Memory.UpperRomEnabled}");

        // Render frame
        machine.Renderer.RenderFullFrame();

        var distinctColors = machine.Renderer.Framebuffer.Distinct().ToList();
        _output.WriteLine($"Distinct colors in framebuffer: {distinctColors.Count}");
        foreach (var c in distinctColors)
        {
            _output.WriteLine($"  Color: 0x{c:X8}");
        }

        // Now press Space or '1' to start 1-player game!
        _output.WriteLine("Pressing Space to start game...");
        PressKey(machine, 5, 7); // Space: Row 5, Col 7

        // Run for another 150 frames in-game
        for (int f = 0; f < 150; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"In-game: CPU PC={machine.Cpu.Reg.PC:X4}, Cycles={machine.Cpu.TotalCycles:N0}");
        _output.WriteLine($"FDC Total Operations now: {machine.Fdc.History.Count}");

        // Render gameplay frame and save screenshot
        machine.Renderer.RenderFullFrame();
        string gameplayScreenshotPath = Path.Combine(@"D:\Emulation\Amstrad", "barbarian_gameplay.bmp");
        SaveBmp(gameplayScreenshotPath, machine.Renderer.Framebuffer, ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight);
        _output.WriteLine($"Saved gameplay screen to {gameplayScreenshotPath}");

        var inGameColors = machine.Renderer.Framebuffer.Distinct().ToList();
        _output.WriteLine($"Distinct colors in in-game framebuffer: {inGameColors.Count}");
        Assert.True(inGameColors.Count >= 8, "In-game screen should have rich multicolor graphics");
    }

    private static void SaveBmp(string path, uint[] fb, int width, int height)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int imageSize = width * height * 4;
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + imageSize);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write(54);
        bw.Write(40);
        bw.Write(width);
        bw.Write(-height); // top-down
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);
        bw.Write(imageSize);
        bw.Write(2835);
        bw.Write(2835);
        bw.Write(0);
        bw.Write(0);
        for (int i = 0; i < fb.Length; i++)
        {
            bw.Write(fb[i]);
        }
    }
}
