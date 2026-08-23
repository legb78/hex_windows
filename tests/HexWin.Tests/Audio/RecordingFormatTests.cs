using HexWin.Audio;
using Xunit;

namespace HexWin.Tests.Audio;

public class RecordingFormatTests
{
    [Fact]
    public void The_format_is_the_one_the_model_requires()
    {
        // These three values are not a choice: the model accepts nothing but
        // 16 kHz mono 16-bit. Changing them would break transcription.
        Assert.Equal(16_000, RecordingFormat.SampleRate);
        Assert.Equal(1, RecordingFormat.Channels);
        Assert.Equal(16, RecordingFormat.BitsPerSample);
        Assert.Equal(32_000, RecordingFormat.BytesPerSecond);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(32_000, 1_000)]
    [InlineData(16_000, 500)]
    [InlineData(3_200, 100)]
    public void A_quantity_of_samples_converts_to_a_duration(long bytes, int expectedMilliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            RecordingFormat.DurationOf(bytes));
    }

    [Theory]
    [InlineData(1_000, 32_000)]
    [InlineData(500, 16_000)]
    [InlineData(0, 0)]
    public void A_duration_converts_to_a_quantity_of_samples(int milliseconds, long expectedBytes)
    {
        Assert.Equal(
            expectedBytes,
            RecordingFormat.BytesFor(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void The_conversion_always_lands_on_a_whole_sample()
    {
        // A 16-bit sample takes two bytes. Cutting one in half would shift the
        // whole following stream by one byte, and the signal would turn into
        // noise.
        long bytes = RecordingFormat.BytesFor(TimeSpan.FromMilliseconds(1.5));

        Assert.Equal(0, bytes % RecordingFormat.BytesPerSample);
    }

    [Fact]
    public void A_round_trip_preserves_the_duration()
    {
        var original = TimeSpan.FromSeconds(2.5);

        Assert.Equal(original, RecordingFormat.DurationOf(RecordingFormat.BytesFor(original)));
    }

    [Fact]
    public void Negative_values_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingFormat.DurationOf(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingFormat.BytesFor(TimeSpan.FromSeconds(-1)));
    }
}
