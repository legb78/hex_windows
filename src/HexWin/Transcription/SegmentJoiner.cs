using System.Text.RegularExpressions;

namespace HexWin.Transcription;

/// <summary>
/// Stitches the segments of a dictation inserted sentence by sentence, so a
/// pause in the middle of a sentence does not leave a full stop behind it.
///
/// <para>The engine transcribes each segment on its own and closes every one
/// with a full stop and opens it with a capital: "Je voudrais parler." then
/// "Avec le client.". Once inserted, that full stop cannot be taken back
/// safely. So it is <b>held</b>: each segment goes in without its final full
/// stop, and the next one decides. If it opens with a word that carries a
/// sentence on — "avec", "and", "que"... — the full stop is dropped and that
/// word lowered; otherwise the full stop goes in first. The last segment keeps
/// its punctuation.</para>
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
    private bool _started;
    private string _held = string.Empty;

    /// <summary>
    /// Returns the text to insert for a new segment.
    /// </summary>
    /// <param name="isLast">
    /// No segment will follow: its full stop is inserted rather than held.
    /// </param>
    public string Next(string text, bool isLast)
    {
        ArgumentNullException.ThrowIfNull(text);

        string joined = _started ? Join(text) : text;
        _started = true;
        _held = string.Empty;

        if (!isLast && joined.EndsWith('.') && !joined.EndsWith("..", StringComparison.Ordinal))
        {
            _held = ".";
            return joined[..^1];
        }

        return joined;
    }

    /// <summary>
    /// The punctuation still held once the dictation is over — when the last
    /// pause was followed by no speech at all. Empty if there is none.
    /// </summary>
    public string Finish()
    {
        string held = _held;
        _held = string.Empty;

        return held;
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
