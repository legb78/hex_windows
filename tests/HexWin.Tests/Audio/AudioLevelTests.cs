using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

public class AudioLevelTests
{
    /// <summary>Construit des échantillons 16 bits signés, petit-boutistes.</summary>
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
    public void Un_signal_nul_donne_un_niveau_nul()
    {
        Assert.Equal(0, AudioLevel.Peak(Samples(0, 0, 0, 0)));
    }

    [Fact]
    public void Un_signal_sature_donne_un_niveau_maximal()
    {
        Assert.Equal(1, AudioLevel.Peak(Samples(short.MaxValue)));
    }

    [Fact]
    public void Le_pic_retenu_est_le_plus_fort_de_l_enregistrement()
    {
        // Une seule pointe suffit : c'est ce qui distingue « quelqu'un a
        // parlé » de « le micro ne capte rien ».
        double peak = AudioLevel.Peak(Samples(10, 20, short.MaxValue, 5));

        Assert.Equal(1, peak);
    }

    [Fact]
    public void Les_valeurs_negatives_comptent_autant_que_les_positives()
    {
        // Un signal audio oscille autour de zéro : ignorer les alternances
        // négatives sous-estimerait le niveau de moitié.
        Assert.Equal(AudioLevel.Peak(Samples(short.MaxValue)), AudioLevel.Peak(Samples(-short.MaxValue)));
    }

    [Fact]
    public void La_borne_negative_extreme_ne_fait_pas_deborder()
    {
        // short.MinValue vaut -32768, dont l'opposé n'est pas représentable
        // sur 16 bits : un Math.Abs naïf y lève une exception.
        double peak = AudioLevel.Peak(Samples(short.MinValue));

        Assert.Equal(1, peak);
    }

    [Fact]
    public void Un_enregistrement_vide_donne_un_niveau_nul()
    {
        Assert.Equal(0, AudioLevel.Peak([]));
    }

    [Fact]
    public void Un_octet_isole_est_ignore_plutot_que_mal_interprete()
    {
        // Un échantillon incomplet en fin de tampon ne doit pas être lu comme
        // une valeur arbitraire.
        Assert.Equal(0, AudioLevel.Peak([42]));
    }

    [Fact]
    public void Un_micro_muet_est_reconnu_comme_silencieux()
    {
        Assert.True(AudioLevel.IsSilent(Samples(0, 1, 2, 0)));
    }

    [Fact]
    public void Une_voix_normale_n_est_pas_prise_pour_du_silence()
    {
        // 10 % de l'échelle : très en dessous d'une parole normale, et déjà
        // dix fois au-dessus du seuil.
        Assert.False(AudioLevel.IsSilent(Samples((short)(short.MaxValue / 10))));
    }
}
