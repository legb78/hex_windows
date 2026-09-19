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
    private readonly AppSettings _settings;
    private readonly SessionLog _log;

    private RecordingOverlay? _overlay;
    private CueTones? _tones;

    public DictationFeedback(AppSettings settings, SessionLog log)
    {
        _settings = settings;
        _log = log;
        _policy = new FeedbackPolicy(settings.Feedback);
        _overlay = _policy.ShowsCircle ? new RecordingOverlay(settings) : null;
        _tones = _policy.PlaysTone ? new CueTones(log) : null;
    }

    /// <summary>
    /// True when a tone is actually emitted, and so when the microphone has a
    /// cue of its own to capture.
    /// </summary>
    public bool PlaysTone => _policy.PlaysTone;

    /// <summary>True when the circle is currently asked for.</summary>
    public bool ShowsCircle => _policy.ShowsCircle;

    /// <summary>The two channels as the single mode settings.json stores.</summary>
    public FeedbackMode Mode => _policy.Mode;

    /// <summary>
    /// Turns the circle on or off while the application runs. Called on the
    /// interface thread: the overlay is a window, and creating or destroying
    /// one anywhere else is not allowed.
    ///
    /// <para>Switching off disposes the window rather than hiding it, so that
    /// a channel the user turned off costs nothing — the same rule the
    /// constructor follows.</para>
    /// </summary>
    public void SetShowsCircle(bool enabled)
    {
        if (_policy.ShowsCircle == enabled)
        {
            return;
        }

        _policy.ShowsCircle = enabled;

        if (enabled)
        {
            _overlay = new RecordingOverlay(_settings);
            return;
        }

        // Hide before disposing: a recording in progress leaves the circle on
        // screen, and disposing a visible layered window flashes it.
        _overlay?.Apply(null);
        _overlay?.Dispose();
        _overlay = null;
    }

    /// <summary>
    /// Turns the tones on or off while the application runs.
    ///
    /// <para>This one also moves the microphone's start: TrayContext delays
    /// capture by a lead time when a tone is played, so the tone is not
    /// recorded. Reading <see cref="PlaysTone"/> at each dictation keeps the
    /// two in step.</para>
    /// </summary>
    public void SetPlaysTone(bool enabled)
    {
        if (_policy.PlaysTone == enabled)
        {
            return;
        }

        _policy.PlaysTone = enabled;
        _tones = enabled ? new CueTones(_log) : null;
    }

    /// <summary>Reflects the state just reached. Called on the interface thread.</summary>
    public void Apply(DictationState state)
    {
        FeedbackCue cue = _policy.Next(state);

        _overlay?.Apply(cue.Overlay);
        _tones?.Play(cue.Tone);
    }

    public void Dispose() => _overlay?.Dispose();
}
