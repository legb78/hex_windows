using System.Text.RegularExpressions;

namespace HexWin.Transcription;

/// <summary>
/// Edits the user makes by voice: hesitations dropped, and a spoken command —
/// "efface ça", "scratch that" — that removes the sentence before it.
///
/// <para>Both are fixed rules, with no language model: they cost nothing and
/// never rewrite what was said. What they cannot do is understand a
/// correction left implicit — "le chat est vert, oh, il est bleu" stays as
/// dictated; only an explicit command erases.</para>
///
/// <para><b>A command must stand as its own clause</b>: at the start, after
/// punctuation or after "non", "no", "pardon"... — erased along with it — and
/// followed by punctuation or the end. "Efface ça du tableau" or "il faut que tu effaces ça" are
/// dictated text, not commands. Everyday phrases such as "never mind" or
/// "oublie ça" are left out for the same reason: they belong in the messages
/// people dictate far more often than they are meant as orders.</para>
///
/// <para>A command with no sentence before it in the text given aims at a
/// sentence inserted earlier — the previous segment of a dictation inserted
/// sentence by sentence. It is removed and counted, for
/// <see cref="SegmentJoiner"/> to erase that sentence from the document.</para>
///
/// <para>Pure logic, entirely testable.</para>
/// </summary>
public static partial class SpokenEdits
{
    /// <returns>
    /// The edited text, and how many sentences inserted before it the user
    /// asked to erase.
    /// </returns>
    public static (string Text, int EarlierErasures) Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return EraseCommands(RemoveFillers(text));
    }

    private static string RemoveFillers(string text)
    {
        // Repeated until nothing is left to remove: in "Hmm, erm, maybe" the
        // first match captures the "e" of "erm" as the letter to capitalise,
        // hiding the second hesitation from that pass.
        string previous;

        do
        {
            previous = text;
            text = Tidy(Filler().Replace(previous, match =>
            {
                string next = match.Groups["next"].Value;
                return StartsSentence(previous, match.Index) ? next.ToUpperInvariant() : " " + next;
            }));
        }
        while (text != previous);

        return text;
    }

    private static (string Text, int EarlierErasures) EraseCommands(string text)
    {
        int earlier = 0;
        Match command = EraseCommand().Match(text);

        while (command.Success)
        {
            string before = text[..command.Index];
            string after = text[(command.Index + command.Length)..].TrimStart(' ', ',', ';', ':', '.', '!', '?', '…');

            if (ContainsMeaning().IsMatch(before))
            {
                before = WithoutLastSentence(before);
            }
            else
            {
                earlier++;
            }

            text = Tidy(before + " " + CapitalizeFirst(after));
            command = EraseCommand().Match(text);
        }

        return (text, earlier);
    }

    /// <summary>
    /// Cuts the text back to the end of its previous sentence. The sentence
    /// the command follows is closed by its own full stop, which is stripped
    /// first so the cut reaches the one before.
    /// </summary>
    internal static string WithoutLastSentence(string text)
    {
        string trimmed = text.TrimEnd(' ', ',', ';', ':', '.', '!', '?', '…');
        int end = trimmed.LastIndexOfAny(['.', '!', '?', '…']);

        return end < 0 ? string.Empty : trimmed[..(end + 1)];
    }

    /// <summary>
    /// An ellipsis does not count as a sentence end: "Alors... on y va" goes
    /// on after it more often than not.
    /// </summary>
    private static bool StartsSentence(string text, int index)
    {
        string before = text[..index].TrimEnd();

        return before.Length == 0
            || before[^1] is '!' or '?'
            || (before[^1] == '.' && !before.EndsWith("..", StringComparison.Ordinal));
    }

    private static string CapitalizeFirst(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Closes the gaps a removal leaves: doubled spaces, a space stranded
    /// before a comma or a full stop, and the full stop of a hesitation the
    /// engine wrote as a sentence of its own — "I think. Uh. We should".
    /// </summary>
    private static string Tidy(string text)
    {
        string tidied = OrphanMark().Replace(Spaces().Replace(text, " "), string.Empty);

        return SpaceBeforeClosingMark().Replace(tidied, "$1").Trim();
    }

    /// <summary>
    /// "euh", "heu", "hum", "hmm", "uh", "um", "erm" and their drawn-out forms
    /// ("euuuh", "hmmm"), with the commas or ellipsis the engine puts around
    /// them. The next letter is captured so it can take the capital when the
    /// hesitation opened the sentence.
    ///
    /// <para>These alternatives are data, matched against what the engine
    /// writes: they must not be translated.</para>
    /// </summary>
    [GeneratedRegex(
        @"(?:,\s*)?\b(?:e+u+h+|h+e+u+h*|h+u+m+|h+m+|m+m+|m+h+|u+h+|u+m+|u+h+m+|e+r+m+)\b"
        + @"(?:\s*(?:,|\.{3}|…))?\s*(?<next>\p{L})?",
        RegexOptions.IgnoreCase)]
    private static partial Regex Filler();

    /// <summary>
    /// The erase commands, French then English. A verb from each list with any
    /// of its objects: "efface ça", "supprime la dernière phrase", "annulez
    /// cette phrase", "scratch that", "delete the last sentence", "undo that"...
    /// "Et face ça" is how Parakeet was measured to hear "efface ça" said
    /// quickly; nobody dictates it for its own sake.
    ///
    /// <para>These alternatives are data, matched against what the engine
    /// writes: they must not be translated.</para>
    /// </summary>
    [GeneratedRegex(
        @"(?:(?<=^|[.!?…,;:]\s*)(?:" + Interjection + @")?|(?<=\s)" + Interjection + ")"
        + @"(?:"
        + @"(?:efface|et\s+face|supprime|annule|retire|enlève|raye|barre)z?\s+"
        + @"(?:ça|ca|cela|ceci|(?:la|cette)\s+(?:dernière\s+)?phrase(?:\s+précédente)?|ce\s+que\s+je\s+viens\s+de\s+dire)"
        + @"|"
        + @"(?:scratch|delete|erase|remove|strike|undo|cancel)\s+"
        + @"(?:that|this|(?:the|that|this)\s+(?:last\s+|previous\s+)?(?:sentence|part|line)|last\s+sentence|what\s+i\s+(?:just\s+)?said)"
        + @")"
        + @"(?=\s*(?:[.!?…,;:]|$))",
        RegexOptions.IgnoreCase)]
    private static partial Regex EraseCommand();

    /// <summary>
    /// "Non, efface ça": the word that announces a correction goes with it.
    /// </summary>
    private const string Interjection = @"\b(?:non|no|oh|pardon|sorry|wait|attends)\s*,?\s+";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[\p{L}\p{N}]")]
    internal static partial Regex ContainsMeaning();

    [GeneratedRegex(@"\s+([,.])")]
    private static partial Regex SpaceBeforeClosingMark();

    /// <summary>
    /// A full stop or comma left with nothing before it: at the very start, or
    /// right after another sentence's end. The ellipsis is spared, its dots
    /// being joined.
    /// </summary>
    [GeneratedRegex(@"^\s*[.,](?!\.)\s*|(?<=[.!?…])\s+[.,](?!\.)")]
    private static partial Regex OrphanMark();
}
