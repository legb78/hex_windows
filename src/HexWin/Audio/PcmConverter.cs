namespace HexWin.Audio;

/// <summary>
/// Convertit les échantillons capturés vers la représentation attendue par
/// le moteur de reconnaissance.
///
/// Le micro rend des entiers 16 bits signés ; ONNX Runtime attend des
/// flottants normalisés entre -1 et 1. Une erreur d'échelle ici ne provoque
/// aucune exception : le modèle reçoit un signal saturé ou inaudible et rend
/// du texte incohérent. D'où les tests sur les bornes.
/// </summary>
public static class PcmConverter
{
    /// <summary>
    /// Diviseur de normalisation. On utilise 32768 (et non 32767) parce que
    /// c'est l'amplitude négative maximale d'un entier 16 bits signé :
    /// diviser par 32767 ferait légèrement dépasser -1 sur le pic négatif.
    /// </summary>
    private const float FullScale = 32768f;

    /// <summary>
    /// Convertit des échantillons 16 bits signés petit-boutistes en flottants
    /// normalisés. Un octet final isolé — tampon tronqué — est ignoré.
    /// </summary>
    public static float[] ToNormalizedSamples(ReadOnlySpan<byte> pcm)
    {
        int sampleCount = pcm.Length / RecordingFormat.BytesPerSample;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = i * RecordingFormat.BytesPerSample;
            short value = (short)(pcm[offset] | (pcm[offset + 1] << 8));

            samples[i] = value / FullScale;
        }

        return samples;
    }

    /// <summary>
    /// Variante prenant un fichier WAV complet : l'en-tête est écartée avant
    /// conversion.
    /// </summary>
    public static float[] FromWav(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < WavFile.HeaderSize)
        {
            return [];
        }

        return ToNormalizedSamples(wav[WavFile.HeaderSize..]);
    }
}
