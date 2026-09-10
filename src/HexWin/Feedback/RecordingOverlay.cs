using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HexWin.Configuration;
using HexWin.Tray;

namespace HexWin.Feedback;

/// <summary>
/// The circle shown at the top of the screen while something is happening.
///
/// <para>It is the tray icon repeated where the eye actually is. Same colours,
/// from <see cref="StatePalette"/>, so the two can never disagree: red while
/// the microphone is open, orange while the engine works. Nothing is shown at
/// rest — a permanent mark on the screen would be noise, and the tray already
/// says "ready" to whoever goes looking.</para>
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
    /// <summary>The states worth putting on the screen. Idle is not one.</summary>
    private static readonly DictationState[] PaintedStates =
        [DictationState.Recording, DictationState.Transcribing];

    private readonly int _diameter;
    private readonly int _topMargin;
    private readonly byte _opacity;
    private readonly Dictionary<DictationState, Bitmap> _circles = [];

    private DictationState? _showing;

    public RecordingOverlay(AppSettings settings)
    {
        _diameter = settings.FeedbackSize;
        _topMargin = settings.FeedbackTopMargin;
        _opacity = (byte)settings.FeedbackOpacity;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(_diameter, _diameter);

        foreach (DictationState state in PaintedStates)
        {
            _circles[state] = DrawCircle(_diameter, ColorFor(state, settings.FeedbackColor));
        }

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

    /// <summary>
    /// Shows the circle in the colour of the given state, or hides it when given
    /// none. Called on the interface thread.
    /// </summary>
    public void Apply(DictationState? state)
    {
        if (state == _showing)
        {
            return;
        }

        _showing = state;

        if (state is not { } visible)
        {
            ShowWindow(Handle, SwHide);
            return;
        }

        // Positioned and painted before being shown: the other way round, the
        // first frame appears as an unpainted rectangle.
        MoveToActiveScreen();
        Render(_circles[visible]);
        ShowWindow(Handle, SwShowNoActivate);
    }

    /// <summary>
    /// A configured colour overrides every state; anything else — "auto", or a
    /// value that does not parse — leaves the palette in charge.
    /// </summary>
    private static Color ColorFor(DictationState state, string configured) =>
        HexColor.TryParse(configured, out int rgb)
            ? Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
            : StatePalette.For(state);

    /// <summary>
    /// Centres the circle at the top of the screen holding the pointer. On a
    /// multi-screen setup the primary screen is rarely the one being worked on,
    /// and a cue on the wrong monitor is no cue at all.
    /// </summary>
    private void MoveToActiveScreen()
    {
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;

        // The margin comes from the configuration, so it can be larger than
        // the screen it is applied to. Clamped rather than trusted: a circle
        // pushed off the display would look exactly like a broken feature.
        int top = Math.Clamp(area.Top + _topMargin, area.Top, area.Bottom - _diameter);

        Location = new Point(
            area.Left + ((area.Width - _diameter) / 2),
            top);
    }

    private void Render(Bitmap circle)
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);

        // GetHbitmap composites over the colour given; over a fully transparent
        // one that multiplies each channel by its own alpha — exactly the
        // premultiplied form UpdateLayeredWindow expects.
        nint bitmap = circle.GetHbitmap(Color.FromArgb(0));
        nint previous = SelectObject(memory, bitmap);

        var size = new NativeSize { Width = _diameter, Height = _diameter };
        var origin = new NativePoint { X = 0, Y = 0 };
        var position = new NativePoint { X = Location.X, Y = Location.Y };

        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            BlendFlags = 0,
            SourceConstantAlpha = _opacity,
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

    private static Bitmap DrawCircle(int diameter, Color color)
    {
        var bitmap = new Bitmap(diameter, diameter, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var fill = new SolidBrush(color);
        graphics.FillEllipse(fill, 1, 1, diameter - 3, diameter - 3);

        // A white rim keeps the circle legible whatever colour it is given and
        // whatever sits behind it. Scaled with the diameter, or a large circle
        // would be outlined by a hairline.
        float thickness = Math.Max(2f, diameter / 21f);

        using var rim = new Pen(Color.White, thickness);
        graphics.DrawEllipse(rim, thickness / 2f, thickness / 2f, diameter - 1 - thickness, diameter - 1 - thickness);

        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Bitmap circle in _circles.Values)
            {
                circle.Dispose();
            }

            _circles.Clear();
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
