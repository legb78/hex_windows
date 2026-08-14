using System.Globalization;

namespace HexWin.Diagnostics;

/// <summary>
/// Journal d'exploitation, écrit à côté des données applicatives de
/// l'utilisateur.
///
/// Sans trace, une dictée qui n'insère rien est indiagnosticable : on ne sait
/// pas si le micro n'a rien capté, si le moteur n'a rien reconnu, ou si
/// l'insertion a échoué. Une ligne par dictée suffit à trancher.
///
/// L'écriture ne doit jamais faire échouer une dictée par ailleurs réussie :
/// toute erreur d'entrée-sortie est absorbée.
/// </summary>
public sealed class SessionLog
{
    private readonly string? _path;
    private readonly Lock _sync = new();

    private SessionLog(string? path) => _path = path;

    /// <summary>Chemin du dossier contenant le journal, même désactivé.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HexWin");

    public static SessionLog Create(bool enabled)
    {
        if (!enabled)
        {
            return new SessionLog(null);
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            return new SessionLog(Path.Combine(Directory, "hexwin.log"));
        }
        catch (IOException)
        {
            return new SessionLog(null);
        }
        catch (UnauthorizedAccessException)
        {
            return new SessionLog(null);
        }
    }

    public void Write(string message)
    {
        if (_path is null)
        {
            return;
        }

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");

        try
        {
            lock (_sync)
            {
                File.AppendAllText(_path, line);
            }
        }
        catch (IOException)
        {
            // Disque plein ou fichier verrouillé : la dictée prime.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }
}
