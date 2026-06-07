namespace Lumos.Core.Crypto;

/// <summary>
/// Result of validating a candidate master password against the spec.
/// </summary>
public sealed record MasterPasswordValidation(
    bool IsAllowed,
    bool IsWeak,
    int? Score,
    string? Message)
{
    public static MasterPasswordValidation Allowed(int score, bool isWeak, string? message = null)
        => new(IsAllowed: true, IsWeak: isWeak, Score: score, Message: message);

    public static MasterPasswordValidation Blocked(string message)
        => new(IsAllowed: false, IsWeak: true, Score: null, Message: message);
}

/// <summary>
/// Master password rules (spec §4.7):
///   - Minimum length: 12 characters
///   - Strength meter (zxcvbn) shown live
///   - Warn but do not block weak passwords (user is informed, not gated)
///   - No composition rules (no "must contain a symbol" — outdated)
/// </summary>
public static class MasterPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int WeakScoreThreshold = 3;  // score < 3 is "weak but allowed"

    public static MasterPasswordValidation Validate(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return MasterPasswordValidation.Blocked("Master password cannot be empty.");

        if (candidate.Length < MinimumLength)
            return MasterPasswordValidation.Blocked(
                $"Master password must be at least {MinimumLength} characters (got {candidate.Length}).");

        var strength = PasswordStrengthService.Evaluate(candidate);
        var score = strength?.Score ?? 0;
        var isWeak = score < WeakScoreThreshold;

        string? message = isWeak
            ? "This password is weaker than recommended. " +
              "Lumos will accept it, but a stronger one would protect your vault better. " +
              "Try a longer passphrase of unrelated words."
            : null;

        return MasterPasswordValidation.Allowed(score, isWeak, message);
    }
}
