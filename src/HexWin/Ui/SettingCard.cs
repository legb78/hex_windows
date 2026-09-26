using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace HexWin.Ui;

/// <summary>
/// A rounded card holding settings one per row, with a hairline between two
/// rows — the layout of the Windows 11 Settings app.
///
/// <para>The rows are inset from the edges so their square corners never
/// cover the rounded ones of the card, and stacked with a one-pixel gap the
/// card fills with the separator colour. That keeps the card the only thing
/// that paints its outline.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms layout and painting.")]
internal sealed class SettingCard : Panel
{
    private static readonly int Inset = Dpi.S(6);
    private static readonly int Radius = Dpi.S(8);
    private static readonly int SeparatorIndent = Dpi.S(10);

    private readonly Theme _theme;
    private readonly List<Control> _rows = [];

    public SettingCard(Theme theme)
    {
        _theme = theme;

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);

        BackColor = theme.Card;
    }

    public void AddRow(Control row)
    {
        _rows.Add(row);
        Controls.Add(row);
        Relayout();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Relayout();
    }

    private void Relayout()
    {
        int top = Inset / 2;

        foreach (Control row in _rows)
        {
            row.SetBounds(Inset, top, Width - (Inset * 2), row.Height);
            top += row.Height + 1;
        }

        Height = top - 1 + (Inset / 2);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? _theme.Window);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using GraphicsPath shape = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
        using var fill = new SolidBrush(_theme.Card);
        using var edge = new Pen(_theme.Border, 1f);

        graphics.FillPath(fill, shape);
        graphics.DrawPath(edge, shape);

        graphics.SmoothingMode = SmoothingMode.None;
        using var separator = new Pen(_theme.IsDark ? _theme.Window : _theme.Border, 1f);

        for (int i = 0; i < _rows.Count - 1; i++)
        {
            int y = _rows[i].Bottom;
            graphics.DrawLine(separator, Inset + SeparatorIndent, y, Width - Inset - SeparatorIndent, y);
        }
    }
}
