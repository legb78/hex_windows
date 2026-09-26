using System.Diagnostics.CodeAnalysis;

namespace HexWin.Ui;

/// <summary>
/// Turns the sizes the settings window was designed with, at 96 dots per
/// inch, into pixels on the screen at hand.
///
/// <para>The window is laid out by hand, and does its own scaling for that
/// reason. Windows Forms' automatic scaling resizes a container before its
/// children: a card that measures its rows on resize measures them at their
/// old size, then the rows grow and spill out of it. Scaling every number
/// once, up front, leaves nothing to happen in the wrong order.</para>
///
/// <para>The process is system-aware (the Windows Forms default), so one
/// factor holds for the whole session; on a second screen with another
/// density, Windows stretches the window itself.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads the density of the screen.")]
internal static class Dpi
{
    public static readonly float Factor = ReadFactor();

    public static int S(int logical) => (int)Math.Round(logical * Factor);

    public static float S(float logical) => logical * Factor;

    private static float ReadFactor()
    {
        using Graphics screen = Graphics.FromHwnd(0);
        return screen.DpiX / 96f;
    }
}
