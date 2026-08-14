using HexWin.Input;
using Xunit;

namespace HexWin.Tests.Input;

/// <summary>
/// Les cas couverts ici sont tous vécus à l'usage et pénibles à reproduire à
/// la main : répétition automatique du clavier, relâchement dans le désordre,
/// touche restée enfoncée après un verrouillage de session. C'est pour les
/// rendre testables que la décision a été séparée du hook Win32.
/// </summary>
public class ChordDetectorTests
{
    private const int Ctrl = VirtualKeys.LeftControl;
    private const int RightCtrl = VirtualKeys.RightControl;
    private const int Win = VirtualKeys.LeftWindows;
    private const int RightWin = VirtualKeys.RightWindows;
    private const int KeyD = 0x44;
    private const int KeyC = 0x43;

    private static ChordDetector CtrlWin() => new(["Ctrl", "Win"]);

    // --- Déclenchement nominal -------------------------------------------------

    [Fact]
    public void Le_raccourci_complet_demarre_la_dictee()
    {
        ChordDetector detector = CtrlWin();

        Assert.Equal(ChordAction.None, detector.OnKeyDown(Ctrl).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(Win).Action);
        Assert.True(detector.IsActive);
    }

    [Fact]
    public void Le_relachement_arrete_la_dictee()
    {
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.Equal(ChordAction.Stop, detector.OnKeyUp(Win).Action);
        Assert.False(detector.IsActive);
    }

    [Fact]
    public void L_ordre_d_appui_est_indifferent()
    {
        ChordDetector detector = CtrlWin();

        Assert.Equal(ChordAction.None, detector.OnKeyDown(Win).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(Ctrl).Action);
    }

    [Fact]
    public void Relacher_n_importe_laquelle_des_deux_touches_arrete()
    {
        // L'utilisateur relâche rarement les deux touches simultanément.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.Equal(ChordAction.Stop, detector.OnKeyUp(Ctrl).Action);
    }

    [Fact]
    public void Les_touches_gauche_et_droite_sont_equivalentes()
    {
        // « Ctrl » désigne les deux touches physiques : l'utilisateur ne
        // devrait pas avoir à se demander laquelle il a sous les doigts.
        ChordDetector detector = CtrlWin();

        Assert.Equal(ChordAction.None, detector.OnKeyDown(RightCtrl).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(RightWin).Action);
    }

    // --- Répétition automatique -------------------------------------------------

    [Fact]
    public void La_repetition_automatique_ne_relance_pas_la_dictee()
    {
        // Maintenir une touche envoie des enfoncements en rafale. Sans cette
        // garde, chaque répétition redémarrerait l'enregistrement à zéro et
        // la dictée ne contiendrait jamais que le dernier fragment.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(ChordAction.None, detector.OnKeyDown(Ctrl).Action);
            Assert.Equal(ChordAction.None, detector.OnKeyDown(Win).Action);
        }

