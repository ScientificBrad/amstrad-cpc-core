namespace AmstradCPC.Core.Cpu;

/// <summary>
/// Z80 Execution tracer and ring-buffer history recorder.
/// Enables deterministic debugging, crash investigation, and firmware verification.
/// </summary>
public sealed class Z80Trace
{
    private readonly string[] _history;
    private int _head;
    private int _count;

    public Z80Trace(int capacity = 1024)
    {
        _history = new string[capacity];
    }

    public void Record(CpuZ80 cpu)
    {
        ushort pc = cpu.Reg.PC;
        var (mnemonic, len) = Z80Disassembler.Disassemble(cpu.ReadMemory, pc);

        string bytes = "";
        for (int i = 0; i < Math.Min(len, 4); i++)
        {
            bytes += $"{cpu.ReadMemory((ushort)(pc + i)):X2} ";
        }
        bytes = bytes.PadRight(12);

        string entry = $"[{cpu.TotalCycles,10}] {pc:X4}: {bytes} {mnemonic,-18} | AF:{cpu.Reg.AF:X4} BC:{cpu.Reg.BC:X4} DE:{cpu.Reg.DE:X4} HL:{cpu.Reg.HL:X4} IX:{cpu.Reg.IX:X4} IY:{cpu.Reg.IY:X4} SP:{cpu.Reg.SP:X4} F:{(cpu.Reg.IsSign ? "S" : "-")}{(cpu.Reg.IsZero ? "Z" : "-")}{(cpu.Reg.IsHalfCarry ? "H" : "-")}{(cpu.Reg.IsParityOverflow ? "P" : "-")}{(cpu.Reg.IsSubtract ? "N" : "-")}{(cpu.Reg.IsCarry ? "C" : "-")}";

        _history[_head] = entry;
        _head = (_head + 1) % _history.Length;
        if (_count < _history.Length) _count++;
    }

    public string[] GetRecentEntries(int maxEntries = 50)
    {
        int fetch = Math.Min(maxEntries, _count);
        var result = new string[fetch];
        int start = (_head - fetch + _history.Length) % _history.Length;
        for (int i = 0; i < fetch; i++)
        {
            result[i] = _history[(start + i) % _history.Length];
        }
        return result;
    }
}
