using System.Diagnostics.CodeAnalysis;
using NAudio.Wave;

namespace HexWin.Audio;

/// <summary>A recording that was kept, ready to be transcribed.</summary>
/// <param name="Wav">Complete WAV file, header included.</param>
/// <param name="Duration">Real duration, derived from the samples received.</param>
public readonly record struct RecordedAudio(byte[] Wav, TimeSpan Duration);

/// <summary>
/// Captures the default Windows microphone, straight into the format the
/// recognition engine expects.
///
/// A deliberately thin shell around NAudio: it opens the device and hands the
/// samples to a <see cref="SpeechSegmenter"/>. The decisions — too short,
/// ceiling reached, pause long enough to cut — belong to
/// <see cref="RecordingGuards"/> and to the segmenter, both testable without a
/// microphone.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "NAudio shell: requires a physical microphone.")]
public sealed class AudioRecorder : IDisposable
{
    private readonly RecordingGuards _guards;
    private readonly Lock _sync = new();

    private WaveInEvent? _device;
    private SpeechSegmenter? _segmenter;
    private long _received;
    private bool _maximumReached;

    /// <param name="pause">Silence that closes a segment; zero to never cut.</param>
    public AudioRecorder(RecordingGuards guards, TimeSpan pause)
    {
        _guards = guards;
        Pause = pause;
    }

    /// <summary>
    /// Silence that closes a segment; zero to never cut. Read when a recording
    /// starts, so a new value saved from the settings window applies from the
    /// next dictation, and never halfway through one.
    /// </summary>
    public TimeSpan Pause { get; set; }

    /// <summary>
    /// Raised, on the capture thread, each time a pause closes a segment
    /// while the recording goes on. The last segment comes out of
    /// <see cref="Stop"/> instead.
    /// </summary>
    public event EventHandler<RecordedAudio>? SegmentReady;

    /// <summary>
    /// Raised when capture stopped by itself, having hit the ceiling. Lets the
    /// interface stop showing a recording in progress.
    /// </summary>
    public event EventHandler? MaximumReached;

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _device is not null;
            }
        }
    }

    /// <summary>
    /// Opens the microphone and starts accumulating. Does nothing if a
    /// recording is already running: keyboard auto-repeat fires calls in
    /// bursts, and those must not restart from zero on every keystroke.
    /// </summary>
    /// <exception cref="InvalidOperationException">No microphone available.</exception>
    public void Start()
    {
        lock (_sync)
        {
            if (_device is not null)
            {
                return;
            }

            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException(
                    "Aucun microphone détecté. Vérifiez qu'un périphérique d'entrée est "
                    + "branché et autorisé dans Paramètres > Confidentialité > Microphone.");
            }

            _maximumReached = false;
            _received = 0;
            _segmenter = new SpeechSegmenter(Pause);

            var device = new WaveInEvent
            {
                WaveFormat = new WaveFormat(
                    RecordingFormat.SampleRate,
                    RecordingFormat.BitsPerSample,
                    RecordingFormat.Channels),

                // Short buffers keep stopping responsive: when the key is
                // released, the worst wait before transcribing is the end of
                // the current buffer.
                BufferMilliseconds = 50,
            };

            device.DataAvailable += OnDataAvailable;
            device.StartRecording();

            _device = device;
        }
    }

    /// <summary>
    /// Closes the microphone and returns what was not yet cut, or <c>null</c>
    /// if the press was too brief to hold speech or nothing worth transcribing
    /// is left.
    /// </summary>
    public RecordedAudio? Stop()
    {
        SpeechSegmenter? segmenter;
        long received;

        lock (_sync)
        {
            if (_device is null)
            {
                return null;
            }

            _device.DataAvailable -= OnDataAvailable;
            _device.StopRecording();
            _device.Dispose();
            _device = null;

            segmenter = _segmenter;
            received = _received;
            _segmenter = null;
        }

        if (segmenter is null)
        {
            return null;
        }

        // Duration comes from the samples actually received, not from the
        // clock: this is what the engine will hear.
        if (_guards.IsTooShort(RecordingFormat.DurationOf(received)))
        {
            return null;
        }

        return segmenter.Flush() is { } remainder ? Wrap(remainder) : null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        bool reachedMaximum = false;
        byte[]? closed = null;

        lock (_sync)
        {
            if (_segmenter is null)
            {
                return;
            }

            long room = _guards.MaximumBytes - _received;

            if (room <= 0)
            {
                return;
            }

            int toWrite = (int)Math.Min(e.BytesRecorded, room);
            closed = _segmenter.Push(e.Buffer.AsSpan(0, toWrite));
            _received += toWrite;

            if (_received >= _guards.MaximumBytes && !_maximumReached)
            {
                _maximumReached = true;
                reachedMaximum = true;
            }
        }

        // Outside the lock: the subscribers may want to call Stop().
        if (closed is not null)
        {
            SegmentReady?.Invoke(this, Wrap(closed));
        }

        if (reachedMaximum)
        {
            MaximumReached?.Invoke(this, EventArgs.Empty);
        }
    }

    private static RecordedAudio Wrap(byte[] pcm) =>
        new(WavFile.Create(pcm), RecordingFormat.DurationOf(pcm.Length));

    public void Dispose()
    {
        lock (_sync)
        {
            if (_device is not null)
            {
                _device.DataAvailable -= OnDataAvailable;
                _device.StopRecording();
                _device.Dispose();
                _device = null;
            }

            _segmenter = null;
        }
    }
}
