using System.Globalization;

namespace HexWin.Ui;

/// <summary>
/// How the settings window writes values and names settings. Kept apart from
/// the window, which cannot be tested, because the wording is exactly what a
/// test can pin down.
/// </summary>
public static class SettingText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>The settings.json keys, as the window names them.</summary>
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["modelPath"] = "Dossier du modèle",
        ["hotkey"] = "Raccourci",
        ["minRecordingMilliseconds"] = "Durée minimale",
        ["maxRecordingSeconds"] = "Durée maximale",
        ["segmentation"] = "Insérer phrase par phrase",
        ["pauseMilliseconds"] = "Pause qui coupe une phrase",
        ["unloadAfterMinutes"] = "Libérer la mémoire après",
        ["provider"] = "Processeur de calcul",
        ["threads"] = "Fils de calcul",
        ["insertion"] = "Insertion du texte",
        ["feedback"] = "Cercle et son",
        ["feedbackColor"] = "Couleur du cercle",
        ["feedbackSize"] = "Taille du cercle",
        ["feedbackOpacity"] = "Opacité du cercle",
        ["feedbackTopMargin"] = "Marge du cercle",
        ["logEnabled"] = "Journal des dictées",
    };

    /// <summary>A setting's name in the window; its key when the window has none.</summary>
    public static string NameOf(string key) => Names.TryGetValue(key, out string? name) ? name : key;

    public static string Milliseconds(int value) => $"{value.ToString("N0", French)} ms";

    /// <summary>Seconds, turned into minutes once there are enough of them.</summary>
    public static string Seconds(int value)
    {
        if (value < 60)
        {
            return $"{value} s";
        }

        int minutes = value / 60;
        int seconds = value % 60;

        return seconds == 0 ? $"{minutes} min" : $"{minutes} min {seconds:00} s";
    }

    /// <summary>
    /// The delay before the model is released. Zero is not a delay but the
    /// absence of one: the model stays loaded, and the text has to say so.
    /// </summary>
    public static string IdleMinutes(int value)
    {
        if (value == 0)
        {
            return "Jamais";
        }

        if (value < 60)
        {
            return $"{value} min";
        }

        int hours = value / 60;
        int minutes = value % 60;

        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes:00}";
    }

    /// <summary>
    /// The pause that closes a sentence. Zero turns the cutting off, and the
    /// text has to say that, not "0 ms", which reads as a pause of no length.
    /// </summary>
    public static string Pause(int value) => value == 0 ? "Désactivée" : Milliseconds(value);

    public static string Pixels(int value) => $"{value} px";

    /// <summary>An opacity from 0 to 255, as the percentage people think in.</summary>
    public static string Opacity(int value) => $"{(int)Math.Round(value * 100 / 255.0)} %";

    public static string Threads(int value) => value == 1 ? "1 fil" : $"{value} fils";
}
