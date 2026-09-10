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

    /// <summary>
    /// Same measurement, ignoring the opening <paramref name="lead"/> of the
    /// recording.
    ///
    /// <para>The start cue of the feedback is emitted as the microphone opens.
    /// On speakers rather than headphones it is captured along with everything
    /// else, and left in it lifts a completely silent recording above
    /// <see cref="SilenceThreshold"/> — turning "the microphone heard nothing"
    /// into "sound was captured but no speech recognised", which is the one
    /// distinction this measurement exists to make.</para>
    ///
    /// <para><b>Never more than half the recording</b>, however long the lead.
    /// A press barely above the minimum is only a little longer than the cue
    /// itself; skipping the lead whole would leave nothing to measure and
    /// report a peak of zero — announcing a dead microphone on the strength of
    /// no samples at all, which is worse than the contamination this avoids.</para>
    /// </summary>
    public static double Peak(ReadOnlySpan<byte> pcm, TimeSpan lead)
    {
        int skip = (int)Math.Min(RecordingFormat.BytesFor(lead), pcm.Length / 2);

        // Halving can land mid-sample, which would pair the high byte of one
        // sample with the low byte of the next.
        skip -= skip % RecordingFormat.BytesPerSample;

        return Peak(pcm[skip..]);
    }

    /// <summary>True when the recording holds nothing audible.</summary>
    public static bool IsSilent(ReadOnlySpan<byte> pcm) => Peak(pcm) < SilenceThreshold;
}
