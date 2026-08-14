using System.Text.RegularExpressions;

namespace HexWin.Transcription;

/// <summary>
/// Met au propre la sortie brute du moteur avant insertion dans le champ actif.
///
/// Deux familles de corrections, d'origines différentes.
///
/// Les filtres d'annotations et de génériques inventés viennent du temps de
/// Whisper, qui émet des marqueurs de bruit ambiant entre crochets et recrache
/// des formules de sous-titrage sur un enregistrement silencieux. Parakeet est
/// bien plus sobre là-dessus, mais ces filtres sont conservés : ils ne coûtent
/// rien et couvrent les cas où le moteur dérape sur du bruit.
///
/// L'espacement typographique, lui, concerne directement Parakeet, qui écrit
/// « vendredi? » à l'anglaise là où l'usage français attend « vendredi ? ».
///
/// Classe volontairement pure : aucune dépendance, entièrement testable.
/// </summary>
public static partial class TranscriptCleaner
{
    /// <summary>
    /// Assemble et nettoie les segments rendus par Whisper. Rend une chaîne
    /// vide s'il ne reste rien de significatif — l'appelant ne doit alors
    /// rien insérer du tout.
    /// </summary>
    public static string Clean(IEnumerable<string?>? segments)
    {
        if (segments is null)
        {
            return string.Empty;
        }

        return Clean(string.Join(' ', segments.Where(s => !string.IsNullOrEmpty(s))));
    }

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = BracketedAnnotation().Replace(text, " ");
        cleaned = NonSpeechParenthetical().Replace(cleaned, " ");
        cleaned = MusicalNotes().Replace(cleaned, " ");
        cleaned = HallucinatedCredits().Replace(cleaned, " ");
        cleaned = Whitespace().Replace(cleaned, " ").Trim();
        cleaned = FrenchPunctuationSpacing().Replace(cleaned, " $1");

        // Après retrait des annotations, il peut ne rester que de la
        // ponctuation orpheline. Insérer un « . » isolé serait pire que
        // de ne rien insérer.
        return ContainsMeaning().IsMatch(cleaned) ? cleaned : string.Empty;
    }

    /// <summary>
    /// Annotations de bruit ambiant : [BLANK_AUDIO], [Musique], [Applause]...
    /// Whisper les met systématiquement entre crochets, jamais la parole.
    /// </summary>
    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex BracketedAnnotation();

    /// <summary>
    /// Mêmes annotations, mais entre parenthèses selon les modèles. Ici on ne
    /// retire que les mentions connues de bruit : une parenthèse peut tout à
    /// fait faire partie de la dictée, et la supprimer serait une perte.
    /// </summary>
    [GeneratedRegex(
        @"\(\s*(?:musiques?|music|applaudissements?|applause|rires?|laughter|silence|"
        + @"bruits?|inaudible|soupirs?|toux|sifflements?)\b[^)]*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NonSpeechParenthetical();

    [GeneratedRegex(@"[♪♫🎵🎶]")]
    private static partial Regex MusicalNotes();

    /// <summary>
    /// Formules que Whisper invente sur un enregistrement quasi silencieux :
    /// il a été entraîné sur des sous-titres de vidéos, dont les génériques
    /// finissent par ressortir. Elles n'ont jamais été prononcées.
    ///
    /// Le mot « sous-titres » seul ne suffit pas à déclencher le retrait : il
    /// faut qu'une marque d'attribution suive (« réalisés par », « Société »,
    /// « ST' »...). Sans cette exigence, dicter « ajoute des sous-titres à la
    /// vidéo » verrait sa phrase amputée.
    /// </summary>
    /// <remarks>
    /// Le motif de queue <c>(?:[^.!?\n]|\.(?=\p{Ll}))*</c> avance jusqu'à la
    /// fin de la phrase, mais franchit un point suivi d'une minuscule : sans
    /// quoi « Amara.org » couperait la correspondance en plein milieu et
    /// laisserait un « org » orphelin dans le texte inséré.
    /// </remarks>
    [GeneratedRegex(
        @"(?:Sous-titr(?:es|age)\s+(?:r[ée]alis[ée]s?\s+par|par|Soci[ée]t[ée]|ST['’]|MFP\b)"
        + @"(?:[^.!?\n]|\.(?=\p{Ll}))*[.!?]?)"
        + @"|(?:SousTitreur\.com)"
        + @"|(?:Amara\.org)"
        + @"|(?:Merci d'avoir regard[ée] cette vid[ée]o\s*!?)"
        + @"|(?:Abonnez-vous\s*!?)"
        + @"|(?:Thanks for watching\s*!?)"
        + @"|(?:Subtitles by(?:[^.!?\n]|\.(?=\p{Ll}))*[.!?]?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex HallucinatedCredits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Rétablit l'espace que la typographie française met avant les signes
    /// doubles. Parakeet écrit « vendredi? », là où l'usage veut
    /// « vendredi ? ».
    ///
    /// Le signe doit suivre une lettre ou un chiffre et être suivi d'une
    /// espace ou de la fin du texte. Sans cette seconde condition, « 14:30 »
    /// et « https://exemple.fr » se retrouveraient coupés en deux.
    /// </summary>
    [GeneratedRegex(@"(?<=[\p{L}\p{N}])([?!;:»])(?=\s|$)")]
    private static partial Regex FrenchPunctuationSpacing();

    /// <summary>Au moins une lettre ou un chiffre : sinon il n'y a rien à insérer.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex ContainsMeaning();
}
