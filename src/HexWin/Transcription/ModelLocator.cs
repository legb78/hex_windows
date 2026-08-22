namespace HexWin.Transcription;

/// <summary>
/// Finds the model file from the path written in settings.json.
///
/// Once the application is published, <c>models/</c> sits next to the
/// executable and the search stops immediately. During development, however,
/// the executable lives deep inside <c>bin/x64/Release/net9.0-windows/</c>
/// while <c>models/</c> is at the root of the repository: without walking up
/// the tree, 1.6 GB would have to be duplicated for every build configuration.
///
/// The lookup is passed in (<paramref name="fileExists"/>) so that the logic
/// stays testable without touching the disk.
/// </summary>
public static class ModelLocator
{
    private const int DefaultMaxAscent = 6;

    /// <summary>
    /// Returns the absolute path of the model, or <c>null</c> if it stays out
    /// of reach.
    /// </summary>
    public static string? Resolve(
        string configuredPath,
        string baseDirectory,
        Func<string, bool> fileExists,
        int maxAscent = DefaultMaxAscent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);

        // An absolute path is taken at its word: the user knows what they want.
        if (Path.IsPathRooted(configuredPath))
        {
            return fileExists(configuredPath) ? configuredPath : null;
        }

        DirectoryInfo? directory = new(baseDirectory);

        for (int level = 0; level <= maxAscent && directory is not null; level++)
        {
            string candidate = Path.GetFullPath(Path.Combine(directory.FullName, configuredPath));

            if (fileExists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// Variant wired to the real file system. The Parakeet model is a folder —
    /// encoder, decoder, joiner and vocabulary — hence Directory.Exists rather
    /// than File.Exists.
    /// </summary>
    public static string? Resolve(string configuredPath, string baseDirectory) =>
        Resolve(configuredPath, baseDirectory, Directory.Exists);
}
