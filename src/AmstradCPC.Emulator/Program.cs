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

            string amsdosPath = FindRomPath("AMSDOS.ROM");
            if (File.Exists(amsdosPath))
            {
                machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));
            }
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
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, "roms", filename);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine("roms", filename);
    }
}
