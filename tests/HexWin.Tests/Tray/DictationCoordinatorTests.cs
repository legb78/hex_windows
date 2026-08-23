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
    public void The_application_starts_out_loading()
    {
        // The model weighs several hundred megabytes: there is inevitably a
        // moment when the shortcut cannot respond yet.
        Assert.Equal(DictationState.Loading, new DictationCoordinator().State);
    }

    [Fact]
    public void The_shortcut_does_nothing_while_loading()
    {
        // Without this guard, a dictation would start with no engine to
        // handle it.
        Assert.False(new DictationCoordinator().TryStartRecording());
    }

    [Fact]
    public void The_shortcut_does_nothing_if_the_model_failed()
    {
        var coordinator = new DictationCoordinator();
        coordinator.MarkFailed();

        Assert.False(coordinator.TryStartRecording());
    }

    [Fact]
    public void A_complete_dictation_returns_to_waiting()
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
    public void A_second_dictation_is_refused_during_transcription()
    {
        // The central case. The user releases, then presses again right away
        // while the engine is working. With no guard, two dictations would
        // tread on each other and the text would arrive out of order.
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();
        coordinator.TryStartTranscribing();

        Assert.False(coordinator.TryStartRecording());
        Assert.Equal(DictationState.Transcribing, coordinator.State);
    }

    [Fact]
    public void A_second_start_during_recording_is_refused()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();

        Assert.False(coordinator.TryStartRecording());
    }

    [Fact]
    public void Transcribing_with_no_prior_recording_is_refused()
    {
        // A release can arrive with no matching start, after a reset of the
        // shortcut caused by a locked session.
        DictationCoordinator coordinator = Ready();

        Assert.False(coordinator.TryStartTranscribing());
    }

    [Fact]
    public void Cancelling_during_recording_signals_that_it_must_stop()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();

        Assert.True(coordinator.Cancel());
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public void Cancelling_while_idle_signals_nothing()
    {
        Assert.False(Ready().Cancel());
    }

    [Fact]
    public void The_shortcut_works_again_after_a_cancellation()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.TryStartRecording();
        coordinator.Cancel();

        Assert.True(coordinator.TryStartRecording());
    }

    [Fact]
    public void Completing_while_idle_changes_nothing()
    {
        DictationCoordinator coordinator = Ready();
        coordinator.Complete();

        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    // --- Notification -----------------------------------------------------------

    [Fact]
    public void Every_change_is_signalled()
    {
        // This is what makes the tray icon follow.
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
    public void A_change_with_no_effect_is_not_signalled()
    {
        // Otherwise the icon would be redrawn for nothing, which makes it
        // flicker.
        var observed = new List<DictationState>();
        DictationCoordinator coordinator = Ready();
        coordinator.StateChanged += (_, state) => observed.Add(state);

        coordinator.Complete();
        coordinator.MarkReady();

        Assert.Empty(observed);
    }
}
