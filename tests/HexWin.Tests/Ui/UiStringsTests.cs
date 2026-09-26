using System.Globalization;
using System.Text.Json;
using HexWin.Configuration;
using HexWin.Input;
using HexWin.Ui;
using Xunit;

namespace HexWin.Tests.Ui;

/// <summary>
/// Which language the interface speaks, and whether each language has a word
/// for everything the interface names. A missing text property does not
/// compile; a missing dictionary entry does, and would reach the user as a
/// raw key — those are the gaps checked here.
/// </summary>
public class UiStringsTests
{
    private static readonly CultureInfo FrenchWindows = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo EnglishWindows = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo GermanWindows = CultureInfo.GetCultureInfo("de-DE");

    public static TheoryData<UiStrings> Languages => [UiStrings.French, UiStrings.English];

    [Fact]
    public void Auto_follows_a_french_windows()
    {
        Assert.Same(UiStrings.French, UiStrings.For("auto", FrenchWindows));
        Assert.Same(UiStrings.French, UiStrings.For("auto", CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Fact]
    public void Auto_follows_an_english_windows()
    {
        Assert.Same(UiStrings.English, UiStrings.For("auto", EnglishWindows));
    }

    [Fact]
    public void Auto_falls_back_to_english_for_a_language_hexwin_does_not_speak()
    {
        Assert.Same(UiStrings.English, UiStrings.For("auto", GermanWindows));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("FR")]
    [InlineData(" fr ")]
    public void A_forced_french_wins_over_windows(string language)
    {
        Assert.Same(UiStrings.French, UiStrings.For(language, EnglishWindows));
    }

    [Fact]
    public void A_forced_english_wins_over_windows()
    {
        Assert.Same(UiStrings.English, UiStrings.For("en", FrenchWindows));
    }

    [Fact]
    public void A_missing_language_behaves_like_auto()
    {
        Assert.Same(UiStrings.French, UiStrings.For(null, FrenchWindows));
        Assert.Same(UiStrings.French, UiStrings.For("", FrenchWindows));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_setting_of_the_file_has_a_name(UiStrings text)
    {
        // The save report names the keys it could not write; a key without a
        // name would show up as "feedbackTopMargin" in the middle of a sentence.
        using JsonDocument settings = JsonDocument.Parse(new AppSettings().ToJson());

        string[] unnamed = [.. settings.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Where(key => !text.SettingNames.ContainsKey(key))];

        Assert.Empty(unnamed);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_key_a_capture_can_produce_has_a_name(UiStrings text)
    {
        int[] capturable =
        [
            VirtualKeys.LeftControl, VirtualKeys.RightControl,
            VirtualKeys.LeftMenu, VirtualKeys.RightMenu,
            VirtualKeys.LeftShift, VirtualKeys.RightShift,
            VirtualKeys.LeftWindows, VirtualKeys.RightWindows,
            VirtualKeys.CapsLock, VirtualKeys.Space,
        ];

        // F13 to F24 are named as they are; everything else needs a word.
        string[] unnamed = [.. capturable
            .Select(key => VirtualKeys.NameOf(key)!)
            .Where(name => !text.KeyNames.ContainsKey(name))];

        Assert.Empty(unnamed);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_generic_key_names_of_the_file_have_a_name_too(UiStrings text)
    {
        // A hand-written settings.json may say "Ctrl" rather than "LeftCtrl".
        Assert.All(["Ctrl", "Alt", "Shift", "Win"], name => Assert.True(text.KeyNames.ContainsKey(name), name));
    }

    [Fact]
    public void A_missing_model_file_is_named_in_the_language_of_the_interface()
    {
        // The engine throws in French, for the log; the window rebuilds the
        // sentence from the path.
        var missing = new FileNotFoundException("Fichier de modèle manquant : x", @"C:\models\encoder.onnx");

        Assert.Equal(@"Model file missing: C:\models\encoder.onnx.", UiStrings.English.ModelProblem(missing));
        Assert.Equal(@"Fichier de modèle manquant : C:\models\encoder.onnx.", UiStrings.French.ModelProblem(missing));
    }

    [Fact]
    public void Any_other_model_problem_is_passed_through()
    {
        var native = new DllNotFoundException("sherpa-onnx-c-api.dll");

        Assert.Equal("sherpa-onnx-c-api.dll", UiStrings.English.ModelProblem(native));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_tray_tooltip_stays_within_what_windows_accepts(UiStrings text)
    {
        // The notification area keeps a tooltip in NOTIFYICONDATA.szTip, 128
        // characters with the terminator. The longest ready tooltip is the one
        // naming the longest shortcut.
        string longest = text.TipReady(HotkeyText.Describe(text, ["LeftCtrl", "LeftAlt", "LeftShift", "LeftWin"]));

        Assert.True(longest.Length <= 127, $"{longest.Length} characters: {longest}");
    }
}
