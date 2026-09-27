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
    private static string Dictate(params string[] segments) => Play(string.Empty, segments);

    /// <summary>Plays the segments into a document, Backspaces included.</summary>
    private static string Play(string document, params string[] segments)
    {
        var joiner = new SegmentJoiner();

        for (int i = 0; i < segments.Length; i++)
        {
            SegmentInsertion insertion = joiner.Next(segments[i], isLast: i == segments.Length - 1);
            document = document[..^insertion.Erase] + insertion.Text;
        }

        return document + joiner.Finish();
    }

    // --- Spoken edits across segments -------------------------------------------

    [Theory]
    [InlineData("Le chat est vert.", "Efface ça.", "Le chat est bleu.")]
    [InlineData("Le chat est vert.", "Non, efface ça. Le chat est bleu.", "")]
    [InlineData("Le chat est vert.", "Efface ça. Le chat est bleu.", "")]
    public void Efface_ca_in_its_own_segment_erases_the_sentence_already_typed(string first, string second, string third)
    {
        string[] segments = third.Length > 0 ? [first, second, third] : [first, second];

        Assert.Equal("Le chat est bleu.", Dictate(segments));
    }

    [Fact]
    public void Only_the_last_typed_sentence_is_erased()
    {
        Assert.Equal(
            "Bonjour. Le chat est bleu.",
            Dictate("Bonjour. Le chat est vert.", "Scratch that.", "Le chat est bleu."));
    }

    [Fact]
    public void Two_commands_erase_back_across_two_segments()
    {
        Assert.Equal("Un. Quatre.", Dictate("Un.", "Deux.", "Trois.", "Efface ça. Efface ça.", "Quatre."));
    }

    [Fact]
    public void Text_typed_before_the_dictation_is_never_erased()
    {
        // The document already held "Cher client, " when the dictation began.
        Assert.Equal("Cher client, Merci.", Play("Cher client, ", "Efface ça.", "Efface ça.", "Merci."));
    }

    [Fact]
    public void The_erase_count_matches_what_was_typed()
    {
        var joiner = new SegmentJoiner();

        // "Le chat est vert" went in, its full stop held.
        Assert.Equal(new SegmentInsertion(0, "Le chat est vert"), joiner.Next("Le chat est vert.", isLast: false));
        Assert.Equal(new SegmentInsertion(16, string.Empty), joiner.Next("Efface ça.", isLast: false));
        Assert.Equal(new SegmentInsertion(0, "Le chat est bleu."), joiner.Next("Le chat est bleu.", isLast: true));
    }

    [Fact]
    public void Hesitations_are_dropped_from_every_segment()
    {
        Assert.Equal("Je pense que c'est prêt.", Dictate("Euh, je pense.", "Que, euh, c'est prêt."));
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

        Assert.Equal("Je voudrais parler", joiner.Next("Je voudrais parler.", isLast: false).Text);
        Assert.Equal(" avec le client.", joiner.Next("Avec le client.", isLast: true).Text);
        Assert.Equal(string.Empty, joiner.Finish());
    }

    [Fact]
    public void A_dictation_ending_on_a_pause_gets_its_held_full_stop_back()
    {
        // The last pause was followed by no speech: no final segment arrives
        // to release the held full stop.
        var joiner = new SegmentJoiner();

        Assert.Equal("Bonjour", joiner.Next("Bonjour.", isLast: false).Text);
        Assert.Equal(".", joiner.Finish());
    }

    [Fact]
    public void An_ellipsis_is_not_mistaken_for_a_full_stop()
    {
        var joiner = new SegmentJoiner();

        Assert.Equal("Alors...", joiner.Next("Alors...", isLast: false).Text);
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
