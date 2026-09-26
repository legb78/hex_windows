using HexWin.Configuration;
using HexWin.Input;
using Xunit;

namespace HexWin.Tests.Input;

/// <summary>
/// The capture reads a gesture — press, hold, let go — into the names
/// settings.json accepts. Each case below is one a user produces without
/// thinking: a key-up left over from the click that started the capture, a
/// letter pressed by mistake, fingers leaving the keys out of order.
/// </summary>
public class HotkeyCaptureTests
{
    private const int KeyA = 0x41;
    private const int Escape = 0x1B;

    [Fact]
    public void A_single_key_pressed_and_released_is_the_shortcut()
    {
        var capture = new HotkeyCapture();

        Assert.Equal(CaptureState.Listening, capture.OnKeyDown(VirtualKeys.RightShift).State);

        CaptureStep step = capture.OnKeyUp(VirtualKeys.RightShift);
        Assert.Equal(CaptureState.Captured, step.State);
        Assert.Equal(["RightShift"], step.Keys);
    }

    [Fact]
    public void A_chord_is_complete_only_once_every_key_is_released()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftControl);
        capture.OnKeyDown(VirtualKeys.LeftWindows);

        Assert.Equal(CaptureState.Listening, capture.OnKeyUp(VirtualKeys.LeftControl).State);

        CaptureStep step = capture.OnKeyUp(VirtualKeys.LeftWindows);
        Assert.Equal(CaptureState.Captured, step.State);
        Assert.Equal(["LeftCtrl", "LeftWin"], step.Keys);
    }

    [Fact]
    public void The_keys_are_listed_in_the_order_they_were_pressed()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftWindows);
        capture.OnKeyDown(VirtualKeys.LeftControl);
        capture.OnKeyUp(VirtualKeys.LeftControl);

        Assert.Equal(["LeftWin", "LeftCtrl"], capture.OnKeyUp(VirtualKeys.LeftWindows).Keys);
    }

    [Fact]
    public void While_keys_are_held_they_are_reported_for_display()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftControl);

        CaptureStep step = capture.OnKeyDown(VirtualKeys.LeftMenu);

        Assert.Equal(CaptureState.Listening, step.State);
        Assert.Equal(["LeftCtrl", "LeftAlt"], step.Keys);
    }

    [Fact]
    public void Auto_repeat_does_not_add_the_key_twice()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.CapsLock);
        capture.OnKeyDown(VirtualKeys.CapsLock);
        capture.OnKeyDown(VirtualKeys.CapsLock);

        Assert.Equal(["CapsLock"], capture.OnKeyUp(VirtualKeys.CapsLock).Keys);
    }

    [Fact]
    public void A_key_released_then_pressed_again_in_the_same_gesture_counts_once()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftControl);
        capture.OnKeyDown(VirtualKeys.LeftWindows);
        capture.OnKeyUp(VirtualKeys.LeftWindows);
        capture.OnKeyDown(VirtualKeys.LeftWindows);
        capture.OnKeyUp(VirtualKeys.LeftWindows);

        Assert.Equal(["LeftCtrl", "LeftWin"], capture.OnKeyUp(VirtualKeys.LeftControl).Keys);
    }

    [Fact]
    public void A_key_up_from_before_the_capture_is_ignored()
    {
        // The capture starts on a click or on Space; the release of whatever
        // was down at that moment says nothing about the new shortcut.
        var capture = new HotkeyCapture();

        Assert.Equal(CaptureState.Listening, capture.OnKeyUp(VirtualKeys.Space).State);

        capture.OnKeyDown(VirtualKeys.F13);
        Assert.Equal(["F13"], capture.OnKeyUp(VirtualKeys.F13).Keys);
    }

    [Fact]
    public void Escape_alone_cancels()
    {
        Assert.Equal(CaptureState.Cancelled, new HotkeyCapture().OnKeyDown(Escape).State);
    }

    [Fact]
    public void Escape_inside_a_gesture_is_an_unsupported_key_not_a_cancel()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftControl);

        Assert.Equal(CaptureState.Unsupported, capture.OnKeyDown(Escape).State);
    }

    [Fact]
    public void An_ordinary_key_is_refused_and_the_gesture_dropped()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftControl);

        CaptureStep refused = capture.OnKeyDown(KeyA);
        Assert.Equal(CaptureState.Unsupported, refused.State);
        Assert.Empty(refused.Keys);

        // Letting go of what was held finishes nothing: that gesture is gone.
        Assert.Equal(CaptureState.Listening, capture.OnKeyUp(KeyA).State);
        Assert.Equal(CaptureState.Listening, capture.OnKeyUp(VirtualKeys.LeftControl).State);
    }

    [Fact]
    public void After_a_refusal_the_next_gesture_is_captured_from_scratch()
    {
        var capture = new HotkeyCapture();
        capture.OnKeyDown(VirtualKeys.LeftControl);
        capture.OnKeyDown(KeyA);
        capture.OnKeyUp(KeyA);
        capture.OnKeyUp(VirtualKeys.LeftControl);

        capture.OnKeyDown(VirtualKeys.RightShift);

        Assert.Equal(["RightShift"], capture.OnKeyUp(VirtualKeys.RightShift).Keys);
    }

    [Fact]
    public void Every_captured_shortcut_survives_the_settings_validation()
    {
        // A capture the file would then replace by its default would look
        // like a save that did nothing.
        int[] supported =
        [
            VirtualKeys.LeftControl, VirtualKeys.RightControl,
            VirtualKeys.LeftMenu, VirtualKeys.RightMenu,
            VirtualKeys.LeftShift, VirtualKeys.RightShift,
            VirtualKeys.LeftWindows, VirtualKeys.RightWindows,
            VirtualKeys.CapsLock, VirtualKeys.Space,
            VirtualKeys.F13, VirtualKeys.F13 + 11,
        ];

        foreach (int key in supported)
        {
            var capture = new HotkeyCapture();
            capture.OnKeyDown(key);
            string[] shortcut = [.. capture.OnKeyUp(key).Keys];

            var settings = new AppSettings { Hotkey = shortcut };
            settings.Normalize();

            Assert.Equal(shortcut, settings.Hotkey);
        }
    }
}
