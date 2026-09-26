using System.Diagnostics.CodeAnalysis;

namespace HexWin.Ui;

/// <summary>
/// A slider with its current value written beside it, in the unit the
/// setting is expressed in.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms layout.")]
internal sealed class LabeledSlider : Panel
{
    private readonly Label _value;
    private readonly Func<int, string> _format;

    public LabeledSlider(Theme theme, int minimum, int maximum, int step, int value, Func<int, string> format)
    {
        _format = format;
        BackColor = theme.Card;
        Size = new Size(Dpi.S(216), Dpi.S(28));

        Slider = new Slider(theme, minimum, maximum, step)
        {
            Location = new Point(0, Dpi.S(2)),
            Size = new Size(Dpi.S(150), Dpi.S(24)),
        };

        _value = new Label
        {
            Font = Theme.Body,
            ForeColor = theme.SecondaryText,
            BackColor = theme.Card,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(Dpi.S(152), 0, Dpi.S(64), Dpi.S(28)),
        };

        Slider.ValueChanged += (_, _) => Show(Slider.Value);
        Slider.Value = value;
        Show(Slider.Value);

        Controls.Add(Slider);
        Controls.Add(_value);
    }

    public Slider Slider { get; }

    public int Value => Slider.Value;

    private void Show(int value)
    {
        _value.Text = _format(value);
        Slider.AccessibleDescription = _value.Text;
    }
}
