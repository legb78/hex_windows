namespace HexWin.Audio;

/// <summary>
/// Converts captured samples into the representation the recognition engine
/// expects.
///
/// The microphone hands back signed 16-bit integers; ONNX Runtime wants
/// floats normalised between -1 and 1. A scaling mistake here raises no
/// exception: the model receives a clipped or inaudible signal and returns
/// incoherent text. Hence the tests on the bounds.
/// </summary>
public static class PcmConverter
{
    /// <summary>
    /// Normalisation divisor. 32768 rather than 32767, because that is the
    /// largest negative amplitude of a signed 16-bit integer: dividing by
    /// 32767 would push the negative peak slightly past -1.
    /// </summary>
    private const float FullScale = 32768f;

    /// <summary>
    /// Converts little-endian signed 16-bit samples into normalised floats.
    /// A lone trailing byte — a truncated buffer — is ignored.
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
    /// Variant taking a whole WAV file: the header is dropped before
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
