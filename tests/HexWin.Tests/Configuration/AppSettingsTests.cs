using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Configuration;

/// <summary>
/// The rule checked end to end here: no input, however damaged, may stop the
/// application from starting with a usable configuration.
/// </summary>
public class AppSettingsTests
{
    private const string DefaultModel = "models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";

    [Fact]
    public void An_empty_json_gives_the_default_values()
    {
        AppSettings settings = AppSettings.Parse("{}");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
        Assert.Equal("cpu", settings.Provider);
        Assert.Equal(InsertionMode.Paste, settings.Insertion);
        Assert.Equal(FeedbackMode.Both, settings.Feedback);
        Assert.Equal("#1971C2", settings.FeedbackColor);
        Assert.Equal(64, settings.FeedbackSize);
        Assert.Equal(DefaultModel, settings.ModelPath);
        Assert.True(settings.LogEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{this is not valid}")]
    [InlineData("[1, 2, 3]")]
    public void An_unreadable_json_gives_the_default_values(string json)
    {
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal("cpu", settings.Provider);
        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        // A key left over from an earlier version must not fail everything.
        // "language" is a real case: the setting existed in the Whisper era,
        // and Parakeet detects the spoken language by itself.
        AppSettings settings = AppSettings.Parse(
            """{"provider": "cpu", "language": "fr", "runtimePreference": ["Vulkan"]}""");

        Assert.Equal("cpu", settings.Provider);
    }

    // --- Compute provider -----------------------------------------------------

    [Theory]
    [InlineData("cpu", "cpu")]
    [InlineData("CPU", "cpu")]
    [InlineData("  Cpu  ", "cpu")]
    public void The_processor_is_recognised_whatever_the_case(string written, string expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"provider": "{{written}}"}""");

        Assert.Equal(expected, settings.Provider);
    }

    [Theory]
    [InlineData("directml")]
    [InlineData("cuda")]
    public void The_GPU_providers_are_refused_as_unavailable(string provider)
    {
        // Removed after testing: sherpa-onnx accepted them, printed a warning
        // on its native output, then fell back to the processor. The setting
        // promised an acceleration that could never happen, with nothing
        // saying so. The sherpa-onnx NuGet packages are built for the
        // processor alone.
        AppSettings settings = AppSettings.Parse($$"""{"provider": "{{provider}}"}""");

        Assert.Equal("cpu", settings.Provider);
    }

    [Theory]
    [InlineData("vulkan")]
    [InlineData("metal")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unknown_provider_falls_back_to_the_processor(string provider)
    {
        AppSettings settings = AppSettings.Parse($$"""{"provider": "{{provider}}"}""");

        Assert.Equal("cpu", settings.Provider);
    }

    // --- Threads --------------------------------------------------------------

    [Theory]
    [InlineData(-4, 1)]
    [InlineData(0, 1)]
    [InlineData(4, 4)]
    [InlineData(1_000, 32)]
    public void The_thread_count_is_brought_back_within_bounds(int written, int expected)
    {
        // Zero threads would stall decoding; a thousand would saturate the
        // machine while speeding nothing up, the model being small.
        AppSettings settings = AppSettings.Parse($$"""{"threads": {{written}}}""");

        Assert.Equal(expected, settings.Threads);
    }

    // --- Shortcut -------------------------------------------------------------

    [Fact]
    public void The_Fn_key_is_refused_and_falls_back_to_the_default()
    {
        // Fn is handled by the embedded controller of the keyboard: it emits
        // no code Windows can observe. Accepting it would give a shortcut that
        // never fires, without a single message.
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["Ctrl", "Fn"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void An_unknown_key_invalidates_the_whole_shortcut()
    {
        // Dropping the offending key would widen the combination instead of
        // narrowing it: ["CapsLock", "Unknown"] would become ["CapsLock"] and
        // dictation would fire on the slightest press of Caps Lock. Falling
        // back to the default is the only behaviour that does not surprise.
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["CapsLock", "Unknown"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void A_fully_valid_shortcut_is_kept()
    {
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["CapsLock"]}""");

        Assert.Equal(["CapsLock"], settings.Hotkey);
    }

    [Fact]
    public void The_shortcut_is_recognised_whatever_the_case()
    {
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["ctrl", "WIN"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Fact]
    public void The_shortcut_is_deduplicated()
    {
        AppSettings settings = AppSettings.Parse("""{"hotkey": ["Ctrl", "ctrl", "Win"]}""");

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    [Theory]
    [InlineData("""{"hotkey": []}""")]
    [InlineData("""{"hotkey": null}""")]
    public void An_empty_shortcut_falls_back_to_the_default(string json)
    {
        // Without this rule, the application would start with no way at all to
        // trigger dictation, in silence.
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal(["Ctrl", "Win"], settings.Hotkey);
    }

    // --- Durations ------------------------------------------------------------

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(250, 250)]
    [InlineData(999_999, 5_000)]
    public void The_minimum_duration_is_brought_back_within_bounds(int written, int expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"minRecordingMilliseconds": {{written}}}""");

        Assert.Equal(expected, settings.MinRecordingMilliseconds);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(120, 120)]
    [InlineData(10_000, 600)]
    public void The_maximum_duration_is_brought_back_within_bounds(int written, int expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"maxRecordingSeconds": {{written}}}""");

