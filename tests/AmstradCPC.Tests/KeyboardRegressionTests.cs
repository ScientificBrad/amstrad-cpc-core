using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AmstradCPC.Core.Cpu;
using AmstradCPC.Core.Input;
using AmstradCPC.Core.Machine;
using AmstradCPC.Core.Memory;
using AmstradCPC.Core.Storage;
using AmstradCPC.Core.Video;
using AmstradCPC.Emulator;
using Xunit;
using Xunit.Abstractions;

namespace AmstradCPC.Tests;

public class KeyboardRegressionTests
{
    private readonly ITestOutputHelper _output;

    public KeyboardRegressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FindPath(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    [Fact]
    public void Enter_AssertsRow2Col2Only_NeverAssertsRow1Col2_AndReleasesCleanly()
    {
        var kb = new KeyboardMatrix();

        // Initially all rows must be 0xFF (active low, 1 = released)
        for (int r = 0; r < 10; r++)
        {
            Assert.Equal(0xFF, kb.ReadRow(r));
        }

        // Press Enter/Return
        AmstradWindow.MapKey(kb, Keys.Return, pressed: true);

        // Row 2, Col 2 must be pressed (bit 2 is 0)
        byte row2 = kb.ReadRow(2);
        Assert.Equal(0, row2 & (1 << 2));

        // CRITICAL CHECK: Row 1, Col 2 (F7 / keypad 7) must NOT be pressed!
        byte row1 = kb.ReadRow(1);
        Assert.NotEqual(0, row1 & (1 << 2)); // Bit 2 must remain 1 (unpressed)
        Assert.Equal(0xFF, row1);           // Entire Row 1 must remain 0xFF

        // Release Enter/Return
        AmstradWindow.MapKey(kb, Keys.Return, pressed: false);

        // All rows must be restored to 0xFF
        for (int r = 0; r < 10; r++)
        {
            Assert.Equal(0xFF, kb.ReadRow(r));
        }
    }

    [Fact]
    public void Space_AssertsRow5Col7_AndJoy0Fire_NeverAssertsRow1Col2_AndReleasesCleanly()
    {
        var kb = new KeyboardMatrix();

        // Press Space
        AmstradWindow.MapKey(kb, Keys.Space, pressed: true);

        // Row 5, Col 7 must be pressed (bit 7 is 0)
        byte row5 = kb.ReadRow(5);
        Assert.Equal(0, row5 & (1 << 7));

        // Row 9, Col 4 (Joy0 Fire 1) must be pressed
        byte row9 = kb.ReadRow(9);
        Assert.Equal(0, row9 & (1 << 4));

        // CRITICAL CHECK: Row 1, Col 2 (F7) must NOT be pressed!
        byte row1 = kb.ReadRow(1);
        Assert.Equal(0xFF, row1);

        // Row 2, Col 2 (Return) must NOT be pressed!
        byte row2 = kb.ReadRow(2);
        Assert.Equal(0xFF, row2);

        // Release Space
        AmstradWindow.MapKey(kb, Keys.Space, pressed: false);

        // All rows must be 0xFF
        for (int r = 0; r < 10; r++)
        {
            Assert.Equal(0xFF, kb.ReadRow(r));
        }
    }

    [Fact]
    public void F7_AssertsRow1Col2Only_AndReleasesCleanly()
    {
        var kb = new KeyboardMatrix();

        // Press F7
        AmstradWindow.MapKey(kb, Keys.F7, pressed: true);

        // Row 1, Col 2 must be pressed
        byte row1 = kb.ReadRow(1);
        Assert.Equal(0, row1 & (1 << 2));

        // Row 2, Col 2 (Return) must NOT be pressed
        byte row2 = kb.ReadRow(2);
        Assert.Equal(0xFF, row2);

        // Row 5, Col 7 (Space) must NOT be pressed
        byte row5 = kb.ReadRow(5);
        Assert.Equal(0xFF, row5);

        // Release F7
        AmstradWindow.MapKey(kb, Keys.F7, pressed: false);
        Assert.Equal(0xFF, kb.ReadRow(1));

        // Same for NumPad7
        AmstradWindow.MapKey(kb, Keys.NumPad7, pressed: true);
        Assert.Equal(0, kb.ReadRow(1) & (1 << 2));
        AmstradWindow.MapKey(kb, Keys.NumPad7, pressed: false);
        Assert.Equal(0xFF, kb.ReadRow(1));
    }

    private static void TypeKey(AmstradMachine machine, Keys key)
    {
        AmstradWindow.MapKey(machine.Keyboard, key, pressed: true);
        for (int i = 0; i < 4; i++) machine.StepFrame();
        AmstradWindow.MapKey(machine.Keyboard, key, pressed: false);
        for (int i = 0; i < 4; i++) machine.StepFrame();
    }

    [Fact]
    public void Basic_TypingCatAndEnter_ExecutesCommandWithoutSyntaxError()
    {
        string? osPath = FindPath(Path.Combine("roms", "OS6128.ROM"));
        string? basicPath = FindPath(Path.Combine("roms", "BASIC1-1.ROM"));
        string? amsdosPath = FindPath(Path.Combine("roms", "AMSDOS.ROM"));
        string? dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));
        if (osPath == null || basicPath == null || amsdosPath == null || dskPath == null) return;

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

        machine.Fdc.History.Clear();

        // Type 'C', 'A', 'T', Return via AmstradWindow.MapKey
        TypeKey(machine, Keys.C);
        TypeKey(machine, Keys.A);
        TypeKey(machine, Keys.T);
        TypeKey(machine, Keys.Return);

