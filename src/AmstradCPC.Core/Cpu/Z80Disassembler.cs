namespace AmstradCPC.Core.Cpu;

/// <summary>
/// Zilog Z80 instruction disassembler.
/// Translates raw bytes into standard Z80 assembly mnemonics.
/// </summary>
public static class Z80Disassembler
{
    private static readonly string[] Reg8 = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };
    private static readonly string[] Reg16 = { "BC", "DE", "HL", "SP" };
    private static readonly string[] Reg16Push = { "BC", "DE", "HL", "AF" };
    private static readonly string[] Conditions = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
    private static readonly string[] AluOps = { "ADD A,", "ADC A,", "SUB", "SBC A,", "AND", "XOR", "OR", "CP" };
    private static readonly string[] RotOps = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };

    public static (string Mnemonic, int Length) Disassemble(Func<ushort, byte> readMem, ushort pc)
    {
        byte op = readMem(pc);

        // Standard opcodes
        switch (op)
        {
            case 0x00: return ("NOP", 1);
            case 0x01: return ($"LD BC, ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
            case 0x02: return ("LD (BC), A", 1);
            case 0x03: return ("INC BC", 1);
            case 0x04: return ("INC B", 1);
            case 0x05: return ("DEC B", 1);
            case 0x06: return ($"LD B, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x07: return ("RLCA", 1);
            case 0x08: return ("EX AF, AF'", 1);
            case 0x09: return ("ADD HL, BC", 1);
            case 0x0A: return ("LD A, (BC)", 1);
            case 0x0B: return ("DEC BC", 1);
            case 0x0C: return ("INC C", 1);
            case 0x0D: return ("DEC C", 1);
            case 0x0E: return ($"LD C, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x0F: return ("RRCA", 1);

            case 0x10: return ($"DJNZ ${pc + 2 + (sbyte)readMem((ushort)(pc + 1)):X4}", 2);
            case 0x11: return ($"LD DE, ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
            case 0x12: return ("LD (DE), A", 1);
            case 0x13: return ("INC DE", 1);
            case 0x14: return ("INC D", 1);
            case 0x15: return ("DEC D", 1);
            case 0x16: return ($"LD D, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x17: return ("RLA", 1);
            case 0x18: return ($"JR ${pc + 2 + (sbyte)readMem((ushort)(pc + 1)):X4}", 2);
            case 0x19: return ("ADD HL, DE", 1);
            case 0x1A: return ("LD A, (DE)", 1);
            case 0x1B: return ("DEC DE", 1);
            case 0x1C: return ("INC E", 1);
            case 0x1D: return ("DEC E", 1);
            case 0x1E: return ($"LD E, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x1F: return ("RRA", 1);

            case 0x20: return ($"JR NZ, ${pc + 2 + (sbyte)readMem((ushort)(pc + 1)):X4}", 2);
            case 0x21: return ($"LD HL, ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
            case 0x22: return ($"LD (${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}), HL", 3);
            case 0x23: return ("INC HL", 1);
            case 0x24: return ("INC H", 1);
            case 0x25: return ("DEC H", 1);
            case 0x26: return ($"LD H, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x27: return ("DAA", 1);
            case 0x28: return ($"JR Z, ${pc + 2 + (sbyte)readMem((ushort)(pc + 1)):X4}", 2);
            case 0x29: return ("ADD HL, HL", 1);
            case 0x2A: return ($"LD HL, (${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2})", 3);
            case 0x2B: return ("DEC HL", 1);
            case 0x2C: return ("INC L", 1);
            case 0x2D: return ("DEC L", 1);
            case 0x2E: return ($"LD L, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x2F: return ("CPL", 1);

            case 0x30: return ($"JR NC, ${pc + 2 + (sbyte)readMem((ushort)(pc + 1)):X4}", 2);
            case 0x31: return ($"LD SP, ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
            case 0x32: return ($"LD (${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}), A", 3);
            case 0x33: return ("INC SP", 1);
            case 0x34: return ("INC (HL)", 1);
            case 0x35: return ("DEC (HL)", 1);
            case 0x36: return ($"LD (HL), ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x37: return ("SCF", 1);
            case 0x38: return ($"JR C, ${pc + 2 + (sbyte)readMem((ushort)(pc + 1)):X4}", 2);
            case 0x39: return ("ADD HL, SP", 1);
            case 0x3A: return ($"LD A, (${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2})", 3);
            case 0x3B: return ("DEC SP", 1);
            case 0x3C: return ("INC A", 1);
            case 0x3D: return ("DEC A", 1);
            case 0x3E: return ($"LD A, ${readMem((ushort)(pc + 1)):X2}", 2);
            case 0x3F: return ("CCF", 1);

            case 0x76: return ("HALT", 1);

            case 0xC3: return ($"JP ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
            case 0xCD: return ($"CALL ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
            case 0xC9: return ("RET", 1);
            case 0xD3: return ($"OUT (${readMem((ushort)(pc + 1)):X2}), A", 2);
            case 0xDB: return ($"IN A, (${readMem((ushort)(pc + 1)):X2})", 2);
            case 0xEB: return ("EX DE, HL", 1);
            case 0xE3: return ("EX (SP), HL", 1);
            case 0xE9: return ("JP (HL)", 1);
            case 0xF9: return ("LD SP, HL", 1);
            case 0xF3: return ("DI", 1);
            case 0xFB: return ("EI", 1);

            case 0xCB:
                {
                    byte cbOp = readMem((ushort)(pc + 1));
                    int reg = cbOp & 7;
                    int bit = (cbOp >> 3) & 7;
                    if (cbOp < 0x40) return ($"{RotOps[(cbOp >> 3) & 7]} {Reg8[reg]}", 2);
                    if (cbOp < 0x80) return ($"BIT {bit}, {Reg8[reg]}", 2);
                    if (cbOp < 0xC0) return ($"RES {bit}, {Reg8[reg]}", 2);
                    return ($"SET {bit}, {Reg8[reg]}", 2);
                }

            case 0xED:
                {
                    byte edOp = readMem((ushort)(pc + 1));
                    return edOp switch
                    {
                        0x47 => ("LD I, A", 2),
                        0x4F => ("LD R, A", 2),
                        0x57 => ("LD A, I", 2),
                        0x5F => ("LD A, R", 2),
                        0x45 => ("RETN", 2),
                        0x4D => ("RETI", 2),
                        0x46 => ("IM 0", 2),
                        0x56 => ("IM 1", 2),
                        0x5E => ("IM 2", 2),
                        0x44 => ("NEG", 2),
                        0x67 => ("RRD", 2),
                        0x6F => ("RLD", 2),
                        0xA0 => ("LDI", 2),
                        0xB0 => ("LDIR", 2),
                        0xA8 => ("LDD", 2),
                        0xB8 => ("LDDR", 2),
                        0xA1 => ("CPI", 2),
                        0xB1 => ("CPIR", 2),
                        0xA9 => ("CPD", 2),
                        0xB9 => ("CPDR", 2),
                        0xA2 => ("INI", 2),
                        0xB2 => ("INIR", 2),
                        0xAA => ("IND", 2),
                        0xBA => ("INDR", 2),
                        0xA3 => ("OUTI", 2),
                        0xB3 => ("OTIR", 2),
                        0xAB => ("OUTD", 2),
                        0xBB => ("OTDR", 2),
                        _ => ($"ED {edOp:X2}", 2)
                    };
                }

            case 0xDD:
            case 0xFD:
                {
                    string idx = (op == 0xDD) ? "IX" : "IY";
                    byte subOp = readMem((ushort)(pc + 1));
                    sbyte off = (sbyte)readMem((ushort)(pc + 2));
                    string offStr = off >= 0 ? $"+{off}" : $"{off}";
                    if (subOp == 0x21) return ($"LD {idx}, ${readMem((ushort)(pc + 3)):X2}{readMem((ushort)(pc + 2)):X2}", 4);
                    if (subOp == 0x7E) return ($"LD A, ({idx}{offStr})", 3);
                    if (subOp == 0x77) return ($"LD ({idx}{offStr}), A", 3);
                    if (subOp == 0xE9) return ($"JP ({idx})", 2);
                    return ($"{idx} {subOp:X2}", 2);
                }

            default:
                if (op >= 0x40 && op < 0x80)
                {
                    int dst = (op >> 3) & 7;
                    int src = op & 7;
                    return ($"LD {Reg8[dst]}, {Reg8[src]}", 1);
                }
                if (op >= 0x80 && op < 0xC0)
                {
                    int alu = (op >> 3) & 7;
                    int src = op & 7;
                    return ($"{AluOps[alu]} {Reg8[src]}", 1);
                }
                if ((op & 0xC7) == 0xC2)
                {
                    int cc = (op >> 3) & 7;
                    return ($"JP {Conditions[cc]}, ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
                }
                if ((op & 0xC7) == 0xC4)
                {
                    int cc = (op >> 3) & 7;
                    return ($"CALL {Conditions[cc]}, ${readMem((ushort)(pc + 2)):X2}{readMem((ushort)(pc + 1)):X2}", 3);
                }
                if ((op & 0xC7) == 0xC0)
                {
                    int cc = (op >> 3) & 7;
                    return ($"RET {Conditions[cc]}", 1);
                }
                if ((op & 0xCF) == 0xC1)
                {
                    int rp = (op >> 4) & 3;
                    return ($"POP {Reg16Push[rp]}", 1);
                }
                if ((op & 0xCF) == 0xC5)
                {
                    int rp = (op >> 4) & 3;
                    return ($"PUSH {Reg16Push[rp]}", 1);
                }
                if ((op & 0xC7) == 0xC7)
                {
                    int rst = op & 0x38;
                    return ($"RST ${rst:X2}", 1);
                }
                return ($"DB ${op:X2}", 1);
        }
    }
}
