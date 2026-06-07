namespace Lumos.Core.Crypto;

/// <summary>
/// Strength assessment of a master password (or any user-supplied password)
/// based on zxcvbn. Score is 0-4 (Dropbox's zxcvbn convention):
///
///   0 = too guessable: risky password (under 1e3 guesses)
///   1 = very guessable: protection from throttled online attacks (under 1e6)
///   2 = somewhat guessable: protection from unthrottled online attacks (under 1e8)
///   3 = safely unguessable: moderate protection from offline slow-hash scenario (under 1e10)
///   4 = very unguessable: strong protection from offline slow-hash scenario
///
/// We don't use the warning/suggestion strings from zxcvbn directly — the UI
/// layer will map score + length to its own copy.
/// </summary>
public sealed record PasswordStrength(
    int Score,
    double GuessesLog10,
    string CrackTimeOfflineSlow,
    string CrackTimeOfflineFast);

public static class PasswordStrengthService
{
    /// <summary>
    /// Score a password. Returns null for null/empty input (callers shouldn't
    /// be calling us in that case, but better than throwing).
    /// </summary>
    public static PasswordStrength? Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return null;

        var result = Zxcvbn.Core.EvaluatePassword(password);
        return new PasswordStrength(
            Score: result.Score,
            GuessesLog10: result.GuessesLog10,
            CrackTimeOfflineSlow: result.CrackTimeDisplay?.OfflineSlowHashing1e4PerSecond ?? "unknown",
            CrackTimeOfflineFast: result.CrackTimeDisplay?.OfflineFastHashing1e10PerSecond ?? "unknown");
    }
}
