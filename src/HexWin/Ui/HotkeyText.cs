namespace HexWin.Ui;

/// <summary>
/// The shortcut as the user reads it: "Maj droite", not "RightShift".
///
/// <para>The names in settings.json are identifiers, in English, and they stay
/// that way in the file. What the window shows is what is printed on a French
/// keyboard, with the side spelled out: telling the left Shift from the right
/// one is the whole point of a shortcut made of a single modifier.</para>
/// </summary>
public static class HotkeyText
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = "Ctrl",
        ["LeftCtrl"] = "Ctrl gauche",
        ["RightCtrl"] = "Ctrl droit",
        ["Alt"] = "Alt",
        ["LeftAlt"] = "Alt gauche",
        ["RightAlt"] = "Alt Gr",
        ["Shift"] = "Maj",
        ["LeftShift"] = "Maj gauche",
        ["RightShift"] = "Maj droite",
        ["Win"] = "Windows",
        ["LeftWin"] = "Windows gauche",
        ["RightWin"] = "Windows droite",
        ["CapsLock"] = "Verr. Maj",
        ["Space"] = "Espace",
    };

    /// <summary>One key; a name it does not know is shown as it is.</summary>
    public static string Describe(string key) =>
        Labels.TryGetValue(key, out string? label) ? label : key;

    /// <summary>The whole shortcut, keys joined the way a keyboard chord is written.</summary>
    public static string Describe(IEnumerable<string> keys) =>
        string.Join(" + ", keys.Select(Describe));

    /// <summary>
    /// A warning when the shortcut takes away something the user types with,
    /// or null when it does not.
    ///
    /// <para>Every key of the shortcut is swallowed while it is held, so a
    /// shortcut of one key costs that key for typing. Space alone is the case
    /// that matters: a writer cannot do without it. A lone Shift keeps the
    /// other Shift for capitals, and says so.</para>
    /// </summary>
    public static string? Caveat(IReadOnlyList<string> keys)
    {
        if (keys.Count != 1)
        {
            return null;
        }

        return keys[0].ToUpperInvariant() switch
        {
            "SPACE" => "Espace seule ne tapera plus d'espaces tant que HexWin tourne.",
            "SHIFT" => "Aucune touche Maj ne servira plus aux majuscules.",
            "LEFTSHIFT" => "Utilisez Maj droite pour les majuscules.",
            "RIGHTSHIFT" => "Utilisez Maj gauche pour les majuscules.",
            "CAPSLOCK" => "Verr. Maj ne verrouillera plus les majuscules.",
            _ => null,
        };
    }
}
