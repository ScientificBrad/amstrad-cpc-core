namespace AmstradCPC.Core.Input;

/// <summary>
/// Authentic Amstrad CPC 10-Row x 8-Column Keyboard Matrix.
/// Active low: unpressed keys return 1, pressed keys pull the line to 0.
/// </summary>
public sealed class KeyboardMatrix
{
    // 10 hardware rows, each with 8 column bits. Default 0xFF (all keys up).
    public byte[] Rows { get; } = new byte[10];

    public KeyboardMatrix()
    {
        Reset();
    }

    public void Reset()
    {
        Array.Fill(Rows, (byte)0xFF);
    }

    public void SetKeyState(int row, int col, bool pressed)
    {
        if (row < 0 || row >= 10 || col < 0 || col >= 8) return;

        if (pressed)
        {
            Rows[row] = (byte)(Rows[row] & ~(1 << col));
        }
        else
        {
            Rows[row] = (byte)(Rows[row] | (1 << col));
        }
    }

    public byte ReadRow(int row)
    {
        if (row >= 0 && row < 10)
        {
            return Rows[row];
        }
        return 0xFF;
    }
}
