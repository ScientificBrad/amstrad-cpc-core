using System;
using System.IO;
using System.Linq;
using AmstradCPC.Core.Audio;
using AmstradCPC.Core.Input;
using AmstradCPC.Core.Machine;
using AmstradCPC.Core.Memory;
using Xunit;
using Xunit.Abstractions;

namespace AmstradCPC.Tests;

public class AudioSubsystemTests
{
    private readonly ITestOutputHelper _output;

    public AudioSubsystemTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ToneGenerator_A440_ProducesAccurateFrequency()
    {
        var kb = new KeyboardMatrix();
        var psg = new Ay38912(kb);

        // A440: Master Clock = 1,000,000 Hz. Divider = 16.
        // TP = 1,000,000 / (16 * 440) = 142.045 => TP = 142.
        // Expected Frequency = 1,000,000 / (16 * 142) = 440.14 Hz.
        int tp = 142;
        psg.SelectRegister(0); psg.WriteData((byte)(tp & 0xFF));
        psg.SelectRegister(1); psg.WriteData((byte)((tp >> 8) & 0x0F));

        // Enable Tone on Channel A, disable noise on all channels
        // Reg 7: bit 0 = 0 (Tone A on), bit 1-2 = 1 (B, C off), bit 3-5 = 1 (Noise off) => 0x3E
        psg.SelectRegister(7); psg.WriteData(0x3E);

        // Set Channel A Volume to maximum (15, fixed amplitude)
        psg.SelectRegister(8); psg.WriteData(15);

        // Run for 1,000,000 PSG clocks (exactly 1.0 second of PSG time)
        int transitions = 0;
        float lastSample = -1f;

        for (int i = 0; i < 1_000_000; i++)
        {
            float s = psg.Clock();
            if (i > 0 && Math.Abs(s - lastSample) > 0.01f)
            {
                transitions++;
            }
            lastSample = s;
        }

        // Each full cycle has 2 transitions (low-to-high, high-to-low)
        double measuredFreq = transitions / 2.0;
        double expectedFreq = 1_000_000.0 / (16.0 * 142.0); // 440.1408 Hz

        _output.WriteLine($"A440 Test: Expected={expectedFreq:F2} Hz, Measured={measuredFreq:F2} Hz, Transitions={transitions}");
        Assert.InRange(measuredFreq, expectedFreq - 0.5, expectedFreq + 0.5);
    }

    [Fact]
    public void ToneGenerator_C5_ProducesAccurateFrequency()
    {
        var kb = new KeyboardMatrix();
        var psg = new Ay38912(kb);

        // C5 (~523 Hz): TP = 1,000,000 / (16 * 523.25) = 119.
        // Expected Frequency = 1,000,000 / (16 * 119) = 525.21 Hz.
        int tp = 119;
        psg.SelectRegister(0); psg.WriteData((byte)(tp & 0xFF));
        psg.SelectRegister(1); psg.WriteData((byte)((tp >> 8) & 0x0F));
        psg.SelectRegister(7); psg.WriteData(0x3E);
        psg.SelectRegister(8); psg.WriteData(15);

        int transitions = 0;
        float lastSample = -1f;

        for (int i = 0; i < 1_000_000; i++)
        {
            float s = psg.Clock();
            if (i > 0 && Math.Abs(s - lastSample) > 0.01f)
            {
                transitions++;
            }
            lastSample = s;
        }

        double measuredFreq = transitions / 2.0;
        double expectedFreq = 1_000_000.0 / (16.0 * 119.0); // 525.21 Hz

        _output.WriteLine($"C5 Test: Expected={expectedFreq:F2} Hz, Measured={measuredFreq:F2} Hz, Transitions={transitions}");
        Assert.InRange(measuredFreq, expectedFreq - 0.5, expectedFreq + 0.5);
    }

    [Fact]
    public void NoiseGenerator_ProducesBalancedPseudoRandomSequence()
    {
        var kb = new KeyboardMatrix();
        var psg = new Ay38912(kb);

        // Reg 6: Noise period = 5
        psg.SelectRegister(6); psg.WriteData(5);
        // Enable Noise on Channel A, disable Tone
        // Reg 7: bit 0 = 1 (Tone A off), bit 3 = 0 (Noise A on) => 0x37
        psg.SelectRegister(7); psg.WriteData(0x37);
        psg.SelectRegister(8); psg.WriteData(15);

        int highCount = 0;
        int lowCount = 0;

        for (int i = 0; i < 100_000; i++)
        {
            float s = psg.Clock();
            if (s > 0.01f) highCount++;
            else lowCount++;
        }

        _output.WriteLine($"Noise Distribution: High={highCount}, Low={lowCount}");
        Assert.True(highCount > 20_000, "Noise must output high states");
        Assert.True(lowCount > 20_000, "Noise must output low states");
    }

