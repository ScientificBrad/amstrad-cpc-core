using System;
using System.Collections.Generic;
using System.Linq;

namespace AmstradCPC.Core.Storage;

public enum FdcPhase
{
    Command,
    Execution,
    Result
}

/// <summary>
/// NEC µPD765A / Intel 8272 Floppy Disk Controller.
/// Reconstructs the Amstrad CPC floppy drive subsystem (CPC 664, CPC 6128, and DDI-1).
/// </summary>
public sealed class Upd765Fdc
{
    // Drives A and B
    public DskImage? DriveA { get; set; }
    public DskImage? DriveB { get; set; }

    public int CurrentCylinderA { get; private set; } = 0;
    public int CurrentCylinderB { get; private set; } = 0;
    public bool MotorOn { get; set; } = false;

    public Action<string>? LogAction { get; set; }
    public List<string> History { get; } = new();

    private void Log(string msg)
    {
        History.Add(msg);
        LogAction?.Invoke(msg);
    }

    // FDC state machine
    public FdcPhase Phase { get; private set; } = FdcPhase.Command;
    private bool _hasCommand = false;
    private byte _currentCommand = 0;
    private readonly List<byte> _commandParams = new();
    private int _expectedParams = 0;

    // Execution phase data buffer (sector read/write)
    private byte[] _dataBuffer = Array.Empty<byte>();
    private int _dataBufferIndex = 0;

    // Result phase FIFO
    private readonly Queue<byte> _resultQueue = new();

    // Interrupt / Seek state
    public bool InterruptPending { get; set; } = false;
    private byte _lastSeekDrive = 0;

    public Upd765Fdc()
    {
        Reset();
    }

    public void Reset()
    {
        Phase = FdcPhase.Command;
        _hasCommand = false;
        _currentCommand = 0;
        _commandParams.Clear();
        _expectedParams = 0;
        _dataBuffer = Array.Empty<byte>();
        _dataBufferIndex = 0;
        _resultQueue.Clear();
        InterruptPending = false;
        CurrentCylinderA = 0;
        CurrentCylinderB = 0;
        MotorOn = false;
    }

    /// <summary>
    /// Reads the Main Status Register (MSR, Port &FB7E).
    /// </summary>
    public byte ReadMainStatus()
    {
        byte msr = 0x80; // RQM = 1 (Ready for data transfer)

        switch (Phase)
        {
            case FdcPhase.Command:
                // DIO = 0 (CPU to FDC write), CB = 0
                break;

            case FdcPhase.Execution:
                // Non-DMA = 1, CB = 1
                msr |= 0x20 | 0x10;
                // If reading data: DIO = 1 (FDC to CPU)
                if (_currentCommand == 0x46 || _currentCommand == 0x66 || _currentCommand == 0x06)
                {
                    msr |= 0x40;
                }
                break;

            case FdcPhase.Result:
                // DIO = 1 (FDC to CPU read), CB = 1
                msr |= 0x40 | 0x10;
                break;
        }

        return msr;
    }

    /// <summary>
    /// Reads from the Data Register (FIFO, Port &FB7F).
    /// </summary>
    public byte ReadData()
    {
        if (Phase == FdcPhase.Execution)
        {
            if (_dataBufferIndex < _dataBuffer.Length)
            {
                byte val = _dataBuffer[_dataBufferIndex++];
                if (_dataBufferIndex >= _dataBuffer.Length)
                {
                    // Execution phase complete, transition to result phase
                    FinishReadSector();
                }
                return val;
            }
            FinishReadSector();
            return 0x00;
        }

        if (Phase == FdcPhase.Result && _resultQueue.Count > 0)
        {
            byte val = _resultQueue.Dequeue();
            if (_resultQueue.Count == 0)
            {
                // Result phase ended, return to command phase
                Phase = FdcPhase.Command;
                _hasCommand = false;
            }
            return val;
        }

        return 0x00;
    }

