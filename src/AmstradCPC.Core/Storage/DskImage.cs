using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AmstradCPC.Core.Storage;

public sealed class DskSector
{
    public byte C { get; set; } // Cylinder
    public byte H { get; set; } // Head
    public byte R { get; set; } // Sector ID
    public byte N { get; set; } // Size code (2 = 512 bytes)
    public byte ST1 { get; set; }
    public byte ST2 { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public sealed class DskTrack
{
    public byte TrackNumber { get; set; }
    public byte SideNumber { get; set; }
    public byte SectorSizeCode { get; set; }
    public List<DskSector> Sectors { get; } = new();

    public DskSector? FindSector(byte sectorId)
    {
        for (int i = 0; i < Sectors.Count; i++)
        {
            if (Sectors[i].R == sectorId) return Sectors[i];
        }
        return null;
    }
}

/// <summary>
/// Parser and manager for standard and extended Amstrad CPC .DSK floppy disk images.
/// </summary>
public sealed class DskImage
{
    public bool IsExtended { get; private set; }
    public int TrackCount { get; private set; }
    public int SideCount { get; private set; }
    public string Creator { get; private set; } = "";

    // Keyed by (Cylinder, Head)
    private readonly Dictionary<(int, int), DskTrack> _tracks = new();

    public static DskImage? Load(string path)
    {
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        return Load(bytes);
    }

    public static DskImage? Load(byte[] data)
    {
        if (data.Length < 256) return null;

        string sig = Encoding.ASCII.GetString(data, 0, Math.Min(32, data.Length));
        bool isStandard = sig.StartsWith("MV - CPCEMU Disk-File");
        bool isExtended = sig.StartsWith("EXTENDED CPC DSK File");

        if (!isStandard && !isExtended) return null;

        var dsk = new DskImage
        {
            IsExtended = isExtended,
            Creator = Encoding.ASCII.GetString(data, 34, 14).TrimEnd('\0', ' '),
            TrackCount = data[0x30],
            SideCount = data[0x31] != 0 ? data[0x31] : 1
        };

        int offset = 256;

        if (isStandard)
        {
            int trackSize = data[0x32] | (data[0x33] << 8);
            if (trackSize == 0) trackSize = 4864; // Default standard track size

            for (int t = 0; t < dsk.TrackCount; t++)
            {
                for (int s = 0; s < dsk.SideCount; s++)
                {
                    if (offset + 256 > data.Length) break;
                    var track = ParseTrack(data, offset, isExtended: false);
                    if (track != null)
                    {
                        dsk._tracks[(track.TrackNumber, track.SideNumber)] = track;
                    }
                    offset += trackSize;
                }
            }
        }
        else // Extended DSK
        {
            int trackIndex = 0;
            for (int t = 0; t < dsk.TrackCount; t++)
            {
                for (int s = 0; s < dsk.SideCount; s++)
                {
                    int sizeByteOffset = 0x34 + trackIndex;
                    int trackLength = (sizeByteOffset < 256) ? data[sizeByteOffset] * 256 : 0;
                    trackIndex++;

                    if (trackLength > 0 && offset + 256 <= data.Length)
                    {
                        var track = ParseTrack(data, offset, isExtended: true);
                        if (track != null)
                        {
                            dsk._tracks[(track.TrackNumber, track.SideNumber)] = track;
                        }
                    }
                    offset += trackLength;
                }
            }
        }

        return dsk;
    }

    private static DskTrack? ParseTrack(byte[] data, int trackOffset, bool isExtended)
    {
        string sig = Encoding.ASCII.GetString(data, trackOffset, Math.Min(12, data.Length - trackOffset));
        if (!sig.StartsWith("Track-Info")) return null;

        var track = new DskTrack
        {
            TrackNumber = data[trackOffset + 16],
            SideNumber = data[trackOffset + 17],
            SectorSizeCode = data[trackOffset + 20]
        };

        int sectorCount = data[trackOffset + 21];
        int sectorDataOffset = trackOffset + 256;

        for (int i = 0; i < sectorCount; i++)
        {
            int infoOffset = trackOffset + 24 + (i * 8);
            if (infoOffset + 8 > data.Length) break;

            byte c = data[infoOffset + 0];
            byte h = data[infoOffset + 1];
            byte r = data[infoOffset + 2];
            byte n = data[infoOffset + 3];
            byte st1 = data[infoOffset + 4];
            byte st2 = data[infoOffset + 5];

            int sectorLen;
            if (isExtended)
            {
                sectorLen = data[infoOffset + 6] | (data[infoOffset + 7] << 8);
                if (sectorLen == 0) sectorLen = 128 << n;
            }
            else
            {
                sectorLen = 128 << n;
            }

            var sector = new DskSector
            {
                C = c,
                H = h,
                R = r,
                N = n,
                ST1 = st1,
                ST2 = st2,
                Data = new byte[sectorLen]
            };

            if (sectorDataOffset + sectorLen <= data.Length)
            {
                Array.Copy(data, sectorDataOffset, sector.Data, 0, sectorLen);
            }
            sectorDataOffset += sectorLen;

            track.Sectors.Add(sector);
        }

        return track;
    }

    public DskTrack? GetTrack(int cylinder, int head = 0)
    {
        _tracks.TryGetValue((cylinder, head), out var track);
        return track;
    }

    public DskSector? GetSector(int cylinder, int head, byte sectorId)
    {
        var track = GetTrack(cylinder, head);
        return track?.FindSector(sectorId);
    }
}
