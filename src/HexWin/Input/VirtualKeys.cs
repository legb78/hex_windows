namespace HexWin.Input;

/// <summary>
/// Traduit les noms de touches de settings.json en codes virtuels Windows.
///
/// Les noms génériques — « Ctrl », « Win » — désignent chacun deux touches
/// physiques. Le hook bas niveau, lui, ne voit jamais le code générique : il
/// reçoit toujours la touche gauche ou droite. C'est cette classe qui fait le
/// pont, en admettant les deux.
/// </summary>
public static class VirtualKeys
{
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;
    public const int LeftMenu = 0xA4;        // Alt gauche
    public const int RightMenu = 0xA5;       // Alt droit
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;
    public const int CapsLock = 0x14;
    public const int Space = 0x20;

    /// <summary>Première touche de fonction inexistante sur un clavier physique.</summary>
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
    /// Codes acceptés pour un nom de touche. Un nom générique en rend deux :
    /// « Ctrl » est satisfait par la touche gauche comme par la droite.
    /// </summary>
    public static int[] Resolve(string name)
    {
        if (ByName.TryGetValue(name.Trim(), out int[]? codes))
        {
            return codes;
        }

        // F13 à F24 se suivent dans la table des codes virtuels.
        if (name.Length >= 3
            && (name[0] is 'F' or 'f')
            && int.TryParse(name.AsSpan(1), out int number)
            && number is >= 13 and <= 24)
        {
            return [F13 + (number - 13)];
        }

        throw new ArgumentException($"Touche inconnue : {name}", nameof(name));
    }

    /// <summary>Vrai si le code désigne l'une des deux touches Windows.</summary>
    public static bool IsWindowsKey(int virtualKey) =>
        virtualKey is LeftWindows or RightWindows;
}