    /// <summary>
    /// Writes to the Data Register (FIFO, Port &FB7F).
    /// </summary>
    public void WriteData(byte value)
    {
        if (Phase == FdcPhase.Command)
        {
            if (!_hasCommand)
            {
                _currentCommand = value;
                _hasCommand = true;
                _expectedParams = GetExpectedParamCount(value);
                _commandParams.Clear();
                if (_expectedParams == 0)
                {
                    ExecuteCommand();
                }
            }
            else
            {
                _commandParams.Add(value);
                if (_commandParams.Count >= _expectedParams)
                {
                    ExecuteCommand();
                }
            }
            return;
        }

        if (Phase == FdcPhase.Execution)
        {
            if (_dataBufferIndex < _dataBuffer.Length)
            {
                _dataBuffer[_dataBufferIndex++] = value;
                if (_dataBufferIndex >= _dataBuffer.Length)
                {
                    FinishWriteSector();
                }
            }
        }
    }

    private int GetExpectedParamCount(byte cmd)
    {
        byte code = (byte)(cmd & 0x1F);
        return code switch
        {
            0x03 => 2, // Specify
            0x04 => 1, // Sense Drive Status
            0x07 => 1, // Recalibrate
            0x08 => 0, // Sense Interrupt Status
            0x0F => 2, // Seek
            0x06 => 8, // Read Data (0x46/0x66)
            0x05 => 8, // Write Data (0x45/0x65)
            0x0A => 1, // Read ID (0x4A)
            _ => 0
        };
    }

