namespace AmstradCPC.Core.Cpu;

/// <summary>
/// Clean-room cycle-accurate Zilog Z80A microprocessor implementation.
/// Designed for the Amstrad CPC computer family with Gate Array wait-state synchronization,
/// full prefix handling (CB, ED, DD, FD, DDCB, FDCB), undocumented instructions,
/// parity/overflow and half-carry tables, and IM 0/1/2 interrupt handling.
/// </summary>
public sealed class CpuZ80
{
    public readonly Z80Registers Reg = new();

    // Memory and I/O bus delegates
    public Func<ushort, byte> ReadMemory { get; set; } = _ => 0xFF;
    public Action<ushort, byte> WriteMemory { get; set; } = (_, _) => { };
    public Func<ushort, byte> ReadPort { get; set; } = _ => 0xFF;
    public Action<ushort, byte> WritePort { get; set; } = (_, _) => { };

    // Total T-states executed
    public long TotalCycles { get; set; }

    // Interrupt lines
    public bool InterruptPending { get; set; }
    public byte InterruptVector { get; set; } = 0xFF;
    public bool NmiPending { get; set; }

    // Amstrad CPC Gate Array wait-state mode (aligns all instruction cycles to multiples of 4 T-states)
    public bool CpcWaitStateMode { get; set; } = true;

    // Precomputed flag tables
    private static readonly byte[] SzTable = new byte[256];
    private static readonly byte[] SzpTable = new byte[256];
    private static readonly byte[] ParityTable = new byte[256];

