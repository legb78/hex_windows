namespace HexWin.Ui;

/// <summary>
/// The shortcut as the user reads it: "Maj droite" or "Right Shift", not
/// "RightShift".
///
/// <para>The names in settings.json are identifiers, in English, and they stay
/// that way in the file. What the window shows is what is printed on the
/// keyboard, with the side spelled out: telling the left Shift from the right
/// one is the whole point of a shortcut made of a single modifier.</para>
/// </summary>
public static class HotkeyText
{
    /// <summary>One key; a name the language does not know is shown as it is.</summary>
    public static string Describe(UiStrings text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.KeyNames.TryGetValue(key, out string? label) ? label : key;
    }

    /// <summary>The whole shortcut, keys joined the way a keyboard chord is written.</summary>
    public static string Describe(UiStrings text, IEnumerable<string> keys) =>
        string.Join(" + ", keys.Select(key => Describe(text, key)));

    /// <summary>
    /// A warning when the shortcut takes away something the user types with,
    /// or null when it does not.
    ///
    /// <para>Every key of the shortcut is swallowed while it is held, so a
    /// shortcut of one key costs that key for typing. Space alone is the case
    /// that matters: a writer cannot do without it. A lone Shift keeps the
    /// other Shift for capitals, and says so.</para>
    /// </summary>
    public static string? Caveat(UiStrings text, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(text);

        // On layouts with an AltGr key — French among them — Windows sends a
        // left Ctrl along with the right Alt, so one press of AltGr is captured
        // as these two keys. Said plainly, because the user pressed a single key
        // and reads two.
        if (keys.Count == 2
            && keys.Contains("LeftCtrl", StringComparer.OrdinalIgnoreCase)
            && keys.Contains("RightAlt", StringComparer.OrdinalIgnoreCase))
        {
            return text.CaveatAltGr;
        }

        if (keys.Count != 1)
        {
            return null;
        }

        return keys[0].ToUpperInvariant() switch
        {
            "SPACE" => text.CaveatSpace,
            "SHIFT" => text.CaveatBothShifts,
            "LEFTSHIFT" => text.CaveatLeftShift,
            "RIGHTSHIFT" => text.CaveatRightShift,
            "CAPSLOCK" => text.CaveatCapsLock,
            _ => null,
        };
    }
}
