using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

public class IdlePolicyTests
{
    private static readonly IdlePolicy FiveMinutes = IdlePolicy.FromMinutes(5);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void Un_delai_nul_ou_negatif_garde_le_modele_resident(int minutes)
    {
        // C'est ainsi qu'on désactive la libération : le modèle reste chargé,
        // et la réponse reste instantanée en toutes circonstances.
        IdlePolicy policy = IdlePolicy.FromMinutes(minutes);

        Assert.False(policy.IsEnabled);
        Assert.False(policy.ShouldUnload(TimeSpan.FromHours(10), isBusy: false));
    }

    [Fact]
    public void Un_delai_positif_active_la_liberation()
    {
        Assert.True(FiveMinutes.IsEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(299)]
    public void En_deca_du_delai_le_modele_reste_charge(int seconds)
    {
        Assert.False(FiveMinutes.ShouldUnload(TimeSpan.FromSeconds(seconds), isBusy: false));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(86_400)]
    public void Au_dela_du_delai_le_modele_est_libere(int seconds)
    {
        Assert.True(FiveMinutes.ShouldUnload(TimeSpan.FromSeconds(seconds), isBusy: false));
    }

    [Fact]
    public void La_borne_du_delai_est_inclusive()
    {
        Assert.True(FiveMinutes.ShouldUnload(TimeSpan.FromMinutes(5), isBusy: false));
    }

    [Fact]
    public void Une_dictee_en_cours_interdit_toute_liberation()
    {
        // Le délai peut tomber pile pendant que l'utilisateur parle. Libérer
        // à cet instant ferait échouer la transcription qu'il attend — le
        // pire moment possible.
        Assert.False(FiveMinutes.ShouldUnload(TimeSpan.FromHours(1), isBusy: true));
    }

    [Fact]
    public void La_liberation_reprend_une_fois_la_dictee_terminee()
    {
        Assert.True(FiveMinutes.ShouldUnload(TimeSpan.FromHours(1), isBusy: false));
    }

    [Fact]
    public void Le_delai_est_exprime_en_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), FiveMinutes.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(30), IdlePolicy.FromMinutes(30).Timeout);
    }
}
