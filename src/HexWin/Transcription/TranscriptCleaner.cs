using System.Text.RegularExpressions;

namespace HexWin.Transcription;

/// <summary>
/// Met au propre la sortie brute de Whisper avant insertion dans le champ actif.
///
/// Whisper ne produit pas que de la parole : il émet aussi des annotations de
/// bruit ambiant entre crochets, et invente parfois des formules toutes faites
/// sur un enregistrement quasi silencieux. Coller ça tel quel serait au mieux
/// surprenant, au pire embarrassant.
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

    /// <summary>Au moins une lettre ou un chiffre : sinon il n'y a rien à insérer.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex ContainsMeaning();
}
