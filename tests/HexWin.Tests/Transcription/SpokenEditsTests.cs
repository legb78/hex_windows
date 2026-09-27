using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

public class SpokenEditsTests
{
    // --- Hesitations -------------------------------------------------------------

    [Theory]
    [InlineData("Euh, le chat est bleu.", "Le chat est bleu.")]
    [InlineData("Je pense, euh, que c'est prêt.", "Je pense que c'est prêt.")]
    [InlineData("Le chat euh est bleu.", "Le chat est bleu.")]
    [InlineData("On se voit à midi, heu.", "On se voit à midi.")]
    [InlineData("C'est fait. Euuuh, on passe à la suite.", "C'est fait. On passe à la suite.")]
    [InlineData("Hum... d'accord.", "D'accord.")]
    [InlineData("Um, the cat is blue.", "The cat is blue.")]
    [InlineData("I think, uh, we should go.", "I think we should go.")]
    [InlineData("Hmm, erm, maybe tomorrow.", "Maybe tomorrow.")]
    [InlineData("I think. Uh. We should go.", "I think. We should go.")]
    [InlineData("Uh. We should go.", "We should go.")]
    [InlineData("Bon. Hum, ou plutôt à 13h.", "Bon. Ou plutôt à 13h.")]
    public void Hesitations_disappear(string dictated, string expected)
    {
        Assert.Equal(expected, SpokenEdits.Apply(dictated).Text);
    }

    [Theory]
    [InlineData("L'humain est au centre.")]
    [InlineData("Il fait humide, heureusement.")]
    [InlineData("The umbrella is under the hub.")]
    public void Words_that_merely_contain_a_hesitation_are_kept(string dictated)
    {
        Assert.Equal(dictated, SpokenEdits.Apply(dictated).Text);
    }

    // --- Erase commands ----------------------------------------------------------

    [Theory]
    [InlineData("Le chat est vert. Efface ça. Le chat est bleu.")]
    [InlineData("Le chat est vert, efface ça, le chat est bleu.")]
    [InlineData("Le chat est vert. Non, efface ça. Le chat est bleu.")]
    [InlineData("Le chat est vert non efface ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Et face ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Efface cela. Le chat est bleu.")]
    [InlineData("Le chat est vert. Effacez ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Supprime ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Annule ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Retire ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Enlève ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Raye ça. Le chat est bleu.")]
    [InlineData("Le chat est vert. Efface la dernière phrase. Le chat est bleu.")]
    [InlineData("Le chat est vert. Supprime cette phrase. Le chat est bleu.")]
    [InlineData("Le chat est vert. Efface la phrase précédente. Le chat est bleu.")]
    [InlineData("Le chat est vert. Efface ce que je viens de dire. Le chat est bleu.")]
    public void A_french_erase_command_removes_the_sentence_before_it(string dictated)
    {
        Assert.Equal("Le chat est bleu.", SpokenEdits.Apply(dictated).Text);
    }

    [Theory]
    [InlineData("The cat is green. Scratch that. The cat is blue.")]
    [InlineData("The cat is green, scratch that, the cat is blue.")]
    [InlineData("The cat is green. No, scratch that. The cat is blue.")]
    [InlineData("The cat is green. Delete that. The cat is blue.")]
    [InlineData("The cat is green. Erase that. The cat is blue.")]
    [InlineData("The cat is green. Strike that. The cat is blue.")]
    [InlineData("The cat is green. Undo that. The cat is blue.")]
    [InlineData("The cat is green. Remove that. The cat is blue.")]
    [InlineData("The cat is green. Delete the last sentence. The cat is blue.")]
    [InlineData("The cat is green. Scratch the previous sentence. The cat is blue.")]
    [InlineData("The cat is green. Delete what I just said. The cat is blue.")]
    public void An_english_erase_command_removes_the_sentence_before_it(string dictated)
    {
        Assert.Equal("The cat is blue.", SpokenEdits.Apply(dictated).Text);
    }

    [Fact]
    public void An_ellipsis_survives_the_tidying()
    {
        Assert.Equal("Alors... on y va.", SpokenEdits.Apply("Alors... euh, on y va.").Text);
    }

    [Fact]
    public void Only_the_last_sentence_is_erased()
    {
        Assert.Equal(
            "Bonjour. Le chat est bleu.",
            SpokenEdits.Apply("Bonjour. Le chat est vert. Efface ça. Le chat est bleu.").Text);
    }

    [Fact]
    public void Two_commands_erase_two_sentences()
    {
        Assert.Equal(
            "Bonjour. Fin.",
            SpokenEdits.Apply("Bonjour. Un. Deux. Efface ça. Efface ça. Fin.").Text);
    }

    [Fact]
    public void A_command_with_nothing_before_it_is_counted_for_the_text_already_inserted()
    {
        // The sentence it aims at went into the document with an earlier
        // segment: only the caller can erase it.
        Assert.Equal(("Le chat est bleu.", 1), SpokenEdits.Apply("Efface ça. Le chat est bleu."));
        Assert.Equal((string.Empty, 1), SpokenEdits.Apply("Scratch that."));
        Assert.Equal(("Fin.", 2), SpokenEdits.Apply("Efface ça. Efface ça. Fin."));
    }

    [Fact]
    public void Hesitations_and_a_command_together()
    {
        Assert.Equal(
            "Le chat est bleu.",
            SpokenEdits.Apply("Euh, le chat est vert. Euh, efface ça. Le chat est bleu.").Text);
    }

    [Theory]
    [InlineData("Efface ça du tableau, s'il te plaît.")]
    [InlineData("Il faut que tu effaces ça avant demain.")]
    [InlineData("Pierre efface ça tous les matins.")]
    [InlineData("Can you delete that file?")]
    [InlineData("Please delete that.")]
    [InlineData("Never mind, I will do it.")]
    [InlineData("Oublie ça, on verra demain.")]
    public void Dictated_text_that_looks_like_a_command_is_kept(string dictated)
    {
        // A command must stand alone, as its own clause: anything else is
        // text the user wants in the document.
        Assert.Equal(dictated, SpokenEdits.Apply(dictated).Text);
    }
}
