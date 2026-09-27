using HexWin.Transcription;
using Xunit;

namespace HexWin.Tests.Transcription;

public class DecodingThreadsTests
{
    [Theory]
    [InlineData(2, 2)]
    [InlineData(6, 6)]
    [InlineData(24, 8)]
    public void Zero_picks_one_thread_per_physical_core_up_to_a_ceiling(int physicalCores, int expected)
    {
        Assert.Equal(expected, DecodingThreads.Resolve(0, physicalCores));
    }

    [Fact]
    public void A_machine_that_reports_no_core_still_gets_one_thread()
    {
        Assert.Equal(1, DecodingThreads.Resolve(0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void A_count_chosen_by_the_user_is_kept_as_is(int chosen)
    {
        // Even past the automatic ceiling: whoever set it may have measured.
        Assert.Equal(chosen, DecodingThreads.Resolve(chosen, 4));
    }

    [Fact]
    public void This_machine_resolves_to_a_sensible_count()
    {
        int threads = DecodingThreads.Resolve(0);

        Assert.InRange(threads, 1, 8);
        Assert.True(threads <= Environment.ProcessorCount);
    }
}
