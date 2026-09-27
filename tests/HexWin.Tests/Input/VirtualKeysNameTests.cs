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
    [InlineData(0x41, "A")]
    [InlineData(0x35, "5")]
    [InlineData(0x1B, "Escape")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x7B, "F12")]
    [InlineData(0x65, "Numpad5")]
    [InlineData(0xB3, "MediaPlayPause")]
    [InlineData(0xBA, "Oem1")]
    public void A_key_is_named_with_its_side(int virtualKey, string expected)
    {
        Assert.Equal(expected, VirtualKeys.NameOf(virtualKey));
    }

    [Theory]
    [InlineData(0x88, "VK_88")] // just past F24
    [InlineData(0xE8, "VK_E8")]
    [InlineData(0x07, "VK_07")]
    public void A_key_with_no_usual_name_is_named_by_its_code(int virtualKey, string expected)
    {
        Assert.Equal(expected, VirtualKeys.NameOf(virtualKey));
    }

    [Fact]
    public void Every_key_resolves_back_from_its_name()
    {
        Assert.All(Enumerable.Range(1, 0xFF), key =>
            Assert.Equal([key], VirtualKeys.Resolve(VirtualKeys.NameOf(key))));
    }

    [Theory]
    [InlineData("a", "A")]
    [InlineData("vk_41", "A")]
    [InlineData(" f5 ", "F5")]
    [InlineData("vk_e8", "VK_E8")]
    [InlineData("ctrl", "Ctrl")]
    [InlineData("rightshift", "RightShift")]
    public void A_name_is_brought_back_to_its_written_form(string name, string expected)
    {
        Assert.Equal(expected, VirtualKeys.Canonical(name));
    }

    [Theory]
    [InlineData("Fn")]
    [InlineData("VK_00")]
    [InlineData("VK_100")]
    [InlineData("VK_ZZ")]
    [InlineData("")]
    public void A_key_that_does_not_exist_has_no_written_form(string name)
    {
        Assert.Null(VirtualKeys.Canonical(name));
    }

    [Theory]
    [InlineData(VirtualKeys.LeftShift)]
    [InlineData(VirtualKeys.RightMenu)]
    [InlineData(VirtualKeys.LeftWindows)]
    [InlineData(VirtualKeys.CapsLock)]
    [InlineData(VirtualKeys.F13 + 5)]
    public void The_name_resolves_back_to_that_key_alone(int virtualKey)
    {
        Assert.Equal([virtualKey], VirtualKeys.Resolve(VirtualKeys.NameOf(virtualKey)));
    }
}
