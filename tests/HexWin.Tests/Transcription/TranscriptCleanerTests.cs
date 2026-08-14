using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// Whisper ne rend pas que de la parole : il annote les bruits ambiants et
/// invente parfois des formules de générique sur un enregistrement silencieux.
/// Ces cas sont vérifiés ici, parce qu'ils finiraient sinon collés tels quels
/// dans le document de l'utilisateur.
/// </summary>
public class TranscriptCleanerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  ")]
    public void Une_entree_vide_ne_produit_rien(string? input)
    {
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(input));
    }

    [Fact]
    public void Les_segments_sont_assembles_dans_l_ordre()
    {
        string[] segments = [" Bonjour,", " ceci est", " un test."];

        Assert.Equal("Bonjour, ceci est un test.", TranscriptCleaner.Clean(segments));
    }

    [Fact]
    public void Une_liste_de_segments_nulle_ne_produit_rien()
    {
        Assert.Equal(string.Empty, TranscriptCleaner.Clean((IEnumerable<string?>?)null));
    }

    // --- Annotations entre crochets -------------------------------------------

    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("[Musique]")]
    [InlineData("[APPLAUSE]")]
    [InlineData("[_BEG_]")]
    [InlineData("[bruit de fond]")]
    public void Les_annotations_entre_crochets_disparaissent(string annotation)
    {
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean($"{annotation} Bonjour."));
    }

    [Fact]
    public void Plusieurs_annotations_disparaissent_ensemble()
    {
        string cleaned = TranscriptCleaner.Clean("[Musique] Bonjour [BLANK_AUDIO] tout le monde. [Fin]");

        Assert.Equal("Bonjour tout le monde.", cleaned);
    }

    [Fact]
    public void Un_texte_reduit_a_des_annotations_ne_produit_rien()
    {
        // Cas très courant : la touche est relâchée avant d'avoir parlé.
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("[BLANK_AUDIO]"));
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(" [Musique] [BLANK_AUDIO] "));
    }

    // --- Annotations entre parenthèses ----------------------------------------

    [Theory]
    [InlineData("(Musique)")]
    [InlineData("(musique douce)")]
    [InlineData("(Applaudissements)")]
    [InlineData("(rires)")]
    [InlineData("(silence)")]
    [InlineData("(inaudible)")]
    public void Les_bruits_entre_parentheses_disparaissent(string annotation)
    {
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean($"{annotation} Bonjour."));
    }

    [Fact]
    public void Une_parenthese_qui_fait_partie_du_propos_est_conservee()
    {
        // Point important : on ne peut pas retirer toutes les parenthèses.
        // Elles font partie de la dictée dès qu'elles ne nomment pas un bruit.
        const string dictated = "Le rapport (version deux) doit partir demain.";

        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    // --- Formules inventées ----------------------------------------------------

    [Theory]
    [InlineData("Sous-titres réalisés par la communauté d'Amara.org")]
    [InlineData("Sous-titrage Société Radio-Canada")]
    [InlineData("Merci d'avoir regardé cette vidéo !")]
    [InlineData("Abonnez-vous !")]
    [InlineData("Thanks for watching!")]
    public void Les_formules_de_generique_inventees_disparaissent(string hallucination)
    {
        // Whisper a été entraîné sur des sous-titres de vidéos : sur un
        // enregistrement quasi silencieux, il recrache ces génériques alors
        // qu'ils n'ont jamais été prononcés.
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(hallucination));
    }

    [Theory]
    [InlineData("Ajoute des sous-titres à la vidéo.")]
    [InlineData("Le sous-titrage est prêt.")]
    [InlineData("Merci d'avoir relu le document.")]
    public void Une_dictee_legitime_qui_evoque_les_sous_titres_est_conservee(string dictated)
    {
        // Garde-fou contre un filtre trop gourmand : le mot seul ne doit pas
        // suffire à faire disparaître la phrase. Il faut une marque
        // d'attribution — « réalisés par », « Société »... — pour conclure
        // qu'il s'agit d'un générique inventé.
        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    [Fact]
    public void Une_formule_inventee_en_fin_de_dictee_est_retiree_sans_toucher_au_reste()
    {
        string cleaned = TranscriptCleaner.Clean(
            "Rappelle-moi d'appeler le client demain. Merci d'avoir regardé cette vidéo !");

        Assert.Equal("Rappelle-moi d'appeler le client demain.", cleaned);
    }

    // --- Mise en forme ---------------------------------------------------------

    [Fact]
    public void Les_espaces_multiples_sont_ramenes_a_un_seul()
    {
        Assert.Equal("Bonjour tout le monde.", TranscriptCleaner.Clean("Bonjour    tout\n\nle\tmonde."));
    }

    [Fact]
    public void L_espace_initial_des_segments_whisper_est_retire()
    {
        // Whisper préfixe systématiquement ses segments d'une espace.
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean(" Bonjour."));
    }

    [Fact]
    public void Les_notes_de_musique_disparaissent()
    {
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("♪ ♫"));
        Assert.Equal("Bonjour.", TranscriptCleaner.Clean("♪ Bonjour. ♪"));
    }

    // --- Typographie française -------------------------------------------------

    [Theory]
    [InlineData("Tu viens vendredi?", "Tu viens vendredi ?")]
    [InlineData("Quelle horreur!", "Quelle horreur !")]
    [InlineData("Voici la liste:", "Voici la liste :")]
    [InlineData("Il part; elle reste.", "Il part ; elle reste.")]
    public void L_espace_avant_les_signes_doubles_est_retablie(string raw, string expected)
    {
        // Parakeet écrit « vendredi? » à l'anglaise. L'usage français met une
        // espace devant les signes doubles.
        Assert.Equal(expected, TranscriptCleaner.Clean(raw));
    }

    [Theory]
    [InlineData("Rendez-vous à 14:30.")]
    [InlineData("Va sur https://exemple.fr aujourd'hui.")]
    [InlineData("Le ratio est de 3:1 environ.")]
    public void Les_deux_points_colles_a_un_chiffre_ou_une_url_ne_sont_pas_touches(string dictated)
    {
        // Le signe ne doit être espacé que s'il termine un mot. Sans cette
        // condition, une heure ou une adresse se retrouverait coupée en deux.
        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    [Fact]
    public void Une_espace_deja_presente_n_est_pas_doublee()
    {
        Assert.Equal("Tu viens vendredi ?", TranscriptCleaner.Clean("Tu viens vendredi ?"));
    }

    [Fact]
    public void Un_texte_sans_lettre_ni_chiffre_ne_produit_rien()
    {
        // Insérer un « . » ou un « ... » isolé serait pire que de ne rien faire.
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("."));
        Assert.Equal(string.Empty, TranscriptCleaner.Clean(" ... "));
        Assert.Equal(string.Empty, TranscriptCleaner.Clean("!?"));
    }

    [Fact]
    public void Un_texte_normal_traverse_le_nettoyage_sans_dommage()
    {
        const string dictated =
            "Bonjour Marie, peux-tu relire le devis numéro 4218 avant vendredi ? Merci beaucoup.";

        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }

    [Fact]
    public void Les_accents_et_apostrophes_sont_preserves()
    {
        const string dictated = "L'équipe a déjà terminé l'intégration côté serveur.";

        Assert.Equal(dictated, TranscriptCleaner.Clean(dictated));
    }
}
