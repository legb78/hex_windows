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
/// A deliberately thin shell around NAudio: it opens the device and
/// accumulates samples. The decisions — too short, ceiling reached — belong
/// to <see cref="RecordingGuards"/>, which is testable without a microphone.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "NAudio shell: requires a physical microphone.")]
public sealed class AudioRecorder : IDisposable
{
    private readonly RecordingGuards _guards;
    private readonly Lock _sync = new();

    private WaveInEvent? _device;
    private MemoryStream? _samples;
    private bool _maximumReached;

    public AudioRecorder(RecordingGuards guards) => _guards = guards;

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
            _samples = new MemoryStream();

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
    /// Closes the microphone and returns the recording, or <c>null</c> if the
    /// press was too brief to hold speech.
    /// </summary>
    public RecordedAudio? Stop()
    {
        MemoryStream? samples;

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

            samples = _samples;
            _samples = null;
        }

        if (samples is null)
        {
            return null;
        }

        using (samples)
        {
            // Duration comes from the samples actually received, not from the
            // clock: this is what the engine will hear.
            TimeSpan duration = RecordingFormat.DurationOf(samples.Length);

            if (_guards.IsTooShort(duration))
            {
                return null;
            }

            return new RecordedAudio(WavFile.Create(samples.GetBuffer().AsSpan(0, (int)samples.Length)), duration);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        bool reachedMaximum = false;

        lock (_sync)
        {
            if (_samples is null)
            {
                return;
            }

            long room = _guards.MaximumBytes - _samples.Length;

            if (room <= 0)
            {
                return;
            }

            int toWrite = (int)Math.Min(e.BytesRecorded, room);
            _samples.Write(e.Buffer, 0, toWrite);

            if (_samples.Length >= _guards.MaximumBytes && !_maximumReached)
            {
                _maximumReached = true;
                reachedMaximum = true;
            }
        }

        // Outside the lock: the subscriber may want to call Stop().
        if (reachedMaximum)
        {
            MaximumReached?.Invoke(this, EventArgs.Empty);
        }
    }

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

            _samples?.Dispose();
            _samples = null;
        }
    }
}
