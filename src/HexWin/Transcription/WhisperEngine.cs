using System.Diagnostics;
using HexWin.Configuration;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace HexWin.Transcription;

/// <summary>Ce qu'une transcription a produit, et à quel prix.</summary>
/// <param name="Text">Texte nettoyé, prêt à être inséré. Vide si rien d'exploitable.</param>
/// <param name="Duration">Temps de calcul, pour mesurer la latence ressentie.</param>
public readonly record struct TranscriptionResult(string Text, TimeSpan Duration);

/// <summary>
/// Transcription locale via Whisper.net (whisper.cpp compilé, sans Python).
///
/// Le modèle est chargé <b>une seule fois</b>, à la construction, et reste en
/// mémoire pour toute la durée de vie de l'application. C'est le point clé de
/// la latence : recharger 1,6 Go à chaque dictée ajouterait plusieurs secondes
/// avant le premier mot. C'est précisément ce que coûterait une approche par
/// sous-processus ou par script relancé à la demande.
/// </summary>
public sealed class WhisperEngine : IDisposable
{
    private readonly WhisperFactory _factory;

    private WhisperEngine(WhisperFactory factory, RuntimeLibrary? loadedRuntime)
    {
        _factory = factory;
        LoadedRuntime = loadedRuntime?.ToString() ?? "inconnu";
    }

    /// <summary>
    /// Moteur de calcul réellement retenu par Whisper.net. À journaliser sans
    /// faute : sans cette information, impossible de distinguer « Vulkan
    /// fonctionne » de « on est retombé sur le processeur sans s'en rendre
    /// compte », les deux transcrivant correctement.
    /// </summary>
    public string LoadedRuntime { get; }

    /// <summary>
    /// Charge le modèle. Opération longue (plusieurs secondes sur un modèle
    /// large) : à lancer au démarrage, en tâche de fond.
    /// </summary>
    /// <exception cref="FileNotFoundException">Le fichier modèle est absent.</exception>
    public static WhisperEngine Load(string modelPath, IEnumerable<string> runtimePreference)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"Modèle introuvable : {modelPath}. Lancez scripts/get-model.ps1 pour le télécharger.",
                modelPath);
        }

        // Doit être positionné avant tout appel natif : une fois une
        // bibliothèque chargée, Whisper.net la conserve pour la suite.
        RuntimeOptions.RuntimeLibraryOrder = [.. ToRuntimeLibraries(runtimePreference)];

        WhisperFactory factory = WhisperFactory.FromPath(modelPath);

        return new WhisperEngine(factory, RuntimeOptions.LoadedLibrary);
    }

    /// <summary>
    /// Transcrit un flux WAV 16 kHz mono. Le flux est lu depuis le début.
    /// </summary>
    public async Task<TranscriptionResult> TranscribeAsync(
        Stream wav,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wav);

        if (wav.CanSeek)
        {
            wav.Position = 0;
        }

        long startedAt = Stopwatch.GetTimestamp();

        // Le processeur est reconstruit à chaque dictée : c'est un objet léger,
        // contrairement au modèle. Cela permet de changer de langue d'une
        // dictée à l'autre sans rien recharger.
        await using WhisperProcessor processor = _factory.CreateBuilder()
            .WithLanguage(language)
            .Build();

        List<string> segments = [];

        await foreach (SegmentData segment in processor.ProcessAsync(wav, cancellationToken))
        {
            segments.Add(segment.Text);
        }

        return new TranscriptionResult(
            TranscriptCleaner.Clean(segments),
            Stopwatch.GetElapsedTime(startedAt));
    }

    /// <summary>
    /// Fait tourner le moteur à vide sur un silence très court.
    ///
    /// Mesuré sur l'Arc Pro 140T : la toute première transcription Vulkan
    /// coûte environ 5 s contre 0,2 s ensuite, le temps que le pilote compile
    /// ses shaders. Sans préchauffage, c'est la première dictée de
    /// l'utilisateur qui paierait cette attente — la plus mauvaise à choisir.
    ///
    /// À appeler au démarrage, en tâche de fond, juste après le chargement.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        using var silence = new MemoryStream(BuildSilentWav(TimeSpan.FromMilliseconds(200)));

        try
        {
            await TranscribeAsync(silence, "fr", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Fermeture pendant le préchauffage : sans conséquence.
        }
    }

    /// <summary>
    /// Construit en mémoire un WAV 16 kHz mono 16 bits entièrement silencieux.
    /// </summary>
    private static byte[] BuildSilentWav(TimeSpan duration)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;

        int sampleCount = (int)(sampleRate * duration.TotalSeconds);
        int dataBytes = sampleCount * channels * (bitsPerSample / 8);

        using var buffer = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(buffer);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                                       // taille du bloc fmt
        writer.Write((short)1);                                 // PCM non compressé
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);                      // le silence lui-même

        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>
    /// Traduit les noms lus dans settings.json en valeurs de Whisper.net.
    /// Les noms inconnus sont déjà écartés par <see cref="AppSettings"/>, qui
    /// garantit également la présence de Cpu en dernier recours.
    /// </summary>
    private static IEnumerable<RuntimeLibrary> ToRuntimeLibraries(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (Enum.TryParse(name, ignoreCase: true, out RuntimeLibrary library))
            {
                yield return library;
            }
        }
    }

    public void Dispose() => _factory.Dispose();
}
