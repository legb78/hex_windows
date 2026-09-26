using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Configuration;

/// <summary>
/// The diff decides what the settings window writes to the file, and what it
/// tells the user will wait for a restart. Writing an unchanged key would be
/// harmless; missing a changed one would lose it at the next start.
/// </summary>
public class SettingsDiffTests
{
    [Fact]
    public void Two_identical_configurations_differ_in_nothing()
    {
        Assert.Empty(SettingsDiff.Between(new AppSettings(), new AppSettings()));
    }

    [Fact]
    public void A_clone_differs_in_nothing()
    {
        var settings = new AppSettings { Hotkey = ["RightShift"], FeedbackColor = "#00FF00", Threads = 8 };
        settings.Normalize();

        Assert.Empty(SettingsDiff.Between(settings, settings.Clone()));
    }

    [Fact]
    public void Each_changed_setting_is_reported_under_its_file_key()
    {
        var before = new AppSettings();
        var after = new AppSettings { Insertion = InsertionMode.Type, FeedbackSize = 96 };

        IReadOnlyList<SettingChange> changes = SettingsDiff.Between(before, after);

        Assert.Equal(["insertion", "feedbackSize"], changes.Select(change => change.Key));
        Assert.Equal("\"Type\"", changes[0].JsonValue);
        Assert.Equal("96", changes[1].JsonValue);
    }

    [Fact]
    public void A_shortcut_is_written_on_a_single_line()
    {
        // The rewrite replaces one line. The serializer indents arrays over
        // several, which would leave the end of the old value behind.
        var after = new AppSettings { Hotkey = ["LeftCtrl", "LeftWin"] };

        SettingChange change = Assert.Single(SettingsDiff.Between(new AppSettings(), after));

        Assert.Equal("hotkey", change.Key);
        Assert.Equal("[\"LeftCtrl\", \"LeftWin\"]", change.JsonValue);
    }

    [Theory]
    [InlineData("modelPath")]
    [InlineData("threads")]
    [InlineData("unloadAfterMinutes")]
    [InlineData("minRecordingMilliseconds")]
    [InlineData("maxRecordingSeconds")]
    [InlineData("logEnabled")]
    public void Settings_read_at_startup_are_flagged_for_a_restart(string key)
    {
        var after = new AppSettings
        {
            ModelPath = "models/other",
            Threads = 2,
            UnloadAfterMinutes = 0,
            MinRecordingMilliseconds = 500,
            MaxRecordingSeconds = 60,
            LogEnabled = false,
        };

        SettingChange change = SettingsDiff.Between(new AppSettings(), after).Single(c => c.Key == key);

        Assert.True(change.RequiresRestart);
    }

    [Theory]
    [InlineData("hotkey")]
    [InlineData("insertion")]
    [InlineData("feedback")]
    [InlineData("feedbackColor")]
    [InlineData("feedbackOpacity")]
    [InlineData("pauseMilliseconds")]
    [InlineData("segmentation")]
    [InlineData("language")]
    public void Settings_applied_on_the_fly_are_not_flagged(string key)
    {
        var after = new AppSettings
        {
            Language = "en",
            Segmentation = true,
            PauseMilliseconds = 0,
            Hotkey = ["F13"],
            Insertion = InsertionMode.Type,
            Feedback = FeedbackMode.None,
            FeedbackColor = "#123456",
            FeedbackOpacity = 100,
        };

        SettingChange change = SettingsDiff.Between(new AppSettings(), after).Single(c => c.Key == key);

        Assert.False(change.RequiresRestart);
    }

    [Fact]
    public void A_written_change_reads_back_as_the_new_value()
    {
        // The value the diff produces must be one the file parses back into
        // the same setting — otherwise the next start would quietly revert it.
        var after = new AppSettings { Hotkey = ["LeftCtrl", "LeftWin"], FeedbackColor = "#ABCDEF", Insertion = InsertionMode.Type };
        after.Normalize();

        IReadOnlyList<SettingChange> changes = SettingsDiff.Between(new AppSettings(), after);
        string json = "{" + string.Join(",", changes.Select(change => $"\"{change.Key}\": {change.JsonValue}")) + "}";

        AppSettings reread = AppSettings.Parse(json);

        Assert.Empty(SettingsDiff.Between(after, reread));
    }
}
