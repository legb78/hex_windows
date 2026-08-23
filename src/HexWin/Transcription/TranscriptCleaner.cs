using System.Text.RegularExpressions;

namespace HexWin.Transcription;

/// <summary>
/// Tidies up the raw engine output before it goes into the active field.
///
/// Two families of correction, from different origins.
///
/// The filters for annotations and invented credits date from the Whisper era:
/// Whisper emits ambient-noise markers in square brackets and spits out
/// subtitling boilerplate on a silent recording. Parakeet is far more sober
/// about that, but the filters are kept: they cost nothing and cover the cases
/// where the engine slips on noise.
///
/// The typographic spacing, by contrast, concerns Parakeet directly, which
/// writes "vendredi?" the English way where French usage expects
/// "vendredi ?".
///
/// A deliberately pure class: no dependency, entirely testable.
/// </summary>
public static partial class TranscriptCleaner
{
    /// <summary>
    /// Joins and cleans the segments the engine returned. Returns an empty
    /// string if nothing meaningful is left — the caller must then insert
    /// nothing at all.
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

        // Once the annotations are gone, nothing may remain but orphaned
        // punctuation. Inserting a lone "." would be worse than inserting
        // nothing.
        return ContainsMeaning().IsMatch(cleaned) ? cleaned : string.Empty;
    }

    /// <summary>
    /// Ambient-noise annotations: [BLANK_AUDIO], [Musique], [Applause]...
    /// Whisper always puts them in square brackets, and never speech.
    /// </summary>
    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex BracketedAnnotation();

    /// <summary>
    /// The same annotations, but in parentheses depending on the model. Here
    /// only known noise mentions are removed: a parenthesis can perfectly well
    /// be part of the dictation, and deleting it would be a loss.
    ///
    /// The alternatives below are data, not prose: they match what the model
    /// emits in French as well as in English, and must not be translated.
    /// </summary>
    [GeneratedRegex(
        @"\(\s*(?:musiques?|music|applaudissements?|applause|rires?|laughter|silence|"
        + @"bruits?|inaudible|soupirs?|toux|sifflements?)\b[^)]*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NonSpeechParenthetical();

    [GeneratedRegex(@"[♪♫🎵🎶]")]
    private static partial Regex MusicalNotes();

    /// <summary>
    /// Boilerplate Whisper invents on a near-silent recording: it was trained
    /// on video subtitles, whose closing credits end up resurfacing. None of
    /// it was ever spoken.
    ///
    /// The word "sous-titres" alone is not enough to trigger removal: an
    /// attribution marker has to follow ("réalisés par", "Société", "ST'"...).
    /// Without that requirement, dictating "ajoute des sous-titres à la vidéo"
    /// would see the sentence cut short.
    ///
    /// Like the pattern above, these alternatives are data: they reproduce the
    /// exact French wording the model produces, and translating them would
    /// silently stop the cleaning from matching anything.
    /// </summary>
    /// <remarks>
    /// The tail pattern <c>(?:[^.!?\n]|\.(?=\p{Ll}))*</c> runs to the end of
    /// the sentence but steps over a full stop followed by a lowercase letter:
    /// otherwise "Amara.org" would cut the match in half and leave an orphaned
    /// "org" in the inserted text.
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
    /// Restores the space French typography puts before double punctuation
    /// marks. Parakeet writes "vendredi?" where usage wants "vendredi ?".
    ///
    /// The mark must follow a letter or a digit and be followed by a space or
    /// the end of the text. Without that second condition, "14:30" and
    /// "https://exemple.fr" would end up cut in two.
    /// </summary>
    [GeneratedRegex(@"(?<=[\p{L}\p{N}])([?!;:»])(?=\s|$)")]
    private static partial Regex FrenchPunctuationSpacing();

    /// <summary>At least one letter or digit: otherwise there is nothing to insert.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex ContainsMeaning();
}
