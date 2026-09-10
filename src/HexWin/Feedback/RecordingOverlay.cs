using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HexWin.Feedback;

/// <summary>
/// The circle shown at the top of the screen while the microphone is open.
///
/// <para><b>It must never take the focus.</b> The window a dictation is aimed
/// at is captured with GetForegroundWindow the moment the shortcut is released
/// (see <see cref="Output.TargetWindow"/>). Were this window to become the
/// foreground one, even briefly, it would become that target — and every
/// dictation would be inserted into a window that accepts no text, so the text
/// would simply vanish, with nothing to explain where it went. WS_EX_NOACTIVATE
/// and <see cref="ShowWithoutActivation"/> are what rule that out.
/// WS_EX_TRANSPARENT additionally lets clicks through, so the circle never
/// intercepts anything meant for the window underneath.</para>
///
/// <para>Drawn through UpdateLayeredWindow rather than with a TransparencyKey.
/// A colour key gives one bit of transparency, which leaves every antialiased
/// edge fringed with the key colour; a layered window carries a real alpha
/// channel per pixel, so the outline stays clean over any background.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: needs an interactive session and a real desktop.")]
internal sealed partial class RecordingOverlay : Form
{
    private const int Diameter = 64;

    /// <summary>Gap below the top edge of the usable area.</summary>
    private const int TopMargin = 40;

    /// <summary>Slightly translucent: present without masking what is behind.</summary>
    private const byte Alpha = 235;

    private readonly Bitmap _circle;
    private bool _shown;

    public RecordingOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(Diameter, Diameter);

        _circle = DrawCircle();

        // The window is brought into existence now rather than on the first
        // dictation. Showing it happens inside the keyboard hook callback,
        // which runs on the message loop and must never stall: past the
        // deadline Windows uninstalls the hook without a word.
        _ = Handle;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= ExStyleLayered | ExStyleTransparent | ExStyleNoActivate | ExStyleToolWindow;

            return parameters;
        }
    }

    /// <summary>Belt and braces alongside WS_EX_NOACTIVATE.</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>Shows or hides the circle. Called on the interface thread.</summary>
    public void SetVisible(bool visible)
    {
        if (visible == _shown)
        {
            return;
        }

        _shown = visible;

        if (!visible)
        {
            ShowWindow(Handle, SwHide);
            return;
        }

        // Positioned and painted before being shown: the other way round, the
        // first frame appears as an unpainted rectangle.
        MoveToActiveScreen();
        Render();
        ShowWindow(Handle, SwShowNoActivate);
    }

    /// <summary>
    /// Centres the circle at the top of the screen holding the pointer. On a
    /// multi-screen setup the primary screen is rarely the one being worked on,
    /// and a cue on the wrong monitor is no cue at all.
    /// </summary>
    private void MoveToActiveScreen()
    {
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;

        Location = new Point(
            area.Left + ((area.Width - Diameter) / 2),
            area.Top + TopMargin);
    }

    private void Render()
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);

        // GetHbitmap composites over the colour given; over a fully transparent
        // one that multiplies each channel by its own alpha — exactly the
        // premultiplied form UpdateLayeredWindow expects.
        nint bitmap = _circle.GetHbitmap(Color.FromArgb(0));
        nint previous = SelectObject(memory, bitmap);

        var size = new NativeSize { Width = Diameter, Height = Diameter };
        var origin = new NativePoint { X = 0, Y = 0 };
        var position = new NativePoint { X = Location.X, Y = Location.Y };

        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            BlendFlags = 0,
            SourceConstantAlpha = Alpha,
            AlphaFormat = AcSrcAlpha,
        };

        try
        {
            UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref origin, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private static Bitmap DrawCircle()
    {
        var bitmap = new Bitmap(Diameter, Diameter, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // The blue of the ready-state tray icon, so the two read as one
        // application.
        using var fill = new SolidBrush(Color.FromArgb(25, 113, 194));
        graphics.FillEllipse(fill, 1, 1, Diameter - 3, Diameter - 3);

        // A white rim keeps the circle legible against a blue window behind it.
        using var rim = new Pen(Color.White, 3f);
        graphics.DrawEllipse(rim, 2.5f, 2.5f, Diameter - 6, Diameter - 6);

        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _circle.Dispose();
        }

        base.Dispose(disposing);
    }

    private const int ExStyleTransparent = 0x0000_0020;
    private const int ExStyleToolWindow = 0x0000_0080;
    private const int ExStyleLayered = 0x0008_0000;
    private const int ExStyleNoActivate = 0x0800_0000;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int UlwAlpha = 0x02;

    // System.Drawing.Point and Size cannot cross a LibraryImport boundary:
    // they carry marshalling the source generator refuses to emit. Declaring
    // the Win32 shapes here keeps the rest of the assembly on the default
    // marshalling rules.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int command);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(
        nint hWnd,
        nint destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        nint sourceDc,
        ref NativePoint source,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);
}
