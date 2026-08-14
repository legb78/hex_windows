using HexWin.Output;
using Xunit;

namespace HexWin.Tests.Output;

/// <summary>
/// L'envoi se fait en Unicode et non en codes de touches, ce qui rend
/// l'injection indépendante de la disposition du clavier. Sur un clavier
/// français, « a » et « q » ne sont pas là où un programme les attendrait, et
/// « é » n'existe sur aucune touche d'un clavier américain.
/// </summary>
public class UnicodeKeystrokesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_texte_vide_ne_produit_aucune_frappe(string? text)
    {
        Assert.Empty(UnicodeKeystrokes.Build(text));
    }

    [Fact]
    public void Chaque_caractere_donne_un_enfoncement_puis_un_relachement()
    {
        Keystroke[] strokes = UnicodeKeystrokes.Build("ab");

        Assert.Equal(4, strokes.Length);
        Assert.Equal(new Keystroke('a', IsKeyUp: false), strokes[0]);
        Assert.Equal(new Keystroke('a', IsKeyUp: true), strokes[1]);
        Assert.Equal(new Keystroke('b', IsKeyUp: false), strokes[2]);
        Assert.Equal(new Keystroke('b', IsKeyUp: true), strokes[3]);
    }

    [Fact]
    public void Les_lettres_accentuees_passent_telles_quelles()
    {
        // Le point de toute l'approche : « é » n'a aucun code de touche sur un
        // clavier américain, mais son unité de code Unicode est universelle.
        Keystroke[] strokes = UnicodeKeystrokes.Build("é");

        Assert.Equal(2, strokes.Length);
        Assert.Equal('é', strokes[0].Unit);
    }

    [Fact]
    public void Un_emoji_est_envoye_en_deux_unites_distinctes()
    {
        // Les caractères hors du plan multilingue de base occupent deux
        // unités UTF-16. Les envoyer ensemble n'insérerait rien : Windows
        // attend deux frappes et recompose lui-même.
        Keystroke[] strokes = UnicodeKeystrokes.Build("🙂");

        Assert.Equal(4, strokes.Length);
        Assert.True(char.IsHighSurrogate((char)strokes[0].Unit));
        Assert.True(char.IsLowSurrogate((char)strokes[2].Unit));
    }

    [Fact]
    public void Une_fin_de_ligne_devient_la_touche_Entree()
    {
        // Envoyé en Unicode, un saut de ligne n'insère rien du tout : il faut
        // la vraie touche.
        Keystroke[] strokes = UnicodeKeystrokes.Build("\n");

        Assert.Equal(2, strokes.Length);
        Assert.True(UnicodeKeystrokes.IsReturn(strokes[0]));
        Assert.False(strokes[0].IsKeyUp);
        Assert.True(strokes[1].IsKeyUp);
    }

    [Fact]
    public void Une_fin_de_ligne_Windows_ne_produit_qu_une_seule_touche_Entree()
    {
        // Sans cette règle, « \r\n » insérerait deux sauts de ligne.
        Keystroke[] strokes = UnicodeKeystrokes.Build("a\r\nb");

        Assert.Equal(6, strokes.Length);
        Assert.Single(strokes, s => UnicodeKeystrokes.IsReturn(s) && !s.IsKeyUp);
    }

    [Fact]
    public void Une_phrase_complete_conserve_son_ordre()
    {
        const string sentence = "Bonjour Marie, à demain !";

        Keystroke[] strokes = UnicodeKeystrokes.Build(sentence);
        string rebuilt = new([.. strokes.Where(s => !s.IsKeyUp).Select(s => (char)s.Unit)]);

        Assert.Equal(sentence, rebuilt);
    }

    [Fact]
    public void Les_espaces_et_la_ponctuation_ne_sont_pas_traites_a_part()
    {
        Keystroke[] strokes = UnicodeKeystrokes.Build(" ,");

        Assert.Equal(4, strokes.Length);
        Assert.Equal(' ', strokes[0].Unit);
        Assert.Equal(',', strokes[2].Unit);
    }
}
