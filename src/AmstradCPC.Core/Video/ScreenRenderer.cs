using AmstradCPC.Core.Memory;

namespace AmstradCPC.Core.Video;

/// <summary>
/// Framebuffer renderer for Amstrad CPC modes 0, 1, 2, and border generation.
/// Maps hardware pens to 32-bit ARGB pixels on a canonical display buffer.
/// </summary>
public sealed class ScreenRenderer
{
    public const int ScreenWidth = 768;   // 640 active + border margins
    public const int ScreenHeight = 272;  // 200 active + border margins

    public uint[] Framebuffer { get; } = new uint[ScreenWidth * ScreenHeight];

    private readonly MemorySubsystem _memory;
    private readonly GateArray _gateArray;
    private readonly Crtc6845 _crtc;

    public ScreenRenderer(MemorySubsystem memory, GateArray gateArray, Crtc6845 crtc)
    {
        _memory = memory;
        _gateArray = gateArray;
        _crtc = crtc;
    }

    /// <summary>
    /// Renders a complete frame by reading video RAM and Gate Array palette.
    /// Can also be driven scanline-by-scanline in real-time.
    /// </summary>
    public void RenderFullFrame()
    {
        uint borderColor = _gateArray.PenRgbColors[16];
        Array.Fill(Framebuffer, borderColor);

        // Standard active display window: 40 character columns (80 bytes), 25 rows (200 scanlines)
        int startX = (ScreenWidth - 640) / 2;
        int startY = (ScreenHeight - 200) / 2;

        ushort baseMa = (ushort)((( _crtc.Registers[12] & 0x3F) << 8) | _crtc.Registers[13]);
        int r1 = _crtc.Registers[1] != 0 ? _crtc.Registers[1] : 40;
        int r6 = _crtc.Registers[6] != 0 ? _crtc.Registers[6] : 25;
        int r9 = (_crtc.Registers[9] & 0x1F) != 0 ? (_crtc.Registers[9] & 0x1F) : 7;
        byte mode = _gateArray.VideoMode;

        int currentY = 0;
        for (int charRow = 0; charRow < r6 && currentY < 200; charRow++)
        {
            ushort rowStartMa = (ushort)(baseMa + (charRow * r1));

            for (int rasterLine = 0; rasterLine <= r9 && currentY < 200; rasterLine++)
            {
                int screenY = startY + currentY;
                int fbRowOffset = screenY * ScreenWidth + startX;

                for (int col = 0; col < r1; col++)
                {
                    ushort ma = (ushort)(rowStartMa + col);
                    // Amstrad CPC Video Address interleaving:
                    // ((MA & 0x3000) << 2) | ((RA & 7) << 11) | ((MA & 0x03FF) << 1)
                    ushort vAddr1 = (ushort)(((ma & 0x3000) << 2) | ((rasterLine & 7) << 11) | ((ma & 0x03FF) << 1));
                    ushort vAddr2 = (ushort)(vAddr1 + 1);

                    byte b1 = _memory.ReadVideoRam(vAddr1);
                    byte b2 = _memory.ReadVideoRam(vAddr2);

                    int fbColOffset = fbRowOffset + (col * 16);

                    RenderByteToPixels(b1, mode, fbColOffset);
                    RenderByteToPixels(b2, mode, fbColOffset + 8);
                }

                currentY++;
            }
        }
    }

    private void RenderByteToPixels(byte b, byte mode, int destIndex)
    {
        switch (mode)
        {
            case 0: // Mode 0: 160x200, 16 colours. 2 pixels per byte, each pixel spans 4 canonical dots.
                {
                    int p0 = ((b & 0x02) << 2) | ((b & 0x08) >> 1) | ((b & 0x20) >> 4) | ((b & 0x80) >> 7);
                    int p1 = ((b & 0x01) << 3) | ((b & 0x04) >> 0) | ((b & 0x10) >> 3) | ((b & 0x40) >> 6);

                    uint c0 = _gateArray.PenRgbColors[p0 & 0x0F];
                    uint c1 = _gateArray.PenRgbColors[p1 & 0x0F];

                    for (int i = 0; i < 4; i++) Framebuffer[destIndex + i] = c0;
                    for (int i = 4; i < 8; i++) Framebuffer[destIndex + i] = c1;
                    break;
                }

            case 1: // Mode 1: 320x200, 4 colours. 4 pixels per byte, each pixel spans 2 canonical dots.
                {
                    int p0 = ((b & 0x80) >> 6) | ((b & 0x08) >> 3);
                    int p1 = ((b & 0x40) >> 5) | ((b & 0x04) >> 2);
                    int p2 = ((b & 0x20) >> 4) | ((b & 0x02) >> 1);
                    int p3 = ((b & 0x10) >> 3) | ((b & 0x01) >> 0);

                    uint c0 = _gateArray.PenRgbColors[p0 & 0x03];
                    uint c1 = _gateArray.PenRgbColors[p1 & 0x03];
                    uint c2 = _gateArray.PenRgbColors[p2 & 0x03];
                    uint c3 = _gateArray.PenRgbColors[p3 & 0x03];

                    Framebuffer[destIndex + 0] = c0;
                    Framebuffer[destIndex + 1] = c0;
                    Framebuffer[destIndex + 2] = c1;
                    Framebuffer[destIndex + 3] = c1;
                    Framebuffer[destIndex + 4] = c2;
                    Framebuffer[destIndex + 5] = c2;
                    Framebuffer[destIndex + 6] = c3;
                    Framebuffer[destIndex + 7] = c3;
                    break;
                }

            case 2: // Mode 2: 640x200, 2 colours. 8 pixels per byte, 1 canonical dot per pixel.
            default:
                {
                    uint c0 = _gateArray.PenRgbColors[0];
                    uint c1 = _gateArray.PenRgbColors[1];

                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool set = ((b >> (7 - bit)) & 1) != 0;
                        Framebuffer[destIndex + bit] = set ? c1 : c0;
                    }
                    break;
                }
        }
    }
}
