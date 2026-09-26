using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Ui;

/// <summary>
/// Asks the Desktop Window Manager for the Windows 11 frame: round corners, a
/// title bar that follows the dark theme, and the Mica material behind it.
///
/// <para>Each attribute is a request, not a requirement. The corner and
/// backdrop attributes arrived with Windows 11, the backdrop in a later build
/// than the corners; a system that does not know one answers with an error
/// code, which is ignored. The window then keeps the classic frame, and
/// nothing else changes. That is the whole reason this goes through DwmSetWindowAttribute
/// rather than a skin — on an older system it degrades to a plain window
/// instead of breaking.</para>
///
/// <para>Mica only shows through the title bar. The client area is painted by
/// Windows Forms, which has no notion of a translucent background; letting
/// the material through there would mean painting every control with alpha,
/// which the stock controls cannot do.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: talks to the Desktop Window Manager.")]
internal static partial class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;

    public static void Apply(nint window, bool dark)
    {
        Set(window, DwmwaUseImmersiveDarkMode, dark ? 1 : 0);
        Set(window, DwmwaWindowCornerPreference, DwmwcpRound);
        Set(window, DwmwaSystemBackdropType, DwmsbtMainWindow);
    }

    private static void Set(nint window, int attribute, int value) =>
        _ = DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
