using Lumos.Core.Crypto;
using Xunit;

namespace Lumos.Core.Tests;

public class PasswordStrengthServiceTests
{
    [Fact]
    public void Null_or_empty_returns_null()
    {
        Assert.Null(PasswordStrengthService.Evaluate(null));
        Assert.Null(PasswordStrengthService.Evaluate(""));
    }

    [Fact]
    public void Common_password_scores_very_low()
    {
        var s = PasswordStrengthService.Evaluate("password");
        Assert.NotNull(s);
        Assert.True(s!.Score <= 1, $"Expected score <= 1, got {s.Score}");
    }

    [Fact]
    public void Strong_passphrase_scores_high()
    {
        var s = PasswordStrengthService.Evaluate("correct horse battery staple thunder");
        Assert.NotNull(s);
        Assert.True(s!.Score >= 3, $"Expected score >= 3, got {s.Score}");
    }

    [Fact]
    public void Score_is_in_valid_range()
    {
        var samples = new[]
        {
            "a", "abc", "abc123", "Password1!", "T7$xKp9!Mw2QnB4rL8z",
            "tr0ub4dor&3", "this is a long phrase with several words"
        };
        foreach (var s in samples)
        {
            var r = PasswordStrengthService.Evaluate(s);
            Assert.NotNull(r);
            Assert.InRange(r!.Score, 0, 4);
        }
    }
}
