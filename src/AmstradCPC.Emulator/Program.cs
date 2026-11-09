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
        string amsdosPath = FindRomPath("AMSDOS.ROM");

        machine.Memory.LoadLowerRom(BuiltInRoms.LoadRomOrFallback(osPath, "OS464.ROM"));
        machine.Memory.LoadUpperRom(0, BuiltInRoms.LoadRomOrFallback(basicPath, "BASIC1-0.ROM"));

        byte[]? amsdos = BuiltInRoms.GetRom("AMSDOS.ROM");
        if (File.Exists(amsdosPath)) amsdos = File.ReadAllBytes(amsdosPath);
        if (amsdos != null)
        {
            machine.Memory.LoadUpperRom(7, amsdos);
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
