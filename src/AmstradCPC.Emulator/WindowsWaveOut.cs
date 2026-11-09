using System;
using System.Runtime.InteropServices;

namespace AmstradCPC.Emulator;

/// <summary>
/// Native WinMM buffered audio output for low-latency sound reproduction.
/// Plays 44.1 kHz 16-bit mono PCM samples from AY-3-8912 PSG emulation.
/// </summary>
internal sealed class WindowsWaveOut : IDisposable
{
    private const int Slots = 16;
    private const int MaxSamples = 2048;
    private const uint WHDR_DONE = 0x00000001;
    private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutOpen(out IntPtr handle, uint deviceId, ref WaveFormat format,
        IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutPrepareHeader(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutUnprepareHeader(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutWrite(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutReset(IntPtr h);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutClose(IntPtr h);

    private readonly IntPtr[] _buffers = new IntPtr[Slots];
    private readonly IntPtr[] _headers = new IntPtr[Slots];
    private readonly bool[] _prepared = new bool[Slots];
    private IntPtr _handle;
    private int _next;
    private bool _disposed;

    public bool Muted { get; set; } = false;
    public float Volume { get; set; } = 1.0f;

    public int QueuedCount
    {
        get
        {
            if (_handle == IntPtr.Zero || _disposed) return 0;
            int count = 0;
            for (int i = 0; i < Slots; i++)
            {
                if (_prepared[i])
                {
                    var hdr = Marshal.PtrToStructure<WaveHeader>(_headers[i]);
                    if ((hdr.Flags & WHDR_DONE) == 0) count++;
                }
            }
            return count;
        }
    }

    public WindowsWaveOut()
    {
        var format = new WaveFormat
        {
            FormatTag = 1, // PCM
            Channels = 1,
            SamplesPerSec = 44100,
            AvgBytesPerSec = 88200,
            BlockAlign = 2,
            BitsPerSample = 16,
            ExtraSize = 0
        };

        if (waveOutOpen(out _handle, 0xFFFFFFFF, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        {
            _handle = IntPtr.Zero;
            return;
        }

        for (int i = 0; i < Slots; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(MaxSamples * 2);
            _headers[i] = Marshal.AllocHGlobal(HeaderSize);
        }
    }

    public void WriteSamples(float[] samples, int count)
    {
        if (_handle == IntPtr.Zero || _disposed || count == 0) return;

        count = Math.Min(count, MaxSamples);
        short[] pcm = new short[count];
        if (!Muted)
        {
            float vol = Math.Clamp(Volume, 0f, 1f);
            for (int i = 0; i < count; i++)
            {
                float s = Math.Clamp(samples[i] * vol, -1.0f, 1.0f);
                pcm[i] = (short)(s * 28000f);
            }
        }

        // Maintain comfortable queue depth (target 3-4 buffers = 60-80 ms of latency).
        // If queue exceeds 5 buffers, wait briefly for driver to play oldest buffer (never drop audio frames!).
        int waitMs = 25;
        while (QueuedCount >= 5 && waitMs-- > 0)
        {
            Thread.Sleep(1);
        }

        int slot = _next;
        if (_prepared[slot])
        {
            var hdr = Marshal.PtrToStructure<WaveHeader>(_headers[slot]);
            if ((hdr.Flags & WHDR_DONE) == 0)
            {
                int spin = 10;
                while ((hdr.Flags & WHDR_DONE) == 0 && spin-- > 0)
                {
                    Thread.Sleep(1);
                    hdr = Marshal.PtrToStructure<WaveHeader>(_headers[slot]);
                }
            }
            waveOutUnprepareHeader(_handle, _headers[slot], (uint)HeaderSize);
            _prepared[slot] = false;
        }

        Marshal.Copy(pcm, 0, _buffers[slot], count);

        var header = new WaveHeader
        {
            Data = _buffers[slot],
            BufferLength = (uint)(count * 2)
        };
        Marshal.StructureToPtr(header, _headers[slot], false);

        if (waveOutPrepareHeader(_handle, _headers[slot], (uint)HeaderSize) == 0)
        {
            _prepared[slot] = true;
            waveOutWrite(_handle, _headers[slot], (uint)HeaderSize);
            _next = (_next + 1) % Slots;
        }
    }

    public void Reset()
    {
        if (_handle == IntPtr.Zero || _disposed) return;
        waveOutReset(_handle);
        for (int i = 0; i < Slots; i++)
        {
            if (_prepared[i])
            {
                waveOutUnprepareHeader(_handle, _headers[i], (uint)HeaderSize);
                _prepared[i] = false;
            }
        }
        _next = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            waveOutReset(_handle);
            for (int i = 0; i < Slots; i++)
            {
                if (_prepared[i]) waveOutUnprepareHeader(_handle, _headers[i], (uint)HeaderSize);
                if (_buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buffers[i]);
                if (_headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_headers[i]);
            }
            waveOutClose(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
