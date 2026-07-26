using Lumos.Core.Vault;
using Xunit;

namespace Lumos.Core.Tests;

public class UnlockBackoffTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 10)]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 60)]
    [InlineData(10, 60)]      // end of the plateau
    [InlineData(11, 70)]      // linear growth starts: +10s per attempt
    [InlineData(12, 80)]
    [InlineData(20, 160)]
    [InlineData(100, 960)]    // 16 minutes at the final attempt
    public void Curve_matches_spec(int failedAttempts, int expectedSeconds)
    {
        var delay = UnlockBackoff.GetDelayAfterFailure(failedAttempts);
        Assert.Equal(expectedSeconds, (int)delay.TotalSeconds);
    }

    [Fact]
    public void Delay_is_monotonically_non_decreasing()
    {
        var previous = TimeSpan.MinValue;
        for (var i = 1; i <= 150; i++)
        {
            var delay = UnlockBackoff.GetDelayAfterFailure(i);
            Assert.True(delay >= previous,
                $"Delay went down at attempt {i}: {previous} -> {delay}");
            previous = delay;
        }
    }

    [Fact]
    public void Total_wait_to_exhaust_all_attempts_is_many_hours()
    {
        var total = TimeSpan.Zero;
        for (var i = 1; i <= UnlockBackoff.SelfDestructThreshold; i++)
            total += UnlockBackoff.GetDelayAfterFailure(i);

        // ~13 hours. Asserted loosely so a future tweak to the early curve
        // doesn't fail the build over a few seconds.
        Assert.True(total.TotalHours > 10,
            $"Expected >10 hours of cumulative backoff, got {total.TotalHours:F1}.");
    }

    [Fact]
    public void Zero_or_negative_attempt_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnlockBackoff.GetDelayAfterFailure(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnlockBackoff.GetDelayAfterFailure(-1));
    }

    [Theory]
    [InlineData(10, false)]     // no longer wipes at 10
    [InlineData(50, false)]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    public void Self_destruct_triggers_at_threshold(int attempts, bool expected)
    {
        Assert.Equal(expected, UnlockBackoff.ShouldTriggerSelfDestruct(attempts));
    }
}
