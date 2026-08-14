namespace HexWin;

internal static class Program
{
    /// <summary>
    /// Point d'entrée. STAThread est exigé par Windows Forms et par les API
    /// de presse-papiers utilisées plus tard pour l'insertion du texte.
    /// </summary>
    [STAThread]
    private static int Main()
    {
        // Le câblage réel (barre système, raccourci, transcription) arrive
        // dans les PR suivantes. Cette PR ne pose que le socle et la CI.
        return 0;
    }
}
