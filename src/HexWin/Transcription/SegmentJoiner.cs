using System.Text.RegularExpressions;

namespace HexWin.Transcription;

/// <summary>What a segment changes in the document.</summary>
/// <param name="Erase">Characters to delete first, backwards from the caret.</param>
/// <param name="Text">Text to insert after that.</param>
public readonly record struct SegmentInsertion(int Erase, string Text);

/// <summary>
/// Turns the segments of one dictation into what goes into the document: the
/// edits the user spoke, and the stitching across pauses.
///
/// <para><b>Spoken edits.</b> Hesitations are dropped and "efface ça" removes
/// the sentence before it, see <see cref="SpokenEdits"/>. When a dictation is
/// inserted sentence by sentence, that sentence is often already typed: the
/// user says it, pauses, then says "efface ça". The joiner keeps the text it
/// inserted during the dictation, and answers with the number of characters
/// to erase. Nothing typed before the dictation is ever touched.</para>
///
/// <para><b>Stitching.</b> The engine transcribes each segment on its own and
/// closes every one with a full stop and opens it with a capital: "Je voudrais
/// parler." then "Avec le client.". Once inserted, that full stop could only
/// be taken back by erasing. So it is <b>held</b>: each segment goes in
/// without its final full stop, and the next one decides. If it opens with a
/// word that carries a sentence on — "avec", "and", "que"... — the full stop
/// is dropped and that word lowered; otherwise the full stop goes in first.
/// The last segment keeps its punctuation.</para>
///
/// <para>Only words that almost never open a sentence count. Articles are left
/// out: "Le train était en retard." starts a sentence far more often than it
/// continues one. Question and exclamation marks are never held either: a
/// pause after them does end the sentence.</para>
///
/// <para>Pure logic, one instance per dictation.</para>
/// </summary>
public sealed partial class SegmentJoiner
{
    /// <summary>What this dictation has put in the document so far.</summary>
    private string _document = string.Empty;

    private string _held = string.Empty;

    /// <param name="text">The cleaned transcription of the segment.</param>
    /// <param name="isLast">
    /// No segment will follow: its full stop is inserted rather than held.
    /// </param>
    public SegmentInsertion Next(string text, bool isLast)
    {
        ArgumentNullException.ThrowIfNull(text);

        (string edited, int earlierErasures) = SpokenEdits.Apply(text);
        int erase = EraseSentences(earlierErasures);

        if (!SpokenEdits.ContainsMeaning().IsMatch(edited))
        {
            return new SegmentInsertion(erase, string.Empty);
        }

        string joined = _document.Length == 0 ? edited : Join(edited);
        _held = string.Empty;

        if (!isLast && joined.EndsWith('.') && !joined.EndsWith("..", StringComparison.Ordinal))
        {
            _held = ".";
            joined = joined[..^1];
        }

        _document += joined;

        return new SegmentInsertion(erase, joined);
    }

    /// <summary>
    /// The punctuation still held once the dictation is over — when the last
    /// pause was followed by no speech at all. Empty if there is none.
    /// </summary>
    public string Finish()
    {
        string held = _held;
        _document += held;
        _held = string.Empty;

        return held;
    }

    /// <summary>
    /// Takes the last sentences back out of what this dictation inserted, and
    /// returns how many characters that removes. A held full stop was never
    /// inserted: it simply goes with its sentence.
    /// </summary>
    private int EraseSentences(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        string kept = _document;

        for (int i = 0; i < count && kept.Length > 0; i++)
        {
            kept = SpokenEdits.WithoutLastSentence(kept);
        }

        int erased = _document.Length - kept.Length;
        _document = kept;
        _held = string.Empty;

        return erased;
    }

    private string Join(string text)
    {
        if (!_held.Equals(".", StringComparison.Ordinal) || !ContinuesSentence(text))
        {
            return _held + " " + text;
        }

        return " " + char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static bool ContinuesSentence(string text)
    {
        Match first = FirstWord().Match(text);

        return first.Success && ContinuationWords.Contains(first.Value.ToLowerInvariant());
    }

    /// <summary>
    /// Data, matched against what the engine writes: not to be translated.
    /// The elided "qu'" and "d'" are left out on purpose: "Qu'est-ce que" and
    /// "D'accord" open sentences all the time.
    /// </summary>
    private static readonly HashSet<string> ContinuationWords =
    [
        "et", "ou", "donc", "car", "que", "qui", "dont", "de", "du", "des",
        "à", "au", "aux", "pour", "par", "avec", "sans", "sur", "sous", "dans",
        "chez", "vers", "entre", "parce", "puisque", "lorsque", "afin",
        "and", "or", "because", "that", "which", "whom", "whose", "of", "to",
        "for", "with", "without", "from", "into", "than", "about", "by",
    ];

    [GeneratedRegex(@"^\p{L}+")]
    private static partial Regex FirstWord();
}
