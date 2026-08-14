using System.Runtime.InteropServices;

namespace HexWin.Interop;

/// <summary>
/// Rend une sortie console utilisable depuis une application compilée en
/// WinExe.
///
/// HexWin vit dans la barre système : il est compilé sans console, sinon une
/// fenêtre noire s'ouvrirait à chaque lancement. Mais le mode de diagnostic
/// en ligne de commande doit tout de même pouvoir écrire quelque part. On se
/// rattache donc à la console du terminal appelant.
/// </summary>
internal static partial class ConsoleBridge
{
    /// <summary>Valeur conventionnelle désignant le processus parent.</summary>
    private const uint AttachParentProcess = 0xFFFFFFFF;

    /// <summary>
    /// Se rattache à la console appelante, ou en crée une si le programme a
    /// été lancé sans terminal (double-clic).
    /// </summary>
    public static void Attach()
    {
        if (!AttachConsole(AttachParentProcess) && !AllocConsole())
        {
            return;
        }

        // Après rattachement, les flux standard pointent encore dans le vide :
        // ils ont été initialisés alors qu'aucune console n'existait.
        var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(output);

        var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetError(error);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
