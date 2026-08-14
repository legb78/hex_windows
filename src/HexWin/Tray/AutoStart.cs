using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace HexWin.Tray;

/// <summary>
/// Lancement automatique à l'ouverture de session, via la clé Run de
/// l'utilisateur courant.
///
/// Cette clé plutôt qu'un raccourci dans le dossier Démarrage, et surtout
/// plutôt qu'une tâche planifiée : elle ne demande aucun droit
/// administrateur, et se retire aussi facilement qu'elle se pose.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille Win32 : écrit dans le registre de l'utilisateur courant.")]
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
                // Les guillemets sont indispensables : le chemin traverse
                // « Documents » et d'autres dossiers pouvant contenir des
                // espaces, que Windows découperait sinon en arguments.
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Stratégie de groupe restrictive : on renonce sans bruit plutôt
            // que de faire tomber l'application sur un réglage secondaire.
        }
    }
}
