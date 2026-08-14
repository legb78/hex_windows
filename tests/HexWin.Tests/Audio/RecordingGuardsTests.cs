using HexWin.Audio;
using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Audio;

public class RecordingGuardsTests
{
    private static readonly RecordingGuards Guards = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(120));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(249)]
    public void Un_appui_accidentel_est_ecarte(int milliseconds)
    {
        // Sans ce garde-fou, un effleurement ferait tourner le moteur pour
        // rien — et le modèle inventerait volontiers une phrase sur du vide.
        Assert.True(Guards.IsTooShort(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(250)]
    [InlineData(251)]
    [InlineData(5_000)]
    public void Un_appui_assez_long_est_retenu(int milliseconds)
    {
        Assert.False(Guards.IsTooShort(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void La_borne_minimale_est_inclusive()
    {
        // Exactement à la limite, on garde : refuser serait plus surprenant.
        Assert.False(Guards.IsTooShort(TimeSpan.FromMilliseconds(250)));
    }

    [Theory]
    [InlineData(119)]
    [InlineData(0)]
    public void En_deca_du_plafond_la_capture_continue(int seconds)
    {
        Assert.False(Guards.HasReachedMaximum(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(121)]
    [InlineData(3_600)]
    public void Au_plafond_la_capture_doit_s_arreter(int seconds)
    {
        // Touche restée enfoncée dans une poche : sans plafond, la mémoire
        // grossirait sans fin.
        Assert.True(Guards.HasReachedMaximum(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Le_plafond_se_traduit_en_taille_exploitable()
    {
        Assert.Equal(120L * RecordingFormat.BytesPerSecond, Guards.MaximumBytes);
    }

    [Fact]
    public void Les_bornes_proviennent_de_la_configuration()
    {
        AppSettings settings = AppSettings.Parse(
            """{"minRecordingMilliseconds": 400, "maxRecordingSeconds": 30}""");

        RecordingGuards guards = RecordingGuards.From(settings);

        Assert.Equal(TimeSpan.FromMilliseconds(400), guards.Minimum);
        Assert.Equal(TimeSpan.FromSeconds(30), guards.Maximum);
    }

    [Fact]
    public void Une_duree_minimale_nulle_accepte_tout()
    {
        // Configuration limite, mais légitime : certains veulent dicter des
        // mots isolés très courts.
        var permissive = new RecordingGuards(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        Assert.False(permissive.IsTooShort(TimeSpan.Zero));
    }
}
