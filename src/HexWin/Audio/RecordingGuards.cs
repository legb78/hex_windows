using HexWin.Configuration;

namespace HexWin.Audio;

/// <summary>
/// Bornes de durée d'un enregistrement, et décisions qui en découlent.
///
/// Deux situations à couvrir, toutes deux vécues par n'importe quel
/// utilisateur de push-to-talk :
///
/// - l'appui accidentel, trop bref pour contenir de la parole. Le transcrire
///   ferait tourner le moteur pour rien et risquerait d'insérer une formule
///   inventée par le modèle ;
/// - la touche restée enfoncée — poche, autre fenêtre, distraction. Sans
///   plafond, l'enregistrement grossirait indéfiniment en mémoire.
///
/// Logique pure, donc entièrement testable sans micro.
/// </summary>
public readonly record struct RecordingGuards(TimeSpan Minimum, TimeSpan Maximum)
{
    public static RecordingGuards From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new RecordingGuards(
            TimeSpan.FromMilliseconds(settings.MinRecordingMilliseconds),
            TimeSpan.FromSeconds(settings.MaxRecordingSeconds));
    }

    /// <summary>Appui trop bref : rien ne doit être transcrit.</summary>
    public bool IsTooShort(TimeSpan duration) => duration < Minimum;

    /// <summary>Plafond atteint : la capture doit être coupée d'elle-même.</summary>
    public bool HasReachedMaximum(TimeSpan elapsed) => elapsed >= Maximum;

    /// <summary>Taille au-delà de laquelle la capture cesse d'accumuler.</summary>
    public long MaximumBytes => RecordingFormat.BytesFor(Maximum);
}
