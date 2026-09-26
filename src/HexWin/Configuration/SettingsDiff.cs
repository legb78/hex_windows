using System.Text.Json;

namespace HexWin.Configuration;

/// <summary>One setting that differs between two configurations.</summary>
/// <param name="Key">Its name in settings.json.</param>
/// <param name="JsonValue">The new value, as it must be written on its line.</param>
/// <param name="RequiresRestart">
/// True when the running application cannot take the new value in, and only
/// the next start will.
/// </param>
public sealed record SettingChange(string Key, string JsonValue, bool RequiresRestart);

/// <summary>
/// What a settings window changed, setting by setting.
///
/// <para>Worked out from the JSON of each side rather than property by
/// property, so a setting added to <see cref="AppSettings"/> later is compared
/// and written without this class having to learn about it. The key and the
/// spelling of each value are the serializer's, which is what the file is read
/// with.</para>
/// </summary>
public static class SettingsDiff
{
    /// <summary>
    /// Settings read once, when the application starts. The engine is built
    /// from the model, the provider, the threads and the idle delay; the
    /// recorder takes its guards at construction; the log is opened or not
    /// for the whole session. Everything else is read at the moment it is used,
    /// or re-applied by the tray when saved.
    /// </summary>
    private static readonly HashSet<string> ReadAtStartup = new(StringComparer.Ordinal)
    {
        "modelPath",
        "provider",
        "threads",
        "unloadAfterMinutes",
        "minRecordingMilliseconds",
        "maxRecordingSeconds",
        "logEnabled",
    };

    public static IReadOnlyList<SettingChange> Between(AppSettings before, AppSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        using JsonDocument old = JsonDocument.Parse(before.ToJson());
        using JsonDocument now = JsonDocument.Parse(after.ToJson());

        List<SettingChange> changes = [];

        foreach (JsonProperty property in now.RootElement.EnumerateObject())
        {
            string value = OnOneLine(property.Value);

            bool unchanged = old.RootElement.TryGetProperty(property.Name, out JsonElement previous)
                && OnOneLine(previous) == value;

            if (!unchanged)
            {
                changes.Add(new SettingChange(property.Name, value, ReadAtStartup.Contains(property.Name)));
            }
        }

        return changes;
    }

    /// <summary>
    /// The value on a single line. The serializer indents, which spreads an
    /// array over several lines; the rewrite replaces exactly one, so a
    /// multi-line value would leave the tail of the old one behind.
    /// </summary>
    private static string OnOneLine(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? $"[{string.Join(", ", element.EnumerateArray().Select(OnOneLine))}]"
            : element.GetRawText();
}
