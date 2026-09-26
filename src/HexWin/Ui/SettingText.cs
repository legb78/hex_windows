namespace HexWin.Ui;

/// <summary>
/// How the settings window writes values and names settings. Kept apart from
/// the window, which cannot be tested, because the wording is exactly what a
/// test can pin down. Every method takes the language to write in, so a test
/// never depends on the one in force.
/// </summary>
public static class SettingText
{
    /// <summary>A setting's name in the window; its key when the language has none.</summary>
    public static string NameOf(UiStrings text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.SettingNames.TryGetValue(key, out string? name) ? name : key;
    }

    /// <summary>Thousands grouped the way the language groups them: 5 000 or 5,000.</summary>
    public static string Milliseconds(UiStrings text, int value)
    {
        ArgumentNullException.ThrowIfNull(text);

        return $"{value.ToString("N0", text.Culture)} ms";
    }

    /// <summary>Seconds, turned into minutes once there are enough of them.</summary>
    public static string Seconds(int value)
    {
        if (value < 60)
        {
            return $"{value} s";
        }

        int minutes = value / 60;
        int seconds = value % 60;

        return seconds == 0 ? $"{minutes} min" : $"{minutes} min {seconds:00} s";
    }

    /// <summary>
    /// The delay before the model is released. Zero is not a delay but the
    /// absence of one: the model stays loaded, and the text has to say so.
    /// </summary>
    public static string IdleMinutes(UiStrings text, int value)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (value == 0)
        {
            return text.Never;
        }

        if (value < 60)
        {
            return $"{value} min";
        }

        int hours = value / 60;
        int minutes = value % 60;

        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes:00}";
    }

    /// <summary>
    /// The pause that closes a sentence. Zero turns the cutting off, and the
    /// text has to say that, not "0 ms", which reads as a pause of no length.
    /// </summary>
    public static string Pause(UiStrings text, int value)
    {
        ArgumentNullException.ThrowIfNull(text);

        return value == 0 ? text.Off : Milliseconds(text, value);
    }

    public static string Pixels(int value) => $"{value} px";

    /// <summary>An opacity from 0 to 255, as the percentage people think in.</summary>
    public static string Opacity(UiStrings text, int value)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Percent((int)Math.Round(value * 100 / 255.0));
    }

    public static string Threads(UiStrings text, int value)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.ThreadCount(value);
    }
}
