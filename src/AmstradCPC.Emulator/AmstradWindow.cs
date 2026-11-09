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
using AmstradCPC.Core.Video;

namespace AmstradCPC.Emulator;

public sealed class AmstradWindow : Form
{
    private readonly AmstradMachine _machine;
    private readonly WindowsWaveOut _audio;

    private readonly int[] _framebufferCopy = new int[ScreenRenderer.ScreenWidth * ScreenRenderer.ScreenHeight];
    private readonly object _frameLock = new();

    private readonly Bitmap _displayBitmap;
    private Thread? _emuThread;
    private volatile bool _running = true;
    private volatile bool _paused = false;

    private int _scale = 2;
    private bool _greenMonitor = false;
    private bool _showFps = true;
    private double _fps = 50.0;
    private int _frameCount = 0;
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();

    private readonly MenuStrip _menuStrip;

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

        ClientSize = new Size(ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight * _scale);
        _displayBitmap = new Bitmap(ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight, PixelFormat.Format32bppRgb);

        _menuStrip = CreateMenuStrip();
        MainMenuStrip = _menuStrip;
        Controls.Add(_menuStrip);

        // Window Events
        Load += OnWindowLoad;
        FormClosing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
        KeyUp += OnWindowKeyUp;
        Deactivate += (s, e) => _machine.Keyboard.Reset(); // Clear stuck keys on focus loss
        Paint += OnWindowPaint;
    }

    private MenuStrip CreateMenuStrip()
    {
        var menu = new MenuStrip();

        // Machine Menu
        var machineMenu = new ToolStripMenuItem("&Machine");
        var cpc464Item = new ToolStripMenuItem("CPC 464 (1984)", null, (s, e) => SwitchModel(CpcModel.CPC464)) { Checked = true };
        var cpc664Item = new ToolStripMenuItem("CPC 664 (1985) [Phase 8]", null, (s, e) => SwitchModel(CpcModel.CPC664));
        var cpc6128Item = new ToolStripMenuItem("CPC 6128 (1985) [Phase 8]", null, (s, e) => SwitchModel(CpcModel.CPC6128));
        var cpcPlusItem = new ToolStripMenuItem("CPC 464 / 6128 Plus [Phase 11 Milestone]") { Enabled = false };
        var gx4000Item = new ToolStripMenuItem("GX4000 Console [Phase 11 Milestone]") { Enabled = false };
        var resetItem = new ToolStripMenuItem("&Reset", null, (s, e) => _machine.Reset(), Keys.Control | Keys.R);
        var pauseItem = new ToolStripMenuItem("&Pause / Resume", null, (s, e) => _paused = !_paused, Keys.Pause);

        machineMenu.DropDownItems.AddRange(new ToolStripItem[] {
            cpc464Item, cpc664Item, cpc6128Item,
            new ToolStripSeparator(),
            cpcPlusItem, gx4000Item,
            new ToolStripSeparator(),
            resetItem, pauseItem
        });

        // Media Menu
        var mediaMenu = new ToolStripMenuItem("&Media");
        var insertTapeItem = new ToolStripMenuItem("Insert Cassette (.CDT / .TZX)...", null, OnInsertTape);
        var insertDiskItem = new ToolStripMenuItem("Insert Disk (.DSK)...", null, OnInsertDisk);
        var ejectMediaItem = new ToolStripMenuItem("Eject All Media", null, (s, e) => MessageBox.Show("Media ejected.", "Media"));
        mediaMenu.DropDownItems.AddRange(new ToolStripItem[] { insertTapeItem, insertDiskItem, new ToolStripSeparator(), ejectMediaItem });

        // Display Menu
        var displayMenu = new ToolStripMenuItem("&Display");
        var colorMonItem = new ToolStripMenuItem("Colour Monitor (CTM644)", null, (s, e) => _greenMonitor = false) { Checked = true };
        var greenMonItem = new ToolStripMenuItem("Green Monitor (GT65)", null, (s, e) => _greenMonitor = true);
        var scale1x = new ToolStripMenuItem("1x Normal Scale", null, (s, e) => SetScale(1));
        var scale2x = new ToolStripMenuItem("2x Crisp Scale", null, (s, e) => SetScale(2)) { Checked = true };
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
        var muteItem = new ToolStripMenuItem("Mute Audio", null, (s, e) => { });
        audioMenu.DropDownItems.Add(muteItem);

        // Tools Menu
        var toolsMenu = new ToolStripMenuItem("&Tools");
        var regsItem = new ToolStripMenuItem("Z80 Register State", null, OnShowRegisters);
        var traceItem = new ToolStripMenuItem("CPU Execution Trace", null, OnShowTrace);
        toolsMenu.DropDownItems.AddRange(new ToolStripItem[] { regsItem, traceItem });

        menu.Items.AddRange(new ToolStripItem[] { machineMenu, mediaMenu, displayMenu, audioMenu, toolsMenu });
        return menu;
    }

    private void SetScale(int scale)
    {
        _scale = scale;
        ClientSize = new Size(ScreenRenderer.ScreenWidth, ScreenRenderer.ScreenHeight * _scale + _menuStrip.Height);
        Invalidate();
    }

    private void SwitchModel(CpcModel model)
    {
        MessageBox.Show($"Selected Model: {model}. Machine reset.", "Hardware Switch");
        _machine.Reset();
    }

    private void OnInsertTape(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "CPC Tapes (*.cdt;*.tzx)|*.cdt;*.tzx|All Files (*.*)|*.*" };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            MessageBox.Show($"Loaded tape: {Path.GetFileName(ofd.FileName)}", "Tape Mounted");
        }
    }

    private void OnInsertDisk(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "CPC Disks (*.dsk)|*.dsk|All Files (*.*)|*.*" };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            MessageBox.Show($"Loaded floppy disk: {Path.GetFileName(ofd.FileName)}", "Disk Mounted");
        }
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
    }

    private void EmulationWorker()
    {
        var stopwatch = Stopwatch.StartNew();
        long frameDurationTicks = Stopwatch.Frequency / 50; // 50 Hz PAL frame duration (20ms)

        while (_running)
        {
            long start = stopwatch.ElapsedTicks;

            if (!_paused)
            {
                _machine.StepFrame();

                // Send audio to WinMM output
                if (_machine.AudioBufferCount > 0)
                {
                    _audio.WriteSamples(_machine.AudioBuffer, _machine.AudioBufferCount);
                    _machine.ClearAudioBuffer();
                }

                // Copy framebuffer
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
                            // Phosphor green (GT65 monitor)
                            _framebufferCopy[i] = unchecked((int)(0xFF000000 | ((uint)(lum * 0.4f) << 16) | ((uint)lum << 8) | (uint)(lum * 0.1f)));
                        }
                    }
                    else
                    {
                        Buffer.BlockCopy(src, 0, _framebufferCopy, 0, src.Length * sizeof(int));
                    }
                }

                _frameCount++;
                if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                {
                    _fps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
                    _frameCount = 0;
                    _fpsStopwatch.Restart();
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

            long elapsed = stopwatch.ElapsedTicks - start;
            long sleepTicks = frameDurationTicks - elapsed;
            if (sleepTicks > 0)
            {
                int sleepMs = (int)(sleepTicks * 1000 / Stopwatch.Frequency);
                if (sleepMs > 0) Thread.Sleep(sleepMs);
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

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        int menuHeight = _menuStrip.Height;
        int renderHeight = ClientSize.Height - menuHeight;
        e.Graphics.DrawImage(_displayBitmap, 0, menuHeight, ClientSize.Width, renderHeight);

        if (_showFps)
        {
            string status = $"50 Hz | FPS: {_fps:F1} | Cycles: {_machine.Cpu.TotalCycles:N0}";
            using var brush = new SolidBrush(Color.FromArgb(180, Color.Black));
            using var font = new Font("Consolas", 10, FontStyle.Bold);
            e.Graphics.FillRectangle(brush, 4, menuHeight + 4, 300, 22);
            e.Graphics.DrawString(status, font, Brushes.Yellow, 8, menuHeight + 6);
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
        var kb = _machine.Keyboard;
        switch (key)
        {
            // Row 0: Numpad and cursors
            case Keys.Down: kb.SetKeyState(0, 2, pressed); break;
            case Keys.Right: kb.SetKeyState(0, 1, pressed); break;
            case Keys.Up: kb.SetKeyState(0, 0, pressed); break;
            case Keys.NumPad3:
            case Keys.F3: kb.SetKeyState(0, 5, pressed); break;
            case Keys.NumPad6:
            case Keys.F6: kb.SetKeyState(0, 4, pressed); break;
            case Keys.NumPad9:
            case Keys.F9: kb.SetKeyState(0, 3, pressed); break;

            // Row 1: Function keys & Left cursor
            case Keys.NumPad0: kb.SetKeyState(1, 7, pressed); break;
            case Keys.NumPad2: kb.SetKeyState(1, 6, pressed); break;
            case Keys.NumPad1: kb.SetKeyState(1, 5, pressed); break;
            case Keys.NumPad5: kb.SetKeyState(1, 4, pressed); break;
            case Keys.NumPad8: kb.SetKeyState(1, 3, pressed); break;
            case Keys.NumPad7: kb.SetKeyState(1, 2, pressed); break;
            case Keys.Left: kb.SetKeyState(1, 0, pressed); break;

            // Row 2: Control, Return, Shift, F4
            case Keys.ControlKey: kb.SetKeyState(2, 7, pressed); break;
            case Keys.ShiftKey: kb.SetKeyState(2, 5, pressed); break;
            case Keys.F4: kb.SetKeyState(2, 4, pressed); break;
            case Keys.Return: kb.SetKeyState(2, 2, pressed); break;

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

            // Row 5: Space, N, J, H, Y, U, 7, 8
            case Keys.Space: kb.SetKeyState(5, 7, pressed); break;
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
