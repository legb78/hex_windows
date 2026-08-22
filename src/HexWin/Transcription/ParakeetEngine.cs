using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using HexWin.Audio;
using SherpaOnnx;

namespace HexWin.Transcription;

/// <summary>What a transcription produced, and at what cost.</summary>
/// <param name="Text">Cleaned text, ready to insert. Empty if nothing usable.</param>
/// <param name="Duration">Compute time, to measure the latency as felt.</param>
public readonly record struct TranscriptionResult(string Text, TimeSpan Duration);

/// <summary>
/// Local speech recognition by Parakeet TDT v3 (NVIDIA), run through
/// sherpa-onnx and ONNX Runtime.
///
/// This is the engine Hex uses on macOS. The choice comes down to its
/// architecture: Parakeet is a <i>transducer</i>, where Whisper is an
/// autoregressive encoder-decoder producing its text token by token. That
/// sequential decoding imposes a fixed cost per transcription — measured at
/// 1.4 s on this machine — independent of the length of the recording, and so
/// especially punishing on short dictations, which are the normal use.
///
/// As with Whisper, the model is loaded once and stays resident.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Covered by the integration tests, which load the native engine and are excluded from CI.")]
public sealed class ParakeetEngine : IDisposable
{
    private const int FeatureDimension = 80;

    private readonly OfflineRecognizer _recognizer;

    private ParakeetEngine(OfflineRecognizer recognizer, string provider)
    {
        _recognizer = recognizer;
        LoadedRuntime = provider;
    }

    /// <summary>
    /// ONNX Runtime provider actually requested. Logged for the same reason as
    /// with Whisper: a silent fallback to the processor transcribes just as
    /// correctly, and only the duration betrays the difference.
    /// </summary>
    public string LoadedRuntime { get; }

    /// <summary>
    /// Checks that the model is complete, without loading anything into
    /// memory.
    ///
    /// Lets a missing or incomplete model be reported at startup, including
    /// when loading itself is deferred to the first dictation: discovering the
    /// problem while the user is speaking would be the worst possible moment,
    /// their sentence being already lost by then.
    /// </summary>
    /// <exception cref="FileNotFoundException">A model file is missing.</exception>
    public static void Validate(string modelDirectory)
    {
        foreach (string file in RequiredFiles)
        {
            RequireFile(modelDirectory, file);
        }
    }

    private static readonly string[] RequiredFiles =
        ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

    /// <summary>
    /// Loads the model from its folder. Unlike Whisper, Parakeet comes as
    /// several files — encoder, decoder, joiner and vocabulary — hence a
    /// folder rather than a single file.
    /// </summary>
    /// <exception cref="FileNotFoundException">A model file is missing.</exception>
    public static ParakeetEngine Load(string modelDirectory, string provider, int threads)
    {
        string encoder = RequireFile(modelDirectory, "encoder.int8.onnx");
        string decoder = RequireFile(modelDirectory, "decoder.int8.onnx");
        string joiner = RequireFile(modelDirectory, "joiner.int8.onnx");
        string tokens = RequireFile(modelDirectory, "tokens.txt");

        var config = new OfflineRecognizerConfig();

        config.FeatConfig.SampleRate = RecordingFormat.SampleRate;
        config.FeatConfig.FeatureDim = FeatureDimension;

        config.ModelConfig.Transducer.Encoder = encoder;
        config.ModelConfig.Transducer.Decoder = decoder;
        config.ModelConfig.Transducer.Joiner = joiner;
        config.ModelConfig.Tokens = tokens;
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.Provider = provider;
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Debug = 0;

        config.DecodingMethod = "greedy_search";

        return new ParakeetEngine(new OfflineRecognizer(config), provider);
    }

    /// <summary>
    /// Transcribes a 16 kHz mono WAV stream.
    ///
    /// No language needs to be given: Parakeet v3 works out on its own which
    /// of its 25 European languages is being spoken. The "language" setting
    /// therefore disappears from the configuration, along with the FR/EN
    /// toggle that had been planned for the menu.
    /// </summary>
    public async Task<TranscriptionResult> TranscribeAsync(
        Stream wav,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wav);

        if (wav.CanSeek)
        {
            wav.Position = 0;
        }

        using var buffer = new MemoryStream();
        await wav.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        float[] samples = PcmConverter.FromWav(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));

        // Decoding is synchronous and compute-hungry: moving it off the
        // calling thread keeps the interface from freezing during
        // transcription.
        return await Task.Run(() => Decode(samples), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the engine on a very short silence, to absorb the cost of the
    /// first inference: allocating the ONNX buffers and picking the compute
    /// kernels. Otherwise the first dictation of the user would pay for it.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        using var silence = new MemoryStream(WavFile.CreateSilence(TimeSpan.FromMilliseconds(200)));

        try
        {
            await TranscribeAsync(silence, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down during warm-up: no consequence.
        }
    }

    private TranscriptionResult Decode(float[] samples)
    {
        long startedAt = Stopwatch.GetTimestamp();

        using OfflineStream stream = _recognizer.CreateStream();
        stream.AcceptWaveform(RecordingFormat.SampleRate, samples);

        _recognizer.Decode(stream);

        return new TranscriptionResult(
            TranscriptCleaner.Clean(stream.Result.Text),
            Stopwatch.GetElapsedTime(startedAt));
    }

    private static string RequireFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Fichier de modèle manquant : {path}. "
                + "Lancez scripts/get-model.ps1 pour télécharger le modèle complet.",
                path);
        }

        return path;
    }

    public void Dispose() => _recognizer.Dispose();
}
