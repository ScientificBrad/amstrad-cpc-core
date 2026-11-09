using System;
using AmstradCPC.Core.Memory;

namespace AmstradCPC.Core.Video;

/// <summary>
/// Framebuffer renderer for Amstrad CPC modes 0, 1, 2, and border generation.
/// Maps hardware pens to 32-bit ARGB pixels on a canonical display buffer.
/// Precomputes pixel decoding tables for zero-allocation, high-performance 50 FPS rendering.
/// </summary>
public sealed class ScreenRenderer
{
    public const int ScreenWidth = 768;   // Accommodates standard 640 active + full overscan & borders
    public const int ScreenHeight = 272;  // Accommodates standard 200 active + full overscan & borders

    public uint[] Framebuffer { get; } = new uint[ScreenWidth * ScreenHeight];

    private readonly MemorySubsystem _memory;
    private readonly GateArray _gateArray;
    private readonly Crtc6845 _crtc;

    // Precomputed decoding tables: byte -> pen indices
    private static readonly byte[][] Mode0Pens = new byte[256][];
    private static readonly byte[][] Mode1Pens = new byte[256][];
    private static readonly byte[][] Mode2Pens = new byte[256][];

    static ScreenRenderer()
    {
        for (int b = 0; b < 256; b++)
        {
            // Mode 0: 2 pixels per byte (16 colours, 4 bpp)
            int p0 = ((b & 0x02) << 2) | ((b & 0x08) >> 1) | ((b & 0x20) >> 4) | ((b & 0x80) >> 7);
            int p1 = ((b & 0x01) << 3) | ((b & 0x04) >> 0) | ((b & 0x10) >> 3) | ((b & 0x40) >> 6);
            Mode0Pens[b] = new byte[] { (byte)(p0 & 0x0F), (byte)(p1 & 0x0F) };

            // Mode 1: 4 pixels per byte (4 colours, 2 bpp)
            int m1p0 = ((b & 0x80) >> 6) | ((b & 0x08) >> 3);
            int m1p1 = ((b & 0x40) >> 5) | ((b & 0x04) >> 2);
            int m1p2 = ((b & 0x20) >> 4) | ((b & 0x02) >> 1);
            int m1p3 = ((b & 0x10) >> 3) | ((b & 0x01) >> 0);
            Mode1Pens[b] = new byte[] { (byte)(m1p0 & 0x03), (byte)(m1p1 & 0x03), (byte)(m1p2 & 0x03), (byte)(m1p3 & 0x03) };

            // Mode 2: 8 pixels per byte (2 colours, 1 bpp)
            Mode2Pens[b] = new byte[8];
            for (int bit = 0; bit < 8; bit++)
            {
                Mode2Pens[b][bit] = (byte)((b >> (7 - bit)) & 1);
            }
        }
    }

    public ScreenRenderer(MemorySubsystem memory, GateArray gateArray, Crtc6845 crtc)
    {
        _memory = memory;
        _gateArray = gateArray;
        _crtc = crtc;
    }

    /// <summary>
    /// Renders a complete frame by reading video RAM and Gate Array palette.
    /// Handles standard resolution and overscan without clipping.
    /// </summary>
    public void RenderFullFrame()
    {
        uint borderColor = _gateArray.PenRgbColors[16];
        Array.Fill(Framebuffer, borderColor);

        int r1 = _crtc.Registers[1] != 0 ? _crtc.Registers[1] : 40;
        int r6 = _crtc.Registers[6] != 0 ? _crtc.Registers[6] : 25;
        int r9 = (_crtc.Registers[9] & 0x1F) != 0 ? (_crtc.Registers[9] & 0x1F) : 7;
        byte mode = _gateArray.VideoMode;

        int activeWidth = r1 * 16;
        int activeHeight = r6 * (r9 + 1);

        // Center active display area within the 768x272 buffer
        int startX = Math.Max(0, (ScreenWidth - activeWidth) / 2);
        int startY = Math.Max(0, (ScreenHeight - activeHeight) / 2);

        ushort baseMa = (ushort)((( _crtc.Registers[12] & 0x3F) << 8) | _crtc.Registers[13]);
        uint[] palette = _gateArray.PenRgbColors;

        int currentY = 0;
        for (int charRow = 0; charRow < r6; charRow++)
        {
            ushort rowStartMa = (ushort)(baseMa + (charRow * r1));

            for (int rasterLine = 0; rasterLine <= r9; rasterLine++)
            {
                int screenY = startY + currentY;
                if (screenY >= ScreenHeight) break;

                int fbRowOffset = screenY * ScreenWidth + startX;

                for (int col = 0; col < r1; col++)
                {
                    int fbColOffset = fbRowOffset + (col * 16);
                    if (fbColOffset + 16 > (screenY + 1) * ScreenWidth) break;

                    ushort ma = (ushort)(rowStartMa + col);
                    // Amstrad CPC Video Address interleaving:
                    // ((MA & 0x3000) << 2) | ((RA & 7) << 11) | ((MA & 0x03FF) << 1)
                    ushort vAddr1 = (ushort)(((ma & 0x3000) << 2) | ((rasterLine & 7) << 11) | ((ma & 0x03FF) << 1));
                    ushort vAddr2 = (ushort)(vAddr1 + 1);

                    byte b1 = _memory.ReadVideoRam(vAddr1);
                    byte b2 = _memory.ReadVideoRam(vAddr2);

                    RenderByteFast(b1, mode, palette, fbColOffset);
                    RenderByteFast(b2, mode, palette, fbColOffset + 8);
                }

                currentY++;
            }
        }
    }

    private void RenderByteFast(byte b, byte mode, uint[] palette, int destIndex)
    {
        switch (mode)
        {
            case 0: // Mode 0: 2 pixels, 4 screen dots per pixel
                {
                    byte[] pens = Mode0Pens[b];
                    uint c0 = palette[pens[0]];
                    uint c1 = palette[pens[1]];

                    Framebuffer[destIndex + 0] = c0;
                    Framebuffer[destIndex + 1] = c0;
                    Framebuffer[destIndex + 2] = c0;
                    Framebuffer[destIndex + 3] = c0;
                    Framebuffer[destIndex + 4] = c1;
                    Framebuffer[destIndex + 5] = c1;
                    Framebuffer[destIndex + 6] = c1;
                    Framebuffer[destIndex + 7] = c1;
                    break;
                }

            case 1: // Mode 1: 4 pixels, 2 screen dots per pixel
                {
                    byte[] pens = Mode1Pens[b];
                    uint c0 = palette[pens[0]];
                    uint c1 = palette[pens[1]];
                    uint c2 = palette[pens[2]];
                    uint c3 = palette[pens[3]];

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

            case 2: // Mode 2: 8 pixels, 1 screen dot per pixel
            default:
                {
                    byte[] pens = Mode2Pens[b];
                    uint c0 = palette[0];
                    uint c1 = palette[1];

                    for (int i = 0; i < 8; i++)
                    {
                        Framebuffer[destIndex + i] = (pens[i] != 0) ? c1 : c0;
                    }
                    break;
                }
        }
    }
}
