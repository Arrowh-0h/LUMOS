using System.Security.Cryptography;
using System.Text;
using Lumos.Core.Crypto;

namespace Lumos.Core.Generator;

/// <summary>
/// Cryptographically secure password generator. Two modes:
///   - Random characters with class toggles.
///   - Diceware-style passphrases from the EFF Large Wordlist.
///
/// Also exposes a strength-aware helper that regenerates until the result
/// scores at or above a target zxcvbn score.
///
/// All randomness comes from RandomNumberGenerator (CSPRNG); never System.Random.
/// </summary>
public static class PasswordGenerator
{
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits    = "0123456789";
    private const string Symbols   = "!@#$%^&*()-_=+[]{};:,.<>?/~";

    // Visually confusable characters to strip when ExcludeAmbiguous is on.
    private static readonly HashSet<char> AmbiguousChars =
        new("0O1lI|`'\"".ToCharArray());

    // ---------- Random characters ----------

    public static string GenerateRandom(RandomPasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        // Build per-class pools (with ambiguous chars optionally filtered).
        var classes = new List<string>();
        if (options.IncludeUppercase) classes.Add(Filter(Uppercase, options.ExcludeAmbiguous));
        if (options.IncludeLowercase) classes.Add(Filter(Lowercase, options.ExcludeAmbiguous));
        if (options.IncludeDigits)    classes.Add(Filter(Digits,    options.ExcludeAmbiguous));
        if (options.IncludeSymbols)   classes.Add(Filter(Symbols,   options.ExcludeAmbiguous));

        // Validate that filtering didn't leave any class empty (e.g. someone
        // enabled "digits only" + "exclude ambiguous" which removes 0 and 1).
        foreach (var c in classes)
            if (c.Length == 0)
                throw new InvalidOperationException(
                    "A character class is empty after filtering ambiguous characters. " +
                    "Disable ExcludeAmbiguous or enable another class.");

        var combined = string.Concat(classes);
        var chars = new char[options.Length];

        // If RequireOneOfEachClass, seed one slot per class first.
        var seeded = 0;
        if (options.RequireOneOfEachClass)
        {
            foreach (var c in classes)
            {
                chars[seeded++] = c[RandomNumberGenerator.GetInt32(c.Length)];
            }
        }

        // Fill remaining slots from the combined pool.
        for (int i = seeded; i < options.Length; i++)
        {
            chars[i] = combined[RandomNumberGenerator.GetInt32(combined.Length)];
        }

        // Shuffle so the guaranteed-class characters aren't all at the front.
        Shuffle(chars);

        return new string(chars);
    }

    private static string Filter(string pool, bool excludeAmbiguous)
    {
        if (!excludeAmbiguous) return pool;
        var sb = new StringBuilder(pool.Length);
        foreach (var c in pool)
            if (!AmbiguousChars.Contains(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Fisher-Yates shuffle using a CSPRNG for index selection.
    /// </summary>
    private static void Shuffle(char[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    // ---------- Passphrase ----------

    public static string GeneratePassphrase(PassphraseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var words = EffWordList.Words;
        var picked = new string[options.WordCount];
        for (int i = 0; i < options.WordCount; i++)
        {
            var word = words[RandomNumberGenerator.GetInt32(words.Count)];
            if (options.CapitalizeWords && word.Length > 0)
            {
                word = char.ToUpperInvariant(word[0]) + word[1..];
            }
            picked[i] = word;
        }

        var phrase = string.Join(options.Separator, picked);

        if (options.AppendDigits)
        {
            var max = (int)Math.Pow(10, options.DigitCount);
            var n = RandomNumberGenerator.GetInt32(max);
            phrase = phrase + options.Separator + n.ToString(new string('0', options.DigitCount));
        }

        return phrase;
    }

    // ---------- Strength-aware helpers ----------

    /// <summary>
    /// Generate a random password and retry up to <paramref name="maxAttempts"/>
    /// times until the result scores at or above <paramref name="minimumScore"/>
    /// on zxcvbn (0-4). If we can't meet the target, the last attempt is
    /// returned with the actual score reported.
    /// </summary>
    public static (string Password, int Score) GenerateRandomWithMinimumStrength(
        RandomPasswordOptions options,
        int minimumScore = 4,
        int maxAttempts = 10)
    {
        if (minimumScore < 0 || minimumScore > 4)
            throw new ArgumentOutOfRangeException(nameof(minimumScore));
        if (maxAttempts < 1) maxAttempts = 1;

        string last = "";
        int lastScore = 0;
        for (int i = 0; i < maxAttempts; i++)
        {
            last = GenerateRandom(options);
            lastScore = PasswordStrengthService.Evaluate(last)?.Score ?? 0;
            if (lastScore >= minimumScore) return (last, lastScore);
        }
        return (last, lastScore);
    }

    /// <summary>
    /// Same idea, for passphrases.
    /// </summary>
    public static (string Passphrase, int Score) GeneratePassphraseWithMinimumStrength(
        PassphraseOptions options,
        int minimumScore = 4,
        int maxAttempts = 10)
    {
        if (minimumScore < 0 || minimumScore > 4)
            throw new ArgumentOutOfRangeException(nameof(minimumScore));
        if (maxAttempts < 1) maxAttempts = 1;

        string last = "";
        int lastScore = 0;
        for (int i = 0; i < maxAttempts; i++)
        {
            last = GeneratePassphrase(options);
            lastScore = PasswordStrengthService.Evaluate(last)?.Score ?? 0;
            if (lastScore >= minimumScore) return (last, lastScore);
        }
        return (last, lastScore);
    }
}
