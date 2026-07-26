using Lumos.Core.Crypto;

namespace Lumos.Desktop.Common;

/// <summary>
/// Turns a zxcvbn score into a label and a short piece of coaching for the
/// vault entry forms.
///
/// This is COACHING, NOT ENFORCEMENT. Lumos never blocks a save because a
/// stored password is weak. Plenty of perfectly reasonable entries are weak by
/// necessity — a router PIN, a legacy system that caps you at eight characters,
/// a code someone else chose. Refusing to store those would just push people
/// back to a text file, which is strictly worse.
///
/// So the advice names something concrete the user could do, and then gets out
/// of the way.
/// </summary>
public sealed record PasswordCoaching(int Score, string Label, string Advice);

public static class PasswordCoach
{
    /// <summary>Length below which we mention length before anything else.</summary>
    private const int ShortPasswordLength = 12;

    /// <summary>
    /// Evaluate a stored password. Returns null for empty input — an empty
    /// field is not weak, it is just empty, and nagging about it while someone
    /// is still typing the title would be obnoxious.
    /// </summary>
    public static PasswordCoaching? Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return null;

        var strength = PasswordStrengthService.Evaluate(password);
        if (strength is null) return null;

        var label = strength.Score switch
        {
            0 => "Very weak",
            1 => "Weak",
            2 => "Fair",
            3 => "Strong",
            4 => "Very strong",
            _ => "",
        };

        return new PasswordCoaching(strength.Score, label, BuildAdvice(password, strength));
    }

    private static string BuildAdvice(string password, PasswordStrength strength)
    {
        // Strong enough — say so briefly and stop talking. A meter that keeps
        // lecturing after the user has done the right thing trains people to
        // ignore it.
        if (strength.Score >= 3)
            return $"Good — an offline attack would take about {strength.CrackTimeOfflineSlow}.";

        // Length is the single highest-leverage change, so lead with it when
        // it's the obvious problem rather than talking about character classes.
        if (password.Length < ShortPasswordLength)
            return $"Short passwords fall quickly. {ShortPasswordLength}+ characters helps far more " +
                   "than adding symbols. The generator can make one for you.";

        return strength.Score switch
        {
            0 => "This is very guessable — it likely appears in common password lists. " +
                 "Consider generating one instead.",
            1 => "This resembles a common pattern (a word plus a number, or a keyboard run). " +
                 "A generated password would be far stronger.",
            _ => "Reasonable, but a longer or generated password would hold up better " +
                 "if this site is ever breached.",
        };
    }
}
