using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

public class AudioLevelTests
{
    /// <summary>Builds little-endian signed 16-bit samples.</summary>
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
    public void A_null_signal_gives_a_null_level()
    {
        Assert.Equal(0, AudioLevel.Peak(Samples(0, 0, 0, 0)));
    }

    [Fact]
    public void A_clipped_signal_gives_the_maximum_level()
    {
        Assert.Equal(1, AudioLevel.Peak(Samples(short.MaxValue)));
    }

    [Fact]
    public void The_peak_kept_is_the_loudest_of_the_recording()
    {
        // A single spike is enough: that is what tells "somebody spoke" apart
        // from "the microphone catches nothing".
        double peak = AudioLevel.Peak(Samples(10, 20, short.MaxValue, 5));

        Assert.Equal(1, peak);
    }

    [Fact]
    public void Negative_values_count_as_much_as_positive_ones()
    {
        // An audio signal swings around zero: ignoring the negative half would
        // understate the level by half.
        Assert.Equal(AudioLevel.Peak(Samples(short.MaxValue)), AudioLevel.Peak(Samples(-short.MaxValue)));
    }

    [Fact]
    public void The_extreme_negative_bound_does_not_overflow()
    {
        // short.MinValue is -32768, whose opposite is not representable in 16
        // bits: a naive Math.Abs throws on it.
        double peak = AudioLevel.Peak(Samples(short.MinValue));

        Assert.Equal(1, peak);
    }

    [Fact]
    public void An_empty_recording_gives_a_null_level()
    {
        Assert.Equal(0, AudioLevel.Peak([]));
    }

    [Fact]
    public void A_lone_byte_is_ignored_rather_than_misread()
    {
        // An incomplete sample at the end of a buffer must not be read as an
        // arbitrary value.
        Assert.Equal(0, AudioLevel.Peak([42]));
    }

    [Fact]
    public void A_muted_microphone_is_recognised_as_silent()
    {
        Assert.True(AudioLevel.IsSilent(Samples(0, 1, 2, 0)));
    }

    [Fact]
    public void A_normal_voice_is_not_taken_for_silence()
    {
        // 10 % of full scale: well below normal speech, and already ten times
        // above the threshold.
        Assert.False(AudioLevel.IsSilent(Samples((short)(short.MaxValue / 10))));
    }
}
