using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

/// <summary>
/// A scaling mistake here raises no exception: the model receives a clipped or
/// inaudible signal and returns incoherent text. Hence checking the bounds
/// rather than merely the shape.
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
    public void Silence_stays_silence()
    {
        Assert.Equal([0f, 0f], PcmConverter.ToNormalizedSamples(Samples(0, 0)));
    }

    [Fact]
    public void The_negative_peak_reaches_exactly_minus_one()
    {
        // This is why we divide by 32768 and not 32767: -32768 is the largest
        // negative amplitude of a 16-bit integer.
        Assert.Equal(-1f, PcmConverter.ToNormalizedSamples(Samples(short.MinValue))[0]);
    }

    [Fact]
    public void The_positive_peak_stays_within_bounds()
    {
        float value = PcmConverter.ToNormalizedSamples(Samples(short.MaxValue))[0];

        Assert.True(value < 1f);
        Assert.True(value > 0.999f);
    }

    [Fact]
    public void The_sign_is_preserved()
    {
        float[] samples = PcmConverter.ToNormalizedSamples(Samples(1000, -1000));

        Assert.True(samples[0] > 0);
        Assert.True(samples[1] < 0);
        Assert.Equal(samples[0], -samples[1]);
    }

    [Fact]
    public void Half_of_full_scale_gives_one_half()
    {
        Assert.Equal(0.5f, PcmConverter.ToNormalizedSamples(Samples(16_384))[0]);
    }

    [Fact]
    public void A_lone_trailing_byte_is_ignored()
    {
        // Truncated buffer: better to lose half a sample than to shift the
        // whole stream.
        Assert.Single(PcmConverter.ToNormalizedSamples([0, 0, 42]));
    }

    [Fact]
    public void An_empty_input_produces_no_sample()
    {
        Assert.Empty(PcmConverter.ToNormalizedSamples([]));
    }

    [Fact]
    public void The_wav_header_is_dropped()
    {
        byte[] wav = WavFile.Create(Samples(16_384, -16_384));

        float[] samples = PcmConverter.FromWav(wav);

        Assert.Equal(2, samples.Length);
        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(-0.5f, samples[1]);
    }

    [Fact]
    public void A_file_shorter_than_a_header_produces_nothing()
    {
        Assert.Empty(PcmConverter.FromWav(new byte[10]));
    }
}
