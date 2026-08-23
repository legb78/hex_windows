namespace HexWin.Audio;

/// <summary>
/// Measures the amplitude of a recording.
///
/// Answers a very concrete diagnostic question: is the microphone actually
/// picking anything up? A muted, unplugged or privacy-blocked microphone
/// produces a perfectly valid file, of the right duration, and entirely
/// silent — which the engine then transcribes as an invented sentence.
/// Without a measurement, troubleshooting heads the wrong way.
/// </summary>
public static class AudioLevel
{
    /// <summary>Below this, we consider there was nothing to hear.</summary>
    public const double SilenceThreshold = 0.01;

    /// <summary>
    /// Loudest amplitude encountered, scaled between 0 (dead silence) and
    /// 1 (clipping).
    /// </summary>
    public static double Peak(ReadOnlySpan<byte> pcm)
    {
        int peak = 0;

        // Signed 16-bit little-endian samples: two bytes each.
        for (int offset = 0; offset + 1 < pcm.Length; offset += RecordingFormat.BytesPerSample)
        {
            int sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));

            // Math.Abs fails on short.MinValue, whose opposite is not
            // representable. Fold it onto the neighbouring positive value.
            int magnitude = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);

            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return (double)peak / short.MaxValue;
    }

    /// <summary>True when the recording holds nothing audible.</summary>
    public static bool IsSilent(ReadOnlySpan<byte> pcm) => Peak(pcm) < SilenceThreshold;
}
