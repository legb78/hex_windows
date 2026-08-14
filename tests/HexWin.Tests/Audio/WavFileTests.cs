using System.Text;
using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

/// <summary>
/// L'en-tête est vérifiée octet par octet, parce qu'une erreur ici ne provoque
/// aucune exception : le moteur charge le fichier et transcrit du bruit.
/// Le symptôme serait « la dictée rend n'importe quoi », très loin de la cause.
/// </summary>
public class WavFileTests
{
    private static readonly byte[] Pcm = [1, 2, 3, 4, 5, 6, 7, 8];

    private static string Ascii(byte[] file, int offset, int length) =>
        Encoding.ASCII.GetString(file, offset, length);

    private static int Int32At(byte[] file, int offset) => BitConverter.ToInt32(file, offset);

    private static short Int16At(byte[] file, int offset) => BitConverter.ToInt16(file, offset);

    [Fact]
    public void Le_fichier_commence_par_la_signature_RIFF_WAVE()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal("RIFF", Ascii(file, 0, 4));
        Assert.Equal("WAVE", Ascii(file, 8, 4));
        Assert.Equal("fmt ", Ascii(file, 12, 4));
        Assert.Equal("data", Ascii(file, 36, 4));
    }

    [Fact]
    public void La_taille_RIFF_vaut_la_taille_totale_moins_huit()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(file.Length - 8, Int32At(file, 4));
    }

    [Fact]
    public void Le_bloc_data_annonce_la_taille_reelle_des_echantillons()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(Pcm.Length, Int32At(file, 40));
    }

    [Fact]
    public void Le_format_annonce_est_celui_qu_attend_le_moteur()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(16, Int32At(file, 16));                          // taille du bloc fmt
        Assert.Equal(1, Int16At(file, 20));                           // PCM non compressé
        Assert.Equal(RecordingFormat.Channels, Int16At(file, 22));    // mono
        Assert.Equal(RecordingFormat.SampleRate, Int32At(file, 24));  // 16 kHz
        Assert.Equal(RecordingFormat.BitsPerSample, Int16At(file, 34));
    }

    [Fact]
    public void Le_debit_et_l_alignement_sont_coherents_avec_le_format()
    {
        // Ces deux champs sont redondants avec les précédents. Un lecteur qui
        // s'y fie lirait le flux à la mauvaise vitesse s'ils étaient faux.
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(RecordingFormat.BytesPerSecond, Int32At(file, 28));
        Assert.Equal(RecordingFormat.Channels * RecordingFormat.BytesPerSample, Int16At(file, 32));
    }

    [Fact]
    public void Les_echantillons_suivent_l_en_tete_sans_alteration()
    {
        byte[] file = WavFile.Create(Pcm);

        Assert.Equal(WavFile.HeaderSize + Pcm.Length, file.Length);
        Assert.Equal(Pcm, file[WavFile.HeaderSize..]);
    }

    [Fact]
    public void Un_enregistrement_vide_produit_une_en_tete_seule_et_valide()
    {
        byte[] file = WavFile.Create([]);

        Assert.Equal(WavFile.HeaderSize, file.Length);
        Assert.Equal("RIFF", Ascii(file, 0, 4));
        Assert.Equal(0, Int32At(file, 40));
    }

    [Fact]
    public void Le_silence_dure_le_temps_demande()
    {
        byte[] file = WavFile.CreateSilence(TimeSpan.FromSeconds(1));

        Assert.Equal(RecordingFormat.BytesPerSecond, Int32At(file, 40));
        Assert.All(file[WavFile.HeaderSize..], sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void Une_zone_trop_petite_pour_l_en_tete_est_refusee()
    {
        Assert.Throws<ArgumentException>(() => WavFile.WriteHeader(new byte[10], 0));
    }
}