        // Step for 100 frames (~2s) for AMSDOS to read floppy directory
        for (int f = 0; f < 100; f++)
        {
            machine.StepFrame();
        }

        _output.WriteLine($"FDC history count after CAT: {machine.Fdc.History.Count}");
        foreach (var op in machine.Fdc.History)
        {
            _output.WriteLine($"  {op}");
        }

        // If 'Enter' had injected '7' (making it CAT7), AMSDOS would never execute CAT
        // and FDC would have 0 operations.
        // With authentic Return, AMSDOS executed Recalibrate, Seek, and Read Sectors &C1-&C4!
        Assert.True(machine.Fdc.History.Count >= 5, "CAT command must execute FDC sector reads, not fail with CAT7 Syntax error");
        Assert.True(machine.Fdc.MotorOn, "Floppy motor must be turned on by CAT");
    }

    [Fact]
    public void Barbarian_BootRunAndActualF7_StartsCombatFromDemo()
    {
        string? osPath = FindPath(Path.Combine("roms", "OS6128.ROM"));
        string? basicPath = FindPath(Path.Combine("roms", "BASIC1-1.ROM"));
        string? amsdosPath = FindPath(Path.Combine("roms", "AMSDOS.ROM"));
        string? dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));
        if (osPath == null || basicPath == null || amsdosPath == null || dskPath == null) return;

        var machine = new AmstradMachine(CpcModel.CPC6128);
        machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
        machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
        machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));

        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);
        machine.Fdc.DriveA = dsk;

        // Boot to BASIC
        for (int f = 0; f < 50; f++)
        {
            machine.StepFrame();
        }

        // Type RUN"BARB1A and Enter via AmstradWindow.MapKey
        TypeKey(machine, Keys.R);
        TypeKey(machine, Keys.U);
        TypeKey(machine, Keys.N);

        // Shift + 2 for quote
        AmstradWindow.MapKey(machine.Keyboard, Keys.ShiftKey, pressed: true);
        TypeKey(machine, Keys.D2);
        AmstradWindow.MapKey(machine.Keyboard, Keys.ShiftKey, pressed: false);
        for (int i = 0; i < 4; i++) machine.StepFrame();

        TypeKey(machine, Keys.B);
        TypeKey(machine, Keys.A);
        TypeKey(machine, Keys.R);
        TypeKey(machine, Keys.B);
        TypeKey(machine, Keys.D1);
        TypeKey(machine, Keys.A);
        TypeKey(machine, Keys.Return);

        // Step 450 frames for full disk load to DEMO screen
        for (int f = 0; f < 450; f++)
        {
            machine.StepFrame();
        }

        // Render DEMO screen
        machine.Renderer.RenderFullFrame();
        uint[] demoFrame = (uint[])machine.Renderer.Framebuffer.Clone();

        // Test pressing Enter: does it cycle game options?
        _output.WriteLine("Pressing Return on DEMO screen...");
        AmstradWindow.MapKey(machine.Keyboard, Keys.Return, pressed: true);
        for (int i = 0; i < 4; i++) machine.StepFrame();
        AmstradWindow.MapKey(machine.Keyboard, Keys.Return, pressed: false);
        for (int i = 0; i < 10; i++) machine.StepFrame();

        machine.Renderer.RenderFullFrame();
        int pixelsAfterEnter = 0;
        for (int i = 0; i < demoFrame.Length; i++)
        {
            if (demoFrame[i] != machine.Renderer.Framebuffer[i]) pixelsAfterEnter++;
        }
        _output.WriteLine($"Pixels changed after Return: {pixelsAfterEnter:N0}");

        // Now test pressing Space: does it start game?
        _output.WriteLine("Pressing Space...");
        AmstradWindow.MapKey(machine.Keyboard, Keys.Space, pressed: true);
        for (int i = 0; i < 4; i++) machine.StepFrame();
        AmstradWindow.MapKey(machine.Keyboard, Keys.Space, pressed: false);
        for (int i = 0; i < 50; i++) machine.StepFrame();

        machine.Renderer.RenderFullFrame();
        int pixelsAfterSpace = 0;
        for (int i = 0; i < demoFrame.Length; i++)
        {
            if (demoFrame[i] != machine.Renderer.Framebuffer[i]) pixelsAfterSpace++;
        }
        _output.WriteLine($"Pixels changed after Space: {pixelsAfterSpace:N0}");

        // Press actual F7 key (via AmstradWindow.MapKey) to start match
        _output.WriteLine("Pressing F7 to start match...");
        AmstradWindow.MapKey(machine.Keyboard, Keys.F7, pressed: true);
        for (int i = 0; i < 4; i++) machine.StepFrame();
        AmstradWindow.MapKey(machine.Keyboard, Keys.F7, pressed: false);
        for (int i = 0; i < 4; i++) machine.StepFrame();

        // Step 150 frames in-game
        for (int f = 0; f < 150; f++)
        {
            machine.StepFrame();
        }

        machine.Renderer.RenderFullFrame();
        int pixelsChanged = 0;
        for (int i = 0; i < demoFrame.Length; i++)
        {
            if (demoFrame[i] != machine.Renderer.Framebuffer[i]) pixelsChanged++;
        }

        _output.WriteLine($"Pixels changed after actual F7: {pixelsChanged:N0}");
        Assert.True(pixelsChanged > 3000, "Actual F7 must start the match from DEMO screen");
    }
}
