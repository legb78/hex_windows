using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Output;

/// <summary>
/// Remembers the window the dictation was aimed at, and brings it back to the
/// foreground before inserting the text.
///
/// <para><b>The problem solved.</b> Insertion writes into whichever window is
/// active when it runs, not the one the user was speaking into. While
/// transcription took two tenths of a second, the gap was theoretical. Since
/// the model is released after a period of inactivity, the first dictation
/// following a pause takes around two seconds — ample time to switch to
/// another application. The text would then land elsewhere, in a conversation
/// or a document that never asked for it.</para>
///
/// <para>Restoration is only attempted if the window actually changed, and it
/// does nothing if the target window has vanished in the meantime.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: manipulates the windows of the real desktop.")]
internal sealed partial class TargetWindow
{
    private readonly nint _handle;

    private TargetWindow(nint handle) => _handle = handle;

    /// <summary>
    /// Window title, for the logs and the diagnostic mode. Without it, there
    /// is no telling "restoration failed" apart from "we restored the wrong
    /// window" — two very different faults.
    /// </summary>
    public unsafe string Title
    {
        get
        {
            const int max = 256;
            char* buffer = stackalloc char[max];

            int length = GetWindowTextW(_handle, buffer, max);

            return length > 0 ? new string(buffer, 0, length) : "(sans titre)";
        }
    }

    /// <summary>Window currently in the foreground, or none.</summary>
    public static TargetWindow? Capture()
    {
        nint handle = GetForegroundWindow();

        return handle == 0 ? null : new TargetWindow(handle);
    }

    /// <summary>
    /// Brings the window back to the foreground if it is no longer there.
    /// Returns false if it has vanished or if Windows refused the change.
    /// </summary>
    public bool Restore()
    {
        if (!IsWindow(_handle))
        {
            return false;
        }

        if (GetForegroundWindow() == _handle)
        {
            return true;
        }

        // Windows does not let any process steal the foreground. Attaching to
        // the input thread of the target window lifts that restriction, for
        // the duration of the call.
        uint us = GetCurrentThreadId();
        uint them = GetWindowThreadProcessId(_handle, 0);

        bool attached = us != them && AttachThreadInput(us, them, true);

        try
        {
            if (!SetForegroundWindow(_handle))
            {
                return false;
            }
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(us, them, false);
            }
        }

        return WaitUntilForeground();
    }

    /// <summary>
    /// Waits until the switch has actually happened.
    ///
    /// <para><b>SetForegroundWindow is asynchronous</b>: it requests the change
    /// and returns straight away. Pasting immediately afterwards sends the
    /// keystrokes to the <i>previous</i> window, or to nobody — the text
    /// vanishes with no error and no trace. Seen in testing: restoration
    /// succeeded, and the text arrived nowhere.</para>
    ///
    /// <para>So we wait for confirmation rather than for a fixed delay, which
    /// would be too short on a loaded machine and wasted otherwise.</para>
    /// </summary>
    private bool WaitUntilForeground()
    {
        for (int elapsed = 0; elapsed < ForegroundTimeoutMs; elapsed += ForegroundPollMs)
        {
            if (GetForegroundWindow() == _handle)
            {
                return true;
            }

            Thread.Sleep(ForegroundPollMs);
        }

        return GetForegroundWindow() == _handle;
    }

    private const int ForegroundTimeoutMs = 600;
    private const int ForegroundPollMs = 20;

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, nint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowTextW(nint hWnd, char* text, int maxCount);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);
}
