using HexWin.Input;
using Xunit;

namespace HexWin.Tests.Input;

/// <summary>
/// Every case covered here happens in real use and is awkward to reproduce by
/// hand: keyboard auto-repeat, releasing out of order, a key left held after a
/// locked session. Making them testable is exactly why the deciding was
/// separated from the Win32 hook.
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

    // --- Nominal triggering ----------------------------------------------------

    [Fact]
    public void The_complete_shortcut_starts_the_dictation()
    {
        ChordDetector detector = CtrlWin();

        Assert.Equal(ChordAction.None, detector.OnKeyDown(Ctrl).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(Win).Action);
        Assert.True(detector.IsActive);
    }

    [Fact]
    public void Releasing_stops_the_dictation()
    {
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.Equal(ChordAction.Stop, detector.OnKeyUp(Win).Action);
        Assert.False(detector.IsActive);
    }

    [Fact]
    public void The_press_order_does_not_matter()
    {
        ChordDetector detector = CtrlWin();

        Assert.Equal(ChordAction.None, detector.OnKeyDown(Win).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(Ctrl).Action);
    }

    [Fact]
    public void Releasing_either_of_the_two_keys_stops_it()
    {
        // Users rarely release both keys at exactly the same instant.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.Equal(ChordAction.Stop, detector.OnKeyUp(Ctrl).Action);
    }

    [Fact]
    public void The_left_and_right_keys_are_equivalent()
    {
        // "Ctrl" means both physical keys: the user should not have to wonder
        // which one is under their fingers.
        ChordDetector detector = CtrlWin();

        Assert.Equal(ChordAction.None, detector.OnKeyDown(RightCtrl).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(RightWin).Action);
    }

    // --- Auto-repeat ------------------------------------------------------------

    [Fact]
    public void Auto_repeat_does_not_restart_the_dictation()
    {
        // Holding a key sends key-downs in bursts. Without this guard, every
        // repeat would restart the recording from zero and the dictation would
        // only ever hold the last fragment.
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
    public void A_repeat_stays_swallowed_if_the_initial_press_was()
    {
        // Otherwise the repeats of the Windows key would leak to the system
        // for the whole dictation.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.True(detector.OnKeyDown(Win).Swallow);
    }

    // --- Swallowing balance -----------------------------------------------------

    [Fact]
    public void The_key_that_completes_the_shortcut_is_swallowed()
    {
        // This is what stops the Start menu from opening: Windows never sees
        // the press of the Windows key.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);

        Assert.True(detector.OnKeyDown(Win).Swallow);
    }

    [Fact]
    public void A_key_passed_through_on_press_is_passed_through_on_release()
    {
        // The central invariant. Swallowing the release of a key whose press
        // got through would leave Windows believing the modifier is still
        // held: the keyboard would become unusable.
        ChordDetector detector = CtrlWin();

        Assert.False(detector.OnKeyDown(Ctrl).Swallow);
        detector.OnKeyDown(Win);

        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
    }

    [Fact]
    public void A_key_swallowed_on_press_is_swallowed_on_release()
    {
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);

        Assert.True(detector.OnKeyDown(Win).Swallow);
        Assert.True(detector.OnKeyUp(Win).Swallow);
    }

    [Fact]
    public void Nothing_is_swallowed_while_the_shortcut_is_incomplete()
    {
        // Vital point: swallowing Ctrl on its press would break Ctrl+C, Ctrl+V
        // and everything else. The decision can only be taken once the
        // shortcut is complete.
        ChordDetector detector = CtrlWin();

        Assert.False(detector.OnKeyDown(Ctrl).Swallow);
        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
    }

    [Fact]
    public void A_foreign_combination_passes_through_untouched()
    {
        // Ctrl+C has to keep working normally.
        ChordDetector detector = CtrlWin();

        Assert.False(detector.OnKeyDown(Ctrl).Swallow);
        Assert.False(detector.OnKeyDown(KeyC).Swallow);
        Assert.False(detector.OnKeyUp(KeyC).Swallow);
        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
        Assert.False(detector.IsActive);
    }

    // --- Start menu -------------------------------------------------------------

    [Fact]
    public void Pressing_Ctrl_then_Windows_needs_no_neutralisation()
    {
        // The Windows key completes the shortcut: it is swallowed, Windows
        // never sees it, and there is nothing to neutralise.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);

        Assert.False(detector.OnKeyDown(Win).NeutralizeStartMenu);
    }

    [Fact]
    public void Pressing_Windows_first_needs_a_neutralisation()
    {
        // Here the press on Windows was already delivered before there was any
        // way to know it belonged to our shortcut. On release, Windows would
        // open the Start menu: the caller has to inject a key with no effect
        // to break the sequence.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Win);

        Assert.True(detector.OnKeyDown(Ctrl).NeutralizeStartMenu);
    }

    [Fact]
    public void A_shortcut_without_a_Windows_key_never_needs_neutralisation()
    {
        var detector = new ChordDetector(["Ctrl", "Alt"]);
        detector.OnKeyDown(Ctrl);

        Assert.False(detector.OnKeyDown(VirtualKeys.LeftMenu).NeutralizeStartMenu);
    }

    // --- Interruption -----------------------------------------------------------

    [Fact]
    public void A_foreign_key_during_the_dictation_cancels_it()
    {
        // Ctrl+Win+D creates a virtual desktop. The user was not asking for a
        // transcription: better to give up than to paste text into a desktop
        // that has just changed.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        ChordDecision decision = detector.OnKeyDown(KeyD);

        Assert.Equal(ChordAction.Cancel, decision.Action);
        Assert.False(detector.IsActive);
    }

    [Fact]
    public void The_key_that_cancels_is_passed_through_normally()
    {
        // The user did mean to create their virtual desktop.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.False(detector.OnKeyDown(KeyD).Swallow);
    }

    [Fact]
    public void After_a_cancellation_releasing_triggers_no_transcription()
    {
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);
        detector.OnKeyDown(KeyD);

        Assert.Equal(ChordAction.None, detector.OnKeyUp(Win).Action);
        Assert.Equal(ChordAction.None, detector.OnKeyUp(Ctrl).Action);
    }

    [Fact]
    public void After_a_cancellation_the_swallowing_balance_is_preserved()
    {
        // Cancelled or not, a key swallowed on press must have its release
        // swallowed too, otherwise the modifier stays a phantom.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);
        detector.OnKeyDown(KeyD);

        Assert.True(detector.OnKeyUp(Win).Swallow);
        Assert.False(detector.OnKeyUp(Ctrl).Swallow);
    }

    // --- Reset ------------------------------------------------------------------

    [Fact]
    public void A_reset_cancels_a_dictation_in_progress()
    {
        // A real case: the session locks during the dictation. Windows stops
        // delivering releases, and the key would stay "held" forever.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);

        Assert.Equal(ChordAction.Cancel, detector.Reset().Action);
        Assert.False(detector.IsActive);
    }

    [Fact]
    public void A_reset_while_idle_does_nothing()
    {
        Assert.Equal(ChordAction.None, CtrlWin().Reset().Action);
    }

    [Fact]
    public void The_shortcut_works_again_after_a_reset()
    {
        // Without that guarantee, a locked session would disarm dictation
        // until the application restarts.
        ChordDetector detector = CtrlWin();
        detector.OnKeyDown(Ctrl);
        detector.OnKeyDown(Win);
        detector.Reset();

        detector.OnKeyDown(Ctrl);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(Win).Action);
    }

    // --- Construction -----------------------------------------------------------

    [Fact]
    public void A_single_key_shortcut_is_accepted()
    {
        var detector = new ChordDetector(["CapsLock"]);

        Assert.Equal(ChordAction.Start, detector.OnKeyDown(VirtualKeys.CapsLock).Action);
        Assert.Equal(ChordAction.Stop, detector.OnKeyUp(VirtualKeys.CapsLock).Action);
    }

    [Fact]
    public void A_three_key_shortcut_requires_all_three()
    {
        var detector = new ChordDetector(["Ctrl", "Shift", "Space"]);

        Assert.Equal(ChordAction.None, detector.OnKeyDown(Ctrl).Action);
        Assert.Equal(ChordAction.None, detector.OnKeyDown(VirtualKeys.LeftShift).Action);
        Assert.Equal(ChordAction.Start, detector.OnKeyDown(VirtualKeys.Space).Action);
    }

    [Fact]
    public void An_empty_shortcut_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ChordDetector([]));
    }

    [Fact]
    public void An_unknown_key_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new ChordDetector(["Fn"]));
    }
}
