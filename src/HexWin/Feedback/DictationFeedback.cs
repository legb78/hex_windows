using System.Diagnostics.CodeAnalysis;
using HexWin.Configuration;
using HexWin.Diagnostics;
using HexWin.Tray;

namespace HexWin.Feedback;

/// <summary>
/// Ties the policy to the two shells that carry it out.
///
/// Nothing is built for a channel the user turned off: no window, no waveform.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Assembles the Windows shells; the decision they follow is tested in FeedbackPolicy.")]
internal sealed class DictationFeedback : IDisposable
{
    private readonly FeedbackPolicy _policy;
    private readonly RecordingOverlay? _overlay;
    private readonly CueTones? _tones;

    public DictationFeedback(AppSettings settings, SessionLog log)
    {
        _policy = new FeedbackPolicy(settings.Feedback);
        _overlay = _policy.ShowsCircle ? new RecordingOverlay(settings) : null;
        _tones = _policy.PlaysTone ? new CueTones(log) : null;
    }

    /// <summary>
    /// True when a tone is actually emitted, and so when the microphone has a
    /// cue of its own to capture.
    /// </summary>
    public bool PlaysTone => _policy.PlaysTone;

    /// <summary>Reflects the state just reached. Called on the interface thread.</summary>
    public void Apply(DictationState state)
    {
        FeedbackCue cue = _policy.Next(state);

        _overlay?.Apply(cue.Overlay);
        _tones?.Play(cue.Tone);
    }

    public void Dispose() => _overlay?.Dispose();
}
