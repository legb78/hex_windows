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
