namespace HexWin.Audio;

/// <summary>
/// Capture format, dictated by the model: 16 kHz, mono, signed 16-bit.
///
/// Recording straight into this format avoids any resampling between the
/// microphone and the engine. Resampling would cost time on the critical
/// path — the one the user waits through after releasing the key — and
/// degrade the signal for nothing in return.
/// </summary>
public static class RecordingFormat
{
    public const int SampleRate = 16_000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    public const int BytesPerSample = BitsPerSample / 8;
    public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;

    /// <summary>Duration represented by a quantity of raw samples.</summary>
    public static TimeSpan DurationOf(long pcmByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pcmByteCount);

        return TimeSpan.FromSeconds((double)pcmByteCount / BytesPerSecond);
    }

    /// <summary>Quantity of raw samples taken up by a duration.</summary>
    public static long BytesFor(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        // Truncated to a whole number of samples: cutting a 16-bit sample in
        // half would shift the rest of the stream by one byte.
        long bytes = (long)(duration.TotalSeconds * BytesPerSecond);

        return bytes - (bytes % BytesPerSample);
    }
}
