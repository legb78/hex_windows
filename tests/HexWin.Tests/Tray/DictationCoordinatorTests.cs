using HexWin.Tray;
using Xunit;

namespace HexWin.Tests.Tray;

public class DictationCoordinatorTests
{
    private static DictationCoordinator Ready()
    {
        var coordinator = new DictationCoordinator();
        coordinator.MarkReady();
        return coordinator;
    }

    [Fact]
    public void L_application_demarre_en_chargement()
    {
        // Le modèle pèse plusieurs centaines de mégaoctets : il y a forcément
        // un moment où le raccourci ne peut pas encore répondre.
        Assert.Equal(DictationState.Loading, new DictationCoordinator().State);
    }

    [Fact]
    public void Le_raccourci_reste_sans_effet_pendant_le_chargement()
    {
        // Sans cette garde, une dictée partirait sans moteur pour la traiter.
        Assert.False(new DictationCoordinator().TryStartRecording());
    }

    [Fact]
    public void Le_raccourci_reste_sans_effet_si_le_modele_a_echoue()
    {
        var coordinator = new DictationCoordinator();
        coordinator.MarkFailed();

        Assert.False(coordinator.TryStartRecording());
    }

    [Fact]
    public void Une_dictee_complete_ramene_a_l_attente()
    {
        DictationCoordinator coordinator = Ready();

        Assert.True(coordinator.TryStartRecording());
        Assert.Equal(DictationState.Recording, coordinator.State);

        Assert.True(coordinator.TryStartTranscribing());
        Assert.Equal(DictationState.Transcribing, coordinator.State);

        coordinator.Complete();
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public void Une_seconde_dictee_est_refusee_pendant_la_transcription()
    {
        // Le cas central. L'utilisateur relâche, puis represse aussitôt
        // pendant que le moteur travaille. Sans garde, deux dictées se
        // marcheraient dessus et le texte arriverait dans le désordre.
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();
        coordinator.TryStartTranscribing();

        Assert.False(coordinator.TryStartRecording());
        Assert.Equal(DictationState.Transcribing, coordinator.State);
    }

    [Fact]
    public void Un_second_demarrage_pendant_l_enregistrement_est_refuse()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();

        Assert.False(coordinator.TryStartRecording());
    }

    [Fact]
    public void Une_transcription_sans_enregistrement_prealable_est_refusee()
    {
        // Un relâchement peut arriver sans début associé, après une remise à
        // zéro du raccourci provoquée par un verrouillage de session.
        DictationCoordinator coordinator = Ready();

        Assert.False(coordinator.TryStartTranscribing());
    }

    [Fact]
    public void L_annulation_pendant_l_enregistrement_signale_qu_il_faut_interrompre()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();

        Assert.True(coordinator.Cancel());
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public void L_annulation_au_repos_ne_signale_rien()
    {
        Assert.False(Ready().Cancel());
    }

    [Fact]
    public void Le_raccourci_refonctionne_apres_une_annulation()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();
        coordinator.Cancel();

        Assert.True(coordinator.TryStartRecording());
    }

    [Fact]
    public void Terminer_au_repos_ne_change_rien()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.Complete();

        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    // --- Notification -----------------------------------------------------------

    [Fact]
    public void Chaque_changement_est_signale()
    {
        // C'est ce qui fait suivre l'icône de la barre système.
        var observed = new List<DictationState>();
        var coordinator = new DictationCoordinator();
        coordinator.StateChanged += (_, state) => observed.Add(state);

        coordinator.MarkReady();
        coordinator.TryStartRecording();
        coordinator.TryStartTranscribing();
        coordinator.Complete();

        Assert.Equal(
            [DictationState.Idle, DictationState.Recording, DictationState.Transcribing, DictationState.Idle],
            observed);
    }

    [Fact]
    public void Un_changement_sans_effet_n_est_pas_signale()
    {
        // Sinon l'icône serait redessinée pour rien, ce qui la fait clignoter.
        var observed = new List<DictationState>();
        DictationCoordinator coordinator = Ready();
        coordinator.StateChanged += (_, state) => observed.Add(state);

        coordinator.Complete();
        coordinator.MarkReady();

        Assert.Empty(observed);
    }
}
