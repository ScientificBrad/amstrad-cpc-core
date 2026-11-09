using System;
using System.IO;
using System.Windows.Forms;
using AmstradCPC.Core.Machine;
using AmstradCPC.Core.Memory;

namespace AmstradCPC.Emulator;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var machine = new AmstradMachine(CpcModel.CPC464);

        string osPath = FindRomPath("OS464.ROM");
        string basicPath = FindRomPath("BASIC1-0.ROM");

        if (File.Exists(osPath) && File.Exists(basicPath))
        {
            machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
            machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
        }
        else
        {
            MessageBox.Show($"Could not find ROMs in roms/ directory.\nPlease provide OS464.ROM and BASIC1-0.ROM.",
                "ROM Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Application.Run(new AmstradWindow(machine));
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
}
