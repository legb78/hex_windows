using HexWin.Configuration;

namespace HexWin.Audio;

/// <summary>
/// Duration bounds for a recording, and the decisions that follow from them.
///
/// Two situations to cover, both of which any push-to-talk user runs into:
///
/// - the accidental tap, too brief to hold speech. Transcribing it would run
///   the engine for nothing and risk inserting a phrase the model invented;
/// - the key left held down — a pocket, another window, a distraction.
///   Without a ceiling, the recording would grow in memory without end.
///
/// Pure logic, so entirely testable without a microphone.
/// </summary>
public readonly record struct RecordingGuards(TimeSpan Minimum, TimeSpan Maximum)
{
    public static RecordingGuards From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new RecordingGuards(
            TimeSpan.FromMilliseconds(settings.MinRecordingMilliseconds),
            TimeSpan.FromSeconds(settings.MaxRecordingSeconds));
    }

    /// <summary>Tap too brief: nothing should be transcribed.</summary>
    public bool IsTooShort(TimeSpan duration) => duration < Minimum;

    /// <summary>Ceiling reached: capture must stop on its own.</summary>
    public bool HasReachedMaximum(TimeSpan elapsed) => elapsed >= Maximum;

    /// <summary>Size past which capture stops accumulating.</summary>
    public long MaximumBytes => RecordingFormat.BytesFor(Maximum);
}