    private void ExecuteCommand()
    {
        byte code = (byte)(_currentCommand & 0x1F);
        Log($"FDC Exec: 0x{code:X2} (cmd=0x{_currentCommand:X2}, params=[{string.Join(",", _commandParams.Select(p => $"0x{p:X2}"))}])");
        switch (code)
        {
            case 0x03: // Specify
                Phase = FdcPhase.Command;
                _hasCommand = false;
                _commandParams.Clear();
                break;

            case 0x04: // Sense Drive Status
                {
                    byte drive = (byte)(_commandParams.Count > 0 ? (_commandParams[0] & 0x03) : 0);
                    byte head = (byte)(_commandParams.Count > 0 ? ((_commandParams[0] >> 2) & 0x01) : 0);
                    byte st3 = (byte)(0x20 | (head << 2) | drive); // Track 0 status
                    int curCyl = (drive == 0) ? CurrentCylinderA : CurrentCylinderB;
                    if (curCyl == 0) st3 |= 0x10; // Track 0
                    if (GetDisk(drive) != null) st3 |= 0x08; // Ready

                    _resultQueue.Clear();
                    _resultQueue.Enqueue(st3);
                    Phase = FdcPhase.Result;
                    _commandParams.Clear();
                    break;
                }

            case 0x07: // Recalibrate
                {
                    byte drive = (byte)(_commandParams.Count > 0 ? (_commandParams[0] & 0x03) : 0);
                    if (drive == 0) CurrentCylinderA = 0;
                    else CurrentCylinderB = 0;

                    _lastSeekDrive = drive;
                    InterruptPending = true;
                    Phase = FdcPhase.Command;
                    _hasCommand = false;
                    _commandParams.Clear();
                    break;
                }

            case 0x08: // Sense Interrupt Status
                {
                    _resultQueue.Clear();
                    byte drive = _lastSeekDrive;
                    int curCyl = (drive == 0) ? CurrentCylinderA : CurrentCylinderB;
                    byte st0 = (byte)(0x20 | drive); // Seek complete (bit 5)
                    _resultQueue.Enqueue(st0);
                    _resultQueue.Enqueue((byte)curCyl);
                    InterruptPending = false;
                    Phase = FdcPhase.Result;
                    _commandParams.Clear();
                    break;
                }

            case 0x0F: // Seek
                {
                    byte drive = (byte)(_commandParams.Count > 0 ? (_commandParams[0] & 0x03) : 0);
                    byte newCyl = (byte)(_commandParams.Count > 1 ? _commandParams[1] : 0);
                    if (drive == 0) CurrentCylinderA = newCyl;
                    else CurrentCylinderB = newCyl;

                    _lastSeekDrive = drive;
                    InterruptPending = true;
                    Phase = FdcPhase.Command;
                    _hasCommand = false;
                    _commandParams.Clear();
                    break;
                }

            case 0x06: // Read Data (0x46 / 0x66)
                {
                    byte drive = (byte)(_commandParams[0] & 0x03);
                    byte c = _commandParams[1];
                    byte h = _commandParams[2];
                    byte r = _commandParams[3];
                    byte n = _commandParams[4];

                    var disk = GetDisk(drive);
                    var sector = disk?.GetSector(c, h, r);

                    if (sector != null && sector.Data.Length > 0)
                    {
                        _dataBuffer = sector.Data;
                        _dataBufferIndex = 0;
                        Phase = FdcPhase.Execution;
                    }
                    else
                    {
                        // Sector not found error
                        _resultQueue.Clear();
                        _resultQueue.Enqueue((byte)(0x40 | drive)); // ST0: Abort / error
                        _resultQueue.Enqueue(0x04);         // ST1: No Data (ND)
                        _resultQueue.Enqueue(0x00);         // ST2
                        _resultQueue.Enqueue(c);
                        _resultQueue.Enqueue(h);
                        _resultQueue.Enqueue(r);
                        _resultQueue.Enqueue(n);
                        Phase = FdcPhase.Result;
                        _commandParams.Clear();
                    }
                    break;
                }

            case 0x0A: // Read ID (0x4A)
                {
                    byte drive = (byte)(_commandParams.Count > 0 ? (_commandParams[0] & 0x03) : 0);
                    int curCyl = (drive == 0) ? CurrentCylinderA : CurrentCylinderB;
                    var disk = GetDisk(drive);
                    var track = disk?.GetTrack(curCyl, 0);

                    byte r = (track != null && track.Sectors.Count > 0) ? track.Sectors[0].R : (byte)0xC1;
                    byte n = (track != null && track.Sectors.Count > 0) ? track.Sectors[0].N : (byte)2;

                    _resultQueue.Clear();
                    _resultQueue.Enqueue((byte)(drive)); // ST0: Normal
                    _resultQueue.Enqueue(0x00);          // ST1
                    _resultQueue.Enqueue(0x00);          // ST2
                    _resultQueue.Enqueue((byte)curCyl);
                    _resultQueue.Enqueue(0);
                    _resultQueue.Enqueue(r);
                    _resultQueue.Enqueue(n);
                    Phase = FdcPhase.Result;
                    _commandParams.Clear();
                    break;
                }

            default:
                Phase = FdcPhase.Command;
                _hasCommand = false;
                _commandParams.Clear();
                break;
        }
    }

    private void FinishReadSector()
    {
        byte drive = (byte)(_commandParams[0] & 0x03);
        byte c = _commandParams[1];
        byte h = _commandParams[2];
        byte r = _commandParams[3];
        byte n = _commandParams[4];

        _resultQueue.Clear();
        _resultQueue.Enqueue(drive);         // ST0: Success
        _resultQueue.Enqueue(0x00);          // ST1: Normal
        _resultQueue.Enqueue(0x00);          // ST2: Normal
        _resultQueue.Enqueue(c);
        _resultQueue.Enqueue(h);
        _resultQueue.Enqueue((byte)(r + 1)); // Next sector
        _resultQueue.Enqueue(n);

        Phase = FdcPhase.Result;
        _commandParams.Clear();
    }

    private void FinishWriteSector()
    {
        FinishReadSector();
    }

    private DskImage? GetDisk(byte drive) => drive == 0 ? DriveA : DriveB;
}
