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
    [InlineData(50, 60)]
    public void Curve_matches_spec(int failedAttempts, int expectedSeconds)
    {
        var delay = UnlockBackoff.GetDelayAfterFailure(failedAttempts);
        Assert.Equal(expectedSeconds, (int)delay.TotalSeconds);
    }

    [Fact]
    public void Zero_or_negative_attempt_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnlockBackoff.GetDelayAfterFailure(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnlockBackoff.GetDelayAfterFailure(-1));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void Self_destruct_triggers_at_threshold(int attempts, bool expected)
    {
        Assert.Equal(expected, UnlockBackoff.ShouldTriggerSelfDestruct(attempts));
    }
}
