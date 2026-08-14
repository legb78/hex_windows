using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

/// <summary>
/// Une erreur d'échelle ici ne lève aucune exception : le modèle reçoit un
/// signal saturé ou inaudible et rend du texte incohérent. D'où la
/// vérification des bornes plutôt qu'un simple test de forme.
/// </summary>
public class PcmConverterTests
{
    private static byte[] Samples(params short[] values)
    {
        byte[] pcm = new byte[values.Length * 2];

        for (int i = 0; i < values.Length; i++)
        {
            pcm[i * 2] = (byte)values[i];
            pcm[(i * 2) + 1] = (byte)(values[i] >> 8);
        }

        return pcm;
    }

    [Fact]
    public void Le_silence_reste_du_silence()
    {
        Assert.Equal([0f, 0f], PcmConverter.ToNormalizedSamples(Samples(0, 0)));
    }

    [Fact]
    public void Le_pic_negatif_atteint_exactement_moins_un()
    {
        // C'est la raison pour laquelle on divise par 32768 et non 32767 :
        // -32768 est l'amplitude négative maximale d'un entier 16 bits.
        Assert.Equal(-1f, PcmConverter.ToNormalizedSamples(Samples(short.MinValue))[0]);
    }

    [Fact]
    public void Le_pic_positif_reste_dans_les_bornes()
    {
        float value = PcmConverter.ToNormalizedSamples(Samples(short.MaxValue))[0];

        Assert.True(value < 1f);
        Assert.True(value > 0.999f);
    }

    [Fact]
    public void Le_signe_est_conserve()
    {
        float[] samples = PcmConverter.ToNormalizedSamples(Samples(1000, -1000));

        Assert.True(samples[0] > 0);
        Assert.True(samples[1] < 0);
        Assert.Equal(samples[0], -samples[1]);
    }

    [Fact]
    public void La_moitie_de_l_echelle_donne_un_demi()
    {
        Assert.Equal(0.5f, PcmConverter.ToNormalizedSamples(Samples(16_384))[0]);
    }

    [Fact]
    public void Un_octet_final_isole_est_ignore()
    {
        // Tampon tronqué : mieux vaut perdre un demi-échantillon que décaler
        // tout le flux.
        Assert.Single(PcmConverter.ToNormalizedSamples([0, 0, 42]));
    }

    [Fact]
    public void Une_entree_vide_ne_produit_aucun_echantillon()
    {
        Assert.Empty(PcmConverter.ToNormalizedSamples([]));
    }

    [Fact]
    public void L_en_tete_wav_est_ecartee()
    {
        byte[] wav = WavFile.Create(Samples(16_384, -16_384));

        float[] samples = PcmConverter.FromWav(wav);

        Assert.Equal(2, samples.Length);
        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(-0.5f, samples[1]);
    }

    [Fact]
    public void Un_fichier_plus_court_qu_une_en_tete_ne_produit_rien()
    {
        Assert.Empty(PcmConverter.FromWav(new byte[10]));
    }
}
