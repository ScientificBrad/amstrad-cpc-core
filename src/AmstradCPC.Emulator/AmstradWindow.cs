using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AmstradCPC.Core.Cpu;
using AmstradCPC.Core.Input;
using AmstradCPC.Core.Machine;
using AmstradCPC.Core.Memory;
using AmstradCPC.Core.Storage;
using AmstradCPC.Core.Video;

namespace AmstradCPC.Emulator;

public sealed class AmstradWindow : Form
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    private AmstradMachine _machine;
    private readonly WindowsWaveOut _audio;

    private ToolStripMenuItem? _cpc464Item;
    private ToolStripMenuItem? _cpc664Item;
    private ToolStripMenuItem? _cpc6128Item;

    private readonly int[] _framebufferCopy = new int[ScreenRenderer.ScreenWidth * ScreenRenderer.ScreenHeight];
    private readonly object _frameLock = new();

    private readonly Bitmap _displayBitmap;
    private Thread? _emuThread;
    private volatile bool _running = true;
    private volatile bool _paused = false;
    private volatile bool _paintPending = false;

    private int _scale = 2; // Default 2x vertical (768 x 544), ideal 4:3 CPC aspect ratio
    private bool _greenMonitor = false;
    private bool _showFps = true;
    private double _fps = 50.0;
    private int _frameCount = 0;
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();

    private readonly MenuStrip _menuStrip;
    private readonly SolidBrush _fpsBgBrush = new(Color.FromArgb(180, Color.Black));
    private readonly Font _fpsFont = new("Consolas", 10, FontStyle.Bold);

    private KeyboardMatrixViewerForm? _matrixViewerForm;

    public AmstradWindow(AmstradMachine machine)
    {
        _machine = machine;
        _audio = new WindowsWaveOut();

        Text = "Amstrad CPC 464 (1984) — Complete Family Reconstruction";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Color.Black;
        KeyPreview = true;

        _menuStrip = CreateMenuStrip();
        MainMenuStrip = _menuStrip;
        Controls.Add(_menuStrip);

        // Aspect ratio: CPC standard 768 width x (272 * 2 = 544) height gives authentic 4:3 ratio
        ClientSize = new Size(ScreenRenderer.ScreenWidth, (ScreenHeightEffective() * _scale / 2) + _menuStrip.Height);
        _displayBitmap = new Bitmap(ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight, PixelFormat.Format32bppRgb);

        // Window Events
        Load += OnWindowLoad;
        FormClosing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
        KeyUp += OnWindowKeyUp;
        Deactivate += (s, e) => _machine.Keyboard.Reset(); // Clear stuck keys on focus loss
        Paint += OnWindowPaint;
        MouseDown += (s, e) => this.Focus();

        // Drag & Drop media support
        AllowDrop = true;
        DragEnter += (s, e) => {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (s, e) => {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                string file = files[0];
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".dsk")
                {
                    var dsk = DskImage.Load(file);
                    if (dsk != null)
                    {
                        _machine.Fdc.DriveA = dsk;
                        MessageBox.Show($"Mounted floppy disk: {Path.GetFileName(file)}", "Disk Mounted");
                    }
                }
                else if (ext == ".cdt" || ext == ".tzx")
                {
                    var tape = CdtTape.Load(file);
                    if (tape != null)
                    {
                        _machine.Tape = tape;
                        MessageBox.Show($"Mounted tape: {Path.GetFileName(file)}", "Tape Mounted");
                    }
                }
            }
        };
    }

    private static int ScreenHeightEffective() => ScreenRenderer.ScreenHeight * 2; // 544 lines

    protected override void WndProc(ref Message m)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;

        if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
        {
            Keys key = (Keys)(long)m.WParam;
            if (key == Keys.F12)
            {
                OnShowKeyboardMatrixViewer(this, EventArgs.Empty);
            }
            else
            {
                MapKey(key, pressed: true);
            }
        }
        else if (m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP)
        {
            Keys key = (Keys)(long)m.WParam;
            if (key != Keys.F12)
            {
                MapKey(key, pressed: false);
            }
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Prevent Windows Forms from intercepting emulator keys as dialog focus/navigation keys
        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
            key == Keys.Tab || key == Keys.Return || key == Keys.Space || key == Keys.Escape ||
            (key >= Keys.F1 && key <= Keys.F12))
        {
            return false;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;

        // Preserve menu accelerators (Ctrl+R for Reset, Ctrl+P for Pause)
        if ((keyData & Keys.Control) != 0 && (key == Keys.R || key == Keys.P))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Prevent Windows Forms menu strip from swallowing F10, Enter, Space, Escape, Tab, Arrows, or Function keys
        if (key == Keys.F10 || key == Keys.Return || key == Keys.Space ||
            key == Keys.Tab || key == Keys.Escape ||
            key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
            (key >= Keys.F1 && key <= Keys.F12))
        {
            return false;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private MenuStrip CreateMenuStrip()
    {
        var menu = new MenuStrip();

        // Machine Menu
        var machineMenu = new ToolStripMenuItem("&Machine");
        _cpc464Item = new ToolStripMenuItem("CPC 464 (1984)", null, (s, e) => SwitchModel(CpcModel.CPC464)) { Checked = (_machine.Model == CpcModel.CPC464) };
        _cpc664Item = new ToolStripMenuItem("CPC 664 (1985)", null, (s, e) => SwitchModel(CpcModel.CPC664)) { Checked = (_machine.Model == CpcModel.CPC664) };
        _cpc6128Item = new ToolStripMenuItem("CPC 6128 (1985)", null, (s, e) => SwitchModel(CpcModel.CPC6128)) { Checked = (_machine.Model == CpcModel.CPC6128) };
        var cpcPlusItem = new ToolStripMenuItem("CPC 464 / 6128 Plus [Phase 11 Milestone]") { Enabled = false };
        var gx4000Item = new ToolStripMenuItem("GX4000 Console [Phase 11 Milestone]") { Enabled = false };
        var resetItem = new ToolStripMenuItem("&Reset", null, (s, e) => _machine.Reset(), Keys.Control | Keys.R);
        var pauseItem = new ToolStripMenuItem("&Pause / Resume", null, (s, e) => _paused = !_paused, Keys.Control | Keys.P);

        machineMenu.DropDownItems.AddRange(new ToolStripItem[] {
            _cpc464Item, _cpc664Item, _cpc6128Item,
            new ToolStripSeparator(),
            cpcPlusItem, gx4000Item,
            new ToolStripSeparator(),
            resetItem, pauseItem
        });

        // Media Menu
        var mediaMenu = new ToolStripMenuItem("&Media");
        var insertTapeItem = new ToolStripMenuItem("Insert Cassette (.CDT / .TZX)...", null, OnInsertTape);
        var insertDiskItem = new ToolStripMenuItem("Insert Disk A (.DSK)...", null, OnInsertDisk);
        var fastLoadItem = new ToolStripMenuItem("Fast Tape / Disk Loading", null, (s, e) => {
            var item = (ToolStripMenuItem)s!;
            item.Checked = !item.Checked;
            if (_machine.Tape != null) _machine.Tape.FastLoadEnabled = item.Checked;
        }) { Checked = true };
        var ejectMediaItem = new ToolStripMenuItem("Eject All Media", null, (s, e) => {
            _machine.Tape = null;
            _machine.Fdc.DriveA = null;
            _machine.Fdc.DriveB = null;
            MessageBox.Show("All media ejected.", "Media Ejected");
        });
        mediaMenu.DropDownItems.AddRange(new ToolStripItem[] { insertTapeItem, insertDiskItem, fastLoadItem, new ToolStripSeparator(), ejectMediaItem });

        // Display Menu
        var displayMenu = new ToolStripMenuItem("&Display");
        var colorMonItem = new ToolStripMenuItem("Colour Monitor (CTM644)", null, (s, e) => _greenMonitor = false) { Checked = true };
        var greenMonItem = new ToolStripMenuItem("Green Monitor (GT65)", null, (s, e) => _greenMonitor = true);
        var scale1x = new ToolStripMenuItem("Standard 4:3 Window (768 x 544)", null, (s, e) => SetScale(2)) { Checked = true };
        var scale2x = new ToolStripMenuItem("Double Size Window (1536 x 1088)", null, (s, e) => SetScale(4));
        var showFpsItem = new ToolStripMenuItem("Show Frame Rate", null, (s, e) => _showFps = !_showFps) { Checked = true };

        displayMenu.DropDownItems.AddRange(new ToolStripItem[] {
            colorMonItem, greenMonItem,
            new ToolStripSeparator(),
            scale1x, scale2x,
            new ToolStripSeparator(),
            showFpsItem
        });

        // Audio Menu
        var audioMenu = new ToolStripMenuItem("&Audio");
        var muteItem = new ToolStripMenuItem("Mute Audio", null, (s, e) => {
            _audio.Muted = !_audio.Muted;
            if (s is ToolStripMenuItem mi) mi.Checked = _audio.Muted;
        }) { Checked = _audio.Muted };

        var vol100 = new ToolStripMenuItem("Volume 100%", null);
        var vol75 = new ToolStripMenuItem("Volume 75%", null);
        var vol50 = new ToolStripMenuItem("Volume 50%", null);
        var vol25 = new ToolStripMenuItem("Volume 25%", null);

        vol100.Checked = true;
        vol100.Click += (s, e) => SetVolume(1.0f, vol100, vol75, vol50, vol25);
        vol75.Click += (s, e) => SetVolume(0.75f, vol75, vol100, vol50, vol25);
        vol50.Click += (s, e) => SetVolume(0.50f, vol50, vol100, vol75, vol25);
        vol25.Click += (s, e) => SetVolume(0.25f, vol25, vol100, vol75, vol50);

        audioMenu.DropDownItems.AddRange(new ToolStripItem[] {
            muteItem,
            new ToolStripSeparator(),
            vol100, vol75, vol50, vol25
        });

        // Tools Menu
        var toolsMenu = new ToolStripMenuItem("&Tools");
        var matrixViewerItem = new ToolStripMenuItem("Keyboard Matrix Viewer (F12)", null, OnShowKeyboardMatrixViewer);
        var regsItem = new ToolStripMenuItem("Z80 Register State", null, OnShowRegisters);
        var traceItem = new ToolStripMenuItem("CPU Execution Trace", null, OnShowTrace);
        toolsMenu.DropDownItems.AddRange(new ToolStripItem[] { matrixViewerItem, new ToolStripSeparator(), regsItem, traceItem });

        menu.Items.AddRange(new ToolStripItem[] { machineMenu, mediaMenu, displayMenu, audioMenu, toolsMenu });
        return menu;
    }

    private void SetScale(int scale)
    {
        _scale = scale;
        int targetW = ScreenRenderer.ScreenWidth * (_scale / 2);
        int targetH = (ScreenHeightEffective() * _scale / 4) + _menuStrip.Height;
        ClientSize = new Size(targetW, targetH);
        Invalidate();
    }

    private void SwitchModel(CpcModel model)
    {
        lock (_frameLock)
        {
            var oldDriveA = _machine.Fdc.DriveA;
            var oldDriveB = _machine.Fdc.DriveB;
            var oldTape = _machine.Tape;

            _machine = new AmstradMachine(model);
            LoadRomsForModel(model);

            _machine.Fdc.DriveA = oldDriveA;
            _machine.Fdc.DriveB = oldDriveB;
            _machine.Tape = oldTape;

            Text = model switch
            {
                CpcModel.CPC6128 => "Amstrad CPC 6128 (1985) — Complete Family Reconstruction",
                CpcModel.CPC664 => "Amstrad CPC 664 (1985) — Complete Family Reconstruction",
                _ => "Amstrad CPC 464 (1984) — Complete Family Reconstruction"
            };

            if (_cpc464Item != null) _cpc464Item.Checked = (model == CpcModel.CPC464);
            if (_cpc664Item != null) _cpc664Item.Checked = (model == CpcModel.CPC664);
            if (_cpc6128Item != null) _cpc6128Item.Checked = (model == CpcModel.CPC6128);

            _machine.Reset();
            _audio.Reset();
        }
    }

    private void LoadRomsForModel(CpcModel model)
    {
        string amsdosPath = FindRomPath("AMSDOS.ROM");
        switch (model)
        {
            case CpcModel.CPC6128:
            case CpcModel.CPC664:
                {
                    string osPath = FindRomPath("OS6128.ROM");
                    string basicPath = FindRomPath("BASIC1-1.ROM");
                    if (File.Exists(osPath)) _machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
                    if (File.Exists(basicPath)) _machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
                    if (File.Exists(amsdosPath)) _machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));
                    break;
                }
            default: // CPC464
                {
                    string osPath = FindRomPath("OS464.ROM");
                    string basicPath = FindRomPath("BASIC1-0.ROM");
                    if (File.Exists(osPath)) _machine.Memory.LoadLowerRom(File.ReadAllBytes(osPath));
                    if (File.Exists(basicPath)) _machine.Memory.LoadUpperRom(0, File.ReadAllBytes(basicPath));
                    if (File.Exists(amsdosPath)) _machine.Memory.LoadUpperRom(7, File.ReadAllBytes(amsdosPath));
                    break;
                }
        }
    }

    private static string FindRomPath(string filename)
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, "roms", filename);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine("roms", filename);
    }

    private void OnInsertTape(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "CPC Tapes (*.cdt;*.tzx)|*.cdt;*.tzx|All Files (*.*)|*.*" };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            var tape = CdtTape.Load(ofd.FileName);
            if (tape != null)
            {
                _machine.Tape = tape;
                MessageBox.Show($"Loaded tape: {Path.GetFileName(ofd.FileName)}\nTotal pulses: {tape.TotalPulses:N0}\nReady to load with RUN\"\"", "Tape Mounted");
            }
            else
            {
                MessageBox.Show("Failed to parse CDT tape file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OnInsertDisk(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "CPC Disks (*.dsk)|*.dsk|All Files (*.*)|*.*" };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            var dsk = DskImage.Load(ofd.FileName);
            if (dsk != null)
            {
                _machine.Fdc.DriveA = dsk;
                MessageBox.Show($"Loaded floppy disk: {Path.GetFileName(ofd.FileName)}\nFormat: {(dsk.IsExtended ? "Extended" : "Standard")} DSK\nTracks: {dsk.TrackCount}, Sides: {dsk.SideCount}\nReady to use with CAT or RUN\"FILENAME\"", "Disk Mounted");
            }
            else
            {
                MessageBox.Show("Failed to parse DSK floppy disk image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void SetVolume(float vol, ToolStripMenuItem active, params ToolStripMenuItem[] others)
    {
        _audio.Volume = vol;
        active.Checked = true;
        foreach (var item in others) item.Checked = false;
    }

    private void OnShowKeyboardMatrixViewer(object? sender, EventArgs e)
    {
        if (_matrixViewerForm == null || _matrixViewerForm.IsDisposed)
        {
            _matrixViewerForm = new KeyboardMatrixViewerForm(_machine);
        }
        _matrixViewerForm.Show();
        _matrixViewerForm.BringToFront();
    }

    private void OnShowRegisters(object? sender, EventArgs e)
    {
        MessageBox.Show(_machine.Cpu.Reg.ToString(), "Z80 Registers");
    }

    private void OnShowTrace(object? sender, EventArgs e)
    {
        var entries = _machine.Trace.GetRecentEntries(25);
        string text = string.Join(Environment.NewLine, entries);
        MessageBox.Show(text, "Recent CPU Execution Trace");
    }

    private void OnWindowLoad(object? sender, EventArgs e)
    {
        TimeBeginPeriod(1);

        _emuThread = new Thread(EmulationWorker)
        {
            Name = "AmstradCPC_EmuThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _emuThread.Start();
    }

    private void OnWindowClosing(object? sender, FormClosingEventArgs e)
    {
        _running = false;
        _emuThread?.Join(500);
        _audio.Dispose();
        TimeEndPeriod(1);
    }

    private void EmulationWorker()
    {
        var stopwatch = Stopwatch.StartNew();
        long frameDurationTicks = Stopwatch.Frequency / 50; // Exact 50 Hz PAL frame duration (20.0 ms)
        long nextTargetTicks = stopwatch.ElapsedTicks + frameDurationTicks;

        while (_running)
        {
            if (!_paused)
            {
                _machine.StepFrame();

                // Send downsampled 44.1 kHz audio to WinMM
                if (_machine.AudioBufferCount > 0)
                {
                    _audio.WriteSamples(_machine.AudioBuffer, _machine.AudioBufferCount);
                    _machine.ClearAudioBuffer();
                }

                // Copy framebuffer when UI is ready for paint
                if (!_paintPending)
                {
                    _paintPending = true;
                    lock (_frameLock)
                    {
                        uint[] src = _machine.Renderer.Framebuffer;
                        if (_greenMonitor)
                        {
                            for (int i = 0; i < src.Length; i++)
                            {
                                uint c = src[i];
                                int r = (int)((c >> 16) & 0xFF);
                                int g = (int)((c >> 8) & 0xFF);
                                int b = (int)(c & 0xFF);
                                int lum = (r * 30 + g * 59 + b * 11) / 100;
                                _framebufferCopy[i] = unchecked((int)(0xFF000000 | ((uint)(lum * 0.4f) << 16) | ((uint)lum << 8) | (uint)(lum * 0.1f)));
                            }
                        }
                        else
                        {
                            Buffer.BlockCopy(src, 0, _framebufferCopy, 0, src.Length * sizeof(int));
                        }
                    }

                    try
                    {
                        BeginInvoke((MethodInvoker)Invalidate);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }

                _frameCount++;
                if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    _fps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
                    _frameCount = 0;
                    _fpsStopwatch.Restart();
                }
            }

            // High precision timing loop with audio dynamic rate pacing
            if (_audio.QueuedCount >= 3)
            {
                long currentTicks = stopwatch.ElapsedTicks;
                long remainingTicks = nextTargetTicks - currentTicks;
                if (remainingTicks > 0)
                {
                    int remainingMs = (int)(remainingTicks * 1000 / Stopwatch.Frequency);
                    if (remainingMs > 2)
                    {
                        Thread.Sleep(remainingMs - 1);
                    }
                    while (stopwatch.ElapsedTicks < nextTargetTicks)
                    {
                        Thread.Yield();
                    }
                }
            }

            nextTargetTicks += frameDurationTicks;
            if (stopwatch.ElapsedTicks > nextTargetTicks + frameDurationTicks)
            {
                nextTargetTicks = stopwatch.ElapsedTicks + frameDurationTicks;
            }
        }
    }

    private void OnWindowPaint(object? sender, PaintEventArgs e)
    {
        lock (_frameLock)
        {
            var data = _displayBitmap.LockBits(
                new Rectangle(0, 0, ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb);

            Marshal.Copy(_framebufferCopy, 0, data.Scan0, _framebufferCopy.Length);
            _displayBitmap.UnlockBits(data);
        }

        _paintPending = false;

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        int menuHeight = _menuStrip.Height;
        int renderHeight = ClientSize.Height - menuHeight;
        e.Graphics.DrawImage(_displayBitmap, 0, menuHeight, ClientSize.Width, renderHeight);

        if (_showFps)
        {
            string status = $"50 Hz | FPS: {_fps:F1} | Cycles: {_machine.Cpu.TotalCycles:N0}";
            e.Graphics.FillRectangle(_fpsBgBrush, 4, menuHeight + 4, 300, 22);
            e.Graphics.DrawString(status, _fpsFont, Brushes.Yellow, 8, menuHeight + 6);
        }
    }

    #region Authentic Keyboard Matrix Mapping

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        MapKey(e.KeyCode, pressed: true);
        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        MapKey(e.KeyCode, pressed: false);
        e.Handled = true;
    }

    private void MapKey(Keys key, bool pressed)
    {
        MapKey(_machine.Keyboard, key, pressed);
    }

    public static void MapKey(KeyboardMatrix kb, Keys key, bool pressed)
    {
        switch (key)
        {
            // Row 0: Numpad and cursors (and Joy0 emulation)
            case Keys.Down:
                kb.SetKeyState(0, 2, pressed);
                kb.SetKeyState(9, 1, pressed); // Joy0 Down
                break;
            case Keys.Right:
                kb.SetKeyState(0, 1, pressed);
                kb.SetKeyState(9, 3, pressed); // Joy0 Right
                break;
            case Keys.Up:
                kb.SetKeyState(0, 0, pressed);
                kb.SetKeyState(9, 0, pressed); // Joy0 Up
                break;
            case Keys.Left:
                kb.SetKeyState(1, 0, pressed);
                kb.SetKeyState(9, 2, pressed); // Joy0 Left
                break;
            case Keys.NumPad3:
            case Keys.F3: kb.SetKeyState(0, 5, pressed); break;
            case Keys.NumPad6:
            case Keys.F6: kb.SetKeyState(0, 4, pressed); break;
            case Keys.NumPad9:
            case Keys.F9: kb.SetKeyState(0, 3, pressed); break;

            // Row 1: Function keys
            case Keys.NumPad0:
            case Keys.F10:
                kb.SetKeyState(1, 7, pressed);
                break;
            case Keys.NumPad2:
            case Keys.F2:
                kb.SetKeyState(1, 6, pressed);
                break;
            case Keys.NumPad1:
            case Keys.F1:
                kb.SetKeyState(1, 5, pressed);
                break;
            case Keys.NumPad5:
            case Keys.F5:
                kb.SetKeyState(1, 4, pressed);
                break;
            case Keys.NumPad8:
            case Keys.F8:
                kb.SetKeyState(1, 3, pressed);
                break;
            case Keys.NumPad7:
            case Keys.F7:
                kb.SetKeyState(1, 2, pressed);
                break;

            // Row 2: Control, Return, Shift, F4
            case Keys.ControlKey: kb.SetKeyState(2, 7, pressed); break;
            case Keys.ShiftKey: kb.SetKeyState(2, 5, pressed); break;
            case Keys.NumPad4:
            case Keys.F4: kb.SetKeyState(2, 4, pressed); break;
            case Keys.Return:
                kb.SetKeyState(2, 2, pressed);
                kb.SetKeyState(0, 6, pressed); // NumPad Enter
                break;

            // Row 3: Symbols & P
            case Keys.P: kb.SetKeyState(3, 3, pressed); break;
            case Keys.OemPeriod: kb.SetKeyState(3, 7, pressed); break;
            case Keys.OemQuestion: kb.SetKeyState(3, 6, pressed); break;
            case Keys.OemSemicolon: kb.SetKeyState(3, 4, pressed); break;
            case Keys.OemMinus: kb.SetKeyState(3, 1, pressed); break;

            // Row 4: Letters & Numbers
            case Keys.Oemcomma: kb.SetKeyState(4, 7, pressed); break;
            case Keys.M: kb.SetKeyState(4, 6, pressed); break;
            case Keys.K: kb.SetKeyState(4, 5, pressed); break;
            case Keys.L: kb.SetKeyState(4, 4, pressed); break;
            case Keys.I: kb.SetKeyState(4, 3, pressed); break;
            case Keys.O: kb.SetKeyState(4, 2, pressed); break;
            case Keys.D9: kb.SetKeyState(4, 1, pressed); break;
            case Keys.D0: kb.SetKeyState(4, 0, pressed); break;

            // Row 5: Space, N, J, H, Y, U, 7, 8 (Space also triggers Joy0 Fire 1)
            case Keys.Space:
                kb.SetKeyState(5, 7, pressed);
                kb.SetKeyState(9, 4, pressed); // Joy0 Fire 1

                break;
            case Keys.Alt:
            case Keys.Menu:
                kb.SetKeyState(9, 5, pressed); // Joy0 Fire 2
                break;
            case Keys.N: kb.SetKeyState(5, 6, pressed); break;
            case Keys.J: kb.SetKeyState(5, 5, pressed); break;
            case Keys.H: kb.SetKeyState(5, 4, pressed); break;
            case Keys.Y: kb.SetKeyState(5, 3, pressed); break;
            case Keys.U: kb.SetKeyState(5, 2, pressed); break;
            case Keys.D7: kb.SetKeyState(5, 1, pressed); break;
            case Keys.D8: kb.SetKeyState(5, 0, pressed); break;

            // Row 6: V, B, F, G, T, R, 5, 6
            case Keys.V: kb.SetKeyState(6, 7, pressed); break;
            case Keys.B: kb.SetKeyState(6, 6, pressed); break;
            case Keys.F: kb.SetKeyState(6, 5, pressed); break;
            case Keys.G: kb.SetKeyState(6, 4, pressed); break;
            case Keys.T: kb.SetKeyState(6, 3, pressed); break;
            case Keys.R: kb.SetKeyState(6, 2, pressed); break;
            case Keys.D5: kb.SetKeyState(6, 1, pressed); break;
            case Keys.D6: kb.SetKeyState(6, 0, pressed); break;

            // Row 7: X, C, D, S, W, E, 3, 4
            case Keys.X: kb.SetKeyState(7, 7, pressed); break;
            case Keys.C: kb.SetKeyState(7, 6, pressed); break;
            case Keys.D: kb.SetKeyState(7, 5, pressed); break;
            case Keys.S: kb.SetKeyState(7, 4, pressed); break;
            case Keys.W: kb.SetKeyState(7, 3, pressed); break;
            case Keys.E: kb.SetKeyState(7, 2, pressed); break;
            case Keys.D3: kb.SetKeyState(7, 1, pressed); break;
            case Keys.D4: kb.SetKeyState(7, 0, pressed); break;

            // Row 8: Z, Caps, A, Tab, Q, Escape, 2, 1
            case Keys.Z: kb.SetKeyState(8, 7, pressed); break;
            case Keys.Capital: kb.SetKeyState(8, 6, pressed); break;
            case Keys.A: kb.SetKeyState(8, 5, pressed); break;
            case Keys.Tab: kb.SetKeyState(8, 4, pressed); break;
            case Keys.Q: kb.SetKeyState(8, 3, pressed); break;
            case Keys.Escape: kb.SetKeyState(8, 2, pressed); break;
            case Keys.D2: kb.SetKeyState(8, 1, pressed); break;
            case Keys.D1: kb.SetKeyState(8, 0, pressed); break;

            // Row 9: Delete / Backspace
            case Keys.Back:
            case Keys.Delete:
                kb.SetKeyState(9, 7, pressed);
                break;
        }
    }

    #endregion
}
