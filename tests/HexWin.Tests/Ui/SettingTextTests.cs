using HexWin.Ui;
using Xunit;

namespace HexWin.Tests.Ui;

/// <summary>
/// What the settings window writes on screen. The shortcut has to say which
/// side of the keyboard, and the values the unit they are in — in French and
/// in English alike. Every test names its language, so none depends on the one
/// in force.
/// </summary>
public class SettingTextTests
{
    private static readonly UiStrings Fr = UiStrings.French;
    private static readonly UiStrings En = UiStrings.English;

    [Theory]
    [InlineData("RightShift", "Maj droite")]
    [InlineData("LeftShift", "Maj gauche")]
    [InlineData("LeftCtrl", "Ctrl gauche")]
    [InlineData("RightAlt", "Alt Gr")]
    [InlineData("CapsLock", "Verr. Maj")]
    [InlineData("F13", "F13")]
    public void A_key_is_named_as_printed_on_a_french_keyboard(string key, string expected)
    {
        Assert.Equal(expected, HotkeyText.Describe(Fr, key));
    }

    [Theory]
    [InlineData("RightShift", "Right Shift")]
    [InlineData("LeftCtrl", "Left Ctrl")]
    [InlineData("RightAlt", "Right Alt")]
    [InlineData("CapsLock", "Caps Lock")]
    [InlineData("F13", "F13")]
    public void A_key_is_named_as_printed_on_an_english_keyboard(string key, string expected)
    {
        Assert.Equal(expected, HotkeyText.Describe(En, key));
    }

    [Fact]
    public void A_chord_is_written_with_plus_signs()
    {
        Assert.Equal("Ctrl + Windows", HotkeyText.Describe(Fr, ["Ctrl", "Win"]));
        Assert.Equal("Left Ctrl + Left Windows", HotkeyText.Describe(En, ["LeftCtrl", "LeftWin"]));
    }

    [Fact]
    public void A_lone_shift_points_to_the_other_one_for_capitals()
    {
        Assert.Equal("Utilisez Maj gauche pour les majuscules.", HotkeyText.Caveat(Fr, ["RightShift"]));
        Assert.Equal("Use the left Shift for capitals.", HotkeyText.Caveat(En, ["RightShift"]));
    }

    [Fact]
    public void Space_alone_is_warned_about()
    {
        Assert.NotNull(HotkeyText.Caveat(Fr, ["Space"]));
        Assert.NotNull(HotkeyText.Caveat(En, ["Space"]));
    }

    [Theory]
    [InlineData("LeftCtrl", "RightAlt")]
    [InlineData("RightAlt", "LeftCtrl")]
    public void Left_ctrl_with_right_alt_is_named_as_altgr(string first, string second)
    {
        // What Windows sends for one press of AltGr on a French keyboard; the
        // user pressed one key and must not be left wondering about two.
        Assert.Contains("AltGr", HotkeyText.Caveat(Fr, [first, second]));
        Assert.Contains("AltGr", HotkeyText.Caveat(En, [first, second]));
    }

    [Fact]
    public void A_chord_costs_no_key_for_typing_and_carries_no_warning()
    {
        Assert.Null(HotkeyText.Caveat(Fr, ["LeftCtrl", "Space"]));
        Assert.Null(HotkeyText.Caveat(En, ["F13"]));
    }

    [Theory]
    [InlineData(0, "Jamais", "Never")]
    [InlineData(5, "5 min", "5 min")]
    [InlineData(60, "1 h", "1 h")]
    [InlineData(90, "1 h 30", "1 h 30")]
    public void The_idle_delay_says_never_for_zero(int minutes, string french, string english)
    {
        Assert.Equal(french, SettingText.IdleMinutes(Fr, minutes));
        Assert.Equal(english, SettingText.IdleMinutes(En, minutes));
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
    [InlineData(255, "100 %", "100%")]
    [InlineData(235, "92 %", "92%")]
    [InlineData(20, "8 %", "8%")]
    public void Opacity_is_a_percentage_typeset_the_way_each_language_does(int opacity, string french, string english)
    {
        Assert.Equal(french, SettingText.Opacity(Fr, opacity));
        Assert.Equal(english, SettingText.Opacity(En, opacity));
    }

    [Fact]
    public void Milliseconds_group_their_thousands_the_way_each_language_does()
    {
        Assert.Equal("250 ms", SettingText.Milliseconds(Fr, 250));
        Assert.Equal("5,000 ms", SettingText.Milliseconds(En, 5_000));

        // French groups with a narrow no-break space; the exact character is
        // the culture's business, the digits either side of it are ours.
        string french = SettingText.Milliseconds(Fr, 5_000);
        Assert.StartsWith("5", french);
        Assert.EndsWith("000 ms", french);
        Assert.DoesNotContain(",", french);
    }

    [Fact]
    public void One_thread_is_singular()
    {
        Assert.Equal("1 fil", SettingText.Threads(Fr, 1));
        Assert.Equal("4 fils", SettingText.Threads(Fr, 4));
        Assert.Equal("1 thread", SettingText.Threads(En, 1));
        Assert.Equal("4 threads", SettingText.Threads(En, 4));
    }

    [Fact]
    public void A_pause_of_zero_says_the_cutting_is_off()
    {
        Assert.Equal("Désactivée", SettingText.Pause(Fr, 0));
        Assert.Equal("Off", SettingText.Pause(En, 0));
        Assert.Equal("700 ms", SettingText.Pause(En, 700));
    }

    [Fact]
    public void A_key_the_window_does_not_name_is_shown_as_is()
    {
        Assert.Equal("Dossier du modèle", SettingText.NameOf(Fr, "modelPath"));
        Assert.Equal("Model folder", SettingText.NameOf(En, "modelPath"));
        Assert.Equal("somethingNew", SettingText.NameOf(En, "somethingNew"));
    }
}
