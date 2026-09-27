using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Ui;

/// <summary>
/// What a key prints on the keyboard layout in use. Punctuation keys sit at
/// the same place on every keyboard but print something different on each:
/// the key a US layout calls ";" is "$" on a French one.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: the answer depends on the layout of the machine.")]
internal static partial class KeyboardLayout
{
    private const uint MapVirtualKeyToChar = 2;

    /// <summary>The character the key types, or null when it types none.</summary>
    public static string? CharacterOf(int virtualKey)
    {
        // The high bit marks a dead key, such as the French circumflex.
        char character = (char)(MapVirtualKeyW((uint)virtualKey, MapVirtualKeyToChar) & 0x7FFFFFFF);

        return character == '\0' ? null : character.ToString();
    }

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint uCode, uint uMapType);
}
