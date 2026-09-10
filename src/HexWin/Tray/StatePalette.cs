using System.Diagnostics.CodeAnalysis;

namespace HexWin.Tray;

/// <summary>
/// One colour per state, for the tray icon and the on-screen circle alike.
///
/// They are the same signal shown twice, in two places the eye reaches at
/// different moments: the tray when you go looking, the circle when you are
/// watching your own text. Reading a different colour in each would be worse
/// than showing nothing.
///
/// The hues are deliberately distinct rather than shades of one another: the
/// icon has to stay legible at sixteen pixels across, over a tray background
/// that may be light or dark.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "A table of colours; nothing to decide.")]
internal static class StatePalette
{
    public static Color For(DictationState state) => state switch
    {
        DictationState.Loading => Color.FromArgb(134, 142, 150),
        DictationState.Idle => Color.FromArgb(25, 113, 194),
        DictationState.Recording => Color.FromArgb(224, 49, 49),
        DictationState.Transcribing => Color.FromArgb(232, 89, 12),
        _ => Color.FromArgb(64, 64, 64),
    };
}
