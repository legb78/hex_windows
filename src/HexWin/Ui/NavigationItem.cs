using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace HexWin.Ui;

/// <summary>
/// One entry of the side bar. The selected one gets a soft background and a
/// short accent bar on its left edge, as in the Windows 11 Settings app.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms painting and input.")]
internal sealed class NavigationItem : Control
{
    private readonly Theme _theme;
    private bool _selected;
    private bool _hovered;

    public NavigationItem(Theme theme, string text)
    {
        _theme = theme;

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);

        Text = text;
        Font = Theme.Body;
        TabStop = true;
        Height = Dpi.S(36);
        Cursor = Cursors.Hand;
        BackColor = theme.Window;
        AccessibleRole = AccessibleRole.PageTab;
        AccessibleName = text;
    }

    public event EventHandler? Chosen;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        Chosen?.Invoke(this, EventArgs.Empty);
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Chosen?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0.5f, 2.5f, Width - 1.5f, Height - 5.5f);

        if (_selected || _hovered)
        {
            using GraphicsPath pill = Theme.RoundedRectangle(bounds, Dpi.S(6f));
            using var fill = new SolidBrush(_selected ? _theme.Card : _theme.Hover);
            graphics.FillPath(fill, pill);
        }

        if (_selected)
        {
            float barHeight = Height * 0.4f;
            using GraphicsPath bar = Theme.RoundedRectangle(new RectangleF(1, (Height - barHeight) / 2f, Dpi.S(3f), barHeight), Dpi.S(1.5f));
            using var accent = new SolidBrush(_theme.Accent);
            graphics.FillPath(accent, bar);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(_theme.Text, 1f) { DashStyle = DashStyle.Dot };
            using GraphicsPath focus = Theme.RoundedRectangle(bounds, Dpi.S(6f));
            graphics.DrawPath(ring, focus);
        }

        var textBounds = new Rectangle(Dpi.S(16), 0, Width - Dpi.S(20), Height);
        TextRenderer.DrawText(
            graphics,
            Text,
            _selected ? Theme.Strong : Font,
            textBounds,
            _theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
