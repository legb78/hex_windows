using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

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
[ExcludeFromCodeCoverage(Justification = "Coquille Win32 : se rattache à la console du processus appelant.")]
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

        // cmd.exe démarre en codepage 850 sur une installation française :
        // les accents s'y affichent comme « d├®but ». Basculer la console en
        // UTF-8 est la seule façon d'obtenir un texte lisible, et il en faut
        // pour des messages écrits en français.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Sortie redirigée vers un fichier ou un tube : l'encodage de la
            // console n'a alors plus de sens, et l'échec est sans conséquence.
        }

        // Après rattachement, les flux standard pointent encore dans le vide :
        // ils ont été initialisés alors qu'aucune console n'existait.
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
