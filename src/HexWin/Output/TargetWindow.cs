using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Output;

/// <summary>
/// Mémorise la fenêtre visée par la dictée, et la ramène au premier plan avant
/// d'y insérer le texte.
///
/// <para><b>Le problème résolu.</b> L'insertion écrit dans la fenêtre active au
/// moment où elle s'exécute, pas dans celle où l'utilisateur parlait. Tant que
/// la transcription durait deux dixièmes de seconde, l'écart était théorique.
/// Depuis que le modèle est libéré après inactivité, la première dictée qui
/// suit une pause demande environ deux secondes — largement le temps de
/// basculer sur une autre application. Le texte partirait alors ailleurs, dans
/// une conversation ou un document qui n'a rien demandé.</para>
///
/// <para>La restauration n'est tentée que si la fenêtre a effectivement changé,
/// et elle est sans effet si la fenêtre visée a disparu entre-temps.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Coquille Win32 : manipule les fenêtres du bureau réel.")]
internal sealed partial class TargetWindow
{
    private readonly nint _handle;

    private TargetWindow(nint handle) => _handle = handle;

    /// <summary>
    /// Titre de la fenêtre, pour les journaux et le mode diagnostic. Sans lui,
    /// on ne peut pas distinguer « la restauration a échoué » de « on a
    /// restauré la mauvaise fenêtre » — deux pannes très différentes.
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

    /// <summary>Fenêtre actuellement au premier plan, ou aucune.</summary>
    public static TargetWindow? Capture()
    {
        nint handle = GetForegroundWindow();

        return handle == 0 ? null : new TargetWindow(handle);
    }

    /// <summary>
    /// Ramène la fenêtre au premier plan si elle ne l'est plus. Rend faux si
    /// elle a disparu ou si Windows a refusé le changement.
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

        // Windows n'autorise pas n'importe quel processus à voler le premier
        // plan. Se rattacher au fil d'entrée de la fenêtre visée lève cette
        // restriction, le temps de l'appel.
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
    /// Attend que la bascule soit effective.
    ///
    /// <para><b>SetForegroundWindow est asynchrone</b> : il demande le
    /// changement et rend la main aussitôt. Coller dans la foulée envoie les
    /// frappes à la fenêtre <i>précédente</i>, ou à personne — le texte
    /// disparaît sans erreur ni trace. Constaté à l'essai : la restauration
    /// réussissait, et le texte n'arrivait nulle part.</para>
    ///
    /// <para>On attend donc la confirmation plutôt qu'une durée fixe, qui
    /// serait trop courte sur une machine chargée et perdue sinon.</para>
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
