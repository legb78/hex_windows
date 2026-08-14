namespace HexWin.Transcription;

/// <summary>
/// Retrouve le fichier modèle à partir du chemin inscrit dans settings.json.
///
/// Une fois l'application publiée, <c>models/</c> est posé à côté de
/// l'exécutable et la recherche s'arrête immédiatement. Pendant le
/// développement en revanche, l'exécutable vit au fond de
/// <c>bin/x64/Release/net9.0-windows/</c> alors que <c>models/</c> est à la
/// racine du dépôt : sans remontée dans l'arborescence, il faudrait dupliquer
/// 1,6 Go à chaque configuration de compilation.
///
/// La recherche est passée en paramètre (<paramref name="fileExists"/>) pour
/// que la logique reste testable sans toucher au disque.
/// </summary>
public static class ModelLocator
{
    private const int DefaultMaxAscent = 6;

    /// <summary>
    /// Rend le chemin absolu du modèle, ou <c>null</c> s'il reste introuvable.
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

        // Un chemin absolu est pris au mot : l'utilisateur sait ce qu'il veut.
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
    /// Variante branchée sur le vrai système de fichiers. Le modèle Parakeet
    /// est un dossier — encodeur, décodeur, joiner et vocabulaire — d'où
    /// Directory.Exists plutôt que File.Exists.
    /// </summary>
    public static string? Resolve(string configuredPath, string baseDirectory) =>
        Resolve(configuredPath, baseDirectory, Directory.Exists);
}
