namespace HexWin.Tray;

/// <summary>What the application is doing at a given moment.</summary>
public enum DictationState
{
    /// <summary>The model is loading. The shortcut does not respond yet.</summary>
    Loading,

    /// <summary>Ready, waiting for the shortcut.</summary>
    Idle,

    /// <summary>The microphone is running.</summary>
    Recording,

    /// <summary>The engine is working on the recording.</summary>
    Transcribing,

    /// <summary>The model could not be loaded: the application is unusable.</summary>
    Failed,
}

/// <summary>
/// Decides what is allowed given the current state.
///
/// Pure logic, pulled out from the rest for the same reason as
/// <see cref="Input.ChordDetector"/>: the situations to cover are races
/// between the user and the machine, awkward to provoke by hand but trivial to
/// describe in a test.
///
/// The case that matters: pressing the shortcut again while a transcription is
/// running. With no guard, two dictations would tread on each other and the
/// text would arrive out of order.
/// </summary>
public sealed class DictationCoordinator
{
    public DictationState State { get; private set; } = DictationState.Loading;

    /// <summary>Raised on every change, so the icon can follow.</summary>
    public event EventHandler<DictationState>? StateChanged;

    /// <summary>The model is loaded: the shortcut becomes live.</summary>
    public void MarkReady() => MoveTo(DictationState.Idle);

    /// <summary>The model could not be loaded.</summary>
    public void MarkFailed() => MoveTo(DictationState.Failed);

    /// <summary>
    /// Tries to start a recording. Returns false if the state does not allow
    /// it — model not loaded, or a transcription still running.
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
    /// Tries to move on to transcription. Returns false if no recording was
    /// running: a release can arrive with no matching start, after a reset of
    /// the shortcut for instance.
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

    /// <summary>The dictation is over, successful or not: back to waiting.</summary>
    public void Complete()
    {
        if (State is DictationState.Recording or DictationState.Transcribing)
        {
            MoveTo(DictationState.Idle);
        }
    }

    /// <summary>
    /// Abandon: a foreign key, a locked session, a recording too short.
    /// Returns true if a recording really was running and so must be
    /// interrupted.
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
