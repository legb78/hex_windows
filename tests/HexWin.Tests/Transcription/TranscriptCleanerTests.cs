using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// Whisper does not return speech alone: it annotates ambient noise and
/// sometimes invents closing-credit boilerplate on a silent recording. Those
/// cases are checked here, because otherwise they would end up pasted verbatim
/// into the document of the user.
///
/// The dictated samples stay in French on purpose: they exercise the
/// French-specific patterns and the French typography rules, which is exactly
/// what the cleaner exists for.
/// </summary>
public class TranscriptCleanerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  ")]
    public void An_empty_input_produces_nothing(string? input)
    {
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(input));
    }

    [Fact]
    public void The_segments_are_joined_in_order()
    {
        string[] segments = [" Bonjour,", " ceci est", " un test."];

        Assert.Equal("Bonjour, ceci est un test.", TranscriptCleaner.Clean(segments));
    }

    [Fact]
    public void A_null_segment_list_produces_nothing()
    {
        Assert.Equal(string.Empty, TranscriptCleaner.Clean((IEnumerable<string?>?)null));
    }

    // --- Bracketed annotations -------------------------------------------------

    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("[Musique]")]
    [InlineData("[APPLAUSE]")]
    [InlineData("[_BEG_]")]
    [InlineData("[bruit de fond]")]
    public void Bracketed_annotations_disappear(string annotation)
    {
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean($"{annotation} Bonjour."));
    }

    [Fact]
    public void Several_annotations_disappear_together()
    {
        string cleaned = TranscriptCleaner.Clean("[Musique] Bonjour [BLANK_AUDIO] tout le monde. [Fin]");

        Assert.Equal("Bonjour tout le monde.", cleaned);
    }

    [Fact]
    public void A_text_reduced_to_annotations_produces_nothing()
    {
        // A very common case: the key is released before anything was said.
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("[BLANK_AUDIO]"));
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(" [Musique] [BLANK_AUDIO] "));
    }

    // --- Parenthesised annotations ---------------------------------------------

    [Theory]
    [InlineData("(Musique)")]
    [InlineData("(musique douce)")]
    [InlineData("(Applaudissements)")]
    [InlineData("(rires)")]
    [InlineData("(silence)")]
    [InlineData("(inaudible)")]
    public void Parenthesised_noises_disappear(string annotation)
    {
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean($"{annotation} Bonjour."));
    }

    [Fact]
    public void A_parenthesis_that_is_part_of_the_speech_is_kept()
    {
        // An important point: not every parenthesis can be removed. They are
        // part of the dictation as soon as they do not name a noise.
        const string dictated = "Le rapport (version deux) doit partir demain.";

        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    // --- Invented boilerplate ---------------------------------------------------

    [Theory]
    [InlineData("Sous-titres réalisés par la communauté d'Amara.org")]
    [InlineData("Sous-titrage Société Radio-Canada")]
    [InlineData("Merci d'avoir regardé cette vidéo !")]
    [InlineData("Abonnez-vous !")]
    [InlineData("Thanks for watching!")]
    public void Invented_closing_credits_disappear(string hallucination)
    {
        // Whisper was trained on video subtitles: on a near-silent recording it
        // spits these credits back out, though none of it was ever spoken.
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(hallucination));
    }

    [Theory]
    [InlineData("Ajoute des sous-titres à la vidéo.")]
    [InlineData("Le sous-titrage est prêt.")]
    [InlineData("Merci d'avoir relu le document.")]
    public void A_legitimate_dictation_mentioning_subtitles_is_kept(string dictated)
    {
        // A guard against an over-eager filter: the word alone must not be
        // enough to make the sentence vanish. An attribution marker is needed
        // — "réalisés par", "Société"... — to conclude it is invented credits.
        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    [Fact]
    public void Invented_boilerplate_at_the_end_is_removed_without_touching_the_rest()
    {
        string cleaned = TranscriptCleaner.Clean(
            "Rappelle-moi d'appeler le client demain. Merci d'avoir regardé cette vidéo !");

        Assert.Equal("Rappelle-moi d'appeler le client demain.", cleaned);
    }

    // --- Formatting -------------------------------------------------------------

    [Fact]
    public void Multiple_spaces_are_reduced_to_one()
    {
        Assert.Equal("Bonjour tout le monde.", TranscriptCleaner.Clean("Bonjour    tout\n\nle\tmonde."));
    }

    [Fact]
    public void The_leading_space_of_whisper_segments_is_removed()
    {
        // Whisper always prefixes its segments with a space.
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean(" Bonjour."));
    }

    [Fact]
    public void Musical_notes_disappear()
    {
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("♪ ♫"));
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean("♪ Bonjour. ♪"));
    }

    // --- French typography -------------------------------------------------------

    [Theory]
    [InlineData("Tu viens vendredi?", "Tu viens vendredi ?")]
    [InlineData("Quelle horreur!", "Quelle horreur !")]
    [InlineData("Voici la liste:", "Voici la liste :")]
    [InlineData("Il part; elle reste.", "Il part ; elle reste.")]
    public void The_space_before_double_punctuation_is_restored(string raw, string expected)
    {
        // Parakeet writes "vendredi?" the English way. French usage puts a
        // space before double punctuation marks.
        Assert.Equal(expected, TranscriptCleaner.Clean(raw));
    }

    [Theory]
    [InlineData("Rendez-vous à 14:30.")]
    [InlineData("Va sur https://exemple.fr aujourd'hui.")]
    [InlineData("Le ratio est de 3:1 environ.")]
    public void A_colon_against_a_digit_or_a_url_is_left_alone(string dictated)
    {
        // The mark should only be spaced when it ends a word. Without that
        // condition, a time or an address would end up cut in two.
        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    [Fact]
    public void A_space_already_present_is_not_doubled()
    {
        Assert.Equal("Tu viens vendredi ?", TranscriptCleaner.Clean("Tu viens vendredi ?"));
    }

    [Fact]
    public void A_text_with_no_letter_or_digit_produces_nothing()
    {
        // Inserting a lone "." or "..." would be worse than doing nothing.
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("."));
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(" ... "));
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("!?"));
    }

    [Fact]
    public void A_normal_text_passes_through_the_cleaning_unharmed()
    {
        const string dictated =
            "Bonjour Marie, peux-tu relire le devis numéro 4218 avant vendredi ? Merci beaucoup.";

        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    [Fact]
    public void Accents_and_apostrophes_are_preserved()
    {
        const string dictated = "L'équipe a déjà terminé l'intégration côté serveur.";

        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }
}
