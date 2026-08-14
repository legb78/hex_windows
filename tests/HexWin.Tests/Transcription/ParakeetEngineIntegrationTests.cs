using HexWin.Audio;
using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// Ces tests chargent réellement ONNX Runtime et transcrivent un vrai fichier.
/// Ils sont exclus de la CI — le modèle pèse 578 Mo et les minutes Windows
/// sont facturées double — mais restent le seul moyen de vérifier que la
/// bibliothèque native se charge et que la chaîne complète fonctionne.
///
///     .\scripts\get-model.ps1
///     dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public class ParakeetEngineIntegrationTests
{
    private const string ModelDirectory = "models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";

    private static string ModelPath =>
        ModelLocator.Resolve(ModelDirectory, AppContext.BaseDirectory)
        ?? throw new InvalidOperationException(
            @"Modèle absent. Lancez : .\scripts\get-model.ps1");

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "bonjour-fr.wav");

    private static ParakeetEngine Load() => ParakeetEngine.Load(ModelPath, "cpu", 4);

    [Fact]
    public async Task Un_enregistrement_francais_est_transcrit()
    {
        using ParakeetEngine engine = Load();

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav);

        Assert.Contains("transcription", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task Le_francais_est_reconnu_sans_qu_on_ait_a_le_preciser()
    {
        // Parakeet v3 identifie seul la langue parmi les 25 qu'il couvre.
        // C'est ce qui a fait disparaître le réglage « langue » et la bascule
        // FR/EN qui était prévue au menu.
        using ParakeetEngine engine = Load();

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav);

        Assert.Contains("Bonjour", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Une_dictee_courte_reste_sous_la_demi_seconde()
    {
        // La raison d'être de la bascule depuis Whisper : sur ce même
        // enregistrement de 5 s, Whisper demandait 2,3 s. Ce seuil est
        // volontairement large pour ne pas rendre le test instable sur une
        // machine chargée, tout en détectant une régression franche.
        using ParakeetEngine engine = Load();
        await engine.WarmUpAsync();

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav);

        Assert.True(
            result.Duration < TimeSpan.FromSeconds(1),
            $"Transcription en {result.Duration.TotalSeconds:F2} s, attendu moins d'une seconde.");
    }

    [Fact]
    public async Task Le_prechauffage_ne_leve_pas()
    {
        using ParakeetEngine engine = Load();

        await engine.WarmUpAsync();
    }

    [Fact]
    public async Task Un_enregistrement_silencieux_ne_produit_aucun_texte()
    {
        // Cas fréquent : la touche est relâchée avant d'avoir parlé. Rien ne
        // doit être inséré.
        using ParakeetEngine engine = Load();

        using var silence = new MemoryStream(WavFile.CreateSilence(TimeSpan.FromSeconds(1)));
        TranscriptionResult result = await engine.TranscribeAsync(silence);

        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void Un_modele_absent_donne_un_message_actionnable()
    {
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => ParakeetEngine.Load(@"C:\modele\qui\n\existe\pas", "cpu", 4));

        Assert.Contains("get-model.ps1", error.Message, StringComparison.Ordinal);
    }
}
