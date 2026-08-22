using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using HexWin.Configuration;
using HexWin.Input;

namespace HexWin.Output;

/// <summary>
/// Hands the transcribed text back to the active application.
///
/// Two routes. Pasting goes through the clipboard then Ctrl+V: instant
/// whatever the volume, and the default. Simulated typing sends the characters
/// one by one, more slowly, but it gets through in applications that ignore
/// the clipboard.
///
/// Win32 shell: building the keystrokes belongs to
/// <see cref="UnicodeKeystrokes"/>, which is testable.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: SendInput writes into the active window of the real desktop.")]
internal sealed partial class TextInjector
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    private const int ControlKey = VirtualKeys.LeftControl;
    private const ushort KeyV = 0x56;

    /// <summary>
    /// Grace period left to the target application to read the clipboard
    /// before we restore it. Too short and the paste would pick up the old
    /// content; too long and the user would find the dictated text still in
    /// their clipboard if they paste again right away.
    /// </summary>
    private static readonly TimeSpan ClipboardRestoreDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Inserts the text into the active field, using the requested mode.
    /// </summary>
    public static void Insert(string text, InsertionMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (mode == InsertionMode.Type)
        {
            SendAsKeystrokes(text);
            return;
        }

        SendAsPaste(text);
    }

    /// <summary>
    /// Pastes through the clipboard, then puts back whatever was there.
    ///
    /// Restoring matters: without it, every dictation would overwrite what the
    /// user had copied, which gets noticed at the worst possible moment.
    /// </summary>
    private static void SendAsPaste(string text)
    {
        ClipboardSnapshot previous = ClipboardSnapshot.Capture();

        // copy: true keeps the data alive after the process ends. Without that
        // flag, the text would vanish from the clipboard as soon as the
        // application closed, and the user could no longer paste it.
        Clipboard.SetDataObject(BuildPrivateTransfer(text), copy: true);
        SendPasteShortcut();

        // Restoration is deferred: Ctrl+V is asynchronous, and the target
        // application has not read the clipboard yet when SendInput returns.
        RestoreAfterDelay(previous);
    }

    /// <summary>
    /// Wraps the text while asking Windows not to keep it.
    ///
    /// <para>A plain <c>SetDataObject(text)</c> puts the dictation into the
    /// clipboard history — the <c>Win+V</c> one, on by default on many
    /// machines — where it stays readable long after the insertion. And if
    /// cross-device sync is on, it goes to Microsoft: an application that
    /// claims to send nothing over the network cannot afford that.</para>
    ///
    /// <para>The three formats below are the documented way to opt out, the
    /// one password managers use. They expect binary values: a zeroed 32-bit
    /// integer for the first two, mere presence for the third.</para>
    /// </summary>
    private static DataObject BuildPrivateTransfer(string text)
    {
        var transfer = new DataObject();
        transfer.SetText(text);

        transfer.SetData(ClipboardHistoryFormat, new byte[] { 0, 0, 0, 0 });
        transfer.SetData(CloudClipboardFormat, new byte[] { 0, 0, 0, 0 });
        transfer.SetData(MonitorProcessingFormat, new byte[] { 0 });

        return transfer;
    }

    private const string ClipboardHistoryFormat = "CanIncludeInClipboardHistory";
    private const string CloudClipboardFormat = "CanUploadToCloudClipboard";
    private const string MonitorProcessingFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>
    /// Writes the saved content back after a short delay, on the calling
    /// thread.
    ///
    /// <para>That delay deliberately blocks the caller. An early version moved
    /// restoration onto a dedicated thread: marking that thread STA is not
    /// enough, since the clipboard APIs also need an OLE initialisation that
    /// <c>Thread</c> does not provide. Restoration then failed in silence, and
    /// the clipboard of the user stayed lost.</para>
    ///
    /// <para>Blocking is harmless here: the application has no window, and the
    /// text is already pasted when the wait begins.</para>
    /// </summary>
    private static void RestoreAfterDelay(ClipboardSnapshot previous)
    {
        // Called even when nothing could be captured: in that case Restore
        // empties the clipboard, instead of leaving the dictated text in it.
        // An early version returned here when the snapshot was empty, which
        // short-circuited exactly that emptying — a double guard, at two
        // levels, one of which cancelled the other.
        Thread.Sleep(ClipboardRestoreDelay);
        previous.Restore();
    }

    /// <summary>
    /// Releases the modifiers the system still believes are held, before
    /// injecting a shortcut.
    ///
    /// <para>Without that precaution, <c>Ctrl+V</c> combines with whatever is
    /// still active and becomes an entirely different shortcut. The case we
    /// hit: the Windows key still held turned the paste into <b>Win+Ctrl+V</b>,
    /// which opens the Windows audio output panel. The panel stole the focus,
    /// and the transcribed text disappeared — no error, no trace, and
    /// intermittently, depending on the order in which the user released their
    /// keys.</para>
    ///
    /// <para>We only inject a release for keys that are genuinely down: a
    /// superfluous release is harmless, but there is no point cluttering the
    /// event queue.</para>
    /// </summary>
    private static void ReleaseStrayModifiers()
    {
        int[] stray = [.. ModifiersToClear.Where(IsPhysicallyDown)];

        if (stray.Length == 0)
        {
            return;
        }

        Input[] inputs = [.. stray.Select(key => NewVirtualKey((ushort)key, keyUp: true))];

        Send(inputs);
    }

    /// <summary>
    /// Modifiers liable to hijack Ctrl+V. The Ctrl we inject ourselves is not
    /// among them, naturally.
    /// </summary>
    private static readonly int[] ModifiersToClear =
    [
        VirtualKeys.LeftWindows,
        VirtualKeys.RightWindows,
        VirtualKeys.LeftMenu,
        VirtualKeys.RightMenu,
        VirtualKeys.LeftShift,
        VirtualKeys.RightShift,
    ];

    /// <summary>The high bit marks a key that is currently held down.</summary>
    private static bool IsPhysicallyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void SendPasteShortcut()
    {
        ReleaseStrayModifiers();

        Span<Input> inputs =
        [
            NewVirtualKey((ushort)ControlKey, keyUp: false),
            NewVirtualKey(KeyV, keyUp: false),
            NewVirtualKey(KeyV, keyUp: true),
            NewVirtualKey((ushort)ControlKey, keyUp: true),
        ];

        Send(inputs);
    }

    private static void SendAsKeystrokes(string text)
    {
        Keystroke[] strokes = UnicodeKeystrokes.Build(text);
        Input[] inputs = new Input[strokes.Length];

        for (int i = 0; i < strokes.Length; i++)
        {
            Keystroke stroke = strokes[i];

            inputs[i] = UnicodeKeystrokes.IsReturn(stroke)
                ? NewVirtualKey(stroke.Unit, stroke.IsKeyUp)
                : NewUnicodeKey(stroke.Unit, stroke.IsKeyUp);
        }

        Send(inputs);
    }

    private static void Send(Span<Input> inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), Marshal.SizeOf<Input>());
    }

    private static Input NewUnicodeKey(ushort unit, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                // In Unicode mode the character travels in the scan code, and
                // the virtual code must stay zero.
                VirtualKey = 0,
                ScanCode = unit,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
            },
        },
    };

    private static Input NewVirtualKey(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        /// <summary>
        /// Reserves room for the largest variant of the union, so that the
        /// structure is the size Win32 expects.
        /// </summary>
        [FieldOffset(0)]
        private MouseInputData _mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, ref Input pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);
}