        Assert.True(detector.IsActive);
    }

    [Fact]
    public void La_repetition_reste_avalee_si_l_appui_initial_l_etait()
    {
        // Sinon les répétitions de la touche Windows fuiraient vers le
        // système pendant toute la dictée.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.True(detector.OnKeyDown(Win).Swallow);
    }

    // --- Équilibre de l'avalage -------------------------------------------------

    [Fact]
    public void La_touche_qui_complete_le_raccourci_est_avalee()
    {
        // C'est ce qui empêche le menu Démarrer de s'ouvrir : Windows ne voit
        // jamais l'appui sur la touche Windows.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);

        Assert.True(detector.OnKeyDown(Win).Swallow);
    }

    [Fact]
    public void Une_touche_transmise_a_l_enfoncement_l_est_aussi_au_relachement()
    {
        // L'invariant central. Avaler le relâchement d'une touche dont
        // l'enfoncement est passé laisserait Windows croire le modificateur
        // toujours enfoncé : le clavier deviendrait inutilisable.
        ChordDetector detector = CtrlWin();

        Assert.False(detector.OnKeyDown(Ctrl).Swallow);
        detector.OnKeyDown(Win);

        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
    }

    [Fact]
    public void Une_touche_avalee_a_l_enfoncement_l_est_aussi_au_relachement()
    {
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);

        Assert.True(detector.OnKeyDown(Win).Swallow);
        Assert.True(detector.OnKeyUp(Win).Swallow);
    }

    [Fact]
    public void Rien_n_est_avale_tant_que_le_raccourci_est_incomplet()
    {
        // Point vital : avaler Ctrl dès son appui casserait Ctrl+C, Ctrl+V et
        // tout le reste. On ne peut décider qu'une fois le raccourci complet.
        ChordDetector detector = CtrlWin();

        Assert.False(detector.OnKeyDown(Ctrl).Swallow);
        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
    }

    [Fact]
    public void Une_combinaison_etrangere_traverse_sans_encombre()
    {
        // Ctrl+C doit continuer de fonctionner normalement.
        ChordDetector detector = CtrlWin();

        Assert.False(detector.OnKeyDown(Ctrl).Swallow);
        Assert.False(detector.OnKeyDown(KeyC).Swallow);
        Assert.False(detector.OnKeyUp(KeyC).Swallow);
        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
        Assert.False(detector.IsActive);
    }

    // --- Menu Démarrer ----------------------------------------------------------

    [Fact]
    public void Presser_Ctrl_puis_Windows_n_exige_aucune_neutralisation()
    {
        // La touche Windows complète le raccourci : elle est avalée, Windows
        // ne la voit jamais, rien à neutraliser.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);

        Assert.False(detector.OnKeyDown(Win).NeutralizeStartMenu);
    }

    [Fact]
    public void Presser_Windows_en_premier_exige_une_neutralisation()
    {
        // Ici l'appui sur Windows a déjà été transmis avant qu'on puisse
        // savoir qu'il s'agissait de notre raccourci. Au relâchement, Windows
        // ouvrirait le menu Démarrer : l'appelant doit injecter une touche
        // sans effet pour rompre la séquence.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Win);

        Assert.True(detector.OnKeyDown(Ctrl).NeutralizeStartMenu);
    }

    [Fact]
    public void Un_raccourci_sans_touche_Windows_n_exige_jamais_de_neutralisation()
    {
        var detector = new ChordDetector(["Ctrl", "Alt"]);
        detector.OnKeyDown(Ctrl);

        Assert.False(detector.OnKeyDown(VirtualKeys.LeftMenu).NeutralizeStartMenu);
    }

    // --- Interruption -----------------------------------------------------------

    [Fact]
    public void Une_touche_etrangere_pendant_la_dictee_l_annule()
    {
        // Ctrl+Win+D crée un bureau virtuel. L'utilisateur ne demandait pas
        // une transcription : mieux vaut abandonner que coller du texte dans
        // un bureau qui vient de changer.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        ChordDecision decision = detector.OnKeyDown(KeyD);

        Assert.Equal(ChordAction.Cancel, decision.Action);
        Assert.False(detector.IsActive);
    }

    [Fact]
    public void La_touche_qui_annule_est_transmise_normalement()
    {
        // L'utilisateur voulait bien créer son bureau virtuel.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.False(detector.OnKeyDown(KeyD).Swallow);
    }

    [Fact]
    public void Apres_annulation_le_relachement_ne_declenche_pas_de_transcription()
    {
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);
        detector.OnKeyDown(KeyD);

        Assert.Equal(ChordAction.None, detector.OnKeyUp(Win).Action);
        Assert.Equal(ChordAction.None, detector.OnKeyUp(Ctrl).Action);
    }

    [Fact]
    public void Apres_annulation_l_equilibre_de_l_avalage_est_preserve()
    {
        // Même annulée, la touche avalée à l'enfoncement doit voir son
        // relâchement avalé, sinon le modificateur reste fantôme.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);
        detector.OnKeyDown(KeyD);

        Assert.True(detector.OnKeyUp(Win).Swallow);
        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
    }

    // --- Remise à zéro ----------------------------------------------------------

    [Fact]
    public void La_remise_a_zero_annule_une_dictee_en_cours()
    {
        // Cas réel : session verrouillée pendant la dictée. Windows cesse de
        // livrer les relâchements, la touche resterait « enfoncée » à jamais.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.Equal(ChordAction.Cancel, detector.Reset().Action);
        Assert.False(detector.IsActive);
    }

    [Fact]
    public void La_remise_a_zero_au_repos_ne_fait_rien()
    {
        Assert.Equal(ChordAction.None, CtrlWin().Reset().Action);
    }

    [Fact]
    public void Le_raccourci_refonctionne_apres_une_remise_a_zero()
    {
        // Sans cette garantie, un verrouillage de session désarmerait la
        // dictée jusqu'au redémarrage de l'application.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);
        detector.Reset();

        detector.OnKeyDown(Ctrl);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(Win).Action);
    }

    // --- Construction -----------------------------------------------------------

    [Fact]
    public void Un_raccourci_a_une_seule_touche_est_accepte()
    {
        var detector = new ChordDetector(["CapsLock"]);

        Assert.Equal(ChordAction.Start, detector.OnKeyDown(VirtualKeys.CapsLock).Action);
        Assert.Equal(ChordAction.Stop, detector.OnKeyUp(VirtualKeys.CapsLock).Action);
    }

    [Fact]
    public void Un_raccourci_a_trois_touches_exige_les_trois()
    {
        var detector = new ChordDetector(["Ctrl", "Shift", "Space"]);

        Assert.Equal(ChordAction.None, detector.OnKeyDown(Ctrl).Action);
        Assert.Equal(ChordAction.None, detector.OnKeyDown(VirtualKeys.LeftShift).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(VirtualKeys.Space).Action);
    }

    [Fact]
    public void Un_raccourci_vide_est_refuse()
    {
        Assert.Throws<ArgumentException>(() => new ChordDetector([]));
    }

    [Fact]
    public void Une_touche_inconnue_est_refusee()
    {
        Assert.Throws<ArgumentException>(() => new ChordDetector(["Fn"]));
    }
}
