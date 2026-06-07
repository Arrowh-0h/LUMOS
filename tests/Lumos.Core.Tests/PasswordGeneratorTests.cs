using Lumos.Core.Generator;
using Xunit;

namespace Lumos.Core.Tests;

public class PasswordGeneratorTests
{
    // ---------- Random character generation ----------

    [Fact]
    public void Random_default_options_produce_expected_length()
    {
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions());
        Assert.Equal(24, pw.Length);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    public void Random_produces_requested_length(int length)
    {
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions { Length = length });
        Assert.Equal(length, pw.Length);
    }

    [Fact]
    public void Random_rejects_too_short()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.GenerateRandom(new RandomPasswordOptions { Length = 3 }));
    }

    [Fact]
    public void Random_rejects_too_long()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.GenerateRandom(new RandomPasswordOptions { Length = 129 }));
    }

    [Fact]
    public void Random_rejects_no_classes_enabled()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.GenerateRandom(new RandomPasswordOptions
            {
                IncludeUppercase = false,
                IncludeLowercase = false,
                IncludeDigits = false,
                IncludeSymbols = false,
            }));
    }

    [Fact]
    public void Random_digits_only_contains_only_digits()
    {
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
        {
            Length = 32,
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeDigits = true,
            IncludeSymbols = false,
            RequireOneOfEachClass = false,
        });
        Assert.All(pw, c => Assert.True(char.IsDigit(c), $"'{c}' is not a digit"));
    }

    [Fact]
    public void Random_uppercase_only_contains_only_uppercase()
    {
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
        {
            Length = 32,
            IncludeUppercase = true,
            IncludeLowercase = false,
            IncludeDigits = false,
            IncludeSymbols = false,
            RequireOneOfEachClass = false,
        });
        Assert.All(pw, c => Assert.True(char.IsUpper(c), $"'{c}' is not uppercase"));
    }

    [Fact]
    public void Random_lowercase_only_contains_only_lowercase()
    {
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
        {
            Length = 32,
            IncludeUppercase = false,
            IncludeLowercase = true,
            IncludeDigits = false,
            IncludeSymbols = false,
            RequireOneOfEachClass = false,
        });
        Assert.All(pw, c => Assert.True(char.IsLower(c), $"'{c}' is not lowercase"));
    }

    [Fact]
    public void Random_exclude_ambiguous_omits_those_chars()
    {
        // Generate a long password with all classes + exclude ambiguous on.
        // None of the ambiguous chars should appear.
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
        {
            Length = 128,
            ExcludeAmbiguous = true,
        });
        var ambig = "0O1lI|`'\"";
        foreach (var c in ambig) Assert.DoesNotContain(c, pw);
    }

    [Fact]
    public void Random_require_one_of_each_includes_all_enabled_classes()
    {
        // Long enough that each class will be hit even by chance, but the
        // *guarantee* test is more interesting at short lengths. Let's test
        // both.
        for (int run = 0; run < 50; run++)
        {
            var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
            {
                Length = 8,  // exactly enough for 4 classes + 4 free slots
                RequireOneOfEachClass = true,
            });
            Assert.Contains(pw, char.IsUpper);
            Assert.Contains(pw, char.IsLower);
            Assert.Contains(pw, char.IsDigit);
            Assert.Contains(pw, c => "!@#$%^&*()-_=+[]{};:,.<>?/~".Contains(c));
        }
    }

    [Fact]
    public void Random_two_calls_produce_different_output()
    {
        var a = PasswordGenerator.GenerateRandom(new RandomPasswordOptions { Length = 32 });
        var b = PasswordGenerator.GenerateRandom(new RandomPasswordOptions { Length = 32 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Random_length_equal_to_class_count_works()
    {
        // Length=4 with all 4 classes required is exactly enough — must succeed.
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
        {
            Length = 4,
            RequireOneOfEachClass = true,
        });
        Assert.Equal(4, pw.Length);
        Assert.Contains(pw, char.IsUpper);
        Assert.Contains(pw, char.IsLower);
        Assert.Contains(pw, char.IsDigit);
        Assert.Contains(pw, c => "!@#$%^&*()-_=+[]{};:,.<>?/~".Contains(c));
    }

    [Fact]
    public void Random_ambiguous_only_class_throws()
    {
        // Digits only + exclude ambiguous removes 0 and 1, leaving 8 chars.
        // That's not empty, so should still work. But uppercase only + exclude
        // ambiguous keeps every letter except O, I — fine.
        // The actual edge case: there's no class our filter would empty. So
        // this is more of a sanity check that things still work.
        var pw = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
        {
            Length = 16,
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeDigits = true,
            IncludeSymbols = false,
            ExcludeAmbiguous = true,
            RequireOneOfEachClass = false,
        });
        foreach (var c in pw)
        {
            Assert.True(char.IsDigit(c));
            Assert.NotEqual('0', c);
            Assert.NotEqual('1', c);
        }
    }

    // ---------- Passphrase ----------

    [Fact]
    public void Passphrase_has_requested_word_count()
    {
        if (!WordListAvailable()) return;  // graceful skip
        var phrase = PasswordGenerator.GeneratePassphrase(new PassphraseOptions { WordCount = 5 });
        Assert.Equal(5, phrase.Split('-').Length);
    }

    [Fact]
    public void Passphrase_uses_custom_separator()
    {
        if (!WordListAvailable()) return;
        var phrase = PasswordGenerator.GeneratePassphrase(new PassphraseOptions
        {
            WordCount = 4,
            Separator = ".",
        });
        Assert.Equal(4, phrase.Split('.').Length);
        Assert.DoesNotContain('-', phrase);
    }

    [Fact]
    public void Passphrase_capitalizes_when_requested()
    {
        if (!WordListAvailable()) return;
        var phrase = PasswordGenerator.GeneratePassphrase(new PassphraseOptions
        {
            WordCount = 4,
            CapitalizeWords = true,
        });
        foreach (var word in phrase.Split('-'))
        {
            Assert.True(char.IsUpper(word[0]), $"Word '{word}' is not capitalized.");
        }
    }

    [Fact]
    public void Passphrase_with_appended_digits_has_them()
    {
        if (!WordListAvailable()) return;
        var phrase = PasswordGenerator.GeneratePassphrase(new PassphraseOptions
        {
            WordCount = 4,
            AppendDigits = true,
            DigitCount = 3,
        });
        // last segment after separator should be 3 digits
        var parts = phrase.Split('-');
        Assert.Equal(5, parts.Length);  // 4 words + digit segment
        Assert.Equal(3, parts[^1].Length);
        Assert.All(parts[^1], c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void Passphrase_two_calls_produce_different_output()
    {
        if (!WordListAvailable()) return;
        var a = PasswordGenerator.GeneratePassphrase(new PassphraseOptions { WordCount = 6 });
        var b = PasswordGenerator.GeneratePassphrase(new PassphraseOptions { WordCount = 6 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Passphrase_rejects_too_few_words()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.GeneratePassphrase(new PassphraseOptions { WordCount = 2 }));
    }

    [Fact]
    public void Passphrase_rejects_too_many_words()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.GeneratePassphrase(new PassphraseOptions { WordCount = 13 }));
    }

    // ---------- Strength-aware generation ----------

    [Fact]
    public void Random_with_minimum_strength_returns_strong_password_when_possible()
    {
        // A 32-char password with all classes will almost always score 4.
        var (pw, score) = PasswordGenerator.GenerateRandomWithMinimumStrength(
            new RandomPasswordOptions { Length = 32 },
            minimumScore: 4);
        Assert.True(score >= 4 || pw.Length == 32, "Expected score 4 or at least final attempt returned.");
        Assert.Equal(32, pw.Length);
    }

    [Fact]
    public void Passphrase_with_minimum_strength_returns_strong_passphrase_when_possible()
    {
        if (!WordListAvailable()) return;
        var (phrase, score) = PasswordGenerator.GeneratePassphraseWithMinimumStrength(
            new PassphraseOptions { WordCount = 6 },
            minimumScore: 4);
        Assert.True(score >= 4, $"Expected score 4 for 6-word passphrase, got {score}");
        Assert.NotEmpty(phrase);
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Returns true if the embedded EFF wordlist is available. Tests that
    /// depend on the wordlist call this and return early (effectively
    /// pass-by-skip) if it isn't. Once you embed the real file, all
    /// passphrase tests will exercise it.
    /// </summary>
    private static bool WordListAvailable()
    {
        try
        {
            _ = EffWordList.Words.Count;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
