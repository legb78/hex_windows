using HexWin.Ui;
using Xunit;

namespace HexWin.Tests.Ui;

/// <summary>
/// What the settings window writes on screen. The shortcut has to say which
/// side of the keyboard, and the values the unit they are in.
/// </summary>
public class SettingTextTests
{
    [Theory]
    [InlineData("RightShift", "Maj droite")]
    [InlineData("LeftShift", "Maj gauche")]
    [InlineData("LeftCtrl", "Ctrl gauche")]
    [InlineData("RightAlt", "Alt Gr")]
    [InlineData("CapsLock", "Verr. Maj")]
    [InlineData("F13", "F13")]
    public void A_key_is_named_as_printed_on_a_french_keyboard(string key, string expected)
    {
        Assert.Equal(expected, HotkeyText.Describe(key));
    }

    [Fact]
    public void A_chord_is_written_with_plus_signs()
    {
        Assert.Equal("Ctrl + Windows", HotkeyText.Describe(["Ctrl", "Win"]));
    }

    [Fact]
    public void A_lone_shift_points_to_the_other_one_for_capitals()
    {
        Assert.Equal("Utilisez Maj gauche pour les majuscules.", HotkeyText.Caveat(["RightShift"]));
    }

    [Fact]
    public void Space_alone_is_warned_about()
    {
        Assert.NotNull(HotkeyText.Caveat(["Space"]));
    }

    [Fact]
    public void A_chord_costs_no_key_for_typing_and_carries_no_warning()
    {
        Assert.Null(HotkeyText.Caveat(["LeftCtrl", "Space"]));
        Assert.Null(HotkeyText.Caveat(["F13"]));
    }

    [Theory]
    [InlineData(0, "Jamais")]
    [InlineData(5, "5 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30")]
    public void The_idle_delay_says_never_for_zero(int minutes, string expected)
    {
        Assert.Equal(expected, SettingText.IdleMinutes(minutes));
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(120, "2 min")]
    [InlineData(150, "2 min 30 s")]
    public void Seconds_turn_into_minutes(int seconds, string expected)
    {
        Assert.Equal(expected, SettingText.Seconds(seconds));
    }

    [Theory]
    [InlineData(255, "100 %")]
    [InlineData(235, "92 %")]
    [InlineData(20, "8 %")]
    public void Opacity_is_shown_as_a_percentage(int opacity, string expected)
    {
        Assert.Equal(expected, SettingText.Opacity(opacity));
    }

    [Fact]
    public void Milliseconds_group_their_thousands_the_french_way()
    {
        Assert.Equal("250 ms", SettingText.Milliseconds(250));
        Assert.StartsWith("5", SettingText.Milliseconds(5_000));
        Assert.EndsWith("000 ms", SettingText.Milliseconds(5_000));
    }

    [Fact]
    public void One_thread_is_singular()
    {
        Assert.Equal("1 fil", SettingText.Threads(1));
        Assert.Equal("4 fils", SettingText.Threads(4));
    }

    [Fact]
    public void A_key_the_window_does_not_name_is_shown_as_is()
    {
        Assert.Equal("Dossier du modèle", SettingText.NameOf("modelPath"));
        Assert.Equal("somethingNew", SettingText.NameOf("somethingNew"));
    }
}
