using HexWin.Audio;
using HexWin.Configuration;
using Xunit;

namespace HexWin.Tests.Audio;

public class RecordingGuardsTests
{
    private static readonly RecordingGuards Guards = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(120));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(249)]
    public void An_accidental_tap_is_discarded(int milliseconds)
    {
        // Without this guard, a brush of the key would run the engine for
        // nothing — and the model would happily invent a sentence out of thin
        // air.
        Assert.True(Guards.IsTooShort(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(250)]
    [InlineData(251)]
    [InlineData(5_000)]
    public void A_long_enough_press_is_kept(int milliseconds)
    {
        Assert.False(Guards.IsTooShort(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void The_minimum_bound_is_inclusive()
    {
        // Exactly on the limit, we keep it: refusing would be more surprising.
        Assert.False(Guards.IsTooShort(TimeSpan.FromMilliseconds(250)));
    }

    [Theory]
    [InlineData(119)]
    [InlineData(0)]
    public void Below_the_ceiling_capture_carries_on(int seconds)
    {
        Assert.False(Guards.HasReachedMaximum(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(121)]
    [InlineData(3_600)]
    public void At_the_ceiling_capture_must_stop(int seconds)
    {
        // Key held down in a pocket: with no ceiling, memory would grow
        // without end.
        Assert.True(Guards.HasReachedMaximum(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void The_ceiling_converts_to_a_usable_size()
    {
        Assert.Equal(120L * RecordingFormat.BytesPerSecond, Guards.MaximumBytes);
    }

    [Fact]
    public void The_bounds_come_from_the_configuration()
    {
        AppSettings settings = AppSettings.Parse(
            """{"minRecordingMilliseconds": 400, "maxRecordingSeconds": 30}""");

        RecordingGuards guards = RecordingGuards.From(settings);

        Assert.Equal(TimeSpan.FromMilliseconds(400), guards.Minimum);
        Assert.Equal(TimeSpan.FromSeconds(30), guards.Maximum);
    }

    [Fact]
    public void A_zero_minimum_duration_accepts_everything()
    {
        // An edge configuration, but a legitimate one: some people want to
        // dictate very short isolated words.
        var permissive = new RecordingGuards(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        Assert.False(permissive.IsTooShort(TimeSpan.Zero));
    }
}
