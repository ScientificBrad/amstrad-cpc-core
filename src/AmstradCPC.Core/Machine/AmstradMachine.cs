using AmstradCPC.Core.Audio;
using AmstradCPC.Core.Cpu;
using AmstradCPC.Core.Input;
using AmstradCPC.Core.Io;
using AmstradCPC.Core.Memory;
using AmstradCPC.Core.Storage;
using AmstradCPC.Core.Video;

namespace AmstradCPC.Core.Machine;

/// <summary>
/// Master Amstrad CPC Machine hardware coordinator.
/// Orchestrates CPU, Gate Array, CRTC, PPI, PSG, FDC, Memory, and Video Renderer.
/// </summary>
public sealed class AmstradMachine
{
    public CpcModel Model { get; }
    public CpuZ80 Cpu { get; }
    public MemorySubsystem Memory { get; }
    public GateArray GateArray { get; }
    public Crtc6845 Crtc { get; }
    public Ppi8255 Ppi { get; }
    public Ay38912 Psg { get; }
    public Upd765Fdc Fdc { get; }
    public CdtTape? Tape { get; set; }
    public KeyboardMatrix Keyboard { get; }
    public IoBus Io { get; }
    public ScreenRenderer Renderer { get; }
    public Z80Trace Trace { get; }

    public const int CyclesPerFrame = 80000; // ~50 Hz PAL: 312 lines * 64 chars * 4 cycles = 79,872 cycles
    public long TotalFrames { get; private set; }

    // Audio sample buffer: exactly 882 samples per 50 Hz frame at 44.1 kHz
    public float[] AudioBuffer { get; } = new float[1024];
    public int AudioBufferCount { get; private set; }
    private int _crtcSubCycleCounter = 0;
    private int _resampleAccumulator = 0;
    private float _psgSampleSum = 0f;
    private int _psgSampleCount = 0;

    public AmstradMachine(CpcModel model = CpcModel.CPC464)
    {
        Model = model;
        Memory = new MemorySubsystem(model);
        GateArray = new GateArray(Memory);
        Crtc = new Crtc6845();
        Keyboard = new KeyboardMatrix();
        Psg = new Ay38912(Keyboard);
        Ppi = new Ppi8255(Psg, Crtc);
        Fdc = new Upd765Fdc();
        Io = new IoBus(GateArray, Crtc, Memory, Ppi, Fdc);
        Renderer = new ScreenRenderer(Memory, GateArray, Crtc);
        Trace = new Z80Trace();

        Cpu = new CpuZ80
        {
            ReadMemory = Memory.Read,
            WriteMemory = Memory.Write,
            ReadPort = Io.ReadPort,
            WritePort = Io.WritePort,
            CpcWaitStateMode = true
        };

        // Wire CRTC sync pulses to Gate Array interrupt controller
        Crtc.OnHsyncStart = () => GateArray.OnHsync();
        Crtc.OnVsyncChange = active => GateArray.SetVsync(active);

        Reset();
    }

    public void Reset()
    {
        Memory.LowerRomEnabled = true;
        Memory.UpperRomEnabled = true;
        Memory.SelectedUpperRomBank = 0;
        GateArray.Reset();
        Crtc.Reset();
        Keyboard.Reset();
        Psg.Reset();
        Ppi.Reset();
        Fdc.Reset();
        Cpu.Reset();
        TotalFrames = 0;
        AudioBufferCount = 0;
        _crtcSubCycleCounter = 0;
        _resampleAccumulator = 0;
        _psgSampleSum = 0f;
        _psgSampleCount = 0;
    }

    /// <summary>
    /// Executes the machine for a complete 50 Hz video frame (approx 80,000 CPU cycles).
    /// </summary>
    public void StepFrame()
    {
        int targetCycles = CyclesPerFrame;
        int frameCycles = 0;

        while (frameCycles < targetCycles)
        {
            // Check for Gate Array interrupt request
            if (GateArray.InterruptRequested)
            {
                GateArray.InterruptRequested = false;
                Cpu.RequestInterrupt(0xFF);
            }

            int stepCycles = Cpu.Step();
            frameCycles += stepCycles;
            _crtcSubCycleCounter += stepCycles;

            // Step Cassette tape player if mounted and motor is on
            if (Tape != null && Tape.IsLoaded)
            {
                Ppi.CassetteDataIn = Tape.Step(stepCycles, Ppi.CassetteMotor);
            }

            // Step CRTC and PSG every 4 CPU cycles (1.0 MHz character/audio clock)
            while (_crtcSubCycleCounter >= 4)
            {
                _crtcSubCycleCounter -= 4;
                Crtc.StepCharacterClock();
                float psgSample = Psg.Clock();
                _psgSampleSum += psgSample;
                _psgSampleCount++;

                // Resample directly from 1.0 MHz PSG clock to 44.1 kHz PCM
                _resampleAccumulator += 44100;
                if (_resampleAccumulator >= 1000000)
                {
                    _resampleAccumulator -= 1000000;
                    if (AudioBufferCount < AudioBuffer.Length)
                    {
                        float avg = _psgSampleCount > 0 ? (_psgSampleSum / _psgSampleCount) : psgSample;
                        _psgSampleSum = 0f;
                        _psgSampleCount = 0;

                        AudioBuffer[AudioBufferCount++] = avg;
                    }
                }
            }
        }

        // Render complete video frame
        Renderer.RenderFullFrame();
        TotalFrames++;
    }

    public void ClearAudioBuffer()
    {
        AudioBufferCount = 0;
    }
}
