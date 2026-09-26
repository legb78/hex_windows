using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace HexWin.Ui;

/// <summary>
/// Colours and type for the settings window, light or dark after the system.
///
/// <para>The values follow the Windows 11 Settings app, so the window reads as
/// part of the system rather than as a foreign skin. Every control in the
/// window is painted from this one table: that is what keeps the dark theme
/// whole, where the stock Windows Forms controls would each leave a light
/// patch — a spin button here, a drop-down there.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "A table of colours and fonts; nothing to decide.")]
internal sealed record Theme(
    bool IsDark,
    Color Window,
    Color Card,
    Color Border,
    Color Text,
    Color SecondaryText,
    Color Accent,
    Color OnAccent,
    Color Hover,
    Color Control,
    Color ControlBorder)
{
    private static readonly Theme Light = new(
        IsDark: false,
        Window: Color.FromArgb(243, 243, 243),
        Card: Color.FromArgb(251, 251, 251),
        Border: Color.FromArgb(229, 229, 229),
        Text: Color.FromArgb(27, 27, 27),
        SecondaryText: Color.FromArgb(96, 96, 96),
        Accent: Color.FromArgb(0, 95, 184),
        OnAccent: Color.White,
        Hover: Color.FromArgb(234, 234, 234),
        Control: Color.White,
        ControlBorder: Color.FromArgb(200, 200, 200));

    private static readonly Theme Dark = new(
        IsDark: true,
        Window: Color.FromArgb(32, 32, 32),
        Card: Color.FromArgb(43, 43, 43),
        Border: Color.FromArgb(29, 29, 29),
        Text: Color.White,
        SecondaryText: Color.FromArgb(200, 200, 200),
        Accent: Color.FromArgb(96, 205, 255),
        OnAccent: Color.Black,
        Hover: Color.FromArgb(50, 50, 50),
        Control: Color.FromArgb(55, 55, 55),
        ControlBorder: Color.FromArgb(80, 80, 80));

    /// <summary>
    /// Windows 11 ships a variable Segoe tuned for screens; Windows 10 only has
    /// the classic one. Asking for a font that is not installed does not fail —
    /// Windows Forms quietly substitutes Microsoft Sans Serif — so the choice
    /// has to be made up front.
    /// </summary>
    private static readonly string FontFamilyName =
        FontFamily.Families.Any(family => family.Name == "Segoe UI Variable Text")
            ? "Segoe UI Variable Text"
            : "Segoe UI";

    public static readonly Font Body = new(FontFamilyName, 9.75f);
    public static readonly Font Caption = new(FontFamilyName, 8.5f);
    public static readonly Font Strong = new(FontFamilyName, 9.75f, FontStyle.Bold);
    public static readonly Font Heading = new(FontFamilyName, 15f, FontStyle.Bold);
    public static readonly Font Section = new(FontFamilyName, 10.5f, FontStyle.Bold);

    /// <summary>The theme the user picked for applications in Windows settings.</summary>
    public static Theme Current => AppsUseDarkTheme() ? Dark : Light;

    private static bool AppsUseDarkTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // Absent before Windows 10 1809, and absent means light.
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A rectangle with round corners, for everything the window paints.</summary>
    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