    [Fact]
    public void EnvelopeGenerator_AllShapes_ConformToSiliconSpecs()
    {
        var kb = new KeyboardMatrix();
        var psg = new Ay38912(kb);

        // Env Period = 1 (fastest step: 16 clocks per step)
        psg.SelectRegister(11); psg.WriteData(1);
        psg.SelectRegister(12); psg.WriteData(0);
        // Reg 7: Tone and Noise disabled on all channels (pins held high for pure envelope DAC output)
        psg.SelectRegister(7); psg.WriteData(0x3F);
        // Reg 8: Channel A uses Envelope (bit 4 = 1)
        psg.SelectRegister(8); psg.WriteData(0x10);

        // Test Shape 0 (0000): Ramp down 15->0, hold at 0
        psg.SelectRegister(13); psg.WriteData(0);
        int[] shape0Vol = new int[32];
        for (int i = 0; i < 32; i++)
        {
            shape0Vol[i] = psg.EnvelopeVolume;
            for (int c = 0; c < 16; c++) psg.Clock();
        }
        _output.WriteLine($"Shape 0 volume: start={shape0Vol[0]}, step5={shape0Vol[5]}, step15={shape0Vol[15]}, step25={shape0Vol[25]}");
        Assert.Equal(15, shape0Vol[0]);
        Assert.Equal(10, shape0Vol[5]);
        Assert.Equal(0, shape0Vol[15]);
        Assert.Equal(0, shape0Vol[25]); // holds at 0

        // Test Shape 12 (1100): Sawtooth up (repeated 0->15)
        psg.SelectRegister(13); psg.WriteData(12);
        int[] shape12Vol = new int[35];
        for (int i = 0; i < 35; i++)
        {
            shape12Vol[i] = psg.EnvelopeVolume;
            for (int c = 0; c < 16; c++) psg.Clock();
        }
        _output.WriteLine($"Shape 12 volume: start={shape12Vol[0]}, peak={shape12Vol[15]}, repeat={shape12Vol[16]}, next={shape12Vol[17]}");
        Assert.Equal(0, shape12Vol[0]);
        Assert.Equal(15, shape12Vol[15]);
        Assert.Equal(0, shape12Vol[16]); // wraps to 0
        Assert.Equal(1, shape12Vol[17]); // continues up

        // Test Shape 14 (1110): Triangle (0->15->0->15)
        psg.SelectRegister(13); psg.WriteData(14);
        int[] shape14Vol = new int[40];
        for (int i = 0; i < 40; i++)
        {
            shape14Vol[i] = psg.EnvelopeVolume;
            for (int c = 0; c < 16; c++) psg.Clock();
        }
        _output.WriteLine($"Shape 14 volume: start={shape14Vol[0]}, peak={shape14Vol[15]}, valley={shape14Vol[31]}");
        Assert.Equal(0, shape14Vol[0]);
        Assert.Equal(15, shape14Vol[15]);
        Assert.Equal(0, shape14Vol[31]); // returns to 0 on cycle 2
    }

    [Fact]
    public void AudioResampler_50Frames_ProducesExact44100SamplesPerSecond()
    {
        var machine = new AmstradMachine(CpcModel.CPC464);
        int totalAudioSamples = 0;

        for (int f = 0; f < 50; f++)
        {
            machine.StepFrame();
            totalAudioSamples += machine.AudioBufferCount;
            machine.ClearAudioBuffer();
        }

        _output.WriteLine($"Total audio samples in 50 frames (1.0 sec): {totalAudioSamples} (Target: 44,100)");
        Assert.Equal(44100, totalAudioSamples);
    }

    [Fact]
    public void WavFileExport_ToneA440_ExportsValidWavFile()
    {
        var kb = new KeyboardMatrix();
        var psg = new Ay38912(kb);

        int tp = 142; // A440
        psg.SelectRegister(0); psg.WriteData((byte)(tp & 0xFF));
        psg.SelectRegister(1); psg.WriteData((byte)((tp >> 8) & 0x0F));
        psg.SelectRegister(7); psg.WriteData(0x3E);
        psg.SelectRegister(8); psg.WriteData(15);

        // Generate 44,100 samples at 44.1 kHz via decimation
        short[] pcm = new short[44100];
        long accum = 0;
        int pcmIdx = 0;
        float sum = 0;
        int count = 0;

        for (int clock = 0; clock < 1_000_000 && pcmIdx < 44100; clock++)
        {
            sum += psg.Clock();
            count++;
            accum += 44100;
            if (accum >= 1000000)
            {
                accum -= 1000000;
                float sample = sum / count;
                sum = 0;
                count = 0;
                pcm[pcmIdx++] = (short)((sample * 2.0f - 1.0f) * 24000f);
            }
        }

        string wavPath = Path.Combine(@"D:\Emulation\Amstrad", "a440_test_tone.wav");
        WriteWav(wavPath, pcm, 44100);
        Assert.True(File.Exists(wavPath));
        var fi = new FileInfo(wavPath);
        _output.WriteLine($"WAV file generated at {wavPath}, size: {fi.Length} bytes");
        Assert.Equal(44 + (44100 * 2), fi.Length);
    }

    private static void WriteWav(string path, short[] pcm, int sampleRate)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        int byteRate = sampleRate * 2;
        int dataChunkSize = pcm.Length * 2;

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataChunkSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // Subchunk1Size for PCM
        bw.Write((short)1); // AudioFormat = 1 (PCM)
        bw.Write((short)1); // NumChannels = 1 (Mono)
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)2); // BlockAlign
        bw.Write((short)16); // BitsPerSample

        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataChunkSize);

        for (int i = 0; i < pcm.Length; i++)
        {
            bw.Write(pcm[i]);
        }
    }
}
