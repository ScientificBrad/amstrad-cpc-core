using AmstradCPC.Core.Cpu;
using Xunit;

namespace AmstradCPC.Tests;

public class CpuZ80Tests
{
    private readonly CpuZ80 _cpu;
    private readonly byte[] _mem = new byte[65536];

    public CpuZ80Tests()
    {
        _cpu = new CpuZ80
        {
            ReadMemory = addr => _mem[addr],
            WriteMemory = (addr, val) => _mem[addr] = val,
            CpcWaitStateMode = true
        };
    }

    [Fact]
    public void Nop_Executes_AndTakesFourCycles()
    {
        _mem[0] = 0x00; // NOP
        int cycles = _cpu.Step();

        Assert.Equal(1, _cpu.Reg.PC);
        Assert.Equal(4, cycles);
        Assert.Equal(4, _cpu.TotalCycles);
    }

    [Fact]
    public void LoadImmediate16_SetsRegisterPair()
    {
        _mem[0] = 0x01; // LD BC, $1234
        _mem[1] = 0x34;
        _mem[2] = 0x12;

        int cycles = _cpu.Step();

        Assert.Equal(0x1234, _cpu.Reg.BC);
        Assert.Equal(0x12, _cpu.Reg.B);
        Assert.Equal(0x34, _cpu.Reg.C);
        Assert.Equal(3, _cpu.Reg.PC);
        Assert.Equal(12, cycles); // 10 cycles rounded up to 12 on CPC
    }

    [Fact]
    public void Add8_CalculatesCorrectSumAndFlags()
    {
        _cpu.Reg.A = 0x0F;
        _mem[0] = 0xC6; // ADD A, $01
        _mem[1] = 0x01;

        _cpu.Step();

        Assert.Equal(0x10, _cpu.Reg.A);
        Assert.False(_cpu.Reg.IsCarry);
        Assert.False(_cpu.Reg.IsZero);
        Assert.True(_cpu.Reg.IsHalfCarry); // Half-carry from bit 3 to 4
    }

    [Fact]
    public void Sub8_UnderflowSetsCarry()
    {
        _cpu.Reg.A = 0x00;
        _mem[0] = 0xD6; // SUB $01
        _mem[1] = 0x01;

        _cpu.Step();

        Assert.Equal(0xFF, _cpu.Reg.A);
        Assert.True(_cpu.Reg.IsCarry);
        Assert.True(_cpu.Reg.IsSubtract);
        Assert.True(_cpu.Reg.IsSign);
    }

    [Fact]
    public void Daa_AdjustsBcdAfterAddition()
    {
        // 0x15 + 0x27 in BCD = 0x42
        _cpu.Reg.A = 0x15;
        _mem[0] = 0xC6; // ADD A, $27
        _mem[1] = 0x27;
        _mem[2] = 0x27; // DAA

        _cpu.Step();
        _cpu.Step();

        Assert.Equal(0x42, _cpu.Reg.A);
    }

    [Fact]
    public void RelativeJump_JumpsCorrectly()
    {
        _mem[0] = 0x18; // JR +5
        _mem[1] = 0x05;

        int cycles = _cpu.Step();

        Assert.Equal(7, _cpu.Reg.PC); // PC = 0 + 2 + 5 = 7
        Assert.Equal(12, cycles);
    }

    [Fact]
    public void DdPrefix_LoadsIndexedMemory()
    {
        _cpu.Reg.IX = 0x2000;
        _mem[0x2004] = 0x77;
        _mem[0] = 0xDD; // LD A, (IX + 4)
        _mem[1] = 0x7E;
        _mem[2] = 0x04;

        int cycles = _cpu.Step();

        Assert.Equal(0x77, _cpu.Reg.A);
        Assert.Equal(20, cycles); // 19 stretched to 20
    }

    [Fact]
    public void CbPrefix_BitInstructionTestsZeroBit()
    {
        _cpu.Reg.A = 0b00001000;
        _mem[0] = 0xCB; // BIT 3, A
        _mem[1] = 0x5F;

        _cpu.Step();

        Assert.False(_cpu.Reg.IsZero); // Bit 3 is 1 -> Zero flag is 0
        Assert.True(_cpu.Reg.IsHalfCarry);

        _mem[2] = 0xCB; // BIT 2, A
        _mem[3] = 0x57;

        _cpu.Step();

        Assert.True(_cpu.Reg.IsZero); // Bit 2 is 0 -> Zero flag is 1
    }

    [Fact]
    public void CallAndRet_ExecutesSubroutineViaStack()
    {
        _cpu.Reg.SP = 0x8000;
        _mem[0x0100] = 0xCD; // CALL $0500
        _mem[0x0101] = 0x00;
        _mem[0x0102] = 0x05;
        _mem[0x0500] = 0xC9; // RET

        _cpu.Reg.PC = 0x0100;

        int callCycles = _cpu.Step();
        Assert.Equal(0x0500, _cpu.Reg.PC);
        Assert.Equal(0x7FFE, _cpu.Reg.SP);
        Assert.Equal(20, callCycles); // 17 stretched to 20

        int retCycles = _cpu.Step();
        Assert.Equal(0x0103, _cpu.Reg.PC);
        Assert.Equal(0x8000, _cpu.Reg.SP);
        Assert.Equal(12, retCycles); // 10 stretched to 12
    }

    [Fact]
    public void InterruptMode1_JumpsToRst38()
    {
        _cpu.Reg.SP = 0x8000;
        _cpu.Reg.PC = 0x1000;
        _cpu.Reg.IM = 1;
        _cpu.Reg.IFF1 = true;
        _cpu.Reg.IFF2 = true;

        _cpu.RequestInterrupt();
        int cycles = _cpu.Step();

        Assert.Equal(0x0038, _cpu.Reg.PC);
        Assert.False(_cpu.Reg.IFF1);
        Assert.False(_cpu.Reg.IFF2);
        Assert.Equal(16, cycles); // 13 stretched to 16
    }
}
