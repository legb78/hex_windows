using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace HexWin.Tray;

/// <summary>
/// The HexWin shortcut on the desktop. Double-clicked, it starts HexWin, or
/// opens its settings when HexWin is already running (see
/// <see cref="SingleInstance"/>).
///
/// <para>HexWin has no installer: it is unzipped, and its first start is the
/// closest thing to an installation. That is when the shortcut is offered —
/// once, and only created if the user says yes. Writing on someone's desktop
/// without asking is exactly what makes an application feel like it takes
/// liberties.</para>
///
/// <para>Built through the shell's own IShellLink rather than by driving
/// WScript.Shell: no scripting host involved, which some security products
/// treat with suspicion in an unsigned executable.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: writes a shortcut through COM and a flag in the registry.")]
internal static class DesktopShortcut
{
    private const string FileName = "HexWin.lnk";
    private const string StateKey = @"Software\HexWin";
    private const string OfferedValue = "DesktopShortcutOffered";

    /// <summary>
    /// Where the shortcut goes. DesktopDirectory rather than a path built by
    /// hand: when OneDrive backs up the desktop, the folder is no longer under
    /// the user profile.
    /// </summary>
    public static string PathOnDesktop =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName);

    public static bool Exists => File.Exists(PathOnDesktop);

    /// <summary>
    /// True once the first-start question has been asked, whatever the answer.
    /// Kept in the registry, next to the launch-at-sign-in entry, rather than in
    /// settings.json: it records a question asked on this machine, not a
    /// preference worth carrying to another one.
    /// </summary>
    public static bool WasOffered
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StateKey);
                return key?.GetValue(OfferedValue) is not null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                // Unreadable: count it as asked. Asking again at every start
                // would be worse than never asking.
                return true;
            }
        }
    }

    public static void MarkOffered()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StateKey);
            key.SetValue(OfferedValue, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Restrictive policy: the question may come back, nothing worse.
        }
    }

    /// <summary>Puts the shortcut on the desktop, or removes it. False when that failed.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(PathOnDesktop);
                return true;
            }

            Create(PathOnDesktop, Environment.ProcessPath ?? Application.ExecutablePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a shortcut to <paramref name="target"/> at <paramref name="shortcutPath"/>.
    /// The working directory is the executable's own: settings.json and the
    /// model are looked up from there.
    /// </summary>
    public static void Create(string shortcutPath, string target)
    {
        var link = (IShellLinkW)new ShellLink();

        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target) ?? "");
            link.SetDescription("HexWin — dictée vocale locale. Ouvre les paramètres quand HexWin tourne déjà.");
            link.SetIconLocation(target, 0);

            ((IPersistFile)link).Save(shortcutPath, fRemember: true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    /// <summary>
    /// The shell's shortcut interface. The methods must stay in exactly this
    /// order: COM calls them by their position in the table, not by name, and
    /// the ones HexWin never calls are there to hold their slots.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, nint findData, uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int maxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int maxIconPath, out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(nint window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
