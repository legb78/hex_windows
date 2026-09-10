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
/// <param name="OverlayVisible">Whether the circle belongs on screen.</param>
/// <param name="Tone">Tone to play, if any.</param>
public readonly record struct FeedbackCue(bool OverlayVisible, CueTone Tone);

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

        return new FeedbackCue(
            isRecording && ShowsCircle,
            PlaysTone ? tone : CueTone.None);
    }
}
