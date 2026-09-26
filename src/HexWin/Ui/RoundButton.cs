using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace HexWin.Ui;

/// <summary>
/// A button with round corners: filled with the accent for the main action,
/// outlined for the others. A Button underneath, so Enter, Space, the dialog
/// result and the screen readers work unchanged.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms painting.")]
internal sealed class RoundButton : Button
{
    private readonly Theme _theme;
    private readonly bool _primary;
    private bool _hovered;
    private bool _pressed;

    public RoundButton(Theme theme, string text, bool primary = false)
    {
        _theme = theme;
        _primary = primary;

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);

        Text = text;
        Font = Theme.Body;
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        BackColor = theme.Card;

        Size measured = TextRenderer.MeasureText(text, Font);
        Size = new Size(Math.Max(Dpi.S(96), measured.Width + Dpi.S(32)), Dpi.S(32));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics graphics = pevent.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using GraphicsPath shape = Theme.RoundedRectangle(bounds, Dpi.S(6f));

        Color fill = _primary ? _theme.Accent : _hovered ? _theme.Hover : _theme.Control;

        if (_primary && (_hovered || _pressed))
        {
            // Lighter on hover in the dark theme, darker in the light one: the
            // accent moves away from the background either way.
            fill = ControlPaint.Light(fill, _pressed ? 0.1f : 0.2f);

            if (!_theme.IsDark)
            {
                fill = ControlPaint.Dark(_theme.Accent, _pressed ? 0.15f : 0.08f);
            }
        }

        using (var brush = new SolidBrush(Enabled ? fill : _theme.Hover))
        {
            graphics.FillPath(brush, shape);
        }

        if (!_primary)
        {
            using var edge = new Pen(_theme.ControlBorder, 1f);
            graphics.DrawPath(edge, shape);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(_theme.Text, 1f) { DashStyle = DashStyle.Dot };
            using GraphicsPath focus = Theme.RoundedRectangle(RectangleF.Inflate(bounds, -Dpi.S(3f), -Dpi.S(3f)), Dpi.S(4f));
            graphics.DrawPath(ring, focus);
        }

        Color text = !Enabled ? _theme.SecondaryText : _primary ? _theme.OnAccent : _theme.Text;

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
