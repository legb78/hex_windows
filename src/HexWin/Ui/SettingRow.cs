using System.Diagnostics.CodeAnalysis;

namespace HexWin.Ui;

/// <summary>
/// One setting: its name and a one-line explanation on the left, the control
/// that changes it on the right.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows Forms layout.")]
internal sealed class SettingRow : Panel
{
    private static readonly int Gutter = Dpi.S(12);
    private static readonly int LineGap = Dpi.S(2);

    private readonly Label _title;
    private readonly Label? _description;
    private readonly ToolTip _tip = new();

    public SettingRow(Theme theme, string title, string? description, Control editor)
    {
        BackColor = theme.Card;
        Editor = editor;

        _title = new Label
        {
            Text = title,
            Font = Theme.Body,
            ForeColor = theme.Text,
            BackColor = theme.Card,
            AutoSize = false,
            AutoEllipsis = true,
        };

        if (description is not null)
        {
            _description = new Label
            {
                Text = description,
                Font = Theme.Caption,
                ForeColor = theme.SecondaryText,
                BackColor = theme.Card,
                AutoSize = false,
                AutoEllipsis = true,
            };

            // An explanation cut short by the ellipsis is still one hover away.
            _tip.SetToolTip(_description, description);
        }

        Height = Dpi.S(description is null ? 52 : 62);

        // A screen reader lands on the focusable part, which for a slider is
        // inside its panel: that is the control that must carry the name.
        Control named = editor is LabeledSlider labeled ? labeled.Slider : editor;

        if (string.IsNullOrEmpty(named.AccessibleName))
        {
            named.AccessibleName = title;
        }

        Controls.Add(_title);

        if (_description is not null)
        {
            Controls.Add(_description);
        }

        Controls.Add(editor);
    }

    public Control Editor { get; }

    /// <summary>
    /// Replaces the explanation, for a row whose meaning moves with its value.
    /// The tooltip defaults to the same text; a row that shortens what it shows
    /// passes the long form there.
    /// </summary>
    public void SetDescription(string text, string? tip = null)
    {
        if (_description is null)
        {
            return;
        }

        _description.Text = text;
        _tip.SetToolTip(_description, tip ?? text);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int editorLeft = Width - Gutter - Editor.Width;
        Editor.Location = new Point(editorLeft, (Height - Editor.Height) / 2);

        int textWidth = Math.Max(0, editorLeft - (Gutter * 2));
        int titleHeight = _title.PreferredHeight;

        if (_description is null)
        {
            _title.SetBounds(Gutter, (Height - titleHeight) / 2, textWidth, titleHeight);
            return;
        }

        int descriptionHeight = _description.PreferredHeight;
        int top = (Height - titleHeight - descriptionHeight - LineGap) / 2;

        _title.SetBounds(Gutter, top, textWidth, titleHeight);
        _description.SetBounds(Gutter, top + titleHeight + LineGap, textWidth, descriptionHeight);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
