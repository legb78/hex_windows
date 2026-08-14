using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexWin.Configuration;

/// <summary>
/// Comment le texte transcrit est remis à l'application active.
/// </summary>
public enum InsertionMode
{
    /// <summary>Presse-papiers puis Ctrl+V. Instantané, même sur un long texte.</summary>
    Paste,

    /// <summary>Frappe simulée caractère par caractère. Plus lent, mais passe
    /// dans les rares applications qui ignorent le collage.</summary>
    Type,
}

/// <summary>
/// Configuration utilisateur, lue depuis settings.json à côté de l'exécutable.
///
/// Toute la logique de cette classe est pure : <see cref="Parse"/> et
/// <see cref="Normalize"/> ne touchent pas au disque, ce qui les rend
/// entièrement testables. Seul <see cref="Load"/> fait des entrées-sorties.
///
/// Le principe est qu'un fichier abîmé ne doit jamais empêcher l'application
/// de démarrer : toute valeur invalide est remplacée par son défaut plutôt
/// que de lever une exception.
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "settings.json";

    /// <summary>Chemin du modèle GGML, relatif au dossier de l'exécutable.</summary>
    public string ModelPath { get; set; } = "models/ggml-large-v3-turbo.bin";

    /// <summary>Langue de dictée : "fr" ou "en".</summary>
    public string Language { get; set; } = DefaultLanguage;

    /// <summary>Touches à maintenir pour dicter.</summary>
    public string[] Hotkey { get; set; } = ["Ctrl", "Win"];

    /// <summary>En deçà, l'appui est considéré comme accidentel et ignoré.</summary>
    public int MinRecordingMilliseconds { get; set; } = 250;

    /// <summary>Coupe l'enregistrement si la touche reste enfoncée.</summary>
    public int MaxRecordingSeconds { get; set; } = 120;

    /// <summary>
    /// Ordre d'essai des moteurs de calcul. Vulkan exploite le GPU Intel Arc ;
    /// Cpu sert de repli et reste toujours présent en dernière position.
    /// </summary>
    public string[] RuntimePreference { get; set; } = ["Vulkan", "Cpu"];

    public InsertionMode Insertion { get; set; } = InsertionMode.Paste;

    /// <summary>Journalise les transcriptions et le moteur réellement chargé.</summary>
    public bool LogEnabled { get; set; } = true;

    // --- Valeurs de référence -------------------------------------------------

    private const string DefaultLanguage = "fr";
    private const string CpuRuntime = "Cpu";

    private static readonly string[] SupportedLanguages = ["fr", "en"];
    private static readonly string[] DefaultHotkey = ["Ctrl", "Win"];
    private static readonly string[] DefaultRuntimePreference = ["Vulkan", CpuRuntime];

    /// <summary>Moteurs connus de Whisper.net, dans leur orthographe attendue.</summary>
    private static readonly string[] KnownRuntimes = ["Vulkan", "Cuda", "Cuda12", "OpenVino", CpuRuntime];

    /// <summary>
    /// Touches admises dans un raccourci. Volontairement restreint : la touche
    /// Fn n'y figure pas car elle est gérée par le contrôleur embarqué du
    /// clavier et n'émet aucun code visible par Windows.
    /// </summary>
    private static readonly string[] KnownHotkeyNames =
    [
        "Ctrl", "LeftCtrl", "RightCtrl",
        "Alt", "LeftAlt", "RightAlt",
        "Shift", "LeftShift", "RightShift",
        "Win", "LeftWin", "RightWin",
        "CapsLock", "Space",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // --- Lecture --------------------------------------------------------------

    /// <summary>
    /// Lit le fichier s'il existe, sinon rend les valeurs par défaut. Un fichier
    /// illisible ou mal formé donne également les valeurs par défaut : la
    /// dictée doit continuer de fonctionner même si la configuration est cassée.
    /// </summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Construit une configuration valide à partir de JSON. Les clés inconnues
    /// sont ignorées, les valeurs invalides remplacées par leur défaut, et un
    /// JSON illisible rend simplement la configuration par défaut.
    /// </summary>
    public static AppSettings Parse(string json)
    {
        AppSettings? settings;

        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            settings = null;
        }

        settings ??= new AppSettings();
        settings.Normalize();
        return settings;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // --- Validation -----------------------------------------------------------

    /// <summary>
    /// Répare en place toute valeur hors domaine. Appelée après chaque lecture.
    /// </summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            ModelPath = new AppSettings().ModelPath;
        }

        Language = NormalizeLanguage(Language);
        Hotkey = NormalizeHotkey(Hotkey);
        RuntimePreference = NormalizeRuntimes(RuntimePreference);

        MinRecordingMilliseconds = Math.Clamp(MinRecordingMilliseconds, 0, 5_000);
        MaxRecordingSeconds = Math.Clamp(MaxRecordingSeconds, 5, 600);

        if (!Enum.IsDefined(Insertion))
        {
            Insertion = InsertionMode.Paste;
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        string? match = SupportedLanguages.FirstOrDefault(
            supported => string.Equals(supported, language?.Trim(), StringComparison.OrdinalIgnoreCase));

        return match ?? DefaultLanguage;
    }

    private static string[] NormalizeHotkey(string[]? hotkey)
    {
        if (hotkey is null || hotkey.Length == 0)
        {
            return [.. DefaultHotkey];
        }

        string?[] canonical = [.. hotkey.Select(CanonicalHotkeyName)];

        // Une touche inconnue invalide le raccourci entier, elle n'est jamais
        // simplement retirée. Retirer une touche ÉLARGIT la combinaison au
        // lieu de la restreindre : ["Ctrl", "Fn"] deviendrait ["Ctrl"], et la
        // dictée se déclencherait à chaque appui sur Ctrl. Mieux vaut revenir
        // au défaut connu que produire un raccourci plus permissif que voulu.
        if (canonical.Any(name => name is null))
        {
            return [.. DefaultHotkey];
        }

        return [.. canonical.OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    private static string? CanonicalHotkeyName(string? name) =>
        KnownHotkeyNames.FirstOrDefault(
            known => string.Equals(known, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string[] NormalizeRuntimes(string[]? runtimes)
    {
        string[] recognized = runtimes is null
            ? []
            : [.. runtimes
                .Select(CanonicalRuntimeName)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)];

        if (recognized.Length == 0)
        {
            return [.. DefaultRuntimePreference];
        }

        // Invariant : le processeur reste toujours joignable en dernier ressort.
        // Sans cette garantie, une configuration ne listant qu'un moteur
        // indisponible laisserait l'application sans aucun moyen de transcrire.
        return recognized.Contains(CpuRuntime, StringComparer.Ordinal)
            ? recognized
            : [.. recognized, CpuRuntime];
    }

    private static string? CanonicalRuntimeName(string? name) =>
        KnownRuntimes.FirstOrDefault(
            known => string.Equals(known, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
