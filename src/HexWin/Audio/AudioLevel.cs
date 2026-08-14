namespace HexWin.Audio;

/// <summary>
/// Mesure l'amplitude d'un enregistrement.
///
/// Sert à répondre à une question de diagnostic très concrète : « le micro
/// capte-t-il réellement quelque chose ? ». Un micro coupé, mal branché ou
/// interdit par les réglages de confidentialité produit un fichier
/// parfaitement valide, de la bonne durée, et entièrement silencieux — que
/// le moteur transcrit en une phrase inventée. Sans mesure, le diagnostic part
/// dans la mauvaise direction.
/// </summary>
public static class AudioLevel
{
    /// <summary>En dessous, on considère qu'il n'y a rien eu à entendre.</summary>
    public const double SilenceThreshold = 0.01;

    /// <summary>
    /// Amplitude maximale rencontrée, ramenée entre 0 (silence absolu) et
    /// 1 (saturation).
    /// </summary>
    public static double Peak(ReadOnlySpan<byte> pcm)
    {
        int peak = 0;

        // Échantillons 16 bits signés, petit-boutistes : deux octets chacun.
        for (int offset = 0; offset + 1 < pcm.Length; offset += RecordingFormat.BytesPerSample)
        {
            int sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));

            // Math.Abs échoue sur short.MinValue, dont l'opposé n'est pas
            // représentable. On le ramène donc à la valeur positive voisine.
            int magnitude = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);

            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return (double)peak / short.MaxValue;
    }

    /// <summary>Vrai si l'enregistrement ne contient rien d'audible.</summary>
    public static bool IsSilent(ReadOnlySpan<byte> pcm) => Peak(pcm) < SilenceThreshold;
}