        Assert.Equal(expected, settings.MaxRecordingSeconds);
    }

    // --- Miscellaneous --------------------------------------------------------

    [Theory]
    [InlineData("""{"modelPath": ""}""")]
    [InlineData("""{"modelPath": "   "}""")]
    [InlineData("""{"modelPath": null}""")]
    public void An_empty_model_path_falls_back_to_the_default(string json)
    {
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal(DefaultModel, settings.ModelPath);
    }

    [Fact]
    public void Comments_are_accepted_in_the_file()
    {
        // settings.json contains some: it is written to be read and edited by
        // somebody who does not program.
        AppSettings settings = AppSettings.Parse(
            """
            {
              // the shortcut
              "hotkey": ["CapsLock"]
            }
            """);

        Assert.Equal(["CapsLock"], settings.Hotkey);
    }

    [Fact]
    public void A_round_trip_through_json_preserves_the_values()
    {
        var original = new AppSettings
        {
            ModelPath = "models/another-model",
            Hotkey = ["CapsLock"],
            MinRecordingMilliseconds = 400,
            MaxRecordingSeconds = 60,
            Provider = "cpu",
            Threads = 8,
            Insertion = InsertionMode.Type,
            Feedback = FeedbackMode.Visual,
            FeedbackColor = "#E03131",
            FeedbackSize = 96,
            FeedbackOpacity = 200,
            FeedbackTopMargin = 120,
            LogEnabled = false,
        };

        AppSettings reread = AppSettings.Parse(original.ToJson());

        Assert.Equal(original.ModelPath, reread.ModelPath);
        Assert.Equal(original.Hotkey, reread.Hotkey);
        Assert.Equal(original.MinRecordingMilliseconds, reread.MinRecordingMilliseconds);
        Assert.Equal(original.MaxRecordingSeconds, reread.MaxRecordingSeconds);
        Assert.Equal(original.Provider, reread.Provider);
        Assert.Equal(original.Threads, reread.Threads);
        Assert.Equal(original.Insertion, reread.Insertion);
        Assert.Equal(original.Feedback, reread.Feedback);
        Assert.Equal(original.FeedbackColor, reread.FeedbackColor);
        Assert.Equal(original.FeedbackSize, reread.FeedbackSize);
        Assert.Equal(original.FeedbackOpacity, reread.FeedbackOpacity);
        Assert.Equal(original.FeedbackTopMargin, reread.FeedbackTopMargin);
        Assert.False(reread.LogEnabled);
    }

    [Fact]
    public void The_insertion_mode_is_written_out_in_words()
    {
        // To stay readable in settings.json, rather than an opaque integer.
        string json = new AppSettings { Insertion = InsertionMode.Type }.ToJson();

        Assert.Contains("\"Type\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"Circle\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    public void An_unknown_feedback_mode_falls_back_to_the_default(string value)
    {
        // Same rule as everywhere else here: a setting typed from memory
        // must not cost the user their dictation.
        AppSettings settings = AppSettings.Parse($$"""{"feedback": {{value}}}""");

        Assert.Equal(FeedbackMode.Both, settings.Feedback);
    }

    [Fact]
    public void A_missing_file_gives_the_default_values()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"hexwin-{Guid.NewGuid():N}.json");

        AppSettings settings = AppSettings.Load(absent);

        Assert.Equal("cpu", settings.Provider);
    }

    [Fact]
    public void A_file_that_is_present_is_read_back_correctly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hexwin-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"threads": 8, "hotkey": ["CapsLock"]}""");

        try
        {
            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(8, settings.Threads);
            Assert.Equal(["CapsLock"], settings.Hotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Theory]
    [InlineData("\"rouge\"")]
    [InlineData("\"#12345\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void An_unreadable_colour_falls_back_to_the_default(string value)
    {
        AppSettings settings = AppSettings.Parse($$"""{"feedbackColor": {{value}}}""");

        Assert.Equal("#1971C2", settings.FeedbackColor);
    }

    [Theory]
    [InlineData("\"#1971c2\"")]
    [InlineData("\"1971C2\"")]
    [InlineData("\"  #1971C2  \"")]
    public void A_colour_is_rewritten_in_one_spelling(string value)
    {
        // So the file settles on a single form whichever of the accepted ones
        // was typed, instead of keeping four ways of saying the same blue.
        AppSettings settings = AppSettings.Parse($$"""{"feedbackColor": {{value}}}""");

        Assert.Equal("#1971C2", settings.FeedbackColor);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(-40, 16)]
    [InlineData(9999, 512)]
    [InlineData(96, 96)]
    public void The_circle_keeps_a_usable_size(int asked, int expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"feedbackSize": {{asked}}}""");

        Assert.Equal(expected, settings.FeedbackSize);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(999, 255)]
    [InlineData(150, 150)]
    public void The_circle_never_becomes_invisible(int asked, int expected)
    {
        // Asking for the circle and getting nothing reads as a broken feature.
        // Someone who wants no circle sets "feedback" instead.
        AppSettings settings = AppSettings.Parse($$"""{"feedbackOpacity": {{asked}}}""");

        Assert.Equal(expected, settings.FeedbackOpacity);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(999999, 2000)]
    [InlineData(120, 120)]
    public void The_top_margin_stays_within_reach(int asked, int expected)
    {
        AppSettings settings = AppSettings.Parse($$"""{"feedbackTopMargin": {{asked}}}""");

        Assert.Equal(expected, settings.FeedbackTopMargin);
    }
}
