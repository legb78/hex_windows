using HexWin.Input;
using Xunit;

namespace HexWin.Tests.Input;

public class VirtualKeysTests
{
    [Theory]
    [InlineData(VirtualKeys.RightMenu)]
    [InlineData(VirtualKeys.RightControl)]
    [InlineData(VirtualKeys.LeftWindows)]
    [InlineData(VirtualKeys.RightWindows)]
    public void The_keys_a_keyboard_reports_with_the_extended_prefix_are_extended(int virtualKey)
    {
        Assert.True(VirtualKeys.IsExtended(virtualKey));
    }

    [Theory]
    [InlineData(VirtualKeys.LeftMenu)]
    [InlineData(VirtualKeys.LeftControl)]
    [InlineData(VirtualKeys.LeftShift)]
    [InlineData(VirtualKeys.RightShift)]
    [InlineData(VirtualKeys.Space)]
    [InlineData(VirtualKeys.F13)]
    public void The_other_keys_are_not(int virtualKey)
    {
        // The right Shift has a scan code of its own, unlike the right Alt
        // and Ctrl: it needs no prefix to be told from the left one.
        Assert.False(VirtualKeys.IsExtended(virtualKey));
    }
}
