namespace HexWin.Input;

/// <summary>
/// Translates the key names from settings.json into Windows virtual codes.
///
/// Generic names — "Ctrl", "Win" — each stand for two physical keys. The
/// low-level hook never sees the generic code: it always receives the left or
/// the right key. This class bridges the two, by accepting either.
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

    private static readonly Dictionary<string, int[]> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = [LeftControl, RightControl],
        ["LeftCtrl"] = [LeftControl],
        ["RightCtrl"] = [RightControl],

        ["Alt"] = [LeftMenu, RightMenu],
        ["LeftAlt"] = [LeftMenu],
        ["RightAlt"] = [RightMenu],

        ["Shift"] = [LeftShift, RightShift],
        ["LeftShift"] = [LeftShift],
        ["RightShift"] = [RightShift],

        ["Win"] = [LeftWindows, RightWindows],
        ["LeftWin"] = [LeftWindows],
        ["RightWin"] = [RightWindows],

        ["CapsLock"] = [CapsLock],
        ["Space"] = [Space],
    };

    /// <summary>
    /// Codes accepted for a key name. A generic name yields two: "Ctrl" is
    /// satisfied by the left key as much as by the right one.
    /// </summary>
    public static int[] Resolve(string name)
    {
        if (ByName.TryGetValue(name.Trim(), out int[]? codes))
        {
            return codes;
        }

        // F13 through F24 are consecutive in the virtual-code table.
        if (name.Length >= 3
            && (name[0] is 'F' or 'f')
            && int.TryParse(name.AsSpan(1), out int number)
            && number is >= 13 and <= 24)
        {
            return [F13 + (number - 13)];
        }

        throw new ArgumentException($"Unknown key: {name}", nameof(name));
    }

    /// <summary>True when the code is one of the two Windows keys.</summary>
    public static bool IsWindowsKey(int virtualKey) =>
        virtualKey is LeftWindows or RightWindows;
}
