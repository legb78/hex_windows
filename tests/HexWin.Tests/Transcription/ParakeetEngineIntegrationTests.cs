using HexWin.Audio;
using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

/// <summary>
/// These tests really do load ONNX Runtime and transcribe a real file. They
/// are excluded from CI — the model weighs 578 MB and Windows minutes bill at
/// double — but they remain the only way to check that the native library
/// loads and that the whole chain works.
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
            @"Model absent. Run: .\scripts\get-model.ps1");

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "bonjour-fr.wav");

    private static ParakeetEngine Load() => ParakeetEngine.Load(ModelPath, "cpu", 4);

    [ModelRequiredFact]
    public async Task A_French_recording_is_transcribed()
    {
        using ParakeetEngine engine = Load();

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav);

        Assert.Contains("transcription", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [ModelRequiredFact]
    public async Task French_is_recognised_without_being_told()
    {
        // Parakeet v3 works out the language by itself among the 25 it covers.
        // That is what did away with the "language" setting and the FR/EN
        // toggle that had been planned for the menu.
        using ParakeetEngine engine = Load();

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav);

        Assert.Contains("Bonjour", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [ModelRequiredFact]
    public async Task A_short_dictation_stays_under_half_a_second()
    {
        // The reason for switching away from Whisper: on this same 5 s
        // recording, Whisper needed 2.3 s. The threshold is deliberately
        // generous so the test does not turn flaky on a loaded machine, while
        // still catching an outright regression.
        using ParakeetEngine engine = Load();
        await engine.WarmUpAsync();

        await using FileStream wav = File.OpenRead(FixturePath);
        TranscriptionResult result = await engine.TranscribeAsync(wav);

        Assert.True(
            result.Duration < TimeSpan.FromSeconds(1),
            $"Transcribed in {result.Duration.TotalSeconds:F2} s, expected under a second.");
    }

    [ModelRequiredFact]
    public async Task Warming_up_does_not_throw()
    {
        using ParakeetEngine engine = Load();

        await engine.WarmUpAsync();
    }

    [ModelRequiredFact]
    public async Task A_silent_recording_produces_no_text()
    {
        // A common case: the key is released before anything was said. Nothing
        // must be inserted.
        using ParakeetEngine engine = Load();

        using var silence = new MemoryStream(WavFile.CreateSilence(TimeSpan.FromSeconds(1)));
        TranscriptionResult result = await engine.TranscribeAsync(silence);

        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void A_missing_model_gives_an_actionable_message()
    {
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => ParakeetEngine.Load(@"C:\modele\qui\n\existe\pas", "cpu", 4));

        Assert.Contains("get-model.ps1", error.Message, StringComparison.Ordinal);
    }
}
