using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace HexWin.Diagnostics;

/// <summary>
/// Operational log, written next to the application data of the user.
///
/// With no trace, a dictation that inserts nothing cannot be diagnosed: there
/// is no telling whether the microphone caught nothing, the engine recognised
/// nothing, or the insertion failed. One line per dictation is enough to
/// settle it.
///
/// Writing must never fail an otherwise successful dictation: every I/O error
/// is absorbed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Writes into the data folder of the user.")]
public sealed class SessionLog
{
    private readonly string? _path;
    private readonly Lock _sync = new();

    private SessionLog(string? path) => _path = path;

    /// <summary>Path of the folder holding the log, even when disabled.</summary>
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
            // Disk full or file locked: the dictation takes priority.
        }
        catch (UnauthorizedAccessException)
        {
            // Same again.
        }
    }
}
