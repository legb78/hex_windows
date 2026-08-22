using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace HexWin.Interop;

/// <summary>
/// Makes console output usable from an application built as WinExe.
///
/// HexWin lives in the system tray: it is built without a console, otherwise a
/// black window would open on every launch. But the command-line diagnostic
/// mode still has to write somewhere. So we attach to the console of the
/// calling terminal.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: attaches to the console of the calling process.")]
internal static partial class ConsoleBridge
{
    /// <summary>Conventional value standing for the parent process.</summary>
    private const uint AttachParentProcess = 0xFFFFFFFF;

    /// <summary>
    /// Attaches to the calling console, or creates one if the program was
    /// started without a terminal (a double-click).
    /// </summary>
    public static void Attach()
    {
        if (!AttachConsole(AttachParentProcess) && !AllocConsole())
        {
            return;
        }

        // cmd.exe starts in codepage 850 on a French installation, where
        // accents show up as "d├®but". Switching the console to UTF-8 is the
        // only way to get readable text, and readable text is needed for
        // messages written in French.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Output redirected to a file or a pipe: the console encoding no
            // longer means anything, and the failure has no consequence.
        }

        // After attaching, the standard streams still point into the void:
        // they were initialised while no console existed.
        var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(output);

        var error = new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true };
        Console.SetError(error);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
