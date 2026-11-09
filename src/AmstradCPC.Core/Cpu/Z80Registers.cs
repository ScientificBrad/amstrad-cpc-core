namespace AmstradCPC.Core.Cpu;

/// <summary>
/// Zilog Z80 Register File and Flag definitions.
/// Represents all 8-bit, 16-bit, alternate register sets, index registers,
/// and interrupt flip-flops.
/// </summary>
public sealed class Z80Registers
{
    // Main 8-bit registers
    public byte A;
    public byte F;
    public byte B;
    public byte C;
    public byte D;
    public byte E;
    public byte H;
    public byte L;

    // Alternate 8-bit registers (' set)
    public byte AAlt;
    public byte FAlt;
    public byte BAlt;
    public byte CAlt;
    public byte DAlt;
    public byte EAlt;
    public byte HAlt;
    public byte LAlt;

    // 16-bit Index registers
    public ushort IX;
    public ushort IY;

    // Pointer and Program Counter
    public ushort SP;
    public ushort PC;

    // Interrupt and Refresh registers
    public byte I;
    public byte R;

    // Internal undocumented temporary register (WZ / MEMPTR)
    public ushort WZ;

    // Interrupt flags
    public bool IFF1;
    public bool IFF2;
    public byte IM; // 0, 1, or 2

    // Execution state
    public bool Halted;
    public bool EiDelay;

    // Flag bit definitions
    public const byte FlagC  = 0x01; // Carry
    public const byte FlagN  = 0x02; // Add/Subtract
    public const byte FlagPV = 0x04; // Parity or Overflow
    public const byte FlagX  = 0x08; // Undocumented Bit 3 (XF)
    public const byte FlagH  = 0x10; // Half Carry
    public const byte FlagY  = 0x20; // Undocumented Bit 5 (YF)
    public const byte FlagZ  = 0x40; // Zero
    public const byte FlagS  = 0x80; // Sign

    // 16-bit register pair accessors
    public ushort AF
    {
        get => (ushort)((A << 8) | F);
        set { A = (byte)(value >> 8); F = (byte)value; }
    }

    public ushort BC
    {
        get => (ushort)((B << 8) | C);
        set { B = (byte)(value >> 8); C = (byte)value; }
    }

    public ushort DE
    {
        get => (ushort)((D << 8) | E);
        set { D = (byte)(value >> 8); E = (byte)value; }
    }

    public ushort HL
    {
        get => (ushort)((H << 8) | L);
        set { H = (byte)(value >> 8); L = (byte)value; }
    }

    public ushort AFAlt
    {
        get => (ushort)((AAlt << 8) | FAlt);
        set { AAlt = (byte)(value >> 8); FAlt = (byte)value; }
    }

    public ushort BCAlt
    {
        get => (ushort)((BAlt << 8) | CAlt);
        set { BAlt = (byte)(value >> 8); CAlt = (byte)value; }
    }

    public ushort DEAlt
    {
        get => (ushort)((DAlt << 8) | EAlt);
        set { DAlt = (byte)(value >> 8); EAlt = (byte)value; }
    }

    public ushort HLAlt
    {
        get => (ushort)((HAlt << 8) | LAlt);
        set { HAlt = (byte)(value >> 8); LAlt = (byte)value; }
    }

    // Index register 8-bit split accessors
    public byte IXH
    {
        get => (byte)(IX >> 8);
        set => IX = (ushort)((value << 8) | (IX & 0x00FF));
    }

    public byte IXL
    {
        get => (byte)IX;
        set => IX = (ushort)((IX & 0xFF00) | value);
    }

    public byte IYH
    {
        get => (byte)(IY >> 8);
        set => IY = (ushort)((value << 8) | (IY & 0x00FF));
    }

    public byte IYL
    {
        get => (byte)IY;
        set => IY = (ushort)((IY & 0xFF00) | value);
    }

    // Flag inspection helpers
    public bool IsCarry => (F & FlagC) != 0;
    public bool IsZero => (F & FlagZ) != 0;
    public bool IsSign => (F & FlagS) != 0;
    public bool IsParityOverflow => (F & FlagPV) != 0;
    public bool IsHalfCarry => (F & FlagH) != 0;
    public bool IsSubtract => (F & FlagN) != 0;

    public void Reset()
    {
        AF = 0xFFFF;
        BC = 0xFFFF;
        DE = 0xFFFF;
        HL = 0xFFFF;
        AFAlt = 0xFFFF;
        BCAlt = 0xFFFF;
        DEAlt = 0xFFFF;
        HLAlt = 0xFFFF;
        IX = 0xFFFF;
        IY = 0xFFFF;
        SP = 0xFFFF;
        PC = 0x0000;
        I = 0x00;
        R = 0x00;
        WZ = 0x0000;
        IFF1 = false;
        IFF2 = false;
        IM = 0;
        Halted = false;
        EiDelay = false;
    }

    public override string ToString()
    {
        return $"PC:{PC:X4} AF:{AF:X4} BC:{BC:X4} DE:{DE:X4} HL:{HL:X4} IX:{IX:X4} IY:{IY:X4} SP:{SP:X4} Flags:{(IsSign ? "S" : "-")}{(IsZero ? "Z" : "-")}{(IsHalfCarry ? "H" : "-")}{(IsParityOverflow ? "P" : "-")}{(IsSubtract ? "N" : "-")}{(IsCarry ? "C" : "-")} IM:{IM} IFF1:{IFF1}";
    }
}
