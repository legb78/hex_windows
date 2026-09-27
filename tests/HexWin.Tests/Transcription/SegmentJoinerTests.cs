using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// A pause cuts a dictation into segments the engine transcribes one by one,
/// each closed with a full stop. These tests follow what reaches the document,
/// insertion after insertion.
/// </summary>
public class SegmentJoinerTests
{
    private static string Dictate(params string[] segments)
    {
        var joiner = new SegmentJoiner();
        string document = string.Empty;

        for (int i = 0; i < segments.Length; i++)
        {
            document += joiner.Next(segments[i], isLast: i == segments.Length - 1);
        }

        return document + joiner.Finish();
    }

    [Fact]
    public void A_single_segment_goes_in_untouched()
    {
        Assert.Equal("Le chat est bleu.", Dictate("Le chat est bleu."));
    }

    [Theory]
    [InlineData("Je voudrais parler.", "Avec le client.", "Je voudrais parler avec le client.")]
    [InlineData("Il faut vérifier.", "Que tout est prêt.", "Il faut vérifier que tout est prêt.")]
    [InlineData("I need to talk.", "To the client.", "I need to talk to the client.")]
    public void A_pause_mid_sentence_leaves_no_full_stop(string first, string second, string expected)
    {
        Assert.Equal(expected, Dictate(first, second));
    }

    [Theory]
    [InlineData("Je suis arrivé.", "Le train était en retard.", "Je suis arrivé. Le train était en retard.")]
    [InlineData("I arrived.", "The train was late.", "I arrived. The train was late.")]
    [InlineData("C'est prêt.", "Qu'est-ce qu'on fait ?", "C'est prêt. Qu'est-ce qu'on fait ?")]
    public void A_new_sentence_keeps_its_full_stop(string first, string second, string expected)
    {
        Assert.Equal(expected, Dictate(first, second));
    }

    [Fact]
    public void A_question_mark_is_never_held_nor_dropped()
    {
        Assert.Equal("Tu viens ? Et ta sœur ?", Dictate("Tu viens ?", "Et ta sœur ?"));
    }

    [Fact]
    public void The_full_stop_of_a_segment_is_held_until_the_next_one_decides()
    {
        var joiner = new SegmentJoiner();

        Assert.Equal("Je voudrais parler", joiner.Next("Je voudrais parler.", isLast: false));
        Assert.Equal(" avec le client.", joiner.Next("Avec le client.", isLast: true));
        Assert.Equal(string.Empty, joiner.Finish());
    }

    [Fact]
    public void A_dictation_ending_on_a_pause_gets_its_held_full_stop_back()
    {
        // The last pause was followed by no speech: no final segment arrives
        // to release the held full stop.
        var joiner = new SegmentJoiner();

        Assert.Equal("Bonjour", joiner.Next("Bonjour.", isLast: false));
        Assert.Equal(".", joiner.Finish());
    }

    [Fact]
    public void An_ellipsis_is_not_mistaken_for_a_full_stop()
    {
        var joiner = new SegmentJoiner();

        Assert.Equal("Alors...", joiner.Next("Alors...", isLast: false));
        Assert.Equal(string.Empty, joiner.Finish());
    }

    [Fact]
    public void Three_segments_chain_in_order()
    {
        Assert.Equal(
            "Je pense que le projet avance bien. Il reste les tests.",
            Dictate("Je pense.", "Que le projet avance bien.", "Il reste les tests."));
    }
}
