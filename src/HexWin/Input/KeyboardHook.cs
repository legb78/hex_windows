using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HexWin.Input;

/// <summary>
/// Low-level keyboard hook: sees every keystroke on the system, whatever the
/// active window.
///
/// A deliberately thin shell. All the deciding belongs to
/// <see cref="ChordDetector"/>, which is testable; here we only wire up Win32
/// and relay.
///
/// <para><b>Hard constraint: never block inside the callback.</b> Windows
/// grants the callback a deadline — <c>LowLevelHooksTimeout</c>, 5 s by
/// default but often far less. Past that deadline the system uninstalls the
/// hook <i>silently</i>: the shortcut stops responding without a single
/// message, and only restarting the application brings it back. Subscribers
/// must therefore return immediately and hand the work to a background
/// thread.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: a global keyboard hook cannot be triggered by a test, because Windows marks keystrokes produced by a program as injected.")]
internal sealed partial class KeyboardHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int HcAction = 0;

    private const nint WmKeyDown = 0x0100;
    private const nint WmKeyUp = 0x0101;
    private const nint WmSysKeyDown = 0x0104;
    private const nint WmSysKeyUp = 0x0105;

    /// <summary>Marks the events we injected ourselves.</summary>
    private const uint LlkhfInjected = 0x10;

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly ChordDetector _detector;

    /// <summary>
    /// The delegate must be held in a field. Without that, the garbage
    /// collector reclaims it while Windows still holds its address, and the
    /// process collapses on the first keystroke.
    /// </summary>
    private readonly HookProc _callback;

    private nint _hook;
    private bool _disposed;

    /// <summary>
    /// Time of the last keyboard event we saw. Lets the watchdog tell a
    /// keyboard nobody is typing on apart from a hook that has died.
    /// </summary>
    private long _lastEventTicks = DateTime.UtcNow.Ticks;

    public KeyboardHook(ChordDetector detector)
    {
        _detector = detector;
        _callback = OnKeyboardEvent;
    }

    /// <summary>The shortcut has just completed: start recording.</summary>
    public event EventHandler? Started;

    /// <summary>The shortcut has just been released: transcribe.</summary>
    public event EventHandler? Stopped;

    /// <summary>The dictation is abandoned without transcribing.</summary>
    public event EventHandler? Cancelled;

    public void Install()
    {
        if (_hook != 0)
        {
            return;
        }

        _hook = SetWindowsHookExW(WhKeyboardLowLevel, _callback, 0, 0);

        if (_hook == 0)
        {
            throw new InvalidOperationException(
                $"Cannot install the keyboard hook (error {Marshal.GetLastWin32Error()}).");
        }

        // Locking the session interrupts the delivery of key-ups: without a
        // reset, a key would be considered held down forever.
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_detector.Reset().Action == ChordAction.Cancel)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reinstalls the hook if it has seen nothing for <paramref name="silence"/>.
    ///
    /// <para>Windows uninstalls a low-level hook <b>without saying so</b> when
    /// its callback overruns the allotted deadline. The application then looks
    /// perfectly healthy — blue icon, no error — but the shortcut no longer
    /// responds, and only a restart brings it back.</para>
    ///
    /// <para>No API lets you query the state of a hook. So we compare what
    /// <i>we</i> saw against what <i>Windows</i> saw: <c>GetLastInputInfo</c>
    /// returns the time of the last input on the system, independently of our
    /// hook. If Windows received keystrokes we did not see, our hook is dead.
    /// If nobody typed anything, there is nothing to repair.</para>
    ///
    /// <para>An early version looked only at the silence on our side, which
    /// reinstalled the hook every two minutes for a whole night: pointless, and
    /// it made the log unreadable — to the point where a real incident would
    /// have drowned in it.</para>
    /// </summary>
    /// <returns>True if a reinstall took place.</returns>
    public bool RefreshIfSilent(TimeSpan silence)
    {
        if (_hook == 0 || _disposed)
        {
            return false;
        }

        long ourLastEvent = Interlocked.Read(ref _lastEventTicks);
        var since = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - ourLastEvent);

        if (since < silence)
        {
            return false;
        }

        // Did the system see keystrokes we missed?
        if (!SystemSawInputAfter(ourLastEvent))
        {
            return false;
        }

        // The new hook goes in BEFORE the old one comes out: the other way
        // round, a keystroke landing between the two calls would be lost.
        nint renewed = SetWindowsHookExW(WhKeyboardLowLevel, _callback, 0, 0);

        if (renewed == 0)
        {
            return false;
        }

        UnhookWindowsHookEx(_hook);
        _hook = renewed;
        Interlocked.Exchange(ref _lastEventTicks, DateTime.UtcNow.Ticks);

        return true;
    }

    private nint OnKeyboardEvent(int code, nint message, nint data)
    {
        Interlocked.Exchange(ref _lastEventTicks, DateTime.UtcNow.Ticks);

        if (code != HcAction)
        {
            return CallNextHookEx(0, code, message, data);
        }

        KeyboardInput input = Marshal.PtrToStructure<KeyboardInput>(data);

        // Do not react to what we inject ourselves, on pain of a loop.
        if ((input.Flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(0, code, message, data);
        }

        ChordDecision decision = message switch
        {
            WmKeyDown or WmSysKeyDown => _detector.OnKeyDown((int)input.VirtualKey),
            WmKeyUp or WmSysKeyUp => _detector.OnKeyUp((int)input.VirtualKey),
            _ => ChordDecision.Ignore,
        };

        if (decision.NeutralizeStartMenu)
        {
            SendNeutralKey();
        }

        switch (decision.Action)
        {
            case ChordAction.Start:
                Started?.Invoke(this, EventArgs.Empty);
                break;
            case ChordAction.Stop:
                Stopped?.Invoke(this, EventArgs.Empty);
                break;
            case ChordAction.Cancel:
                Cancelled?.Invoke(this, EventArgs.Empty);
                break;
            case ChordAction.None:
            default:
                break;
        }

        // Returning 1 consumes the event: Windows will never see it.
        return decision.Swallow ? 1 : CallNextHookEx(0, code, message, data);
    }

    /// <summary>
    /// Neutralises a Windows key the system has already received.
    ///
    /// <para>Two distinct problems, settled by the same sequence.</para>
    ///
    /// <para><b>The Start menu.</b> Windows opens it on a Windows key pressed
    /// and released with no other key in between. F13 breaks that sequence: it
    /// exists on no keyboard sold today, and nothing is bound to it.</para>
    ///
    /// <para><b>The stuck modifier.</b> This one was expensive to diagnose.
    /// When the user presses Windows <i>before</i> the other key, the press has
    /// already gone to the system, which considers the modifier active for the
    /// whole dictation. Insertion then injects Ctrl+V — and Windows reads
    /// <b>Win+Ctrl+V</b>, its shortcut for opening the audio output panel. The
    /// panel appears, steals the focus, and the transcribed text is lost. So we
    /// explicitly release the Windows keys after F13.</para>
    ///
    /// <para>Order matters: F13 first, otherwise the injected release would open
    /// the very Start menu we are trying to avoid.</para>
    /// </summary>
    private static void SendNeutralKey()
    {
        Span<Input> inputs =
        [
            NewKeyboardInput(VirtualKeys.F13, keyUp: false),
            NewKeyboardInput(VirtualKeys.F13, keyUp: true),
            NewKeyboardInput(VirtualKeys.LeftWindows, keyUp: true),
            NewKeyboardInput(VirtualKeys.RightWindows, keyUp: true),
        ];

        SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), Marshal.SizeOf<Input>());
    }

    private static Input NewKeyboardInput(int virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = (ushort)virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    /// <summary>
    /// True if Windows recorded user input later than the given time. The
    /// system counts in milliseconds since it started, hence the conversion.
    /// </summary>
    private static bool SystemSawInputAfter(long ticks)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };

        if (!GetLastInputInfo(ref info))
        {
            // With no information, we would rather reinstall: a dead hook costs
            // more than a pointless reinstall.
            return true;
        }

        long idleMilliseconds = Environment.TickCount64 - info.LastInputTick;
        long systemLastInputTicks = DateTime.UtcNow.Ticks - (idleMilliseconds * TimeSpan.TicksPerMillisecond);

        return systemLastInputTicks > ticks;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint LastInputTick;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    private delegate nint HookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

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
        /// Reserves room for the largest variant of the union (the mouse), so
        /// that the structure is the size Win32 expects.
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
    private static partial nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, ref Input pInputs, int cbSize);
}
