using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using HexWin.Audio;
using SherpaOnnx;

namespace HexWin.Transcription;

/// <summary>Ce qu'une transcription a produit, et à quel prix.</summary>
/// <param name="Text">Texte nettoyé, prêt à être inséré. Vide si rien d'exploitable.</param>
/// <param name="Duration">Temps de calcul, pour mesurer la latence ressentie.</param>
public readonly record struct TranscriptionResult(string Text, TimeSpan Duration);

/// <summary>
/// Reconnaissance vocale locale par Parakeet TDT v3 (NVIDIA), exécuté via
/// sherpa-onnx et ONNX Runtime.
///
/// C'est le moteur qu'utilise Hex sur macOS. Le choix tient à son
/// architecture : Parakeet est un <i>transducteur</i>, là où Whisper est un
/// encodeur-décodeur autorégressif qui produit son texte token par token.
/// Ce décodage séquentiel impose un coût fixe par transcription — mesuré à
/// 1,4 s sur cette machine — indépendant de la longueur de l'enregistrement,
/// et donc particulièrement pénalisant sur les dictées courtes, qui sont le
/// cas d'usage normal.
///
/// Comme pour Whisper, le modèle est chargé une seule fois et reste résident.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Couverte par les tests d'intégration, qui chargent le moteur natif et sont exclus de la CI.")]
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
    /// Fournisseur ONNX Runtime réellement demandé. Journalisé pour la même
    /// raison que pour Whisper : un repli silencieux sur le processeur
    /// transcrit tout aussi correctement, seule la durée trahit la différence.
    /// </summary>
    public string LoadedRuntime { get; }

    /// <summary>
    /// Charge le modèle depuis son dossier. Contrairement à Whisper, Parakeet
    /// se présente en plusieurs fichiers — encodeur, décodeur, joiner et
    /// vocabulaire — d'où un dossier plutôt qu'un fichier unique.
    /// </summary>
    /// <exception cref="FileNotFoundException">Un fichier du modèle manque.</exception>
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
    /// Transcrit un flux WAV 16 kHz mono.
    ///
    /// Aucune langue n'est à préciser : Parakeet v3 reconnaît seul laquelle de
    /// ses 25 langues européennes est parlée. Le réglage « langue » de la
    /// configuration disparaît donc, ainsi que la bascule FR/EN qui était
    /// prévue dans le menu.
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

        // Le décodage est synchrone et gourmand en calcul : le sortir du fil
        // appelant évite de figer l'interface pendant la transcription.
        return await Task.Run(() => Decode(samples), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fait tourner le moteur à vide sur un silence très court, pour absorber
    /// le coût de la première inférence : allocation des tampons ONNX et
    /// choix des noyaux de calcul. Sinon, c'est la première dictée de
    /// l'utilisateur qui le paierait.
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
            // Fermeture pendant le préchauffage : sans conséquence.
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
