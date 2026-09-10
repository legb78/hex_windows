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
/// The colours come from <see cref="StatePalette"/>, shared with the on-screen
/// circle: the two show the same signal in two places, and must never disagree
/// about what a state looks like.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "GDI+ shell: produces graphical resources of the system.")]
internal sealed partial class TrayIcons : IDisposable
{
    private const int Size = 32;

    private readonly Dictionary<DictationState, Icon> _icons = [];

    public TrayIcons()
    {
        foreach (DictationState state in Enum.GetValues<DictationState>())
        {
            _icons[state] = Build(
                StatePalette.For(state),
                cross: state == DictationState.Failed);
        }
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
