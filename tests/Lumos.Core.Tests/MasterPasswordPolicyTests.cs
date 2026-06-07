using Lumos.Core.Crypto;
using Xunit;

namespace Lumos.Core.Tests;

public class MasterPasswordPolicyTests
{
    [Fact]
    public void Empty_password_is_blocked()
    {
        var r = MasterPasswordPolicy.Validate("");
        Assert.False(r.IsAllowed);
        Assert.NotNull(r.Message);
    }

    [Fact]
    public void Null_password_is_blocked()
    {
        var r = MasterPasswordPolicy.Validate(null);
        Assert.False(r.IsAllowed);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("short")]
    [InlineData("eleven_chars")]   // 12 chars exactly is OK; this is 12 chars too
    [InlineData("12345678901")]    // 11 chars
    public void Short_passwords_blocked_when_below_minimum(string pw)
    {
        // Only test ones actually below 12 chars.
        if (pw.Length >= MasterPasswordPolicy.MinimumLength) return;
        var r = MasterPasswordPolicy.Validate(pw);
        Assert.False(r.IsAllowed);
        Assert.Contains("at least", r.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Long_weak_password_is_allowed_but_flagged_weak()
    {
        // 12+ chars but extremely predictable.
        var r = MasterPasswordPolicy.Validate("aaaaaaaaaaaa");
        Assert.True(r.IsAllowed);
        Assert.True(r.IsWeak);
        Assert.NotNull(r.Message);  // user is warned
    }

    [Fact]
    public void Strong_long_passphrase_is_allowed_not_weak()
    {
        var r = MasterPasswordPolicy.Validate("rabbit-trumpet-glacier-spaceship-77");
        Assert.True(r.IsAllowed);
        Assert.False(r.IsWeak);
        Assert.Null(r.Message);
    }

    [Fact]
    public void Minimum_length_is_twelve()
    {
        Assert.Equal(12, MasterPasswordPolicy.MinimumLength);
    }
}
