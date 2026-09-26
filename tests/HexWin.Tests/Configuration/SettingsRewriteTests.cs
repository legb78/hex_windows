using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Configuration;

/// <summary>
/// The rule checked here: rewriting one setting leaves the rest of the file
/// exactly as it was.
///
/// The file is documentation as much as configuration — every value carries a
/// comment explaining what it does. Serialising the whole object back would be
/// shorter and would silently throw all of that away, so the rewrite is
/// line-based, and these tests are what keeps it that way.
/// </summary>
public class SettingsRewriteTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hexwin-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private void Write(string content) => File.WriteAllText(_path, content);

    private string Read() => File.ReadAllText(_path);

    [Fact]
    public void Rewriting_a_value_keeps_the_comments()
    {
        Write("""
            {
              // Which cues mark a recording.
              "feedback": "Both",

              // Diameter of the circle in pixels.
              "feedbackSize": 64
            }
            """);

        Assert.True(AppSettings.TryRewriteValue(_path, "feedback", "\"Visual\""));

        string after = Read();
        Assert.Contains("// Which cues mark a recording.", after);
        Assert.Contains("// Diameter of the circle in pixels.", after);
        Assert.Contains("\"feedback\": \"Visual\",", after);
    }

    [Fact]
    public void Rewriting_a_value_touches_no_other_line()
    {
        Write("""
            {
              "hotkey": ["CapsLock"],
              "feedback": "Both",
              "feedbackSize": 64
            }
            """);

        AppSettings.TryRewriteValue(_path, "feedback", "\"None\"");

        string after = Read();
        Assert.Contains("\"hotkey\": [\"CapsLock\"],", after);
        Assert.Contains("\"feedbackSize\": 64", after);
    }

    [Fact]
    public void The_trailing_comma_is_preserved()
    {
        // Dropping it would leave the file unparseable, and the next start
        // would fall back to the defaults — losing every other setting.
        Write("""
            {
              "feedback": "Both",
              "feedbackSize": 64
            }
            """);

        AppSettings.TryRewriteValue(_path, "feedback", "\"Sound\"");

        Assert.Contains("\"feedback\": \"Sound\",", Read());
    }

    [Fact]
    public void A_value_without_a_trailing_comma_stays_without_one()
    {
        Write("""
            {
              "feedbackSize": 64,
              "feedback": "Both"
            }
            """);

        AppSettings.TryRewriteValue(_path, "feedback", "\"Visual\"");

        string after = Read();
        Assert.Contains("\"feedback\": \"Visual\"", after);
        Assert.DoesNotContain("\"feedback\": \"Visual\",", after);
    }

    [Fact]
    public void The_rewritten_file_still_parses_to_the_new_value()
    {
        Write("""
            {
              // A comment the serializer could not have written back.
              "feedback": "Both",
              "hotkey": ["CapsLock"]
            }
            """);

        AppSettings.TryRewriteValue(_path, "feedback", "\"Sound\"");

        AppSettings reloaded = AppSettings.Load(_path);
        Assert.Equal(FeedbackMode.Sound, reloaded.Feedback);
        Assert.Equal(["CapsLock"], reloaded.Hotkey);
    }

    [Fact]
    public void An_unknown_key_changes_nothing()
    {
        Write("""
            {
              "feedback": "Both"
            }
            """);

        string before = Read();

        Assert.False(AppSettings.TryRewriteValue(_path, "nosuchkey", "\"x\""));
        Assert.Equal(before, Read());
    }

    [Fact]
    public void A_missing_file_is_reported_rather_than_created()
    {
        // The caller has already applied the change in memory. Failing to
        // persist it is worth a log line, not an exception in the menu.
        string absent = Path.Combine(Path.GetTempPath(), $"hexwin-absent-{Guid.NewGuid():N}.json");

        Assert.False(AppSettings.TryRewriteValue(absent, "feedback", "\"Both\""));
        Assert.False(File.Exists(absent));
    }

    // --- Several values at once, for the settings window -------------------------

    private static KeyValuePair<string, string>[] Values(params (string Key, string Json)[] values) =>
        [.. values.Select(value => KeyValuePair.Create(value.Key, value.Json))];

    [Fact]
    public void Several_values_are_rewritten_in_one_pass_and_the_comments_kept()
    {
        Write("""
            {
              // The shortcut.
              "hotkey": ["RightShift"],
              // Diameter of the circle in pixels.
              "feedbackSize": 64,
              "insertion": "Paste"
            }
            """);

        IReadOnlyList<string> failed = AppSettings.RewriteValues(
            _path,
            Values(("hotkey", "[\"LeftCtrl\", \"LeftWin\"]"), ("insertion", "\"Type\"")),
            appendMissing: false);

        Assert.Empty(failed);

        string after = Read();
        Assert.Contains("// The shortcut.", after);
        Assert.Contains("// Diameter of the circle in pixels.", after);
        Assert.Contains("\"hotkey\": [\"LeftCtrl\", \"LeftWin\"],", after);
        Assert.Contains("\"feedbackSize\": 64,", after);
        Assert.Contains("\"insertion\": \"Type\"", after);

        AppSettings reloaded = AppSettings.Load(_path);
        Assert.Equal(["LeftCtrl", "LeftWin"], reloaded.Hotkey);
        Assert.Equal(InsertionMode.Type, reloaded.Insertion);
    }

    [Fact]
    public void Without_appending_a_missing_key_is_reported_and_the_rest_still_written()
    {
        Write("""
            {
              "feedback": "Both"
            }
            """);

        IReadOnlyList<string> failed = AppSettings.RewriteValues(
            _path,
            Values(("feedback", "\"Sound\""), ("feedbackColor", "\"#00FF00\"")),
            appendMissing: false);

        Assert.Equal(["feedbackColor"], failed);
        Assert.Contains("\"feedback\": \"Sound\"", Read());
        Assert.DoesNotContain("feedbackColor", Read());
    }

    [Fact]
    public void A_key_missing_from_an_older_file_is_added_before_the_closing_brace()
    {
        // A settings.json kept from an older version lacks the settings added
        // since. Saving from the window must not skip exactly those.
        Write("""
            {
              // Which cues mark a recording.
              "feedback": "Both"
            }
            """);

        IReadOnlyList<string> failed = AppSettings.RewriteValues(
            _path,
            Values(("feedbackColor", "\"#00FF00\""), ("feedbackSize", "96")),
            appendMissing: true);

        Assert.Empty(failed);

        string after = Read();
        Assert.Contains("// Which cues mark a recording.", after);
        Assert.Contains("\"feedback\": \"Both\",", after);

        AppSettings reloaded = AppSettings.Load(_path);
        Assert.Equal(FeedbackMode.Both, reloaded.Feedback);
        Assert.Equal("#00FF00", reloaded.FeedbackColor);
        Assert.Equal(96, reloaded.FeedbackSize);
    }

    [Fact]
    public void Appending_skips_the_comments_and_blank_lines_before_the_brace()
    {
        Write("""
            {
              "feedback": "Both"

              // A closing remark.
            }
            """);

        AppSettings.RewriteValues(_path, Values(("feedbackSize", "96")), appendMissing: true);

        string after = Read();
        Assert.Contains("\"feedback\": \"Both\",", after);
        Assert.Contains("// A closing remark.", after);
        Assert.Equal(96, AppSettings.Load(_path).FeedbackSize);
    }

    [Fact]
    public void Appending_into_an_empty_object_needs_no_comma()
    {
        Write("""
            {
            }
            """);

        Assert.Empty(AppSettings.RewriteValues(_path, Values(("threads", "8")), appendMissing: true));
        Assert.Equal(8, AppSettings.Load(_path).Threads);
    }

    [Fact]
    public void Appending_is_refused_after_a_trailing_comment_rather_than_breaking_the_file()
    {
        // The comma the last value needs would land inside the comment.
        Write("""
            {
              "feedback": "Both" // what marks a recording
            }
            """);

        string before = Read();

        IReadOnlyList<string> failed = AppSettings.RewriteValues(_path, Values(("feedbackSize", "96")), appendMissing: true);

        Assert.Equal(["feedbackSize"], failed);
        Assert.Equal(before, Read());
    }

    [Fact]
    public void Several_values_in_a_missing_file_are_all_reported()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"hexwin-absent-{Guid.NewGuid():N}.json");

        IReadOnlyList<string> failed = AppSettings.RewriteValues(
            absent,
            Values(("feedback", "\"Both\""), ("threads", "2")),
            appendMissing: true);

        Assert.Equal(["feedback", "threads"], failed);
        Assert.False(File.Exists(absent));
    }

    [Fact]
    public void A_key_appearing_inside_a_comment_is_not_mistaken_for_the_setting()
    {
        Write("""
            {
              // Set "feedback" to None to silence everything.
              "feedback": "Both"
            }
            """);

        AppSettings.TryRewriteValue(_path, "feedback", "\"None\"");

        string after = Read();
        Assert.Contains("// Set \"feedback\" to None to silence everything.", after);
        Assert.Contains("\"feedback\": \"None\"", after);
    }
}
