namespace HexWin.Audio;

/// <summary>
/// Format de capture, imposé par le modèle : 16 kHz, mono, 16 bits signés.
///
/// Enregistrer directement dans ce format évite tout rééchantillonnage entre
/// le micro et le moteur. Un rééchantillonnage coûterait du temps sur le
/// chemin critique — celui que l'utilisateur attend après avoir relâché la
/// touche — et dégraderait le signal sans rien apporter.
/// </summary>
public static class RecordingFormat
{
    public const int SampleRate = 16_000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    public const int BytesPerSample = BitsPerSample / 8;
    public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;

    /// <summary>Durée représentée par une quantité d'échantillons bruts.</summary>
    public static TimeSpan DurationOf(long pcmByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pcmByteCount);

        return TimeSpan.FromSeconds((double)pcmByteCount / BytesPerSecond);
    }

    /// <summary>Quantité d'échantillons bruts occupée par une durée.</summary>
    public static long BytesFor(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        // Tronqué à un multiple d'échantillon complet : couper un échantillon
        // 16 bits en deux décalerait tout le reste du flux d'un octet.
        long bytes = (long)(duration.TotalSeconds * BytesPerSecond);

        return bytes - (bytes % BytesPerSample);
    }
}
