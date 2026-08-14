namespace HexWin.Tray;

/// <summary>Ce que fait l'application à un instant donné.</summary>
public enum DictationState
{
    /// <summary>Le modèle se charge. Le raccourci ne répond pas encore.</summary>
    Loading,

    /// <summary>Prêt, en attente du raccourci.</summary>
    Idle,

    /// <summary>Le micro tourne.</summary>
    Recording,

    /// <summary>Le moteur travaille sur l'enregistrement.</summary>
    Transcribing,

    /// <summary>Le modèle n'a pas pu être chargé : l'application est inutilisable.</summary>
    Failed,
}

/// <summary>
/// Décide ce qui est permis selon l'état courant.
///
/// Logique pure, extraite du reste pour la même raison que
/// <see cref="Input.ChordDetector"/> : les situations à couvrir sont des
/// courses entre l'utilisateur et la machine, pénibles à provoquer à la main
/// mais triviales à décrire en test.
///
/// Le cas qui compte : appuyer de nouveau sur le raccourci pendant qu'une
/// transcription est en cours. Sans garde, deux dictées se marcheraient
/// dessus et le texte arriverait dans le désordre.
/// </summary>
public sealed class DictationCoordinator
{
    public DictationState State { get; private set; } = DictationState.Loading;

    /// <summary>Levé à chaque changement, pour que l'icône suive.</summary>
    public event EventHandler<DictationState>? StateChanged;

    /// <summary>Le modèle est chargé : le raccourci devient opérant.</summary>
    public void MarkReady() => MoveTo(DictationState.Idle);

    /// <summary>Le modèle n'a pas pu être chargé.</summary>
    public void MarkFailed() => MoveTo(DictationState.Failed);

    /// <summary>
    /// Tente de démarrer un enregistrement. Rend faux si l'état ne le permet
    /// pas — modèle non chargé, ou transcription encore en cours.
    /// </summary>
    public bool TryStartRecording()
    {
        if (State != DictationState.Idle)
        {
            return false;
        }

        MoveTo(DictationState.Recording);
        return true;
    }

    /// <summary>
    /// Tente de passer à la transcription. Rend faux si aucun enregistrement
    /// n'était en cours : un relâchement peut arriver sans début associé,
    /// après une remise à zéro du raccourci par exemple.
    /// </summary>
    public bool TryStartTranscribing()
    {
        if (State != DictationState.Recording)
        {
            return false;
        }

        MoveTo(DictationState.Transcribing);
        return true;
    }

    /// <summary>La dictée est terminée, réussie ou non : retour à l'attente.</summary>
    public void Complete()
    {
        if (State is DictationState.Recording or DictationState.Transcribing)
        {
            MoveTo(DictationState.Idle);
        }
    }

    /// <summary>
    /// Abandon : touche étrangère, session verrouillée, enregistrement trop
    /// court. Rend vrai si un enregistrement était réellement en cours et
    /// doit donc être interrompu.
    /// </summary>
    public bool Cancel()
    {
        bool wasBusy = State is DictationState.Recording or DictationState.Transcribing;

        if (wasBusy)
        {
            MoveTo(DictationState.Idle);
        }

        return wasBusy;
    }

    private void MoveTo(DictationState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(this, next);
    }
}
