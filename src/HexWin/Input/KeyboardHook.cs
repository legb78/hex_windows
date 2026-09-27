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
/// grants the callback a deadline — <c>LowLevelHooksTimeout</c>, one second
/// at most since Windows 10. Past it, Windows delivers the key <i>anyway</i>,
/// whatever the callback answers later: a press we meant to swallow reaches
/// the system, its release does not, and the key stays held for good. Past it
/// too often, the system uninstalls the hook silently.</para>
///
/// <para>Hence the hook's own thread, which does nothing but decide. It used to
/// live on the interface thread, which also pastes the text — with a 400 ms
/// wait before restoring the clipboard — and opens the microphone: holding the
/// shortcut while a segment was inserted let an auto-repeated Shift through,
/// and the user typed in capitals after the dictation. Events are raised on the
/// thread that created the hook, by posting to its synchronisation
/// context.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: a global keyboard hook cannot be triggered by a test, because Windows marks keystrokes produced by a program as injected.")]
internal sealed partial class KeyboardHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int HcAction = 0;

    private const nint WmKeyDown = 0x0100;
    private const nint WmSysKeyDown = 0x0104;

    /// <summary>Marks the events we injected ourselves.</summary>
    private const uint LlkhfInjected = 0x10;

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    /// <summary>
    /// Guards the detector and the capture, touched by the hook thread on
    /// every key and by the interface thread when it swaps or resets them.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>Where the events are raised; null outside a message loop.</summary>
    private readonly SynchronizationContext? _owner = SynchronizationContext.Current;

    private ChordDetector _detector;

    /// <summary>The capture under way, or null the rest of the time.</summary>
    private CaptureTarget? _capture;

    /// <summary>
    /// Keys whose key-down went to a capture. Their key-up is swallowed too,
    /// even when it arrives after the capture ended — the same balance rule as
    /// the detector's.
    /// </summary>
    private readonly HashSet<int> _capturedDown = [];

    /// <summary>
    /// The delegate must be held in a field. Without that, the garbage
    /// collector reclaims it while Windows still holds its address, and the
    /// process collapses on the first keystroke.
    /// </summary>
    private readonly HookProc _callback;

    private Thread? _thread;

    /// <summary>Runs work on the hook thread, where Windows requires the hook be set and removed.</summary>
    private SynchronizationContext? _hookThread;

    private nint _hook;
    private int _installError;
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
        if (_thread is not null)
        {
            return;
        }

        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() => RunHookThread(ready))
        {
            IsBackground = true,
            Name = "HexWin keyboard hook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        if (_hook == 0)
        {
            throw new InvalidOperationException(
                $"Cannot install the keyboard hook (error {_installError}).");
        }

        // Locking the session interrupts the delivery of key-ups: without a
        // reset, a key would be considered held down forever.
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>
    /// A low-level hook is called on the thread that set it, and only while
    /// that thread pumps messages: this one does nothing else.
    /// </summary>
    private void RunHookThread(ManualResetEventSlim ready)
    {
        var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        _hookThread = context;

        _hook = SetWindowsHookExW(WhKeyboardLowLevel, _callback, 0, 0);
        _installError = Marshal.GetLastWin32Error();
        bool installed = _hook != 0;
        ready.Set();

        if (installed)
        {
            Application.Run();
        }
    }

    /// <summary>
    /// Puts a new shortcut in force without reinstalling the hook.
    ///
    /// <para>The gate keeps the swap from landing in the middle of a keyboard
    /// event. The caller only swaps while no dictation is under way. The old
    /// detector is reset first, so a key it had swallowed is not held against
    /// the new one. Should one still be down, its key-up reaches Windows
    /// without the matching key-down: a lone release, where the opposite
    /// imbalance — a lone press — would leave a modifier stuck.</para>
    /// </summary>
    public void ReplaceDetector(ChordDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);

        lock (_gate)
        {
            _detector.Reset();
            _detector = detector;
        }
    }

    /// <summary>
    /// Swallows every key while <paramref name="window"/> is in the foreground
    /// and reports it to <paramref name="onKey"/> — virtual code, and true for
    /// a key-down — until <see cref="EndCapture"/>. The first key-down pressed
    /// elsewhere goes on to Windows untouched, ends the capture and calls
    /// <paramref name="onLost"/>: that is how a capture left running behind a
    /// window that lost the focus gives the keyboard back on the very next key,
    /// instead of eating what the user types elsewhere.
    ///
    /// <para>Swallowed, because the keys worth capturing all do something on
    /// their own: the Windows key opens the Start menu, CapsLock toggles
    /// capitals, Alt moves the focus to a menu bar. The shortcut is also not
    /// detected meanwhile, so pressing the current one does not start a
    /// dictation into the settings window.</para>
    ///
    /// <para>Both callbacks are posted to the thread that created the hook,
    /// never run on the hook thread.</para>
    /// </summary>
    public void BeginCapture(nint window, Action<int, bool> onKey, Action onLost)
    {
        ArgumentNullException.ThrowIfNull(onKey);
        ArgumentNullException.ThrowIfNull(onLost);

        lock (_gate)
        {
            CancelDictation();
            _capture = new CaptureTarget(window, onKey, onLost);
        }
    }

    public void EndCapture()
    {
        lock (_gate)
        {
            _capture = null;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        lock (_gate)
        {
            CancelDictation();
        }
    }

    private void CancelDictation()
    {
        if (_detector.Reset().Action == ChordAction.Cancel)
        {
            Raise(Cancelled);
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
        if (_hook == 0 || _disposed || _hookThread is null)
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

        bool renewed = false;
        _hookThread.Send(_ => renewed = Reinstall(), null);

        return renewed;
    }

    private bool Reinstall()
    {
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

        bool keyDown = message is WmKeyDown or WmSysKeyDown;
        int virtualKey = (int)input.VirtualKey;
        bool swallow;

        lock (_gate)
        {
            swallow = TakeByCapture(virtualKey, keyDown) || Decide(virtualKey, keyDown);
        }

        // Returning 1 consumes the event: Windows will never see it.
        return swallow ? 1 : CallNextHookEx(0, code, message, data);
    }

    /// <summary>True when the key belongs to a shortcut capture, and is swallowed.</summary>
    private bool TakeByCapture(int virtualKey, bool keyDown)
    {
        if (!keyDown && _capturedDown.Remove(virtualKey))
        {
            if (_capture is { } current)
            {
                Post(() => current.OnKey(virtualKey, false));
            }

            return true;
        }

        if (!keyDown || _capture is not { } capture)
        {
            return false;
        }

        if (GetForegroundWindow() != capture.Window)
        {
            // The capture is over, and this key belongs to whatever the user
            // is typing into. It falls through to the detector like any other.
            _capture = null;
            Post(capture.OnLost);
            return false;
        }

        _capturedDown.Add(virtualKey);
        Post(() => capture.OnKey(virtualKey, true));

        return true;
    }

    private bool Decide(int virtualKey, bool keyDown)
    {
        ChordDecision decision = keyDown
            ? _detector.OnKeyDown(virtualKey)
            : _detector.OnKeyUp(virtualKey, IsDownForWindows(virtualKey));

        if (decision.NeutralizeStartMenu)
        {
            SendNeutralKey();
        }

        switch (decision.Action)
        {
            case ChordAction.Start:
                Raise(Started);
                break;
            case ChordAction.Stop:
                Raise(Stopped);
                break;
            case ChordAction.Cancel:
                Raise(Cancelled);
                break;
            case ChordAction.None:
            default:
                break;
        }

        return decision.Swallow;
    }

    /// <summary>
    /// Inside the callback, the asynchronous state still describes the key
    /// before this event: down means Windows received a press of it.
    /// </summary>
    private static bool IsDownForWindows(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void Raise(EventHandler? handler)
    {
        if (handler is not null)
        {
            Post(() => handler(this, EventArgs.Empty));
        }
    }

    private void Post(Action action)
    {
        if (_owner is null)
        {
            action();
            return;
        }

        _owner.Post(_ => action(), null);
    }

    private sealed record CaptureTarget(nint Window, Action<int, bool> OnKey, Action OnLost);

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

        _hookThread?.Post(_ =>
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
            Application.ExitThread();
        }, null);
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

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
