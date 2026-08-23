using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

public class IdlePolicyTests
{
    private static readonly IdlePolicy FiveMinutes = IdlePolicy.FromMinutes(5);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void A_zero_or_negative_delay_keeps_the_model_resident(int minutes)
    {
        // This is how releasing is switched off: the model stays loaded, and
        // the response stays instant in every circumstance.
        IdlePolicy policy = IdlePolicy.FromMinutes(minutes);

        Assert.False(policy.IsEnabled);
        Assert.False(policy.ShouldUnload(TimeSpan.FromHours(10), isBusy: false));
    }

    [Fact]
    public void A_positive_delay_enables_releasing()
    {
        Assert.True(FiveMinutes.IsEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(299)]
    public void Below_the_delay_the_model_stays_loaded(int seconds)
    {
        Assert.False(FiveMinutes.ShouldUnload(TimeSpan.FromSeconds(seconds), isBusy: false));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(86_400)]
    public void Past_the_delay_the_model_is_released(int seconds)
    {
        Assert.True(FiveMinutes.ShouldUnload(TimeSpan.FromSeconds(seconds), isBusy: false));
    }

    [Fact]
    public void The_delay_bound_is_inclusive()
    {
        Assert.True(FiveMinutes.ShouldUnload(TimeSpan.FromMinutes(5), isBusy: false));
    }

    [Fact]
    public void A_dictation_under_way_forbids_any_release()
    {
        // The deadline can fall exactly while the user is speaking. Releasing
        // at that moment would fail the transcription they are waiting for —
        // the worst possible moment.
        Assert.False(FiveMinutes.ShouldUnload(TimeSpan.FromHours(1), isBusy: true));
    }

    [Fact]
    public void Releasing_resumes_once_the_dictation_is_over()
    {
        Assert.True(FiveMinutes.ShouldUnload(TimeSpan.FromHours(1), isBusy: false));
    }

    [Fact]
    public void The_delay_is_expressed_in_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), FiveMinutes.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(30), IdlePolicy.FromMinutes(30).Timeout);
    }
}
