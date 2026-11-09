using AmstradCPC.Core.Input;

namespace AmstradCPC.Core.Audio;

/// <summary>
/// General Instrument AY-3-8912 Programmable Sound Generator (PSG).
/// 3 square wave channels, 1 noise generator, 1 envelope generator,
/// and an 8-bit I/O port tied to the Amstrad keyboard matrix.
/// Clock: 1.000 MHz (16 MHz / 16).
/// </summary>
public sealed class Ay38912
{
    private readonly KeyboardMatrix _keyboard;

    public byte[] Registers { get; } = new byte[16];
    public byte SelectedRegister { get; set; } = 0;
    public int SelectedKeyboardRow { get; set; } = 0;

    // Tone channel counters and output states
    private readonly int[] _tonePeriods = new int[3];
    private readonly int[] _toneCounters = new int[3];
    private readonly bool[] _toneOutputs = new bool[3];

    // Noise generator
    private int _noisePeriod = 1;
    private int _noiseCounter = 0;
    private int _noiseLfsr = 1;
    private bool _noiseOutput = true;

    // Envelope generator
    private int _envPeriod = 1;
    private int _envCounter = 0;
    private int _envVolume = 0;
    private bool _envHolding = false;
    private bool _envAlternate = false;
    private bool _envAttack = false;

    // 16-level logarithmic DAC volume table
    private static readonly float[] DacTable = new float[16]
    {
        0.0000f, 0.0078f, 0.0110f, 0.0156f,
        0.0221f, 0.0313f, 0.0442f, 0.0625f,
        0.0884f, 0.1250f, 0.1768f, 0.2500f,
        0.3536f, 0.5000f, 0.7071f, 1.0000f
    };

    public Ay38912(KeyboardMatrix keyboard)
    {
        _keyboard = keyboard;
        Reset();
    }

    public void Reset()
    {
        SelectedRegister = 0;
        SelectedKeyboardRow = 0;
        Array.Clear(Registers, 0, Registers.Length);
        Registers[7] = 0xFF; // Mixer disabled

        for (int i = 0; i < 3; i++)
        {
            _tonePeriods[i] = 1;
            _toneCounters[i] = 0;
            _toneOutputs[i] = false;
        }

        _noisePeriod = 1;
        _noiseCounter = 0;
        _noiseLfsr = 1;
        _noiseOutput = true;

        _envPeriod = 1;
        _envCounter = 0;
        _envVolume = 0;
        _envHolding = false;
        _envAlternate = false;
        _envAttack = false;
    }

    public void SelectRegister(byte reg)
    {
        SelectedRegister = (byte)(reg & 0x0F);
    }

    public void WriteData(byte data)
    {
        if (SelectedRegister < 16)
        {
            Registers[SelectedRegister] = data;
            UpdatePeriods();
        }
    }

    public byte ReadData()
    {
        if (SelectedRegister == 14)
        {
            // Register 14 is AY Port A: returns the 8 keyboard matrix columns for the active row!
            return _keyboard.ReadRow(SelectedKeyboardRow);
        }

        if (SelectedRegister < 16)
        {
            return Registers[SelectedRegister];
        }

        return 0xFF;
    }

    private void UpdatePeriods()
    {
        _tonePeriods[0] = Math.Max(1, (Registers[0] | ((Registers[1] & 0x0F) << 8)));
        _tonePeriods[1] = Math.Max(1, (Registers[2] | ((Registers[3] & 0x0F) << 8)));
        _tonePeriods[2] = Math.Max(1, (Registers[4] | ((Registers[5] & 0x0F) << 8)));
        _noisePeriod = Math.Max(1, (Registers[6] & 0x1F));
        _envPeriod = Math.Max(1, (Registers[11] | (Registers[12] << 8)));

        if (SelectedRegister == 13)
        {
            // Reset envelope generator on write to shape register
            byte shape = (byte)(Registers[13] & 0x0F);
            _envAttack = (shape & 0x04) != 0;
            _envAlternate = (shape & 0x02) != 0;
            _envHolding = (shape & 0x01) != 0;
            _envVolume = _envAttack ? 0 : 15;
            _envCounter = 0;
        }
    }

    /// <summary>
    /// Clocked at 1.0 MHz (1 PSG cycle = 4 Z80 cycles = 1 µs).
    /// Returns the mixed floating-point audio sample (-1.0 to 1.0).
    /// </summary>
    public float Clock()
    {
        // Clock Tone Generators
        for (int i = 0; i < 3; i++)
        {
            _toneCounters[i]++;
            if (_toneCounters[i] >= _tonePeriods[i])
            {
                _toneCounters[i] = 0;
                _toneOutputs[i] = !_toneOutputs[i];
            }
        }

        // Clock Noise Generator (every 2 PSG cycles or according to period)
        _noiseCounter++;
        if (_noiseCounter >= (_noisePeriod * 2))
        {
            _noiseCounter = 0;
            if ((_noiseLfsr & 1) != 0)
            {
                _noiseLfsr ^= 0x24000;
                _noiseOutput = !_noiseOutput;
            }
            _noiseLfsr >>= 1;
        }

        // Clock Envelope Generator
        _envCounter++;
        if (_envCounter >= (_envPeriod * 2))
        {
            _envCounter = 0;
            if (!_envHolding)
            {
                if (_envAttack)
                {
                    if (_envVolume < 15) _envVolume++;
                    else
                    {
                        if (_envAlternate) { _envAttack = false; _envVolume = 15; }
                        else if ((Registers[13] & 0x08) == 0) { _envVolume = 0; _envHolding = true; }
                        else { _envVolume = 0; }
                    }
                }
                else
                {
                    if (_envVolume > 0) _envVolume--;
                    else
                    {
                        if (_envAlternate) { _envAttack = true; _envVolume = 0; }
                        else if ((Registers[13] & 0x08) == 0) { _envVolume = 0; _envHolding = true; }
                        else { _envVolume = 15; }
                    }
                }
            }
        }

        // Mixer: Reg 7 bits 0-2 (Tone enable, active low), bits 3-5 (Noise enable, active low)
        byte mixer = Registers[7];
        float sample = 0;

        for (int ch = 0; ch < 3; ch++)
        {
            bool toneDisabled = (mixer & (1 << ch)) != 0;
            bool noiseDisabled = (mixer & (1 << (ch + 3))) != 0;

            bool chanOut = (toneDisabled || _toneOutputs[ch]) && (noiseDisabled || _noiseOutput);

            if (chanOut)
            {
                byte ampReg = Registers[8 + ch];
                int volume = (ampReg & 0x10) != 0 ? _envVolume : (ampReg & 0x0F);
                sample += DacTable[volume];
            }
        }

        return (sample / 3.0f); // Normalize to 0.0 - 1.0
    }
}
