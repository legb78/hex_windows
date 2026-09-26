using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace HexWin.Ui;

/// <summary>
/// A row of mutually exclusive options, one of which is always selected.
///
/// <para>Stands in for a drop-down where the options are few enough to show
/// at once: the choice is visible without opening anything, and a dark
/// drop-down list is exactly the kind of control Windows Forms leaves
/// light.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms painting and input.")]
internal sealed class SegmentedControl : Control
{
    private readonly Theme _theme;
    private readonly string[] _options;
    private int _selected;
    private int _hovered = -1;

    public SegmentedControl(Theme theme, params string[] options)
    {
        _theme = theme;
        _options = options;

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);

        TabStop = true;
        Font = Theme.Body;
        BackColor = theme.Card;
        Size = new Size(options.Length * Dpi.S(96), Dpi.S(30));
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int clamped = Math.Clamp(value, 0, _options.Length - 1);

            if (clamped == _selected)
            {
                return;
            }

            _selected = clamped;
            AccessibleDescription = _options[clamped];
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float SegmentWidth => (Width - 1f) / _options.Length;

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left)
        {
            SelectedIndex--;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right)
        {
            SelectedIndex++;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        SelectedIndex = SegmentAt(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int segment = SegmentAt(e.X);

        if (segment != _hovered)
        {
            _hovered = segment;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = -1;
        Invalidate();
        base.OnMouseLeave(e);
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

    private int SegmentAt(int x) => Math.Clamp((int)(x / SegmentWidth), 0, _options.Length - 1);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Dpi.S(6f);
        float gap = Dpi.S(3f);

        using (GraphicsPath frame = Theme.RoundedRectangle(outer, radius))
        using (var fill = new SolidBrush(_theme.Control))
        using (var edge = new Pen(Focused ? _theme.Accent : _theme.ControlBorder, 1f))
        {
            graphics.FillPath(fill, frame);
            graphics.DrawPath(edge, frame);
        }

        float width = SegmentWidth;

        for (int i = 0; i < _options.Length; i++)
        {
            var segment = new RectangleF(outer.Left + (i * width) + gap, outer.Top + gap, width - (gap * 2), outer.Height - (gap * 2));
            Color text = _theme.Text;

            if (i == _selected)
            {
                using GraphicsPath pill = Theme.RoundedRectangle(segment, radius - gap);
                using var accent = new SolidBrush(_theme.Accent);
                graphics.FillPath(accent, pill);
                text = _theme.OnAccent;
            }
            else if (i == _hovered)
            {
                using GraphicsPath pill = Theme.RoundedRectangle(segment, radius - gap);
                using var hover = new SolidBrush(_theme.Hover);
                graphics.FillPath(hover, pill);
            }

            TextRenderer.DrawText(
                graphics,
                _options[i],
                Font,
                Rectangle.Round(segment),
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
