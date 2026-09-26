using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace HexWin.Ui;

/// <summary>
/// An on/off switch. A CheckBox underneath, so the keyboard (Space), the
/// screen readers and CheckedChanged all behave as they already do; only the
/// painting changes.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms painting.")]
internal sealed class ToggleSwitch : CheckBox
{
    private readonly Theme _theme;
    private bool _hovered;

    public ToggleSwitch(Theme theme)
    {
        _theme = theme;

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);

        AutoSize = false;
        Size = new Size(Dpi.S(44), Dpi.S(22));
        Cursor = Cursors.Hand;
        BackColor = theme.Card;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventargs);
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventargs);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics graphics = pevent.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Everything is proportional to the control, so it follows the scaling
        // Windows Forms applies on a high-density screen.
        var track = new RectangleF(1.5f, 1.5f, Width - 4f, Height - 4f);
        float radius = track.Height / 2f;

        using (GraphicsPath path = Theme.RoundedRectangle(track, radius))
        {
            if (Checked)
            {
                using var fill = new SolidBrush(_theme.Accent);
                graphics.FillPath(fill, path);
            }
            else
            {
                using var fill = new SolidBrush(_hovered ? _theme.Hover : _theme.Control);
                using var outline = new Pen(_theme.SecondaryText, 1.2f);
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }
        }

        float knob = track.Height * (_hovered ? 0.62f : 0.54f);
        float inset = (track.Height - knob) / 2f;
        float x = Checked ? track.Right - inset - knob : track.Left + inset;

        using (var knobBrush = new SolidBrush(Checked ? _theme.OnAccent : _theme.SecondaryText))
        {
            graphics.FillEllipse(knobBrush, x, track.Top + inset, knob, knob);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(_theme.Text, 1f) { DashStyle = DashStyle.Dot };
            using GraphicsPath focus = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f), radius + 1);
            graphics.DrawPath(ring, focus);
        }
    }
}
