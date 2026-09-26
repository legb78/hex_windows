using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace HexWin.Ui;

/// <summary>
/// A horizontal slider, painted in the window's theme.
///
/// <para>Written rather than borrowed: the stock TrackBar draws its thumb with
/// the light visual style whatever the theme, and NumericUpDown keeps light
/// spin buttons in a dark window. The bounds come from
/// <see cref="Configuration.AppSettings"/>, so the slider cannot offer a value
/// the file would then correct behind the user's back.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms painting and input.")]
internal sealed class Slider : Control
{
    private readonly Theme _theme;
    private int _value;

    public Slider(Theme theme, int minimum, int maximum, int step)
    {
        _theme = theme;
        Minimum = minimum;
        Maximum = maximum;
        Step = Math.Max(1, step);
        _value = minimum;

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);

        TabStop = true;
        Size = new Size(Dpi.S(170), Dpi.S(24));
        Cursor = Cursors.Hand;
        BackColor = theme.Card;
        AccessibleRole = AccessibleRole.Slider;
    }

    public event EventHandler? ValueChanged;

    public int Minimum { get; }

    public int Maximum { get; }

    public int Step { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Maximum);

            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float ThumbDiameter => Height * 0.75f;

    private float TrackLeft => ThumbDiameter / 2f + 1;

    private float TrackRight => Width - (ThumbDiameter / 2f) - 1;

    private float ThumbCentre =>
        Maximum == Minimum
            ? TrackLeft
            : TrackLeft + ((TrackRight - TrackLeft) * (_value - Minimum) / (Maximum - Minimum));

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int? next = e.KeyCode switch
        {
            Keys.Left or Keys.Down => _value - Step,
            Keys.Right or Keys.Up => _value + Step,
            Keys.PageDown => _value - (Step * 10),
            Keys.PageUp => _value + (Step * 10),
            Keys.Home => Minimum,
            Keys.End => Maximum,
            _ => null,
        };

        if (next is { } value)
        {
            Value = value;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();

        if (e.Button == MouseButtons.Left)
        {
            MoveTo(e.X);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            MoveTo(e.X);
        }

        base.OnMouseMove(e);
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

    /// <summary>
    /// The mouse snaps to the step; the keyboard moves by it. A value read from
    /// the file that falls between two steps is kept as it is until the user
    /// actually moves the thumb.
    /// </summary>
    private void MoveTo(int x)
    {
        float ratio = Math.Clamp((x - TrackLeft) / Math.Max(1f, TrackRight - TrackLeft), 0f, 1f);
        float raw = Minimum + (ratio * (Maximum - Minimum));
        int steps = (int)Math.Round((raw - Minimum) / Step);

        Value = Minimum + (steps * Step);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        float centreY = Height / 2f;
        float thickness = Math.Max(3f, Height / 6f);
        float thumb = ThumbCentre;

        // Disabled, the accent goes: the value stays readable, but nothing
        // about the slider invites a drag that would not be taken.
        Color accentColor = Enabled ? _theme.Accent : _theme.SecondaryText;

        using (var rest = new Pen(_theme.ControlBorder, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(rest, TrackLeft, centreY, TrackRight, centreY);
        }

        using (var filled = new Pen(accentColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(filled, TrackLeft, centreY, Math.Max(TrackLeft + 0.1f, thumb), centreY);
        }

        float diameter = ThumbDiameter;
        var outer = new RectangleF(thumb - (diameter / 2f), centreY - (diameter / 2f), diameter, diameter);

        using (var ring = new SolidBrush(_theme.Control))
        using (var ringEdge = new Pen(_theme.ControlBorder, 1f))
        {
            graphics.FillEllipse(ring, outer);
            graphics.DrawEllipse(ringEdge, outer);
        }

        // The inner dot grows under focus, the way the Windows 11 slider does:
        // the keyboard user can see which slider the arrows will move.
        float dot = diameter * (Focused ? 0.55f : 0.42f);

        using (var accent = new SolidBrush(accentColor))
        {
            graphics.FillEllipse(accent, thumb - (dot / 2f), centreY - (dot / 2f), dot, dot);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }
}
