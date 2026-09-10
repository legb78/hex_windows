using HexWin.Configuration;
using HexWin.Tray;

namespace HexWin.Feedback;

/// <summary>Tone marking one boundary of a recording.</summary>
public enum CueTone
{
    /// <summary>Nothing to play.</summary>
    None,

    /// <summary>The microphone has just opened.</summary>
    Start,

    /// <summary>The microphone has just closed.</summary>
    End,
}

/// <summary>What the user should see and hear at a given moment.</summary>
/// <param name="Overlay">
/// State the circle should show, in that state's colour, or <c>null</c> when
/// nothing belongs on the screen.
/// </param>
/// <param name="Tone">Tone to play, if any.</param>
public readonly record struct FeedbackCue(DictationState? Overlay, CueTone Tone);

/// <summary>
/// Turns state changes into cues.
///
/// Pure logic, pulled out for the same reason as
/// <see cref="DictationCoordinator"/>: what matters are the boundaries between
/// states, and the ways of getting one wrong are tedious to provoke by hand —
/// a press too short to count, a shortcut released out of order — and trivial
/// to state as a test.
///
/// The rule is deliberately single: <b>every exit from
/// <see cref="DictationState.Recording"/> closes the loop with the end tone</b>,
/// cancellations included. A start heard with no end would leave the user
/// wondering whether the microphone is still open.
///
/// The circle follows a different rule, the tray icon's: it stays on screen
/// for as long as the application is busy — recording, then transcribing —
/// and changes colour rather than vanishing. That is what tells the user a
/// shortcut pressed during a transcription is being ignored, instead of
/// leaving them pressing at nothing.
/// </summary>
public sealed class FeedbackPolicy
{
    private DictationState _previous = DictationState.Loading;

    public FeedbackPolicy(FeedbackMode mode)
    {
        ShowsCircle = mode is FeedbackMode.Visual or FeedbackMode.Both;
        PlaysTone = mode is FeedbackMode.Sound or FeedbackMode.Both;
    }

    /// <summary>True when the configuration asks for the circle.</summary>
    public bool ShowsCircle { get; }

    /// <summary>True when the configuration asks for the tones.</summary>
    public bool PlaysTone { get; }

    /// <summary>
    /// Cue for the state just reached.
    ///
    /// Reaching the same state twice produces no tone. The shortcut is held
    /// down, so keyboard auto-repeat fires it in bursts: the user must hear one
    /// beep per dictation, not one per keystroke.
    /// </summary>
    public FeedbackCue Next(DictationState state)
    {
        bool wasRecording = _previous == DictationState.Recording;
        bool isRecording = state == DictationState.Recording;

        _previous = state;

        CueTone tone = (wasRecording, isRecording) switch
        {
            (false, true) => CueTone.Start,
            (true, false) => CueTone.End,
            _ => CueTone.None,
        };

        // The circle mirrors the tray icon: shown while something is
        // happening, in that state's own colour, and absent at rest.
        bool onScreen = state is DictationState.Recording or DictationState.Transcribing;

        return new FeedbackCue(
            onScreen && ShowsCircle ? state : null,
            PlaysTone ? tone : CueTone.None);
    }
}
