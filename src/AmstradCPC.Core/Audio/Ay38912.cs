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

    // Envelope generator (MAME silicon-verified state machine)
    private int _envPeriod = 1;
    private int _envCounter = 0;
    private int _envStep = 15;
    private int _envVolume = 0;
    private bool _envHolding = false;
    private bool _envAlternate = false;

    public int EnvelopeVolume => _envVolume;

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
        TriggerEnvelope();
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
            // Reset envelope generator on write to shape register (silicon AY-3-8910 behavior)
            TriggerEnvelope();
        }
    }

    public void TriggerEnvelope()
    {
        _envStep = 15;
        _envHolding = false;
        _envAlternate = false;
        _envCounter = 0;
        UpdateEnvelopeVolume();
    }

    private void UpdateEnvelopeVolume()
    {
        byte shape = (byte)(Registers[13] & 0x0F);
        bool continueBit = (shape & 0x08) != 0;
        bool attackBit = (shape & 0x04) != 0;

        if (!continueBit && _envHolding)
        {
            _envVolume = 0;
            return;
        }

        int vol = _envStep;
        if (attackBit ^ _envAlternate)
        {
            vol = 15 - vol;
        }
        _envVolume = vol;
    }

    /// <summary>
    /// Clocked at 1.0 MHz (1 PSG cycle = 4 Z80 cycles = 1 µs).
    /// Returns the mixed floating-point audio sample (0.0 to 1.0).
    /// </summary>
    public float Clock()
    {
        // Clock Tone Generators: Authentic AY-3-8910 tone countdown (toggles every 8 * TonePeriod clocks)
        for (int i = 0; i < 3; i++)
        {
            _toneCounters[i]++;
            if (_toneCounters[i] >= (_tonePeriods[i] * 8))
            {
                _toneCounters[i] = 0;
                _toneOutputs[i] = !_toneOutputs[i];
            }
        }

        // Clock Noise Generator: shifts every 16 * NoisePeriod clocks using authentic 17-bit LFSR
        _noiseCounter++;
        if (_noiseCounter >= (_noisePeriod * 16))
        {
            _noiseCounter = 0;
            int feedback = ((_noiseLfsr & 1) ^ ((_noiseLfsr >> 3) & 1));
            _noiseLfsr = (_noiseLfsr >> 1) | (feedback << 16);
            _noiseOutput = (_noiseLfsr & 1) != 0;
        }

        // Clock Envelope Generator: steps volume every 16 * EnvPeriod clocks
        _envCounter++;
        if (_envCounter >= (_envPeriod * 16))
        {
            _envCounter = 0;
            if (!_envHolding)
            {
                _envStep--;
                if (_envStep < 0)
                {
                    byte shape = (byte)(Registers[13] & 0x0F);
                    bool continueBit = (shape & 0x08) != 0;
                    bool alternateBit = (shape & 0x02) != 0;
                    bool holdBit = (shape & 0x01) != 0;

                    if (!continueBit)
                    {
                        // Shapes 0-7: single ramp, then 0 forever
                        _envStep = 0;
                        _envHolding = true;
                    }
                    else
                    {
                        if (holdBit)
                        {
                            if (alternateBit)
                                _envAlternate = !_envAlternate;
                            _envStep = 0;
                            _envHolding = true;
                        }
                        else
                        {
                            if (alternateBit)
                                _envAlternate = !_envAlternate;
                            _envStep = 15;
                        }
                    }
                }
                UpdateEnvelopeVolume();
            }
        }

        // Mixer: Reg 7 bits 0-2 (Tone enable, active low), bits 3-5 (Noise enable, active low)
        byte mixer = Registers[7];
        float sample = 0;

        for (int ch = 0; ch < 3; ch++)
        {
            byte ampReg = Registers[8 + ch];
            int volume = (ampReg & 0x10) != 0 ? _envVolume : (ampReg & 0x0F);
            if (volume == 0) continue;
            float amp = DacTable[volume];

            bool toneDisabled = (mixer & (1 << ch)) != 0;
            bool noiseDisabled = (mixer & (1 << (ch + 3))) != 0;

            if (toneDisabled && noiseDisabled)
            {
                // Direct DAC PCM mode (used for digitized sound/speech)
                sample += (amp - 0.5f);
            }
            else
            {
                // Pure AC square/noise output centered around zero (eliminates DC offset and popping)
                bool chanOut = (toneDisabled || _toneOutputs[ch]) && (noiseDisabled || _noiseOutput);
                sample += chanOut ? (amp * 0.5f) : (-amp * 0.5f);
            }
        }

        return (sample / 1.5f); // Normalized bipolar audio (-1.0 to 1.0)
    }
}
