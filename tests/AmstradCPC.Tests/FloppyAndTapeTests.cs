using System.IO;
using AmstradCPC.Core.Storage;
using Xunit;

namespace AmstradCPC.Tests;

public class FloppyAndTapeTests
{
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

    [Fact]
    public void BarbarianDsk_LoadsAndParsesTracksAndSectors()
    {
        string dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));
        Assert.True(File.Exists(dskPath), $"Barbarian DSK not found at {dskPath}");

        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);
        Assert.True(dsk.IsExtended);
        Assert.Equal(43, dsk.TrackCount);
        Assert.Equal(1, dsk.SideCount);

        // Track 0, Sector &C1 should be 512 bytes
        var sector = dsk.GetSector(0, 0, 0xC1);
        Assert.NotNull(sector);
        Assert.Equal(512, sector.Data.Length);
        Assert.Equal(2, sector.N); // 512 bytes

        // Sector 0 on Barbarian contains the AMSDOS directory with filename BARB1A.BIN
        string dirText = System.Text.Encoding.ASCII.GetString(sector.Data);
        Assert.Contains("BARB1A", dirText);
    }

    [Fact]
    public void Upd765Fdc_ExecutesRecalibrateAndReadSector()
    {
        string dskPath = FindPath(Path.Combine("games", "Barbarian (1987)(Palace Software)(Disk 1 of 2).dsk"));
        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);

        var fdc = new Upd765Fdc();
        fdc.DriveA = dsk;
        fdc.MotorOn = true;

        // 1. Recalibrate (0x07, Drive 0)
        fdc.WriteData(0x07);
        fdc.WriteData(0x00);
        Assert.Equal(0, fdc.CurrentCylinderA);
        Assert.True(fdc.InterruptPending);

        // 2. Sense Interrupt Status (0x08)
        fdc.WriteData(0x08);
        byte st0 = fdc.ReadData();
        byte pcn = fdc.ReadData();
        Assert.Equal(0x20, st0 & 0xF8); // Seek complete
        Assert.Equal(0, pcn);

        // 3. Read Sector (0x46, Drive 0, C=0, H=0, R=0xC1, N=2, EOT=9, GPL=0x2A, DTL=0xFF)
        fdc.WriteData(0x46);
        fdc.WriteData(0x00); // Drive 0
        fdc.WriteData(0x00); // C = 0
        fdc.WriteData(0x00); // H = 0
        fdc.WriteData(0xC1); // R = 0xC1
        fdc.WriteData(0x02); // N = 2 (512 bytes)
        fdc.WriteData(0x09); // EOT = 9
        fdc.WriteData(0x2A); // GPL
        fdc.WriteData(0xFF); // DTL

        // MSR should indicate execution phase, DIO=1 (read), Non-DMA=1, RQM=1
        byte msr = fdc.ReadMainStatus();
        Assert.True((msr & 0x80) != 0); // RQM
        Assert.True((msr & 0x40) != 0); // DIO = read

        // Read 512 bytes
        byte[] readBytes = new byte[512];
        for (int i = 0; i < 512; i++)
        {
            readBytes[i] = fdc.ReadData();
        }

        string readText = System.Text.Encoding.ASCII.GetString(readBytes);
        Assert.Contains("BARB1A", readText);

        // Result phase: 7 status bytes
        byte resSt0 = fdc.ReadData();
        byte resSt1 = fdc.ReadData();
        byte resSt2 = fdc.ReadData();
        byte resC = fdc.ReadData();
        byte resH = fdc.ReadData();
        byte resR = fdc.ReadData();
        byte resN = fdc.ReadData();

        Assert.Equal(0x00, resSt0);
        Assert.Equal(0x00, resSt1);
        Assert.Equal(0x00, resSt2);
        Assert.Equal(0, resC);
        Assert.Equal(0, resH);
        Assert.Equal(0xC2, resR); // Advanced to next sector
        Assert.Equal(2, resN);
    }

    [Fact]
    public void SubsunkDsk_InspectDirectory()
    {
        string dskPath = FindPath(Path.Combine("games", "Subsunk (1987)(WiCaMa)(fr).dsk"));
        if (!File.Exists(dskPath)) return;
        var dsk = DskImage.Load(dskPath);
        Assert.NotNull(dsk);

        // Read Track 0 sectors
        var s1 = dsk.GetSector(0, 0, 0xC1);
        Assert.NotNull(s1);

        // Extract file from blocks
        // On CPC System/Data format: Block size = 1024 bytes (2 sectors of 512 bytes).
        // Block 2 = Track 1, Sectors C1 & C2
        // Let's read Track 1 Sector C1 to check AMSDOS header
        string b10Path = FindPath(Path.Combine("roms", "BASIC1-0.ROM"));
        if (File.Exists(b10Path))
        {
            byte[] b10 = File.ReadAllBytes(b10Path);
            // Search for "AFTER" in ROM
            byte[] afterBytes = System.Text.Encoding.ASCII.GetBytes("AFTER");
            int afterIdx = -1;
            for (int i = 0; i < b10.Length - 5; i++)
            {
                if (b10[i] == 'A' && b10[i+1] == 'F' && b10[i+2] == 'T' && b10[i+3] == 'E' && b10[i+4] == 'R')
                {
                    afterIdx = i;
                    break;
                }
            }
            if (afterIdx != -1)
            {
                System.Console.WriteLine($"Token table found at 0x{afterIdx:X4} in BASIC 1.0");
                // Print strings
                int p = afterIdx;
                int token = 0x80;
                while (token <= 0xB8 && p < b10.Length)
                {
                    var sb = new System.Text.StringBuilder();
                    while (p < b10.Length && (b10[p] & 0x80) == 0)
                    {
                        sb.Append((char)b10[p++]);
                    }
                    if (p < b10.Length) sb.Append((char)(b10[p++] & 0x7F)); // last char has bit 7 set
                    System.Console.WriteLine($"Token 0x{token:X2}: {sb}");
                    token++;
                }
            }
        }
        var fileBytes = new System.Collections.Generic.List<byte>();
        // Block 2: T0 SC5, SC6
        // Block 3: T0 SC7, SC8
        // Block 4: T0 SC9, T1 SC1...
        // Let's iterate all sectors on disk in order:
        for (int t = 0; t < dsk.TrackCount; t++)
        {
            for (byte r = 0xC1; r <= 0xC9; r++)
            {
                if (t == 0 && r < 0xC5) continue; // skip directory
                var s = dsk.GetSector(t, 0, r);
                if (s != null) fileBytes.AddRange(s.Data);
            }
        }

        // Skip 128-byte AMSDOS header
        int ptr = 128;
        while (ptr + 4 < fileBytes.Count)
        {
            int lineLen = fileBytes[ptr] | (fileBytes[ptr + 1] << 8);
            if (lineLen == 0) break;
            int lineNum = fileBytes[ptr + 2] | (fileBytes[ptr + 3] << 8);

            if (lineNum >= 2240 && lineNum <= 2260)
            {
                byte[] lineData = fileBytes.Skip(ptr + 4).Take(lineLen - 4).ToArray();
                string hex = string.Join(" ", lineData.Select(b => $"{b:X2}"));
                string ascii = new string(lineData.Select(b => (b >= 32 && b < 127) ? (char)b : '.').ToArray());
                System.Console.WriteLine($"[LINE {lineNum}] {hex} | {ascii}");
            }

            ptr += lineLen;
        }
    }
}
