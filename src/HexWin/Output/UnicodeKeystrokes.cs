namespace HexWin.Output;

/// <summary>A single keystroke to inject: one UTF-16 character, down or up.</summary>
/// <param name="Unit">UTF-16 code unit sent to Windows as-is.</param>
/// <param name="IsKeyUp">True for the release.</param>
public readonly record struct Keystroke(ushort Unit, bool IsKeyUp);

/// <summary>
/// Turns text into simulated keystrokes.
///
/// The fallback for when pasting does not get through: some applications —
/// terminals, virtual machines, games — ignore the clipboard but accept
/// keystrokes.
///
/// Sending happens in Unicode rather than in key codes, which sidesteps the
/// keyboard layout question entirely. On a French keyboard, "a" and "q" are
/// not where a program would expect them, and "é" has no key code at all on a
/// US keyboard. Sending the code unit directly makes the character arrive
/// whatever layout is active.
///
/// Pure logic, so testable without touching the keyboard.
/// </summary>
public static class UnicodeKeystrokes
{
    /// <summary>
    /// Builds the sequence of keystrokes matching the text.
    ///
    /// Each character yields two keystrokes — press then release. Characters
    /// outside the basic multilingual plane, emoji included, take two UTF-16
    /// units that must be sent separately: Windows recombines them by itself.
    /// </summary>
    public static Keystroke[] Build(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var strokes = new List<Keystroke>(text.Length * 2);

        foreach (char unit in text)
        {
            // Line endings need the Enter key, not a character: sent as
            // Unicode, they insert nothing at all.
            if (unit == '\n')
            {
                strokes.Add(new Keystroke(Return, IsKeyUp: false));
                strokes.Add(new Keystroke(Return, IsKeyUp: true));
                continue;
            }

            // The carriage return of a Windows line ending is skipped: the
            // break is already produced by the newline character.
            if (unit == '\r')
            {
                continue;
            }

            strokes.Add(new Keystroke(unit, IsKeyUp: false));
            strokes.Add(new Keystroke(unit, IsKeyUp: true));
        }

        return [.. strokes];
    }

    /// <summary>
    /// Code of the Enter key. Unlike the other keystrokes, this one is a
    /// virtual key code and not a Unicode code unit.
    /// </summary>
    public const ushort Return = 0x0D;

    /// <summary>True if the keystroke means the Enter key rather than a character.</summary>
    public static bool IsReturn(Keystroke stroke) => stroke.Unit == Return;
}
