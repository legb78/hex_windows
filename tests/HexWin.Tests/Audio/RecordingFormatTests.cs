using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

public class RecordingFormatTests
{
    [Fact]
    public void Le_format_est_celui_qu_exige_le_modele()
    {
        // Ces trois valeurs ne sont pas un choix : le modèle n'accepte que
        // du 16 kHz mono 16 bits. Les changer casserait la transcription.
        Assert.Equal(16_000, RecordingFormat.SampleRate);
        Assert.Equal(1, RecordingFormat.Channels);
        Assert.Equal(16, RecordingFormat.BitsPerSample);
        Assert.Equal(32_000, RecordingFormat.BytesPerSecond);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(32_000, 1_000)]
    [InlineData(16_000, 500)]
    [InlineData(3_200, 100)]
    public void Une_quantite_d_echantillons_se_traduit_en_duree(long bytes, int expectedMilliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            RecordingFormat.DurationOf(bytes));
    }

    [Theory]
    [InlineData(1_000, 32_000)]
    [InlineData(500, 16_000)]
    [InlineData(0, 0)]
    public void Une_duree_se_traduit_en_quantite_d_echantillons(int milliseconds, long expectedBytes)
    {
        Assert.Equal(
            expectedBytes,
            RecordingFormat.BytesFor(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void La_conversion_tombe_toujours_sur_un_echantillon_complet()
    {
        // Un échantillon 16 bits occupe deux octets. En couper un en deux
        // décalerait tout le flux suivant d'un octet, et le signal
        // deviendrait du bruit.
        long bytes = RecordingFormat.BytesFor(TimeSpan.FromMilliseconds(1.5));

        Assert.Equal(0, bytes % RecordingFormat.BytesPerSample);
    }

    [Fact]
    public void Un_aller_retour_conserve_la_duree()
    {
        var original = TimeSpan.FromSeconds(2.5);

        Assert.Equal(original, RecordingFormat.DurationOf(RecordingFormat.BytesFor(original)));
    }

    [Fact]
    public void Les_valeurs_negatives_sont_refusees()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingFormat.DurationOf(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingFormat.BytesFor(TimeSpan.FromSeconds(-1)));
    }
}
