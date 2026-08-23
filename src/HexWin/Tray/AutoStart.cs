using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace HexWin.Tray;

/// <summary>
/// Automatic launch at sign-in, through the Run key of the current user.
///
/// That key rather than a shortcut in the Startup folder, and above all rather
/// than a scheduled task: it needs no administrator rights, and comes out as
/// easily as it goes in.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: writes into the registry of the current user.")]
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HexWin";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                // The quotes are indispensable: the path runs through
                // "Documents" and other folders that may contain spaces, which
                // Windows would otherwise split into arguments.
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Restrictive group policy: give up quietly rather than bring the
            // application down over a secondary setting.
        }
    }
}
