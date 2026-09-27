using System.Diagnostics.CodeAnalysis;
using SherpaOnnx;

namespace HexWin.Audio;

/// <summary>
/// Speech detection by Silero VAD, a 650 KB neural network shipped with
/// sherpa-onnx.
///
/// <para>It recognises a voice, where the level threshold only measures
/// loudness: a fan, a keyboard or a busy street stay "silence", so the pauses
/// between sentences are found in a noisy room too, and a cough or a click too
/// short to be speech does not reopen a segment.</para>
///
/// <para>Only the detector's verdict is used, not the segments it assembles:
/// cutting stays the job of <see cref="SpeechSegmenter"/>, whose pause is the
/// one the user chose.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Shell around the native detector; the cutting it feeds is tested with SpeechSegmenter.")]
public sealed class SileroSpeechDetector : ISpeechDetector, IDisposable
{
    public const string FileName = "silero_vad.onnx";

    private readonly VoiceActivityDetector _detector;

    public SileroSpeechDetector(string modelFile)
    {
        var config = new VadModelConfig();

        config.SileroVad.Model = modelFile;
        config.SileroVad.Threshold = 0.5f;

        // The detector's own pause is kept short: the one that closes a
        // segment is measured by SpeechSegmenter, on top of this one.
        config.SileroVad.MinSilenceDuration = 0.1f;

        // Shorter than this is a click or a cough, not a word.
        config.SileroVad.MinSpeechDuration = 0.2f;

        config.SileroVad.WindowSize = 512;
        config.SampleRate = RecordingFormat.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";

        _detector = new VoiceActivityDetector(config, bufferSizeInSeconds: 30);
    }

    public bool IsSpeech(ReadOnlySpan<byte> pcm)
    {
        _detector.AcceptWaveform(PcmConverter.ToNormalizedSamples(pcm));
        bool detected = _detector.IsSpeechDetected();

        // The detector queues the segments it assembles; nobody reads them,
        // so they are dropped before they pile up.
        _detector.Clear();

        return detected;
    }

    public void Reset() => _detector.Reset();

    public void Dispose() => _detector.Dispose();
}
