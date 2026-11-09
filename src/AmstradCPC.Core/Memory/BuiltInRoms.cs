using System;
using System.IO;
using System.Reflection;

namespace AmstradCPC.Core.Memory;

/// <summary>
/// Provides built-in system firmware ROMs (OS464, BASIC1.0, OS6128, BASIC1.1, AMSDOS)
/// embedded directly into the core binary under Amstrad's official redistribution permission.
/// </summary>
public static class BuiltInRoms
{
    private static readonly Assembly CoreAssembly = typeof(BuiltInRoms).Assembly;

    public static byte[]? GetRom(string filename)
    {
        string nameOnly = Path.GetFileName(filename).ToUpperInvariant();

        foreach (string resName in CoreAssembly.GetManifestResourceNames())
        {
            if (resName.ToUpperInvariant().EndsWith(nameOnly))
            {
                using Stream? stream = CoreAssembly.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    byte[] data = new byte[stream.Length];
                    int totalRead = 0;
                    while (totalRead < data.Length)
                    {
                        int read = stream.Read(data, totalRead, data.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    return data;
                }
            }
        }

        return null;
    }

    public static byte[] LoadRomOrFallback(string? filePath, string defaultRomName)
    {
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch
            {
                // Fall back to embedded ROM
            }
        }

        byte[]? embedded = GetRom(defaultRomName);
        if (embedded != null)
        {
            return embedded;
        }

        throw new FileNotFoundException($"System firmware ROM '{defaultRomName}' could not be loaded from disk or embedded resources.");
    }
}
