using Lumos.Core.Recovery;
using Xunit;

namespace Lumos.Core.Tests;

public class RecoveryCodeTests
{
    [Fact]
    public void Generated_code_has_expected_shape()
    {
        var code = RecoveryCode.Generate();

        // Six groups of five, dash separated.
        var groups = code.Split('-');
        Assert.Equal(6, groups.Length);
        Assert.All(groups, g => Assert.Equal(5, g.Length));
        Assert.Equal(RecoveryCode.CharacterCount, code.Replace("-", "").Length);
    }

    [Fact]
    public void Generated_code_uses_only_the_unambiguous_alphabet()
    {
        // No 0/O, 1/I/L, or U anywhere, across many samples.
        for (var i = 0; i < 200; i++)
        {
            var raw = RecoveryCode.Generate().Replace("-", "");
            Assert.DoesNotContain('0', raw);
            Assert.DoesNotContain('O', raw);
            Assert.DoesNotContain('1', raw);
            Assert.DoesNotContain('I', raw);
            Assert.DoesNotContain('L', raw);
            Assert.DoesNotContain('U', raw);
        }
    }

    [Fact]
    public void Generated_codes_are_distinct()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 500; i++)
            Assert.True(seen.Add(RecoveryCode.Generate()), "Generated a duplicate recovery code.");
    }

    [Fact]
    public void Entropy_is_comfortably_above_128_bits()
    {
        Assert.True(RecoveryCode.EntropyBits > 128,
            $"Expected >128 bits, got {RecoveryCode.EntropyBits:F1}.");
    }

    [Fact]
    public void Normalize_round_trips_a_generated_code()
    {
        var code = RecoveryCode.Generate();
        var canonical = RecoveryCode.Normalize(code);

        Assert.NotNull(canonical);
        Assert.Equal(RecoveryCode.CharacterCount, canonical!.Length);
        Assert.Equal(code, RecoveryCode.Format(canonical));
    }

    [Fact]
    public void Normalize_tolerates_case_spacing_and_missing_dashes()
    {
        var code = RecoveryCode.Generate();
        var canonical = RecoveryCode.Normalize(code);

        Assert.Equal(canonical, RecoveryCode.Normalize(code.ToLowerInvariant()));
        Assert.Equal(canonical, RecoveryCode.Normalize(code.Replace("-", "")));
        Assert.Equal(canonical, RecoveryCode.Normalize("  " + code + "  "));
        Assert.Equal(canonical, RecoveryCode.Normalize(code.Replace("-", " ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDE-FGHJK")]                                  // too short
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ234-56789-ABCDE")]     // too long
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ234-5678O")]           // 'O' not in alphabet
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ234-5678!")]           // punctuation
    public void Normalize_rejects_malformed_input(string? input)
    {
        Assert.Null(RecoveryCode.Normalize(input));
        Assert.False(RecoveryCode.IsWellFormed(input));
    }

    [Fact]
    public void Normalize_does_not_silently_autocorrect_confusable_characters()
    {
        // A user typing O for 0 (or vice versa) should get a clear rejection,
        // not a quietly rewritten code that then reports "wrong code".
        Assert.Null(RecoveryCode.Normalize("ABCDE-FGHJK-MNPQR-STVWX-YZ234-5678O"));
    }

    [Fact]
    public void Format_rejects_wrong_length_input()
    {
        Assert.Throws<ArgumentException>(() => RecoveryCode.Format("TOOSHORT"));
    }
}
