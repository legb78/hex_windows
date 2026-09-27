using System.Globalization;

namespace HexWin.Input;

/// <summary>
/// Translates the key names from settings.json into Windows virtual codes.
///
/// Generic names — "Ctrl", "Win" — each stand for two physical keys. The
/// low-level hook never sees the generic code: it always receives the left or
/// the right key. This class bridges the two, by accepting either.
///
/// <para>Every key the hook can see has a name: the usual ones are spelled out,
/// and any other is written as its code, "VK_E2". A shortcut may use any of
/// them; what a lone key costs the user is for the settings window to say, not
/// for this table to forbid.</para>
/// </summary>
public static class VirtualKeys
{
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;
    public const int LeftMenu = 0xA4;        // left Alt
    public const int RightMenu = 0xA5;       // right Alt
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;
    public const int CapsLock = 0x14;
    public const int Space = 0x20;

    /// <summary>First function key that no physical keyboard carries.</summary>
    public const int F13 = 0x7C;

    private const string RawPrefix = "VK_";

    private static readonly Dictionary<string, int[]> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = [LeftControl, RightControl],
        ["Alt"] = [LeftMenu, RightMenu],
        ["Shift"] = [LeftShift, RightShift],
        ["Win"] = [LeftWindows, RightWindows],
    };

    private static readonly Dictionary<int, string> NameByCode = BuildNames();

    private static readonly Dictionary<string, int> CodeByName = NameByCode.ToDictionary(
        pair => pair.Value,
        pair => pair.Key,
        StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [LeftControl] = "LeftCtrl",
            [RightControl] = "RightCtrl",
            [LeftMenu] = "LeftAlt",
            [RightMenu] = "RightAlt",
            [LeftShift] = "LeftShift",
            [RightShift] = "RightShift",
            [LeftWindows] = "LeftWin",
            [RightWindows] = "RightWin",
            [CapsLock] = "CapsLock",
            [Space] = "Space",

            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0C] = "Clear",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x1B] = "Escape",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x5D] = "Apps",
            [0x5F] = "Sleep",

            [0x6A] = "NumpadMultiply",
            [0x6B] = "NumpadAdd",
            [0x6C] = "NumpadSeparator",
            [0x6D] = "NumpadSubtract",
            [0x6E] = "NumpadDecimal",
            [0x6F] = "NumpadDivide",
            [0x90] = "NumLock",
            [0x91] = "ScrollLock",

            [0xA6] = "BrowserBack",
            [0xA7] = "BrowserForward",
            [0xA8] = "BrowserRefresh",
            [0xA9] = "BrowserStop",
            [0xAA] = "BrowserSearch",
            [0xAB] = "BrowserFavorites",
            [0xAC] = "BrowserHome",
            [0xAD] = "VolumeMute",
            [0xAE] = "VolumeDown",
            [0xAF] = "VolumeUp",
            [0xB0] = "MediaNext",
            [0xB1] = "MediaPrevious",
            [0xB2] = "MediaStop",
            [0xB3] = "MediaPlayPause",
            [0xB4] = "LaunchMail",
            [0xB5] = "LaunchMedia",
            [0xB6] = "LaunchApp1",
            [0xB7] = "LaunchApp2",

            // Punctuation keys print a different character on each layout:
            // the name only says where the key sits, as Windows does.
            [0xBA] = "Oem1",
            [0xBB] = "OemPlus",
            [0xBC] = "OemComma",
            [0xBD] = "OemMinus",
            [0xBE] = "OemPeriod",
            [0xBF] = "Oem2",
            [0xC0] = "Oem3",
            [0xDB] = "Oem4",
            [0xDC] = "Oem5",
            [0xDD] = "Oem6",
            [0xDE] = "Oem7",
            [0xDF] = "Oem8",
            [0xE2] = "Oem102",
        };

        for (int i = 0; i < 26; i++)
        {
            names[0x41 + i] = ((char)('A' + i)).ToString();
        }

        for (int i = 0; i < 10; i++)
        {
            names[0x30 + i] = i.ToString(CultureInfo.InvariantCulture);
            names[0x60 + i] = $"Numpad{i}";
        }

        for (int i = 0; i < 24; i++)
        {
            names[0x70 + i] = $"F{i + 1}";
        }

        return names;
    }

    /// <summary>
    /// Codes accepted for a key name. A generic name yields two: "Ctrl" is
    /// satisfied by the left key as much as by the right one.
    /// </summary>
    public static int[] Resolve(string name)
    {
        string trimmed = name.Trim();

        if (Generic.TryGetValue(trimmed, out int[]? codes))
        {
            return codes;
        }

        if (CodeByName.TryGetValue(trimmed, out int code) || TryParseRaw(trimmed, out code))
        {
            return [code];
        }

        throw new ArgumentException($"Unknown key: {name}", nameof(name));
    }

    /// <summary>
    /// The name a key is written with in settings.json, whatever case or form
    /// it was given in — "vk_41" becomes "A" — or null for no known key.
    /// </summary>
    public static string? Canonical(string? name)
    {
        string trimmed = name?.Trim() ?? "";

        if (Generic.Keys.FirstOrDefault(
            known => string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase)) is { } generic)
        {
            return generic;
        }

        if (CodeByName.TryGetValue(trimmed, out int code) || TryParseRaw(trimmed, out code))
        {
            return NameOf(code);
        }

        return null;
    }

    /// <summary>
    /// The settings.json name of one physical key.
    ///
    /// <para>The reverse of <see cref="Resolve"/>, and deliberately the
    /// <i>sided</i> name: the hook only ever sees the left or the right key, so
    /// a captured shortcut says which one was pressed. Returning "Shift" for the
    /// right Shift would silently widen the shortcut to both keys — and take
    /// away the one the user still types capitals with.</para>
    /// </summary>
    public static string NameOf(int virtualKey) =>
        NameByCode.TryGetValue(virtualKey, out string? name)
            ? name
            : $"{RawPrefix}{virtualKey:X2}";

    /// <summary>True when the code is one of the two Windows keys.</summary>
    public static bool IsWindowsKey(int virtualKey) =>
        virtualKey is LeftWindows or RightWindows;

    /// <summary>
    /// True for a key that types nothing on its own: a modifier, or a function
    /// key past F12, which no keyboard sold today carries.
    /// </summary>
    public static bool TypesNothing(int virtualKey) =>
        virtualKey is LeftControl or RightControl or LeftMenu or RightMenu
            or LeftShift or RightShift or LeftWindows or RightWindows
            or (>= F13 and <= F13 + 11);

    private static bool TryParseRaw(string name, out int code)
    {
        code = 0;

        return name.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(RawPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
            && code is > 0 and <= 0xFF;
    }
}
