using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Tray;

/// <summary>
/// What a second launch of HexWin does: ask the one already running to show
/// its settings window, then leave.
///
/// <para>Only one instance may run — two would install two keyboard hooks on
/// the same shortcut and insert every dictation twice. A second launch used to
/// exit in silence, which read as "nothing happens" to anyone who double-clicked
/// the desktop shortcut to change a setting. It now hands that intent over.</para>
///
/// <para>A named event rather than a pipe or a window message: there is nothing
/// to carry but "show yourself", and an event needs neither a window handle to
/// find nor a protocol.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: signals another process.")]
internal static partial class SingleInstance
{
    private const string ShowSettingsEventName = @"Local\HexWin.ShowSettings";

    /// <summary>Any process may take the foreground next.</summary>
    private const uint AsfwAny = unchecked((uint)-1);

    /// <summary>Created by the running instance; set by every later launch.</summary>
    public static EventWaitHandle CreateListener() =>
        new(initialState: false, EventResetMode.AutoReset, ShowSettingsEventName);

    /// <summary>
    /// Called by a launch that found HexWin already running.
    ///
    /// <para>Windows only lets a process bring a window to the front if it is
    /// the one the user is dealing with. The process just launched from the
    /// desktop is; the one sitting in the tray is not. Without handing that
    /// right over first, the settings window would open behind everything and
    /// merely flash in the taskbar.</para>
    ///
    /// <para>Does nothing when the running instance has no listener yet — it
    /// is still starting — which is no worse than the silent exit it
    /// replaces.</para>
    /// </summary>
    public static void AskRunningInstanceToShowSettings()
    {
        if (!EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out EventWaitHandle? signal))
        {
            return;
        }

        using (signal)
        {
            AllowSetForegroundWindow(AsfwAny);
            signal.Set();
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
