using AmstradCPC.Core.Audio;
using AmstradCPC.Core.Input;
using AmstradCPC.Core.Io;
using AmstradCPC.Core.Video;
using Xunit;

namespace AmstradCPC.Tests;

public class PpiAndKeyboardTests
{
    [Fact]
    public void KeyboardMatrix_RowAndColumnScanning_ThroughAyRegister14()
    {
        var keyboard = new KeyboardMatrix();
        var ay = new Ay38912(keyboard);
        var crtc = new Crtc6845();
        var ppi = new Ppi8255(ay, crtc);

        // Press key on Row 5, Column 7 (e.g. SPACE key)
        keyboard.SetKeyState(5, 7, pressed: true);

        // Sequence performed by Amstrad firmware to read Row 5:
        // 1. Select AY Register 14 (Port A = 14, Port C = 0xC0):
        ppi.WritePortA(14);
        ppi.WritePortC(0xC0); // Latch register 14
        ppi.WritePortC(0x00); // Inactive

        // 2. Select Keyboard Row 5 (Port C lower nibble = 5, BC1=0, BDIR=0)
        ppi.WritePortC(0x05);

        // 3. Put AY into Read mode (BC1=1, BDIR=0 -> Port C bits 7-6 = 01 -> 0x45)
        ppi.WritePortC(0x45);

        // 4. Read Port A
        byte val = ppi.ReadPortA();

        // Bit 7 should be 0 (pulled low by pressed key), all other bits should be 1
        Assert.Equal(0x7F, val);

        // Now test Row 6 (unpressed) -> should return 0xFF
        ppi.WritePortC(0x46);
        byte valRow6 = ppi.ReadPortA();
        Assert.Equal(0xFF, valRow6);
    }

    [Fact]
    public void Ay38912_ToneGenerator_ProducesSquareWaveAtCorrectFrequency()
    {
        var keyboard = new KeyboardMatrix();
        var ay = new Ay38912(keyboard);

        // Configure Channel A: Period = 100, Tone enabled, Volume = 15
        ay.SelectRegister(0);
        ay.WriteData(100); // Period low
        ay.SelectRegister(1);
        ay.WriteData(0);   // Period high
        ay.SelectRegister(7);
        ay.WriteData(0xFE); // Mixer: Enable Channel A tone (bit 0 = 0)
        ay.SelectRegister(8);
        ay.WriteData(15);  // Volume = 15 (max)

        // With period = 100, countdown is 8 * 100 = 800 cycles per half-wave toggle
        int toggles = 0;
        float prevSample = ay.Clock();

        for (int i = 0; i < 3200; i++) // 4 half-waves = 2 full cycles
        {
            float sample = ay.Clock();
            if ((prevSample > 0.1f && sample < 0.1f) || (prevSample < 0.1f && sample > 0.1f))
            {
                toggles++;
            }
            prevSample = sample;
        }

        // Over 3,200 PSG clock cycles with half-wave duration 800: exactly 4 toggles!
        Assert.Equal(4, toggles);
    }
}
