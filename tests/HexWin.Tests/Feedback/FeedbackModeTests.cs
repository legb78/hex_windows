using HexWin.Configuration;
using HexWin.Feedback;
using HexWin.Tray;
using Xunit;

namespace HexWin.Tests.Feedback;

/// <summary>
/// The rule checked here: the tray offers two independent switches, the file
/// stores one mode with four values, and the two views agree in both
/// directions.
/// </summary>
public class FeedbackModeTests
{
    [Theory]
    [InlineData(FeedbackMode.Both, true, true)]
    [InlineData(FeedbackMode.Visual, true, false)]
    [InlineData(FeedbackMode.Sound, false, true)]
    [InlineData(FeedbackMode.None, false, false)]
    public void A_stored_mode_opens_the_matching_switches(FeedbackMode mode, bool circle, bool tone)
    {
        var policy = new FeedbackPolicy(mode);

        Assert.Equal(circle, policy.ShowsCircle);
        Assert.Equal(tone, policy.PlaysTone);
    }

    [Theory]
    [InlineData(true, true, FeedbackMode.Both)]
    [InlineData(true, false, FeedbackMode.Visual)]
    [InlineData(false, true, FeedbackMode.Sound)]
    [InlineData(false, false, FeedbackMode.None)]
    public void The_switches_read_back_as_the_mode_to_store(bool circle, bool tone, FeedbackMode expected)
    {
        var policy = new FeedbackPolicy(FeedbackMode.Both)
        {
            ShowsCircle = circle,
            PlaysTone = tone,
        };

        Assert.Equal(expected, policy.Mode);
    }

    [Fact]
    public void Turning_the_circle_off_mid_recording_leaves_the_tone_alone()
    {
        // The two switches are independent: unticking one in the menu while a
        // dictation is running must not silence the other.
        var policy = new FeedbackPolicy(FeedbackMode.Both);
        policy.Next(DictationState.Idle);
        policy.Next(DictationState.Recording);

        policy.ShowsCircle = false;

        FeedbackCue cue = policy.Next(DictationState.Idle);

        Assert.Null(cue.Overlay);
        Assert.Equal(CueTone.End, cue.Tone);
    }

    [Fact]
    public void Turning_the_tone_off_mid_recording_leaves_the_circle_alone()
    {
        var policy = new FeedbackPolicy(FeedbackMode.Both);
        policy.Next(DictationState.Idle);
        policy.Next(DictationState.Recording);

        policy.PlaysTone = false;

        FeedbackCue cue = policy.Next(DictationState.Transcribing);

        Assert.Equal(DictationState.Transcribing, cue.Overlay);
        Assert.Equal(CueTone.None, cue.Tone);
    }

    [Fact]
    public void Turning_the_circle_on_mid_session_shows_it_at_the_next_state()
    {
        // Ticking the entry during a dictation must take effect without
        // waiting for a restart — that is the whole point of the menu.
        var policy = new FeedbackPolicy(FeedbackMode.None);
        policy.Next(DictationState.Idle);

        policy.ShowsCircle = true;

        FeedbackCue cue = policy.Next(DictationState.Recording);

        Assert.Equal(DictationState.Recording, cue.Overlay);
    }
}
