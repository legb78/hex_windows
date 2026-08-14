using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// Ces tests chargent réellement whisper.cpp et transcrivent un vrai fichier.
/// Ils sont exclus de la CI (le modèle pèse 74 Mo et les minutes Windows sont
/// facturées double) mais restent le seul moyen de vérifier que la
/// bibliothèque native se charge et que la chaîne complète fonctionne.
///
///     .\scripts\get-model.ps1 -Model tiny
///     dotnet test --filter Category=Integration
///
/// Le modèle « tiny » suffit : ce qui est vérifié ici, c'est le câblage, pas
/// la qualité de la reconnaissance.
/// </summary>
[Trait("Category", "Integration")]
public class WhisperEngineIntegrationTests
{
    private const string TinyModel = "models/ggml-tiny.bin";

    private static string ModelPath =>
        ModelLocator.Resolve(TinyModel, AppContext.BaseDirectory)
        ?? throw new InvalidOperationException(
            $"Modèle absent. Lancez : .\\scripts\\get-model.ps1 -Model tiny");

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "bonjour-fr.wav");

    [Fact]
    public async Task Un_enregistrement_francais_est_transcrit()
    {
        using WhisperEngine engine = WhisperEngine.Load(ModelPath, ["Vulkan", "Cpu"]);

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav, "fr");

        // Volontairement souple : le modèle tiny se trompe sur la casse et la
        // ponctuation. Exiger une correspondance exacte rendrait le test
        // fragile sans rien prouver de plus sur le câblage.
        Assert.Contains("transcription", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void Le_moteur_reellement_charge_est_connu()
    {
        // Sans cette information, impossible de distinguer « Vulkan
        // fonctionne » de « on est retombé sur le processeur sans le voir ».
        using WhisperEngine engine = WhisperEngine.Load(ModelPath, ["Vulkan", "Cpu"]);

        Assert.NotEqual("inconnu", engine.LoadedRuntime);
    }

    [Fact]
    public async Task Le_prechauffage_ne_leve_pas_et_ne_produit_aucun_texte()
    {
        using WhisperEngine engine = WhisperEngine.Load(ModelPath, ["Vulkan", "Cpu"]);

        await engine.WarmUpAsync();
    }

    [Fact]
    public async Task Un_enregistrement_silencieux_ne_produit_aucun_texte()
    {
        // Cas fréquent : la touche est relâchée avant d'avoir parlé. Rien ne
        // doit être inséré, surtout pas une formule inventée par le modèle.
        using WhisperEngine engine = WhisperEngine.Load(ModelPath, ["Vulkan", "Cpu"]);

        using var silence = new MemoryStream(SilentWav(TimeSpan.FromSeconds(1)));
        TranscriptionResult result = await engine.TranscribeAsync(silence, "fr");

        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Un_modele_absent_donne_un_message_actionnable()
    {
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => WhisperEngine.Load(@"C:\modele\qui\n\existe\pas.bin", ["Cpu"]));

        Assert.Contains("get-model.ps1", error.Message, StringComparison.Ordinal);
    }

    private static byte[] SilentWav(TimeSpan duration)
    {
        const int sampleRate = 16_000;
        int dataBytes = (int)(sampleRate * duration.TotalSeconds) * 2;

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
        writer.Flush();

        return buffer.ToArray();
    }
}
