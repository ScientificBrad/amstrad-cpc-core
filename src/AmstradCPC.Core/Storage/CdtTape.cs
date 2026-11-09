using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AmstradCPC.Core.Storage;

public sealed class TapePulse
{
    public int LengthTStates { get; set; } // Length in 4 MHz T-states
    public bool Level { get; set; }
}

/// <summary>
/// Cassette tape player for authentic Amstrad CPC .CDT and .TZX audio pulse playback.
/// Feeds authentic hardware pulses into PPI Port B bit 0 when motor is enabled.
/// </summary>
public sealed class CdtTape
{
    public bool IsLoaded { get; private set; } = false;
    public string Name { get; private set; } = "";
    public int TotalPulses => _pulses.Count;
    public int CurrentPulseIndex { get; private set; } = 0;
    public bool CurrentSignal { get; private set; } = false;

    public bool FastLoadEnabled { get; set; } = true;

    private readonly List<int> _pulses = new();
    private long _pulseCycleTimer = 0;

    public static CdtTape? Load(string path)
    {
        if (!File.Exists(path)) return null;
        return Load(File.ReadAllBytes(path), Path.GetFileName(path));
    }

    public static CdtTape? Load(byte[] data, string name = "Tape")
    {
        if (data.Length < 10) return null;

        string sig = Encoding.ASCII.GetString(data, 0, 7);
        if (sig != "ZXTape!" && sig != "CPCTape") return null;

        var tape = new CdtTape
        {
            Name = name,
            IsLoaded = true
        };

        tape.ParseBlocks(data);
        return tape;
    }

    private void ParseBlocks(byte[] data)
    {
        int offset = 10;
        _pulses.Clear();

        while (offset < data.Length)
        {
            byte blockId = data[offset++];

            switch (blockId)
            {
                case 0x10: // Standard Speed Data Block
                    {
                        if (offset + 4 > data.Length) break;
                        int pauseMs = data[offset] | (data[offset + 1] << 8);
                        int dataLen = data[offset + 2] | (data[offset + 3] << 8);
                        offset += 4;

                        if (offset + dataLen <= data.Length)
                        {
                            byte[] blockData = new byte[dataLen];
                            Array.Copy(data, offset, blockData, 0, dataLen);
                            offset += dataLen;

                            // Standard CPC pulse lengths (scaled to 4 MHz CPU clock):
                            // Pilot: ~2168 T-states (approx 2470 on CPC, 2168 nominal)
                            int pilotPulse = 2168;
                            int sync1 = 667;
                            int sync2 = 735;
                            int zeroPulse = 855;
                            int onePulse = 1710;
                            int pilotCount = (blockData[0] < 128) ? 4096 : 2048;

                            AddBlockPulses(blockData, pilotPulse, sync1, sync2, zeroPulse, onePulse, pilotCount, 8);

                            // Pause after block (at 4 MHz: 4000 T-states per ms)
                            if (pauseMs > 0)
                            {
                                _pulses.Add(pauseMs * 4000);
                            }
                        }
                        break;
                    }

                case 0x11: // Turbo Speed Data Block
                    {
                        if (offset + 18 > data.Length) break;
                        int pilotPulse = data[offset] | (data[offset + 1] << 8);
                        int sync1 = data[offset + 2] | (data[offset + 3] << 8);
                        int sync2 = data[offset + 4] | (data[offset + 5] << 8);
                        int zeroPulse = data[offset + 6] | (data[offset + 7] << 8);
                        int onePulse = data[offset + 8] | (data[offset + 9] << 8);
                        int pilotCount = data[offset + 10] | (data[offset + 11] << 8);
                        int usedBitsLast = data[offset + 12];
                        int pauseMs = data[offset + 13] | (data[offset + 14] << 8);
                        int dataLen = data[offset + 15] | (data[offset + 16] << 8) | (data[offset + 17] << 16);
                        offset += 18;

                        if (offset + dataLen <= data.Length)
                        {
                            byte[] blockData = new byte[dataLen];
                            Array.Copy(data, offset, blockData, 0, dataLen);
                            offset += dataLen;

                            AddBlockPulses(blockData, pilotPulse, sync1, sync2, zeroPulse, onePulse, pilotCount, usedBitsLast);

                            if (pauseMs > 0)
                            {
                                _pulses.Add(pauseMs * 4000);
                            }
                        }
                        break;
                    }

                case 0x12: // Pure Tone
                    {
                        if (offset + 4 > data.Length) break;
                        int pulseLen = data[offset] | (data[offset + 1] << 8);
                        int count = data[offset + 2] | (data[offset + 3] << 8);
                        offset += 4;
                        for (int i = 0; i < count; i++) _pulses.Add(pulseLen);
                        break;
                    }

                case 0x13: // Sequence of Pulses
                    {
                        if (offset + 1 > data.Length) break;
                        int count = data[offset++];
                        for (int i = 0; i < count && offset + 2 <= data.Length; i++)
                        {
                            int len = data[offset] | (data[offset + 1] << 8);
                            offset += 2;
                            _pulses.Add(len);
                        }
                        break;
                    }

                case 0x20: // Pause
                    {
                        if (offset + 2 > data.Length) break;
                        int pauseMs = data[offset] | (data[offset + 1] << 8);
                        offset += 2;
                        if (pauseMs > 0) _pulses.Add(pauseMs * 4000);
                        break;
                    }

                case 0x30: // Text Description
                    {
                        if (offset + 1 > data.Length) break;
                        int textLen = data[offset++];
                        offset += textLen;
                        break;
                    }

                default:
                    // Skip unrecognized block
                    offset = data.Length;
                    break;
            }
        }
    }

    private void AddBlockPulses(byte[] blockData, int pilotPulse, int sync1, int sync2, int zeroPulse, int onePulse, int pilotCount, int usedBitsLast)
    {
        // 1. Pilot Tone
        for (int i = 0; i < pilotCount; i++)
        {
            _pulses.Add(pilotPulse);
        }

        // 2. Sync Pulses
        if (sync1 > 0) _pulses.Add(sync1);
        if (sync2 > 0) _pulses.Add(sync2);

        // 3. Data Bits
        for (int byteIdx = 0; byteIdx < blockData.Length; byteIdx++)
        {
            byte b = blockData[byteIdx];
            int bitsToRead = (byteIdx == blockData.Length - 1) ? usedBitsLast : 8;

            for (int bit = 7; bit >= 8 - bitsToRead; bit--)
            {
                bool bitSet = ((b >> bit) & 1) != 0;
                int pulseLen = bitSet ? onePulse : zeroPulse;
                // 2 pulses per bit
                _pulses.Add(pulseLen);
                _pulses.Add(pulseLen);
            }
        }
    }

    /// <summary>
    /// Steps the tape player forward by the elapsed CPU cycles when the motor is active.
    /// Returns the active audio signal state (true = high, false = low).
    /// </summary>
    public bool Step(int cpuCycles, bool motorOn)
    {
        if (!IsLoaded || !motorOn || CurrentPulseIndex >= _pulses.Count)
        {
            return CurrentSignal;
        }

        _pulseCycleTimer += cpuCycles;

        while (CurrentPulseIndex < _pulses.Count && _pulseCycleTimer >= _pulses[CurrentPulseIndex])
        {
            _pulseCycleTimer -= _pulses[CurrentPulseIndex];
            CurrentPulseIndex++;
            CurrentSignal = !CurrentSignal;
        }

        return CurrentSignal;
    }

    public void Rewind()
    {
        CurrentPulseIndex = 0;
        _pulseCycleTimer = 0;
        CurrentSignal = false;
    }
}
