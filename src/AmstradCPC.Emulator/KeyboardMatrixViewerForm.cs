using System;
using System.Drawing;
using System.Windows.Forms;
using AmstradCPC.Core.Input;
using AmstradCPC.Core.Machine;

namespace AmstradCPC.Emulator;

/// <summary>
/// Live visual inspection tool for the Amstrad CPC 10-Row x 8-Column keyboard matrix.
/// Displays active hardware matrix state, real-time key depressions, and scanned PPI rows.
/// </summary>
public sealed class KeyboardMatrixViewerForm : Form
{
    private readonly AmstradMachine _machine;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly Panel _gridPanel;
    private readonly Label _statusLabel;
    private readonly ListBox _eventLog;

    private readonly byte[] _lastRows = new byte[10];

    private static readonly string[,] MatrixLabels = new string[10, 8]
    {
        // Row 0
        { "Up", "Right", "Down", "f9", "f6", "f3", "NumEnter", "f." },
        // Row 1
        { "Left", "Copy", "f7", "f8", "f5", "f1", "f2", "f0" },
        // Row 2
        { "Clr", "[", "Return", "]", "f4", "Shift", "\\", "Control" },
        // Row 3
        { "^", "-", "@", "P", ";", ":", "/", "." },
        // Row 4
        { "0", "9", "O", "I", "L", "K", "M", "," },
        // Row 5
        { "8", "7", "U", "Y", "H", "J", "N", "Space" },
        // Row 6
        { "6", "5", "R", "T", "G", "F", "B", "V" },
        // Row 7
        { "4", "3", "E", "W", "S", "D", "C", "X" },
        // Row 8
        { "1", "2", "Esc", "Q", "Tab", "A", "Caps", "Z" },
        // Row 9
        { "Joy0 Up", "Joy0 Down", "Joy0 Left", "Joy0 Right", "Joy0 Fire1", "Joy0 Fire2", "Spare", "Del" }
    };

    public KeyboardMatrixViewerForm(AmstradMachine machine)
    {
        _machine = machine;
        Text = "Amstrad CPC — Live Keyboard Matrix Viewer";
        ClientSize = new Size(740, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(24, 24, 27);
        ForeColor = Color.White;

        Array.Fill(_lastRows, (byte)0xFF);

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 220),
            BackColor = Color.FromArgb(32, 32, 36),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "  Matrix State: Active-Low (0 = Pressed / Green, 1 = Released)"
        };
        Controls.Add(_statusLabel);

        _gridPanel = new MatrixGridPanel(this)
        {
            Dock = DockStyle.Top,
            Height = 310,
            BackColor = Color.FromArgb(20, 20, 22)
        };
        Controls.Add(_gridPanel);

        var logHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.LightGray,
            BackColor = Color.FromArgb(28, 28, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "  Real-time Matrix Event Log:"
        };
        Controls.Add(logHeader);

        _eventLog = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(16, 16, 18),
            ForeColor = Color.LimeGreen,
            Font = new Font("Consolas", 9f),
            IntegralHeight = false
        };
        Controls.Add(_eventLog);

        _updateTimer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 Hz
        _updateTimer.Tick += OnTimerTick;
        _updateTimer.Start();

        FormClosing += (s, e) => _updateTimer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // Check for state changes to log
        for (int r = 0; r < 10; r++)
        {
            byte current = _machine.Keyboard.Rows[r];
            byte last = _lastRows[r];
            if (current != last)
            {
                for (int c = 0; c < 8; c++)
                {
                    bool wasPressed = (last & (1 << c)) == 0;
                    bool isPressed = (current & (1 << c)) == 0;
                    if (isPressed != wasPressed)
                    {
                        string action = isPressed ? "PRESSED " : "RELEASED";
                        string name = MatrixLabels[r, c];
                        string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {action} Row {r}, Col {c} ({name})";
                        _eventLog.Items.Insert(0, entry);
                        if (_eventLog.Items.Count > 100) _eventLog.Items.RemoveAt(100);
                    }
                }
                _lastRows[r] = current;
            }
        }

        int selectedRow = _machine.Psg.SelectedKeyboardRow;
        _statusLabel.Text = $"  Active PPI Row: {selectedRow}  |  Matrix State: Active-Low (Green = Pressed)";
        _gridPanel.Invalidate();
    }

    private sealed class MatrixGridPanel : Panel
    {
        private readonly KeyboardMatrixViewerForm _parent;
        private static readonly Font CellFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private static readonly Font HeaderFont = new Font("Segoe UI", 8f, FontStyle.Bold);

        public MatrixGridPanel(KeyboardMatrixViewerForm parent)
        {
            _parent = parent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            int startX = 65;
            int startY = 22;
            int cellW = 80;
            int cellH = 26;

            // Draw Column Headers
            for (int c = 0; c < 8; c++)
            {
                g.DrawString($"Col {c}", HeaderFont, Brushes.LightGray, startX + c * cellW + 20, 4);
            }

            int activePpiRow = _parent._machine.Psg.SelectedKeyboardRow;

            // Draw Rows
            for (int r = 0; r < 10; r++)
            {
                int y = startY + r * cellH;
                Brush rowHeaderBrush = (r == activePpiRow) ? Brushes.Yellow : Brushes.LightGray;
                g.DrawString($"Row {r}", HeaderFont, rowHeaderBrush, 10, y + 5);

                byte rowByte = _parent._machine.Keyboard.Rows[r];

                for (int c = 0; c < 8; c++)
                {
                    int x = startX + c * cellW;
                    bool pressed = (rowByte & (1 << c)) == 0;

                    Rectangle rect = new Rectangle(x, y, cellW - 2, cellH - 2);
                    Color bg = pressed ? Color.FromArgb(0, 150, 60) : Color.FromArgb(38, 38, 44);
                    using (var b = new SolidBrush(bg)) g.FillRectangle(b, rect);
                    using (var p = new Pen(pressed ? Color.Lime : Color.FromArgb(60, 60, 65))) g.DrawRectangle(p, rect);

                    string label = MatrixLabels[r, c];
                    Brush textBrush = pressed ? Brushes.White : Brushes.Gainsboro;
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(label, CellFont, textBrush, rect, sf);
                }
            }
        }
    }
}
