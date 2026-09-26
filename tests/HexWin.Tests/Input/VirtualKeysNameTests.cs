using HexWin.Input;
using Xunit;

namespace HexWin.Tests.Input;

/// <summary>
/// <see cref="VirtualKeys.NameOf"/> is what turns a captured key back into
/// the text of settings.json. It has to name the side, and to agree with
/// <see cref="VirtualKeys.Resolve"/> both ways.
/// </summary>
public class VirtualKeysNameTests
{
    [Theory]
    [InlineData(VirtualKeys.LeftShift, "LeftShift")]
    [InlineData(VirtualKeys.RightShift, "RightShift")]
    [InlineData(VirtualKeys.LeftControl, "LeftCtrl")]
    [InlineData(VirtualKeys.RightMenu, "RightAlt")]
    [InlineData(VirtualKeys.RightWindows, "RightWin")]
    [InlineData(VirtualKeys.CapsLock, "CapsLock")]
    [InlineData(VirtualKeys.Space, "Space")]
    [InlineData(VirtualKeys.F13, "F13")]
    [InlineData(VirtualKeys.F13 + 11, "F24")]
    public void A_supported_key_is_named_with_its_side(int virtualKey, string expected)
    {
        Assert.Equal(expected, VirtualKeys.NameOf(virtualKey));
    }

    [Theory]
    [InlineData(0x41)] // A
    [InlineData(0x1B)] // Escape
    [InlineData(0x0D)] // Enter
    [InlineData(0x7B)] // F12, carried by every keyboard and bound everywhere
    [InlineData(0x88)] // just past F24
    public void A_key_that_cannot_be_a_shortcut_has_no_name(int virtualKey)
    {
        Assert.Null(VirtualKeys.NameOf(virtualKey));
    }

    [Theory]
    [InlineData(VirtualKeys.LeftShift)]
    [InlineData(VirtualKeys.RightMenu)]
    [InlineData(VirtualKeys.LeftWindows)]
    [InlineData(VirtualKeys.CapsLock)]
    [InlineData(VirtualKeys.F13 + 5)]
    public void The_name_resolves_back_to_that_key_alone(int virtualKey)
    {
        Assert.Equal([virtualKey], VirtualKeys.Resolve(VirtualKeys.NameOf(virtualKey)!));
    }
}
