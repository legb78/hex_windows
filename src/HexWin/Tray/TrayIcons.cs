using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HexWin.Tray;

/// <summary>
/// Draws the tray icons, one per state.
///
/// They are produced by the program rather than shipped as files: they are
/// coloured dots, and embedding them as resources would add nothing but one
/// more binary file to version.
///
/// The state has to be readable at a glance, at sixteen pixels across and with
/// no reliable background — the tray can be light or dark. Hence plainly
/// distinct hues rather than shades.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "GDI+ shell: produces graphical resources of the system.")]
internal sealed partial class TrayIcons : IDisposable
{
    private const int Size = 32;

    private readonly Dictionary<DictationState, Icon> _icons = [];

    public TrayIcons()
    {
        _icons[DictationState.Loading] = Build(Color.FromArgb(134, 142, 150));      // grey
        _icons[DictationState.Idle] = Build(Color.FromArgb(25, 113, 194));          // blue
        _icons[DictationState.Recording] = Build(Color.FromArgb(224, 49, 49));      // red
        _icons[DictationState.Transcribing] = Build(Color.FromArgb(232, 89, 12));   // orange
        _icons[DictationState.Failed] = Build(Color.FromArgb(64, 64, 64), cross: true);
    }

    public Icon this[DictationState state] => _icons[state];

    private static Icon Build(Color color, bool cross = false)
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 2, 2, Size - 5, Size - 5);

            if (cross)
            {
                using var pen = new Pen(Color.White, 4);
                graphics.DrawLine(pen, 11, 11, Size - 12, Size - 12);
                graphics.DrawLine(pen, Size - 12, 11, 11, Size - 12);
            }
        }

        nint handle = bitmap.GetHicon();

        try
        {
            // Icon.FromHandle does not take ownership of the handle: we clone
            // it so the original can be freed without invalidating the icon.
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        foreach (Icon icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
