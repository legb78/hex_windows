using HexWin.Configuration;
using HexWin.Feedback;
using HexWin.Tray;
using Xunit;

namespace HexWin.Tests.Feedback;

/// <summary>
/// The rule checked here: the user hears exactly one beep when the microphone
/// opens and one when it closes, whatever route the state machine took to get
/// there.
/// </summary>
public class FeedbackPolicyTests
{
    private static FeedbackPolicy Ready(FeedbackMode mode = FeedbackMode.Both)
    {
        var policy = new FeedbackPolicy(mode);

        // Every session goes through this: the model finishes loading and the
        // application becomes usable.
        policy.Next(DictationState.Idle);

        return policy;
    }

    [Fact]
    public void Becoming_ready_makes_no_sound()
    {
        // The model finishing its load is not the start of a dictation. A beep
        // here would fire on its own a few seconds after sign-in, with nobody
        // having asked for anything.
        FeedbackCue cue = new FeedbackPolicy(FeedbackMode.Both).Next(DictationState.Idle);

        Assert.Equal(CueTone.None, cue.Tone);
        Assert.Null(cue.Overlay);
    }

    [Fact]
    public void Starting_to_record_shows_the_circle_and_plays_the_start_tone()
    {
        FeedbackCue cue = Ready().Next(DictationState.Recording);

        Assert.Equal(DictationState.Recording, cue.Overlay);
        Assert.Equal(CueTone.Start, cue.Tone);
    }

    [Fact]
    public void Releasing_the_shortcut_turns_the_circle_over_to_transcribing()
    {
        // The circle does not vanish, it changes colour — red to orange, exactly
        // like the tray icon. The end of the recording is marked by the tone; the
        // circle goes on saying the application is still busy.
        FeedbackPolicy policy = Ready();
        policy.Next(DictationState.Recording);

        FeedbackCue cue = policy.Next(DictationState.Transcribing);

        Assert.Equal(DictationState.Transcribing, cue.Overlay);
        Assert.Equal(CueTone.End, cue.Tone);
    }

    [Fact]
    public void An_abandoned_recording_still_plays_the_end_tone()
    {
        // The case that justifies the rule. A press too short to count, a
        // foreign key, a locked session: the recording goes straight back to
        // idle without ever transcribing. Having heard the start tone, the user
        // must hear the end one — otherwise nothing says whether the
        // microphone is still open.
        FeedbackPolicy policy = Ready();
        policy.Next(DictationState.Recording);

        FeedbackCue cue = policy.Next(DictationState.Idle);

        Assert.Null(cue.Overlay);
        Assert.Equal(CueTone.End, cue.Tone);
    }

    [Fact]
    public void Reaching_the_recording_state_again_does_not_replay_the_start_tone()
    {
        // The shortcut is held down, so keyboard auto-repeat fires it in
        // bursts. One beep per dictation, not one per keystroke.
        FeedbackPolicy policy = Ready();
        policy.Next(DictationState.Recording);

        FeedbackCue cue = policy.Next(DictationState.Recording);

        Assert.Equal(DictationState.Recording, cue.Overlay);
        Assert.Equal(CueTone.None, cue.Tone);
    }

    [Fact]
    public void The_end_of_a_transcription_is_silent()
    {
        // The recording already ended when the shortcut was released. Beeping
        // again when the text lands would mark the wrong moment.
        FeedbackPolicy policy = Ready();
        policy.Next(DictationState.Recording);
        policy.Next(DictationState.Transcribing);

        FeedbackCue cue = policy.Next(DictationState.Idle);

        Assert.Null(cue.Overlay);
        Assert.Equal(CueTone.None, cue.Tone);
    }

    [Fact]
    public void A_failed_model_shows_nothing()
    {
        FeedbackCue cue = Ready().Next(DictationState.Failed);

        Assert.Null(cue.Overlay);
        Assert.Equal(CueTone.None, cue.Tone);
    }

    [Theory]
    [InlineData(FeedbackMode.Visual, true, false)]
    [InlineData(FeedbackMode.Sound, false, true)]
    [InlineData(FeedbackMode.Both, true, true)]
    [InlineData(FeedbackMode.None, false, false)]
    public void Each_mode_enables_only_what_it_names(FeedbackMode mode, bool circle, bool tone)
    {
        FeedbackCue cue = Ready(mode).Next(DictationState.Recording);

        Assert.Equal(circle ? DictationState.Recording : null, cue.Overlay);
        Assert.Equal(tone ? CueTone.Start : CueTone.None, cue.Tone);
    }

    [Theory]
    [InlineData(FeedbackMode.Visual, true, false)]
    [InlineData(FeedbackMode.Sound, false, true)]
    [InlineData(FeedbackMode.Both, true, true)]
    [InlineData(FeedbackMode.None, false, false)]
    public void The_mode_is_readable_before_anything_is_built(FeedbackMode mode, bool circle, bool tone)
    {
        // Read at construction to skip creating a window or a waveform that
        // would never be used.
        var policy = new FeedbackPolicy(mode);

        Assert.Equal(circle, policy.ShowsCircle);
        Assert.Equal(tone, policy.PlaysTone);
    }

    [Fact]
    public void Turning_the_sound_off_keeps_the_circle_in_step()
    {
        // Silent mode must still take the circle all the way back off the screen,
        // not only put it there at the start.
        FeedbackPolicy policy = Ready(FeedbackMode.Visual);
        policy.Next(DictationState.Recording);

        Assert.Equal(DictationState.Transcribing, policy.Next(DictationState.Transcribing).Overlay);
        Assert.Null(policy.Next(DictationState.Idle).Overlay);
    }
}