    static CpuZ80()
    {
        for (int i = 0; i < 256; i++)
        {
            byte b = (byte)i;
            byte flags = 0;
            if ((b & 0x80) != 0) flags |= Z80Registers.FlagS;
            if (b == 0) flags |= Z80Registers.FlagZ;
            if ((b & 0x20) != 0) flags |= Z80Registers.FlagY;
            if ((b & 0x08) != 0) flags |= Z80Registers.FlagX;
            SzTable[i] = flags;

            int bits = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if (((b >> bit) & 1) != 0) bits++;
            }
            byte parity = (bits % 2 == 0) ? Z80Registers.FlagPV : (byte)0;
            ParityTable[i] = parity;
            SzpTable[i] = (byte)(flags | parity);
        }
    }

    public CpuZ80()
    {
        Reset();
    }

    public void Reset()
    {
        Reg.Reset();
        TotalCycles = 0;
        InterruptPending = false;
        InterruptVector = 0xFF;
        NmiPending = false;
    }

    /// <summary>
    /// Executes a single instruction, handling interrupts, prefixes, and CPC cycle alignment.
    /// Returns the number of T-states taken.
    /// </summary>
    public int Step()
    {
        int cycles = 0;

        // Process NMI
        if (NmiPending)
        {
            NmiPending = false;
            Reg.Halted = false;
            Reg.IFF2 = Reg.IFF1;
            Reg.IFF1 = false;
            PushWord(Reg.PC);
            Reg.PC = 0x0066;
            cycles = 11;
            return FinalizeCycles(cycles);
        }

        // Process maskable interrupt
        if (InterruptPending && Reg.IFF1 && !Reg.EiDelay)
        {
            InterruptPending = false;
            Reg.Halted = false;
            Reg.IFF1 = false;
            Reg.IFF2 = false;
            PushWord(Reg.PC);

            switch (Reg.IM)
            {
                case 0:
                case 1:
                    // CPC almost exclusively operates in IM 1 (RST 38h)
                    Reg.PC = 0x0038;
                    cycles = 13;
                    break;
                case 2:
                    ushort vectorAddress = (ushort)((Reg.I << 8) | (InterruptVector & 0xFE));
                    Reg.PC = ReadWord(vectorAddress);
                    cycles = 19;
                    break;
            }
            return FinalizeCycles(cycles);
        }

        // Clear EI delay
        if (Reg.EiDelay)
        {
            Reg.EiDelay = false;
        }

        // If CPU is halted, wait until interrupt occurs (consumes 4 T-states)
        if (Reg.Halted)
        {
            Reg.R = (byte)(((Reg.R + 1) & 0x7F) | (Reg.R & 0x80));
            return FinalizeCycles(4);
        }

        // Fetch primary opcode
        byte opcode = FetchByte();
        Reg.R = (byte)(((Reg.R + 1) & 0x7F) | (Reg.R & 0x80));

        cycles = ExecuteOpcode(opcode);
        return FinalizeCycles(cycles);
    }

    private int FinalizeCycles(int rawCycles)
    {
        int cycles = rawCycles;
        if (CpcWaitStateMode)
        {
            // Amstrad Gate Array synchronizes all memory cycles to 1 MHz (4 T-state) boundaries.
            cycles = (cycles + 3) & ~3;
        }
        TotalCycles += cycles;
        return cycles;
    }

    private byte FetchByte()
    {
        byte b = ReadMemory(Reg.PC);
        Reg.PC++;
        return b;
    }

    private ushort FetchWord()
    {
        byte low = FetchByte();
        byte high = FetchByte();
        return (ushort)((high << 8) | low);
    }

    private ushort ReadWord(ushort address)
    {
        byte low = ReadMemory(address);
        byte high = ReadMemory((ushort)(address + 1));
        return (ushort)((high << 8) | low);
    }

    private void WriteWord(ushort address, ushort value)
    {
        WriteMemory(address, (byte)value);
        WriteMemory((ushort)(address + 1), (byte)(value >> 8));
    }

    private void PushWord(ushort value)
    {
        Reg.SP -= 2;
        WriteWord(Reg.SP, value);
    }

    private ushort PopWord()
    {
        ushort value = ReadWord(Reg.SP);
        Reg.SP += 2;
        return value;
    }

    public void RequestInterrupt(byte vector = 0xFF)
    {
        InterruptPending = true;
        InterruptVector = vector;
    }

    public void RequestNmi()
    {
        NmiPending = true;
    }

    #region Primary Instruction Decoder

    private int ExecuteOpcode(byte opcode)
    {
        switch (opcode)
        {
            // NOP
            case 0x00: return 4;

            // 16-bit Loads
            case 0x01: Reg.BC = FetchWord(); return 10;
            case 0x11: Reg.DE = FetchWord(); return 10;
            case 0x21: Reg.HL = FetchWord(); return 10;
            case 0x31: Reg.SP = FetchWord(); return 10;

            // Indirect Stores / Loads
            case 0x02: WriteMemory(Reg.BC, Reg.A); Reg.WZ = (ushort)((Reg.A << 8) | ((Reg.BC + 1) & 0xFF)); return 7;
            case 0x12: WriteMemory(Reg.DE, Reg.A); Reg.WZ = (ushort)((Reg.A << 8) | ((Reg.DE + 1) & 0xFF)); return 7;
            case 0x22: { ushort addr = FetchWord(); WriteWord(addr, Reg.HL); Reg.WZ = (ushort)(addr + 1); return 16; }
            case 0x32: { ushort addr = FetchWord(); WriteMemory(addr, Reg.A); Reg.WZ = (ushort)((Reg.A << 8) | ((addr + 1) & 0xFF)); return 13; }

            case 0x0A: Reg.A = ReadMemory(Reg.BC); Reg.WZ = (ushort)(Reg.BC + 1); return 7;
            case 0x1A: Reg.A = ReadMemory(Reg.DE); Reg.WZ = (ushort)(Reg.DE + 1); return 7;
            case 0x2A: { ushort addr = FetchWord(); Reg.HL = ReadWord(addr); Reg.WZ = (ushort)(addr + 1); return 16; }
            case 0x3A: { ushort addr = FetchWord(); Reg.A = ReadMemory(addr); Reg.WZ = (ushort)(addr + 1); return 13; }

            // 16-bit INC / DEC
            case 0x03: Reg.BC++; return 6;
            case 0x13: Reg.DE++; return 6;
            case 0x23: Reg.HL++; return 6;
            case 0x33: Reg.SP++; return 6;
            case 0x0B: Reg.BC--; return 6;
            case 0x1B: Reg.DE--; return 6;
            case 0x2B: Reg.HL--; return 6;
            case 0x3B: Reg.SP--; return 6;

            // 8-bit INC
            case 0x04: Reg.B = Inc8(Reg.B); return 4;
            case 0x0C: Reg.C = Inc8(Reg.C); return 4;
            case 0x14: Reg.D = Inc8(Reg.D); return 4;
            case 0x1C: Reg.E = Inc8(Reg.E); return 4;
            case 0x24: Reg.H = Inc8(Reg.H); return 4;
            case 0x2C: Reg.L = Inc8(Reg.L); return 4;
            case 0x34: WriteMemory(Reg.HL, Inc8(ReadMemory(Reg.HL))); return 11;
            case 0x3C: Reg.A = Inc8(Reg.A); return 4;

            // 8-bit DEC
            case 0x05: Reg.B = Dec8(Reg.B); return 4;
            case 0x0D: Reg.C = Dec8(Reg.C); return 4;
            case 0x15: Reg.D = Dec8(Reg.D); return 4;
            case 0x1D: Reg.E = Dec8(Reg.E); return 4;
            case 0x25: Reg.H = Dec8(Reg.H); return 4;
            case 0x2D: Reg.L = Dec8(Reg.L); return 4;
            case 0x35: WriteMemory(Reg.HL, Dec8(ReadMemory(Reg.HL))); return 11;
            case 0x3D: Reg.A = Dec8(Reg.A); return 4;

            // 8-bit Immediate Loads
            case 0x06: Reg.B = FetchByte(); return 7;
            case 0x0E: Reg.C = FetchByte(); return 7;
            case 0x16: Reg.D = FetchByte(); return 7;
            case 0x1E: Reg.E = FetchByte(); return 7;
            case 0x26: Reg.H = FetchByte(); return 7;
            case 0x2E: Reg.L = FetchByte(); return 7;
            case 0x36: WriteMemory(Reg.HL, FetchByte()); return 10;
            case 0x3E: Reg.A = FetchByte(); return 7;

            // Rotates of Accumulator
            case 0x07: Rlca(); return 4;
            case 0x0F: Rrca(); return 4;
            case 0x17: Rla(); return 4;
            case 0x1F: Rra(); return 4;

            // Relative Jumps
            case 0x10: // DJNZ e
                {
                    sbyte offset = (sbyte)FetchByte();
                    Reg.B--;
                    if (Reg.B != 0)
                    {
                        Reg.PC = (ushort)(Reg.PC + offset);
                        Reg.WZ = Reg.PC;
                        return 13;
                    }
                    return 8;
                }
            case 0x18: // JR e
                {
                    sbyte offset = (sbyte)FetchByte();
                    Reg.PC = (ushort)(Reg.PC + offset);
                    Reg.WZ = Reg.PC;
                    return 12;
                }
            case 0x20: // JR NZ, e
                {
                    sbyte offset = (sbyte)FetchByte();
                    if (!Reg.IsZero) { Reg.PC = (ushort)(Reg.PC + offset); Reg.WZ = Reg.PC; return 12; }
                    return 7;
                }
            case 0x28: // JR Z, e
                {
                    sbyte offset = (sbyte)FetchByte();
                    if (Reg.IsZero) { Reg.PC = (ushort)(Reg.PC + offset); Reg.WZ = Reg.PC; return 12; }
                    return 7;
                }
            case 0x30: // JR NC, e
                {
                    sbyte offset = (sbyte)FetchByte();
                    if (!Reg.IsCarry) { Reg.PC = (ushort)(Reg.PC + offset); Reg.WZ = Reg.PC; return 12; }
                    return 7;
                }
            case 0x38: // JR C, e
                {
                    sbyte offset = (sbyte)FetchByte();
                    if (Reg.IsCarry) { Reg.PC = (ushort)(Reg.PC + offset); Reg.WZ = Reg.PC; return 12; }
                    return 7;
                }

            // 16-bit ADD HL, ss
            case 0x09: Reg.HL = Add16(Reg.HL, Reg.BC); return 11;
            case 0x19: Reg.HL = Add16(Reg.HL, Reg.DE); return 11;
            case 0x29: Reg.HL = Add16(Reg.HL, Reg.HL); return 11;
            case 0x39: Reg.HL = Add16(Reg.HL, Reg.SP); return 11;

            // DAA, CPL, SCF, CCF
            case 0x27: Daa(); return 4;
            case 0x2F:
                Reg.A ^= 0xFF;
                Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV | Z80Registers.FlagC))
                              | Z80Registers.FlagH | Z80Registers.FlagN
                              | (Reg.A & (Z80Registers.FlagY | Z80Registers.FlagX)));
                return 4;
            case 0x37: // SCF
                Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV))
                              | Z80Registers.FlagC
                              | (Reg.A & (Z80Registers.FlagY | Z80Registers.FlagX)));
                return 4;
            case 0x3F: // CCF
                {
                    bool c = Reg.IsCarry;
                    Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV))
                                  | (c ? Z80Registers.FlagH : 0)
                                  | (c ? 0 : Z80Registers.FlagC)
                                  | (Reg.A & (Z80Registers.FlagY | Z80Registers.FlagX)));
                    return 4;
                }

            // LD r, r' block (0x40 - 0x7F)
            case 0x40: return 4;
            case 0x41: Reg.B = Reg.C; return 4;
            case 0x42: Reg.B = Reg.D; return 4;
            case 0x43: Reg.B = Reg.E; return 4;
            case 0x44: Reg.B = Reg.H; return 4;
            case 0x45: Reg.B = Reg.L; return 4;
            case 0x46: Reg.B = ReadMemory(Reg.HL); return 7;
            case 0x47: Reg.B = Reg.A; return 4;

            case 0x48: Reg.C = Reg.B; return 4;
            case 0x49: return 4;
            case 0x4A: Reg.C = Reg.D; return 4;
            case 0x4B: Reg.C = Reg.E; return 4;
            case 0x4C: Reg.C = Reg.H; return 4;
            case 0x4D: Reg.C = Reg.L; return 4;
            case 0x4E: Reg.C = ReadMemory(Reg.HL); return 7;
            case 0x4F: Reg.C = Reg.A; return 4;

            case 0x50: Reg.D = Reg.B; return 4;
            case 0x51: Reg.D = Reg.C; return 4;
            case 0x52: return 4;
            case 0x53: Reg.D = Reg.E; return 4;
            case 0x54: Reg.D = Reg.H; return 4;
            case 0x55: Reg.D = Reg.L; return 4;
            case 0x56: Reg.D = ReadMemory(Reg.HL); return 7;
            case 0x57: Reg.D = Reg.A; return 4;

            case 0x58: Reg.E = Reg.B; return 4;
            case 0x59: Reg.E = Reg.C; return 4;
            case 0x5A: Reg.E = Reg.D; return 4;
            case 0x5B: return 4;
            case 0x5C: Reg.E = Reg.H; return 4;
            case 0x5D: Reg.E = Reg.L; return 4;
            case 0x5E: Reg.E = ReadMemory(Reg.HL); return 7;
            case 0x5F: Reg.E = Reg.A; return 4;

            case 0x60: Reg.H = Reg.B; return 4;
            case 0x61: Reg.H = Reg.C; return 4;
            case 0x62: Reg.H = Reg.D; return 4;
            case 0x63: Reg.H = Reg.E; return 4;
            case 0x64: return 4;
            case 0x65: Reg.H = Reg.L; return 4;
            case 0x66: Reg.H = ReadMemory(Reg.HL); return 7;
            case 0x67: Reg.H = Reg.A; return 4;

            case 0x68: Reg.L = Reg.B; return 4;
            case 0x69: Reg.L = Reg.C; return 4;
            case 0x6A: Reg.L = Reg.D; return 4;
            case 0x6B: Reg.L = Reg.E; return 4;
            case 0x6C: Reg.L = Reg.H; return 4;
            case 0x6D: return 4;
            case 0x6E: Reg.L = ReadMemory(Reg.HL); return 7;
            case 0x6F: Reg.L = Reg.A; return 4;

            case 0x70: WriteMemory(Reg.HL, Reg.B); return 7;
            case 0x71: WriteMemory(Reg.HL, Reg.C); return 7;
            case 0x72: WriteMemory(Reg.HL, Reg.D); return 7;
            case 0x73: WriteMemory(Reg.HL, Reg.E); return 7;
            case 0x74: WriteMemory(Reg.HL, Reg.H); return 7;
            case 0x75: WriteMemory(Reg.HL, Reg.L); return 7;
            case 0x76: Reg.Halted = true; return 4; // HALT
            case 0x77: WriteMemory(Reg.HL, Reg.A); return 7;

            case 0x78: Reg.A = Reg.B; return 4;
            case 0x79: Reg.A = Reg.C; return 4;
            case 0x7A: Reg.A = Reg.D; return 4;
            case 0x7B: Reg.A = Reg.E; return 4;
            case 0x7C: Reg.A = Reg.H; return 4;
            case 0x7D: Reg.A = Reg.L; return 4;
            case 0x7E: Reg.A = ReadMemory(Reg.HL); return 7;
            case 0x7F: return 4;

            // ALU Operations (0x80 - 0xBF)
            case 0x80: Add8(Reg.B); return 4;
            case 0x81: Add8(Reg.C); return 4;
            case 0x82: Add8(Reg.D); return 4;
            case 0x83: Add8(Reg.E); return 4;
            case 0x84: Add8(Reg.H); return 4;
            case 0x85: Add8(Reg.L); return 4;
            case 0x86: Add8(ReadMemory(Reg.HL)); return 7;
            case 0x87: Add8(Reg.A); return 4;

            case 0x88: Adc8(Reg.B); return 4;
            case 0x89: Adc8(Reg.C); return 4;
            case 0x8A: Adc8(Reg.D); return 4;
            case 0x8B: Adc8(Reg.E); return 4;
            case 0x8C: Adc8(Reg.H); return 4;
            case 0x8D: Adc8(Reg.L); return 4;
            case 0x8E: Adc8(ReadMemory(Reg.HL)); return 7;
            case 0x8F: Adc8(Reg.A); return 4;

            case 0x90: Sub8(Reg.B); return 4;
            case 0x91: Sub8(Reg.C); return 4;
            case 0x92: Sub8(Reg.D); return 4;
            case 0x93: Sub8(Reg.E); return 4;
            case 0x94: Sub8(Reg.H); return 4;
            case 0x95: Sub8(Reg.L); return 4;
            case 0x96: Sub8(ReadMemory(Reg.HL)); return 7;
            case 0x97: Sub8(Reg.A); return 4;

            case 0x98: Sbc8(Reg.B); return 4;
            case 0x99: Sbc8(Reg.C); return 4;
            case 0x9A: Sbc8(Reg.D); return 4;
            case 0x9B: Sbc8(Reg.E); return 4;
            case 0x9C: Sbc8(Reg.H); return 4;
            case 0x9D: Sbc8(Reg.L); return 4;
            case 0x9E: Sbc8(ReadMemory(Reg.HL)); return 7;
            case 0x9F: Sbc8(Reg.A); return 4;

            case 0xA0: And8(Reg.B); return 4;
            case 0xA1: And8(Reg.C); return 4;
            case 0xA2: And8(Reg.D); return 4;
            case 0xA3: And8(Reg.E); return 4;
            case 0xA4: And8(Reg.H); return 4;
            case 0xA5: And8(Reg.L); return 4;
            case 0xA6: And8(ReadMemory(Reg.HL)); return 7;
            case 0xA7: And8(Reg.A); return 4;

            case 0xA8: Xor8(Reg.B); return 4;
            case 0xA9: Xor8(Reg.C); return 4;
            case 0xAA: Xor8(Reg.D); return 4;
            case 0xAB: Xor8(Reg.E); return 4;
            case 0xAC: Xor8(Reg.H); return 4;
            case 0xAD: Xor8(Reg.L); return 4;
            case 0xAE: Xor8(ReadMemory(Reg.HL)); return 7;
            case 0xAF: Xor8(Reg.A); return 4;

            case 0xB0: Or8(Reg.B); return 4;
            case 0xB1: Or8(Reg.C); return 4;
            case 0xB2: Or8(Reg.D); return 4;
            case 0xB3: Or8(Reg.E); return 4;
            case 0xB4: Or8(Reg.H); return 4;
            case 0xB5: Or8(Reg.L); return 4;
            case 0xB6: Or8(ReadMemory(Reg.HL)); return 7;
            case 0xB7: Or8(Reg.A); return 4;

            case 0xB8: Cp8(Reg.B); return 4;
            case 0xB9: Cp8(Reg.C); return 4;
            case 0xBA: Cp8(Reg.D); return 4;
            case 0xBB: Cp8(Reg.E); return 4;
            case 0xBC: Cp8(Reg.H); return 4;
            case 0xBD: Cp8(Reg.L); return 4;
            case 0xBE: Cp8(ReadMemory(Reg.HL)); return 7;
            case 0xBF: Cp8(Reg.A); return 4;

            // Conditional Returns
            case 0xC0: if (!Reg.IsZero) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xC8: if (Reg.IsZero) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xD0: if (!Reg.IsCarry) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xD8: if (Reg.IsCarry) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xE0: if (!Reg.IsParityOverflow) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xE8: if (Reg.IsParityOverflow) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xF0: if (!Reg.IsSign) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;
            case 0xF8: if (Reg.IsSign) { Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 11; } return 5;

            // POP ss
            case 0xC1: Reg.BC = PopWord(); return 10;
            case 0xD1: Reg.DE = PopWord(); return 10;
            case 0xE1: Reg.HL = PopWord(); return 10;
            case 0xF1: Reg.AF = PopWord(); return 10;

            // PUSH ss
            case 0xC5: PushWord(Reg.BC); return 11;
            case 0xD5: PushWord(Reg.DE); return 11;
            case 0xE5: PushWord(Reg.HL); return 11;
            case 0xF5: PushWord(Reg.AF); return 11;

            // RET & EXX & JP (HL) & LD SP, HL
            case 0xC9: Reg.PC = PopWord(); Reg.WZ = Reg.PC; return 10;
            case 0xD9: // EXX
                {
                    (Reg.B, Reg.BAlt) = (Reg.BAlt, Reg.B);
                    (Reg.C, Reg.CAlt) = (Reg.CAlt, Reg.C);
                    (Reg.D, Reg.DAlt) = (Reg.DAlt, Reg.D);
                    (Reg.E, Reg.EAlt) = (Reg.EAlt, Reg.E);
                    (Reg.H, Reg.HAlt) = (Reg.HAlt, Reg.H);
                    (Reg.L, Reg.LAlt) = (Reg.LAlt, Reg.L);
                    return 4;
                }
            case 0xE9: Reg.PC = Reg.HL; return 4;
            case 0xF9: Reg.SP = Reg.HL; return 6;

            // Conditional Jumps JP cc, nn
            case 0xC2: { ushort addr = FetchWord(); Reg.WZ = addr; if (!Reg.IsZero) Reg.PC = addr; return 10; }
            case 0xCA: { ushort addr = FetchWord(); Reg.WZ = addr; if (Reg.IsZero) Reg.PC = addr; return 10; }
            case 0xD2: { ushort addr = FetchWord(); Reg.WZ = addr; if (!Reg.IsCarry) Reg.PC = addr; return 10; }
            case 0xDA: { ushort addr = FetchWord(); Reg.WZ = addr; if (Reg.IsCarry) Reg.PC = addr; return 10; }
            case 0xE2: { ushort addr = FetchWord(); Reg.WZ = addr; if (!Reg.IsParityOverflow) Reg.PC = addr; return 10; }
            case 0xEA: { ushort addr = FetchWord(); Reg.WZ = addr; if (Reg.IsParityOverflow) Reg.PC = addr; return 10; }
            case 0xF2: { ushort addr = FetchWord(); Reg.WZ = addr; if (!Reg.IsSign) Reg.PC = addr; return 10; }
            case 0xFA: { ushort addr = FetchWord(); Reg.WZ = addr; if (Reg.IsSign) Reg.PC = addr; return 10; }

            // JP nn
            case 0xC3: Reg.PC = FetchWord(); Reg.WZ = Reg.PC; return 10;

            // Conditional Calls CALL cc, nn
            case 0xC4: return CallConditional(!Reg.IsZero);
            case 0xCC: return CallConditional(Reg.IsZero);
            case 0xD4: return CallConditional(!Reg.IsCarry);
            case 0xDC: return CallConditional(Reg.IsCarry);
            case 0xE4: return CallConditional(!Reg.IsParityOverflow);
            case 0xEC: return CallConditional(Reg.IsParityOverflow);
            case 0xF4: return CallConditional(!Reg.IsSign);
            case 0xFC: return CallConditional(Reg.IsSign);

            // CALL nn
            case 0xCD:
                {
                    ushort addr = FetchWord();
                    PushWord(Reg.PC);
                    Reg.PC = addr;
                    Reg.WZ = addr;
                    return 17;
                }

            // Immediate ALU
            case 0xC6: Add8(FetchByte()); return 7;
            case 0xCE: Adc8(FetchByte()); return 7;
            case 0xD6: Sub8(FetchByte()); return 7;
            case 0xDE: Sbc8(FetchByte()); return 7;
            case 0xE6: And8(FetchByte()); return 7;
            case 0xEE: Xor8(FetchByte()); return 7;
            case 0xF6: Or8(FetchByte()); return 7;
            case 0xFE: Cp8(FetchByte()); return 7;

            // RST p
            case 0xC7: PushWord(Reg.PC); Reg.PC = 0x0000; Reg.WZ = 0x0000; return 11;
            case 0xCF: PushWord(Reg.PC); Reg.PC = 0x0008; Reg.WZ = 0x0008; return 11;
            case 0xD7: PushWord(Reg.PC); Reg.PC = 0x0010; Reg.WZ = 0x0010; return 11;
            case 0xDF: PushWord(Reg.PC); Reg.PC = 0x0018; Reg.WZ = 0x0018; return 11;
            case 0xE7: PushWord(Reg.PC); Reg.PC = 0x0020; Reg.WZ = 0x0020; return 11;
            case 0xEF: PushWord(Reg.PC); Reg.PC = 0x0028; Reg.WZ = 0x0028; return 11;
            case 0xF7: PushWord(Reg.PC); Reg.PC = 0x0030; Reg.WZ = 0x0030; return 11;
            case 0xFF: PushWord(Reg.PC); Reg.PC = 0x0038; Reg.WZ = 0x0038; return 11;

            // EX AF, AF' & EX (SP), HL & EX DE, HL
            case 0x08: (Reg.AF, Reg.AFAlt) = (Reg.AFAlt, Reg.AF); return 4;
            case 0xEB: (Reg.DE, Reg.HL) = (Reg.HL, Reg.DE); return 4;
            case 0xE3:
                {
                    ushort val = ReadWord(Reg.SP);
                    WriteWord(Reg.SP, Reg.HL);
                    Reg.HL = val;
                    Reg.WZ = val;
                    return 19;
                }

            // I/O: OUT (n), A & IN A, (n)
            case 0xD3: // OUT (n), A
                {
                    byte portLow = FetchByte();
                    ushort port = (ushort)((Reg.A << 8) | portLow);
                    WritePort(port, Reg.A);
                    Reg.WZ = (ushort)((Reg.A << 8) | ((portLow + 1) & 0xFF));
                    return 11;
                }
            case 0xDB: // IN A, (n)
                {
                    byte portLow = FetchByte();
                    ushort port = (ushort)((Reg.A << 8) | portLow);
                    Reg.A = ReadPort(port);
                    Reg.WZ = (ushort)(port + 1);
                    return 11;
                }

            // DI / EI
            case 0xF3: Reg.IFF1 = false; Reg.IFF2 = false; return 4;
            case 0xFB: Reg.IFF1 = true; Reg.IFF2 = true; Reg.EiDelay = true; return 4;

            // Prefixes
            case 0xCB: return ExecuteCb();
            case 0xED: return ExecuteEd();
            case 0xDD: return ExecuteIndex(ref Reg.IX);
            case 0xFD: return ExecuteIndex(ref Reg.IY);
        }
    }

    private int CallConditional(bool condition)
    {
        ushort addr = FetchWord();
        Reg.WZ = addr;
        if (condition)
        {
            PushWord(Reg.PC);
            Reg.PC = addr;
            return 17;
        }
        return 10;
    }

    #endregion

    #region Prefix CB Decoder

    private int ExecuteCb()
    {
        byte opcode = FetchByte();
        Reg.R = (byte)(((Reg.R + 1) & 0x7F) | (Reg.R & 0x80));

        int regIndex = opcode & 0x07;
        int bit = (opcode >> 3) & 0x07;
        byte val = GetReg8(regIndex);
        int cycles = (regIndex == 6) ? 15 : 8;

        if (opcode < 0x40) // Rotates and Shifts
        {
            byte res = (opcode >> 3) switch
            {
                0 => Rlc(val),
                1 => Rrc(val),
                2 => Rl(val),
                3 => Rr(val),
                4 => Sla(val),
                5 => Sra(val),
                6 => Sll(val), // Undocumented SLL/SLS
                7 => Srl(val),
                _ => val
            };
            SetReg8(regIndex, res);
            if (regIndex == 6) cycles = 15;
        }
        else if (opcode < 0x80) // BIT b, r
        {
            Bit(bit, val, regIndex == 6);
            cycles = (regIndex == 6) ? 12 : 8;
        }
        else if (opcode < 0xC0) // RES b, r
        {
            val = (byte)(val & ~(1 << bit));
            SetReg8(regIndex, val);
            if (regIndex == 6) cycles = 15;
        }
        else // SET b, r
        {
            val = (byte)(val | (1 << bit));
            SetReg8(regIndex, val);
            if (regIndex == 6) cycles = 15;
        }

        return cycles;
    }

    #endregion

    #region Prefix ED Decoder

    private int ExecuteEd()
    {
        byte opcode = FetchByte();
        Reg.R = (byte)(((Reg.R + 1) & 0x7F) | (Reg.R & 0x80));

        switch (opcode)
        {
            // IN r, (C)
            case 0x40: Reg.B = InC(); return 12;
            case 0x48: Reg.C = InC(); return 12;
            case 0x50: Reg.D = InC(); return 12;
            case 0x58: Reg.E = InC(); return 12;
            case 0x60: Reg.H = InC(); return 12;
            case 0x68: Reg.L = InC(); return 12;
            case 0x70: InC(); return 12; // Undocumented IN (C) affects flags
            case 0x78: Reg.A = InC(); return 12;

            // OUT (C), r
            case 0x41: WritePort(Reg.BC, Reg.B); return 12;
            case 0x49: WritePort(Reg.BC, Reg.C); return 12;
            case 0x51: WritePort(Reg.BC, Reg.D); return 12;
            case 0x59: WritePort(Reg.BC, Reg.E); return 12;
            case 0x61: WritePort(Reg.BC, Reg.H); return 12;
            case 0x69: WritePort(Reg.BC, Reg.L); return 12;
            case 0x71: WritePort(Reg.BC, 0); return 12; // Undocumented OUT (C), 0
            case 0x79: WritePort(Reg.BC, Reg.A); return 12;

            // SBC HL, ss
            case 0x42: Sbc16(Reg.BC); return 15;
            case 0x52: Sbc16(Reg.DE); return 15;
            case 0x62: Sbc16(Reg.HL); return 15;
            case 0x72: Sbc16(Reg.SP); return 15;

            // ADC HL, ss
            case 0x4A: Adc16(Reg.BC); return 15;
            case 0x5A: Adc16(Reg.DE); return 15;
            case 0x6A: Adc16(Reg.HL); return 15;
            case 0x7A: Adc16(Reg.SP); return 15;

            // LD (nn), dd
            case 0x43: { ushort addr = FetchWord(); WriteWord(addr, Reg.BC); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x53: { ushort addr = FetchWord(); WriteWord(addr, Reg.DE); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x63: { ushort addr = FetchWord(); WriteWord(addr, Reg.HL); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x73: { ushort addr = FetchWord(); WriteWord(addr, Reg.SP); Reg.WZ = (ushort)(addr + 1); return 20; }

            // LD dd, (nn)
            case 0x4B: { ushort addr = FetchWord(); Reg.BC = ReadWord(addr); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x5B: { ushort addr = FetchWord(); Reg.DE = ReadWord(addr); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x6B: { ushort addr = FetchWord(); Reg.HL = ReadWord(addr); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x7B: { ushort addr = FetchWord(); Reg.SP = ReadWord(addr); Reg.WZ = (ushort)(addr + 1); return 20; }

            // NEG
            case 0x44:
            case 0x4C:
            case 0x54:
            case 0x5C:
            case 0x64:
            case 0x6C:
            case 0x74:
            case 0x7C:
                Neg(); return 8;

            // RETN / RETI
            case 0x45:
            case 0x55:
            case 0x65:
            case 0x75: // RETN
                Reg.IFF1 = Reg.IFF2;
                Reg.PC = PopWord();
                Reg.WZ = Reg.PC;
                return 14;

            case 0x4D:
            case 0x5D:
            case 0x6D:
            case 0x7D: // RETI
                Reg.IFF1 = Reg.IFF2;
                Reg.PC = PopWord();
                Reg.WZ = Reg.PC;
                return 14;

            // Interrupt Modes
            case 0x46: case 0x66: Reg.IM = 0; return 8;
            case 0x56: case 0x76: Reg.IM = 1; return 8;
            case 0x5E: case 0x7E: Reg.IM = 2; return 8;

            // Special Register Transfers
            case 0x47: Reg.I = Reg.A; return 9;
            case 0x4F: Reg.R = Reg.A; return 9;
            case 0x57: // LD A, I
                Reg.A = Reg.I;
                Reg.F = (byte)((Reg.F & Z80Registers.FlagC) | SzTable[Reg.A] | (Reg.IFF2 ? Z80Registers.FlagPV : 0));
                return 9;
            case 0x5F: // LD A, R
                Reg.A = Reg.R;
                Reg.F = (byte)((Reg.F & Z80Registers.FlagC) | SzTable[Reg.A] | (Reg.IFF2 ? Z80Registers.FlagPV : 0));
                return 9;

            // RRD / RLD
            case 0x67: Rrd(); return 18;
            case 0x6F: Rld(); return 18;

            // Block Transfers
            case 0xA0: return Ldi();
            case 0xB0: return Ldir();
            case 0xA8: return Ldd();
            case 0xB8: return Lddr();

            // Block Compares
            case 0xA1: return Cpi();
            case 0xB1: return Cpir();
            case 0xA9: return Cpd();
            case 0xB9: return Cpdr();

            // Block I/O
            case 0xA2: return Ini();
            case 0xB2: return Inir();
            case 0xAA: return Ind();
            case 0xBA: return Indr();
            case 0xA3: return Outi();
            case 0xB3: return Otir();
            case 0xAB: return Outd();
            case 0xBB: return Otdr();

            default:
                return 8; // NOP-like for unmapped ED opcodes
        }
    }

    #endregion

    #region Prefix DD / FD Index Decoder

    private int ExecuteIndex(ref ushort indexReg)
    {
        byte opcode = FetchByte();
        Reg.R = (byte)(((Reg.R + 1) & 0x7F) | (Reg.R & 0x80));

        switch (opcode)
        {
            // ADD IX/IY, ss
            case 0x09: indexReg = Add16(indexReg, Reg.BC); return 15;
            case 0x19: indexReg = Add16(indexReg, Reg.DE); return 15;
            case 0x29: indexReg = Add16(indexReg, indexReg); return 15;
            case 0x39: indexReg = Add16(indexReg, Reg.SP); return 15;

            // INC / DEC IX/IY
            case 0x23: indexReg++; return 10;
            case 0x2B: indexReg--; return 10;

            // LD IX/IY, nn
            case 0x21: indexReg = FetchWord(); return 14;

            // LD (nn), IX/IY and LD IX/IY, (nn)
            case 0x22: { ushort addr = FetchWord(); WriteWord(addr, indexReg); Reg.WZ = (ushort)(addr + 1); return 20; }
            case 0x2A: { ushort addr = FetchWord(); indexReg = ReadWord(addr); Reg.WZ = (ushort)(addr + 1); return 20; }

            // High/Low Index Register Operations
            case 0x24: SetIndexH(ref indexReg, Inc8(GetIndexH(indexReg))); return 8;
            case 0x25: SetIndexH(ref indexReg, Dec8(GetIndexH(indexReg))); return 8;
            case 0x26: SetIndexH(ref indexReg, FetchByte()); return 11;
            case 0x2C: SetIndexL(ref indexReg, Inc8(GetIndexL(indexReg))); return 8;
            case 0x2D: SetIndexL(ref indexReg, Dec8(GetIndexL(indexReg))); return 8;
            case 0x2E: SetIndexL(ref indexReg, FetchByte()); return 11;

            // INC / DEC (IX/IY + d)
            case 0x34:
                {
                    ushort ea = EffectiveAddress(indexReg);
                    WriteMemory(ea, Inc8(ReadMemory(ea)));
                    return 23;
                }
            case 0x35:
                {
                    ushort ea = EffectiveAddress(indexReg);
                    WriteMemory(ea, Dec8(ReadMemory(ea)));
                    return 23;
                }
            case 0x36:
                {
                    ushort ea = EffectiveAddress(indexReg);
                    WriteMemory(ea, FetchByte());
                    return 19;
                }

            // LD r, (IX/IY + d)
            case 0x46: Reg.B = ReadMemory(EffectiveAddress(indexReg)); return 19;
            case 0x4E: Reg.C = ReadMemory(EffectiveAddress(indexReg)); return 19;
            case 0x56: Reg.D = ReadMemory(EffectiveAddress(indexReg)); return 19;
            case 0x5E: Reg.E = ReadMemory(EffectiveAddress(indexReg)); return 19;
            case 0x66: Reg.H = ReadMemory(EffectiveAddress(indexReg)); return 19;
            case 0x6E: Reg.L = ReadMemory(EffectiveAddress(indexReg)); return 19;
            case 0x7E: Reg.A = ReadMemory(EffectiveAddress(indexReg)); return 19;

            // LD (IX/IY + d), r
            case 0x70: WriteMemory(EffectiveAddress(indexReg), Reg.B); return 19;
            case 0x71: WriteMemory(EffectiveAddress(indexReg), Reg.C); return 19;
            case 0x72: WriteMemory(EffectiveAddress(indexReg), Reg.D); return 19;
            case 0x73: WriteMemory(EffectiveAddress(indexReg), Reg.E); return 19;
            case 0x74: WriteMemory(EffectiveAddress(indexReg), Reg.H); return 19;
            case 0x75: WriteMemory(EffectiveAddress(indexReg), Reg.L); return 19;
            case 0x77: WriteMemory(EffectiveAddress(indexReg), Reg.A); return 19;

            // ALU with (IX/IY + d)
            case 0x86: Add8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0x8E: Adc8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0x96: Sub8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0x9E: Sbc8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0xA6: And8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0xAE: Xor8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0xB6: Or8(ReadMemory(EffectiveAddress(indexReg))); return 19;
            case 0xBE: Cp8(ReadMemory(EffectiveAddress(indexReg))); return 19;

            // POP / PUSH
            case 0xE1: indexReg = PopWord(); return 14;
            case 0xE5: PushWord(indexReg); return 15;

            // EX (SP), IX/IY
            case 0xE3:
                {
                    ushort val = ReadWord(Reg.SP);
                    WriteWord(Reg.SP, indexReg);
                    indexReg = val;
                    Reg.WZ = val;
                    return 23;
                }

            // JP (IX/IY)
            case 0xE9: Reg.PC = indexReg; return 8;

            // LD SP, IX/IY
            case 0xF9: Reg.SP = indexReg; return 10;

            // DDCB / FDCB Bit Operations
            case 0xCB: return ExecuteIndexCb(indexReg);

            default:
                // If opcode does not use index registers, fall back to standard opcode
                return ExecuteOpcode(opcode);
        }
    }

    private int ExecuteIndexCb(ushort indexReg)
    {
        sbyte offset = (sbyte)FetchByte();
        ushort ea = (ushort)(indexReg + offset);
        Reg.WZ = ea;
        byte opcode = FetchByte();
        Reg.R = (byte)(((Reg.R + 1) & 0x7F) | (Reg.R & 0x80));

        int bit = (opcode >> 3) & 0x07;
        int regDest = opcode & 0x07;
        byte val = ReadMemory(ea);

        if (opcode < 0x40) // Shifts/Rotates on (IX/IY+d)
        {
            byte res = (opcode >> 3) switch
            {
                0 => Rlc(val),
                1 => Rrc(val),
                2 => Rl(val),
                3 => Rr(val),
                4 => Sla(val),
                5 => Sra(val),
                6 => Sll(val),
                7 => Srl(val),
                _ => val
            };
            WriteMemory(ea, res);
            if (regDest != 6) SetReg8(regDest, res);
            return 23;
        }
        if (opcode < 0x80) // BIT b, (IX/IY+d)
        {
            Bit(bit, val, true);
            return 20;
        }
        if (opcode < 0xC0) // RES b, (IX/IY+d)
        {
            val = (byte)(val & ~(1 << bit));
            WriteMemory(ea, val);
            if (regDest != 6) SetReg8(regDest, val);
            return 23;
        }
        // SET b, (IX/IY+d)
        val = (byte)(val | (1 << bit));
        WriteMemory(ea, val);
        if (regDest != 6) SetReg8(regDest, val);
        return 23;
    }

    private ushort EffectiveAddress(ushort baseAddr)
    {
        sbyte offset = (sbyte)FetchByte();
        ushort ea = (ushort)(baseAddr + offset);
        Reg.WZ = ea;
        return ea;
    }

    private static byte GetIndexH(ushort index) => (byte)(index >> 8);
    private static byte GetIndexL(ushort index) => (byte)index;
    private static void SetIndexH(ref ushort index, byte val) => index = (ushort)((val << 8) | (index & 0x00FF));
    private static void SetIndexL(ref ushort index, byte val) => index = (ushort)((index & 0xFF00) | val);

    #endregion

    #region ALU Helper Methods

    private byte Inc8(byte val)
    {
        byte res = (byte)(val + 1);
        byte flags = (byte)((Reg.F & Z80Registers.FlagC) | SzTable[res]);
        if ((val & 0x0F) == 0x0F) flags |= Z80Registers.FlagH;
        if (val == 0x7F) flags |= Z80Registers.FlagPV;
        Reg.F = flags;
        return res;
    }

    private byte Dec8(byte val)
    {
        byte res = (byte)(val - 1);
        byte flags = (byte)((Reg.F & Z80Registers.FlagC) | SzTable[res] | Z80Registers.FlagN);
        if ((val & 0x0F) == 0) flags |= Z80Registers.FlagH;
        if (val == 0x80) flags |= Z80Registers.FlagPV;
        Reg.F = flags;
        return res;
    }

    private void Add8(byte val)
    {
        int a = Reg.A;
        int res = a + val;
        byte result8 = (byte)res;
        byte flags = SzTable[result8];
        if (((a ^ val ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        if ((res & 0x100) != 0) flags |= Z80Registers.FlagC;
        if (((a ^ ~val) & (a ^ res) & 0x80) != 0) flags |= Z80Registers.FlagPV;
        Reg.A = result8;
        Reg.F = flags;
    }

    private void Adc8(byte val)
    {
        int a = Reg.A;
        int c = Reg.IsCarry ? 1 : 0;
        int res = a + val + c;
        byte result8 = (byte)res;
        byte flags = SzTable[result8];
        if (((a ^ val ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        if ((res & 0x100) != 0) flags |= Z80Registers.FlagC;
        if (((a ^ ~val) & (a ^ res) & 0x80) != 0) flags |= Z80Registers.FlagPV;
        Reg.A = result8;
        Reg.F = flags;
    }

    private void Sub8(byte val)
    {
        int a = Reg.A;
        int res = a - val;
        byte result8 = (byte)res;
        byte flags = (byte)(SzTable[result8] | Z80Registers.FlagN);
        if (((a ^ val ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        if ((res & 0x100) != 0) flags |= Z80Registers.FlagC;
        if (((a ^ val) & (a ^ res) & 0x80) != 0) flags |= Z80Registers.FlagPV;
        Reg.A = result8;
        Reg.F = flags;
    }

    private void Sbc8(byte val)
    {
        int a = Reg.A;
        int c = Reg.IsCarry ? 1 : 0;
        int res = a - val - c;
        byte result8 = (byte)res;
        byte flags = (byte)(SzTable[result8] | Z80Registers.FlagN);
        if (((a ^ val ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        if ((res & 0x100) != 0) flags |= Z80Registers.FlagC;
        if (((a ^ val) & (a ^ res) & 0x80) != 0) flags |= Z80Registers.FlagPV;
        Reg.A = result8;
        Reg.F = flags;
    }

    private void And8(byte val)
    {
        Reg.A &= val;
        Reg.F = (byte)(SzpTable[Reg.A] | Z80Registers.FlagH);
    }

    private void Xor8(byte val)
    {
        Reg.A ^= val;
        Reg.F = SzpTable[Reg.A];
    }

    private void Or8(byte val)
    {
        Reg.A |= val;
        Reg.F = SzpTable[Reg.A];
    }

    private void Cp8(byte val)
    {
        int a = Reg.A;
        int res = a - val;
        byte flags = (byte)(SzTable[(byte)res] | Z80Registers.FlagN);
        if (((a ^ val ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        if ((res & 0x100) != 0) flags |= Z80Registers.FlagC;
        if (((a ^ val) & (a ^ res) & 0x80) != 0) flags |= Z80Registers.FlagPV;
        // Undocumented: X and Y flags come from the operand!
        flags = (byte)((flags & ~(Z80Registers.FlagX | Z80Registers.FlagY)) | (val & (Z80Registers.FlagX | Z80Registers.FlagY)));
        Reg.F = flags;
    }

    private ushort Add16(ushort dest, ushort src)
    {
        int res = dest + src;
        Reg.WZ = (ushort)(dest + 1);
        byte flags = (byte)(Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV));
        if (((dest ^ src ^ res) & 0x1000) != 0) flags |= Z80Registers.FlagH;
        if ((res & 0x10000) != 0) flags |= Z80Registers.FlagC;
        flags |= (byte)(((res >> 8) & (Z80Registers.FlagX | Z80Registers.FlagY)));
        Reg.F = flags;
        return (ushort)res;
    }

    private void Adc16(ushort val)
    {
        int hl = Reg.HL;
        int c = Reg.IsCarry ? 1 : 0;
        int res = hl + val + c;
        Reg.WZ = (ushort)(hl + 1);
        byte flags = 0;
        if ((res & 0x8000) != 0) flags |= Z80Registers.FlagS;
        if ((res & 0xFFFF) == 0) flags |= Z80Registers.FlagZ;
        if (((hl ^ val ^ res) & 0x1000) != 0) flags |= Z80Registers.FlagH;
        if (((hl ^ ~val) & (hl ^ res) & 0x8000) != 0) flags |= Z80Registers.FlagPV;
        if ((res & 0x10000) != 0) flags |= Z80Registers.FlagC;
        flags |= (byte)(((res >> 8) & (Z80Registers.FlagX | Z80Registers.FlagY)));
        Reg.HL = (ushort)res;
        Reg.F = flags;
    }

    private void Sbc16(ushort val)
    {
        int hl = Reg.HL;
        int c = Reg.IsCarry ? 1 : 0;
        int res = hl - val - c;
        Reg.WZ = (ushort)(hl + 1);
        byte flags = Z80Registers.FlagN;
        if ((res & 0x8000) != 0) flags |= Z80Registers.FlagS;
        if ((res & 0xFFFF) == 0) flags |= Z80Registers.FlagZ;
        if (((hl ^ val ^ res) & 0x1000) != 0) flags |= Z80Registers.FlagH;
        if (((hl ^ val) & (hl ^ res) & 0x8000) != 0) flags |= Z80Registers.FlagPV;
        if ((res & 0x10000) != 0) flags |= Z80Registers.FlagC;
        flags |= (byte)(((res >> 8) & (Z80Registers.FlagX | Z80Registers.FlagY)));
        Reg.HL = (ushort)res;
        Reg.F = flags;
    }

    private void Daa()
    {
        int a = Reg.A;
        int correction = 0;
        bool carry = Reg.IsCarry;
        bool halfCarry = Reg.IsHalfCarry;

        if (halfCarry || ((a & 0x0F) > 9)) correction |= 0x06;
        if (carry || (a > 0x99)) { correction |= 0x60; carry = true; }

        if (Reg.IsSubtract) a -= correction;
        else a += correction;

        byte res = (byte)a;
        byte flags = SzpTable[res];
        if (carry) flags |= Z80Registers.FlagC;
        if (Reg.IsSubtract) flags |= Z80Registers.FlagN;
        if (Reg.IsSubtract ? (halfCarry && ((Reg.A & 0x0F) < 6)) : ((Reg.A & 0x0F) > 9)) flags |= Z80Registers.FlagH;

        Reg.A = res;
        Reg.F = flags;
    }

    private void Neg()
    {
        byte val = Reg.A;
        Reg.A = 0;
        Sub8(val);
    }

    #endregion

    #region Bit and Shift Methods

    private void Rlca()
    {
        byte b = Reg.A;
        bool c = (b & 0x80) != 0;
        Reg.A = (byte)((b << 1) | (c ? 1 : 0));
        Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV))
                      | (c ? Z80Registers.FlagC : 0)
                      | (Reg.A & (Z80Registers.FlagX | Z80Registers.FlagY)));
    }

    private void Rrca()
    {
        byte b = Reg.A;
        bool c = (b & 0x01) != 0;
        Reg.A = (byte)((b >> 1) | (c ? 0x80 : 0));
        Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV))
                      | (c ? Z80Registers.FlagC : 0)
                      | (Reg.A & (Z80Registers.FlagX | Z80Registers.FlagY)));
    }

    private void Rla()
    {
        byte b = Reg.A;
        bool oldCarry = Reg.IsCarry;
        bool newCarry = (b & 0x80) != 0;
        Reg.A = (byte)((b << 1) | (oldCarry ? 1 : 0));
        Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV))
                      | (newCarry ? Z80Registers.FlagC : 0)
                      | (Reg.A & (Z80Registers.FlagX | Z80Registers.FlagY)));
    }

    private void Rra()
    {
        byte b = Reg.A;
        bool oldCarry = Reg.IsCarry;
        bool newCarry = (b & 0x01) != 0;
        Reg.A = (byte)((b >> 1) | (oldCarry ? 0x80 : 0));
        Reg.F = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagPV))
                      | (newCarry ? Z80Registers.FlagC : 0)
                      | (Reg.A & (Z80Registers.FlagX | Z80Registers.FlagY)));
    }

    private byte Rlc(byte b)
    {
        bool c = (b & 0x80) != 0;
        byte res = (byte)((b << 1) | (c ? 1 : 0));
        Reg.F = (byte)(SzpTable[res] | (c ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Rrc(byte b)
    {
        bool c = (b & 0x01) != 0;
        byte res = (byte)((b >> 1) | (c ? 0x80 : 0));
        Reg.F = (byte)(SzpTable[res] | (c ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Rl(byte b)
    {
        bool oldCarry = Reg.IsCarry;
        bool newCarry = (b & 0x80) != 0;
        byte res = (byte)((b << 1) | (oldCarry ? 1 : 0));
        Reg.F = (byte)(SzpTable[res] | (newCarry ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Rr(byte b)
    {
        bool oldCarry = Reg.IsCarry;
        bool newCarry = (b & 0x01) != 0;
        byte res = (byte)((b >> 1) | (oldCarry ? 0x80 : 0));
        Reg.F = (byte)(SzpTable[res] | (newCarry ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Sla(byte b)
    {
        bool c = (b & 0x80) != 0;
        byte res = (byte)(b << 1);
        Reg.F = (byte)(SzpTable[res] | (c ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Sra(byte b)
    {
        bool c = (b & 0x01) != 0;
        byte res = (byte)((b >> 1) | (b & 0x80));
        Reg.F = (byte)(SzpTable[res] | (c ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Sll(byte b)
    {
        bool c = (b & 0x80) != 0;
        byte res = (byte)((b << 1) | 0x01); // Bit 0 is set
        Reg.F = (byte)(SzpTable[res] | (c ? Z80Registers.FlagC : 0));
        return res;
    }

    private byte Srl(byte b)
    {
        bool c = (b & 0x01) != 0;
        byte res = (byte)(b >> 1);
        Reg.F = (byte)(SzpTable[res] | (c ? Z80Registers.FlagC : 0));
        return res;
    }

    private void Bit(int bit, byte val, bool isIndexed)
    {
        bool isZero = (val & (1 << bit)) == 0;
        byte flags = (byte)((Reg.F & Z80Registers.FlagC) | Z80Registers.FlagH);
        if (isZero) flags |= (byte)(Z80Registers.FlagZ | Z80Registers.FlagPV);
        if (bit == 7 && !isZero) flags |= Z80Registers.FlagS;

        if (isIndexed)
        {
            // For BIT b, (IX+d), XF and YF come from high byte of WZ (effective address)
            flags |= (byte)((Reg.WZ >> 8) & (Z80Registers.FlagX | Z80Registers.FlagY));
        }
        else
        {
            flags |= (byte)(val & (Z80Registers.FlagX | Z80Registers.FlagY));
        }
        Reg.F = flags;
    }

    private void Rrd()
    {
        byte hlVal = ReadMemory(Reg.HL);
        byte aLow = (byte)(Reg.A & 0x0F);
        Reg.A = (byte)((Reg.A & 0xF0) | (hlVal & 0x0F));
        byte newHl = (byte)((aLow << 4) | (hlVal >> 4));
        WriteMemory(Reg.HL, newHl);
        Reg.WZ = (ushort)(Reg.HL + 1);
        Reg.F = (byte)((Reg.F & Z80Registers.FlagC) | SzpTable[Reg.A]);
    }

    private void Rld()
    {
        byte hlVal = ReadMemory(Reg.HL);
        byte aLow = (byte)(Reg.A & 0x0F);
        Reg.A = (byte)((Reg.A & 0xF0) | (hlVal >> 4));
        byte newHl = (byte)((hlVal << 4) | aLow);
        WriteMemory(Reg.HL, newHl);
        Reg.WZ = (ushort)(Reg.HL + 1);
        Reg.F = (byte)((Reg.F & Z80Registers.FlagC) | SzpTable[Reg.A]);
    }

    #endregion

    #region Block Operations

    private int Ldi()
    {
        byte b = ReadMemory(Reg.HL);
        WriteMemory(Reg.DE, b);
        Reg.HL++;
        Reg.DE++;
        Reg.BC--;
        byte n = (byte)(Reg.A + b);
        byte flags = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagC))
                           | (Reg.BC != 0 ? Z80Registers.FlagPV : 0)
                           | ((n & 0x02) != 0 ? Z80Registers.FlagY : 0)
                           | ((n & 0x08) != 0 ? Z80Registers.FlagX : 0));
        Reg.F = flags;
        return 16;
    }

    private int Ldir()
    {
        Ldi();
        if (Reg.BC != 0)
        {
            Reg.PC -= 2;
            Reg.WZ = (ushort)(Reg.PC + 1);
            return 21;
        }
        return 16;
    }

    private int Ldd()
    {
        byte b = ReadMemory(Reg.HL);
        WriteMemory(Reg.DE, b);
        Reg.HL--;
        Reg.DE--;
        Reg.BC--;
        byte n = (byte)(Reg.A + b);
        byte flags = (byte)((Reg.F & (Z80Registers.FlagS | Z80Registers.FlagZ | Z80Registers.FlagC))
                           | (Reg.BC != 0 ? Z80Registers.FlagPV : 0)
                           | ((n & 0x02) != 0 ? Z80Registers.FlagY : 0)
                           | ((n & 0x08) != 0 ? Z80Registers.FlagX : 0));
        Reg.F = flags;
        return 16;
    }

    private int Lddr()
    {
        Ldd();
        if (Reg.BC != 0)
        {
            Reg.PC -= 2;
            Reg.WZ = (ushort)(Reg.PC + 1);
            return 21;
        }
        return 16;
    }

    private int Cpi()
    {
        byte b = ReadMemory(Reg.HL);
        int res = Reg.A - b;
        Reg.HL++;
        Reg.BC--;
        byte flags = (byte)((Reg.F & Z80Registers.FlagC) | Z80Registers.FlagN | (Reg.BC != 0 ? Z80Registers.FlagPV : 0));
        if ((res & 0xFF) == 0) flags |= Z80Registers.FlagZ;
        if ((res & 0x80) != 0) flags |= Z80Registers.FlagS;
        if (((Reg.A ^ b ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        int n = res - (flags & Z80Registers.FlagH);
        if ((n & 0x02) != 0) flags |= Z80Registers.FlagY;
        if ((n & 0x08) != 0) flags |= Z80Registers.FlagX;
        Reg.F = flags;
        Reg.WZ = (ushort)(Reg.WZ + 1);
        return 16;
    }

    private int Cpir()
    {
        Cpi();
        if (Reg.BC != 0 && !Reg.IsZero)
        {
            Reg.PC -= 2;
            Reg.WZ = (ushort)(Reg.PC + 1);
            return 21;
        }
        return 16;
    }

    private int Cpd()
    {
        byte b = ReadMemory(Reg.HL);
        int res = Reg.A - b;
        Reg.HL--;
        Reg.BC--;
        byte flags = (byte)((Reg.F & Z80Registers.FlagC) | Z80Registers.FlagN | (Reg.BC != 0 ? Z80Registers.FlagPV : 0));
        if ((res & 0xFF) == 0) flags |= Z80Registers.FlagZ;
        if ((res & 0x80) != 0) flags |= Z80Registers.FlagS;
        if (((Reg.A ^ b ^ res) & 0x10) != 0) flags |= Z80Registers.FlagH;
        int n = res - (flags & Z80Registers.FlagH);
        if ((n & 0x02) != 0) flags |= Z80Registers.FlagY;
        if ((n & 0x08) != 0) flags |= Z80Registers.FlagX;
        Reg.F = flags;
        Reg.WZ = (ushort)(Reg.WZ - 1);
        return 16;
    }

    private int Cpdr()
    {
        Cpd();
        if (Reg.BC != 0 && !Reg.IsZero)
        {
            Reg.PC -= 2;
            Reg.WZ = (ushort)(Reg.PC + 1);
            return 21;
        }
        return 16;
    }

    private int Ini()
    {
        byte val = ReadPort(Reg.BC);
        WriteMemory(Reg.HL, val);
        Reg.HL++;
        Reg.B--;
        Reg.F = (byte)(SzpTable[Reg.B] | Z80Registers.FlagN);
        Reg.WZ = (ushort)(Reg.BC + 1);
        return 16;
    }

    private int Inir()
    {
        Ini();
        if (Reg.B != 0)
        {
            Reg.PC -= 2;
            return 21;
        }
        return 16;
    }

    private int Ind()
    {
        byte val = ReadPort(Reg.BC);
        WriteMemory(Reg.HL, val);
        Reg.HL--;
        Reg.B--;
        Reg.F = (byte)(SzpTable[Reg.B] | Z80Registers.FlagN);
        Reg.WZ = (ushort)(Reg.BC - 1);
        return 16;
    }

    private int Indr()
    {
        Ind();
        if (Reg.B != 0)
        {
            Reg.PC -= 2;
            return 21;
        }
        return 16;
    }

    private int Outi()
    {
        byte val = ReadMemory(Reg.HL);
        Reg.B--;
        WritePort(Reg.BC, val);
        Reg.HL++;
        Reg.F = (byte)(SzpTable[Reg.B] | Z80Registers.FlagN);
        Reg.WZ = (ushort)(Reg.BC + 1);
        return 16;
    }

    private int Otir()
    {
        Outi();
        if (Reg.B != 0)
        {
            Reg.PC -= 2;
            return 21;
        }
        return 16;
    }

    private int Outd()
    {
        byte val = ReadMemory(Reg.HL);
        Reg.B--;
        WritePort(Reg.BC, val);
        Reg.HL--;
        Reg.F = (byte)(SzpTable[Reg.B] | Z80Registers.FlagN);
        Reg.WZ = (ushort)(Reg.BC - 1);
        return 16;
    }

    private int Otdr()
    {
        Outd();
        if (Reg.B != 0)
        {
            Reg.PC -= 2;
            return 21;
        }
        return 16;
    }

    private byte InC()
    {
        byte val = ReadPort(Reg.BC);
        Reg.F = (byte)((Reg.F & Z80Registers.FlagC) | SzpTable[val]);
        Reg.WZ = (ushort)(Reg.BC + 1);
        return val;
    }

    #endregion

    #region Register Helpers

    private byte GetReg8(int index) => index switch
    {
        0 => Reg.B,
        1 => Reg.C,
        2 => Reg.D,
        3 => Reg.E,
        4 => Reg.H,
        5 => Reg.L,
        6 => ReadMemory(Reg.HL),
        7 => Reg.A,
        _ => 0
    };

    private void SetReg8(int index, byte val)
    {
        switch (index)
        {
            case 0: Reg.B = val; break;
            case 1: Reg.C = val; break;
            case 2: Reg.D = val; break;
            case 3: Reg.E = val; break;
            case 4: Reg.H = val; break;
            case 5: Reg.L = val; break;
            case 6: WriteMemory(Reg.HL, val); break;
            case 7: Reg.A = val; break;
        }
    }

    #endregion
}
