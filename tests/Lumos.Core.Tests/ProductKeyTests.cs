using Lumos.Core.Licensing;
using Xunit;

namespace Lumos.Core.Tests;

public class ProductKeyTests
{
    [Fact]
    public void Generated_key_validates()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        Assert.True(ProductKey.IsValid(key));
    }

    [Fact]
    public void Generated_key_has_expected_shape()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        // LUMOS-XXXX-XXXX-XXXX
        Assert.StartsWith("LUMOS-", key);
        var parts = key.Split('-');
        Assert.Equal(4, parts.Length);
        Assert.Equal("LUMOS", parts[0]);
        Assert.Equal(4, parts[1].Length);
        Assert.Equal(4, parts[2].Length);
        Assert.Equal(4, parts[3].Length);
    }

    [Fact]
    public void Validation_is_case_insensitive()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        Assert.True(ProductKey.IsValid(key.ToLowerInvariant()));
    }

    [Fact]
    public void Validation_tolerates_missing_dashes_and_spaces()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        var noDashes = key.Replace("-", "");
        var spaced = key.Replace("-", " ");
        Assert.True(ProductKey.IsValid(noDashes));
        Assert.True(ProductKey.IsValid(spaced));
    }

    [Fact]
    public void Validation_works_without_the_LUMOS_prefix()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        var withoutPrefix = key["LUMOS-".Length..]; // XXXX-XXXX-XXXX
        Assert.True(ProductKey.IsValid(withoutPrefix));
    }

    [Fact]
    public void Tampered_signature_is_rejected()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        // Flip the last character to (almost certainly) break the signature.
        var chars = key.ToCharArray();
        var last = chars[^1];
        chars[^1] = last == 'A' ? 'B' : 'A';
        var tampered = new string(chars);
        Assert.False(ProductKey.IsValid(tampered));
    }

    [Fact]
    public void Tampered_serial_is_rejected()
    {
        var key = ProductKey.FromSerial("ABCD2345");
        // Change a serial char; signature no longer matches the serial.
        var broken = key.Replace("ABCD", "ABCE");
        Assert.False(ProductKey.IsValid(broken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("LUMOS-1111-1111-1111")]   // contains ambiguous chars not in alphabet
    [InlineData("LUMOS-AAAA-AAAA")]         // too short
    [InlineData("LUMOS-AAAA-AAAA-AAAA-AAAA")] // too long
    public void Garbage_inputs_are_rejected(string? input)
    {
        Assert.False(ProductKey.IsValid(input));
    }

    [Fact]
    public void Different_serials_produce_different_keys()
    {
        var a = ProductKey.FromSerial("ABCD2345");
        var b = ProductKey.FromSerial("ABCD2346");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FromSerial_rejects_bad_serials()
    {
        Assert.Throws<ArgumentException>(() => ProductKey.FromSerial("SHORT"));
        Assert.Throws<ArgumentException>(() => ProductKey.FromSerial("ABCD234O")); // 'O' not in alphabet
    }

    [Fact]
    public void Random_keys_overwhelmingly_fail_validation()
    {
        // A random 12-char payload should almost never validate (the signature
        // space is 30^4 ≈ 810k, so a random guess matches ~1 in 810k).
        var rng = new Random(123);
        const string alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
        int validCount = 0;
        for (int i = 0; i < 2000; i++)
        {
            var payload = new char[12];
            for (int j = 0; j < 12; j++) payload[j] = alphabet[rng.Next(alphabet.Length)];
            if (ProductKey.IsValid(ProductKey.Format(new string(payload))))
                validCount++;
        }
        // Expect zero (or vanishingly few) random hits.
        Assert.True(validCount <= 1, $"Unexpectedly many random keys validated: {validCount}");
    }
}
