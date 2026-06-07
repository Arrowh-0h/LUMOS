using Lumos.Core.Totp;
using Xunit;

namespace Lumos.Core.Tests;

public class TotpGeneratorTests
{
    [Fact]
    public void Base32_roundtrips_arbitrary_bytes()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC };
        var encoded = Base32.Encode(data);
        var decoded = Base32.Decode(encoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Base32_is_valid_recognizes_good_and_bad()
    {
        Assert.True(Base32.IsValid("JBSWY3DPEHPK3PXP"));  // "Hello!" in base32
        Assert.False(Base32.IsValid(""));
        Assert.False(Base32.IsValid("not-base32-at-all!"));
        Assert.False(Base32.IsValid("8888")); // 8 isn't in the alphabet
    }

    [Fact]
    public void Snapshot_within_same_window_yields_same_code()
    {
        const string secret = "JBSWY3DPEHPK3PXP";
        var t = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var t2 = t.AddSeconds(5);
        Assert.Equal(TotpGenerator.Snapshot(secret, t).Code,
                     TotpGenerator.Snapshot(secret, t2).Code);
    }

    [Fact]
    public void Snapshot_at_window_boundary_yields_different_code()
    {
        const string secret = "JBSWY3DPEHPK3PXP";
        var t = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var next = t.AddSeconds(30);
        Assert.NotEqual(TotpGenerator.Snapshot(secret, t).Code,
                        TotpGenerator.Snapshot(secret, next).Code);
    }

    [Fact]
    public void Snapshot_seconds_remaining_decreases_within_window()
    {
        const string secret = "JBSWY3DPEHPK3PXP";
        // Use a step-aligned timestamp: 1700000040 = 56666668 * 30 exactly,
        // so snap1 lands at the very start of a window with full fraction.
        var t = DateTimeOffset.FromUnixTimeSeconds(1700000040);
        var snap1 = TotpGenerator.Snapshot(secret, t);
        var snap2 = TotpGenerator.Snapshot(secret, t.AddSeconds(10));
        Assert.True(snap2.SecondsRemaining < snap1.SecondsRemaining);
        Assert.InRange(snap1.FractionRemaining, 0.99, 1.0);
        Assert.InRange(snap2.FractionRemaining, 0.65, 0.68);
    }

    [Fact]
    public void Code_is_always_six_digits()
    {
        const string secret = "JBSWY3DPEHPK3PXP";
        for (var i = 0; i < 50; i++)
        {
            var t = DateTimeOffset.UtcNow.AddSeconds(i * 7);
            var code = TotpGenerator.Snapshot(secret, t).Code;
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.True(char.IsDigit(c)));
        }
    }

    [Fact]
    public void TryParseSecret_accepts_plain_base32()
    {
        Assert.Equal("JBSWY3DPEHPK3PXP", TotpGenerator.TryParseSecret("JBSWY3DPEHPK3PXP"));
        // Whitespace and lowercase get normalized.
        Assert.Equal("JBSWY3DPEHPK3PXP", TotpGenerator.TryParseSecret("jbswy3dp ehpk3pxp"));
    }

    [Fact]
    public void TryParseSecret_extracts_secret_from_otpauth_uri()
    {
        var uri = "otpauth://totp/Lumos:[email protected]?secret=JBSWY3DPEHPK3PXP&issuer=Lumos&digits=6&period=30";
        Assert.Equal("JBSWY3DPEHPK3PXP", TotpGenerator.TryParseSecret(uri));
    }

    [Fact]
    public void TryParseSecret_rejects_garbage()
    {
        Assert.Null(TotpGenerator.TryParseSecret(""));
        Assert.Null(TotpGenerator.TryParseSecret("   "));
        Assert.Null(TotpGenerator.TryParseSecret("not-a-secret!@#"));
        Assert.Null(TotpGenerator.TryParseSecret("otpauth://totp/x?issuer=x"));  // no secret
        Assert.Null(TotpGenerator.TryParseSecret("otpauth://totp/x?secret=garbage!"));
    }

    [Fact]
    public void TryParseSecret_handles_url_encoded_otpauth()
    {
        // Some apps URL-encode the label portion. Make sure we still find the secret.
        var uri = "otpauth://totp/Lumos%3Aalice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Lumos";
        Assert.Equal("JBSWY3DPEHPK3PXP", TotpGenerator.TryParseSecret(uri));
    }
}
