namespace Lumos.Core.Vault;

/// <summary>
/// Backoff policy for wrong master password attempts.
///
/// Curve (v2):
///   attempt 1   -> 0s
///   attempt 2   -> 1s
///   attempt 3   -> 3s
///   attempt 4   -> 10s
///   attempt 5   -> 30s
///   attempt 6-10 -> 60s
///   attempt 11+ -> 60s + 10s per attempt beyond the 10th
///
/// So attempt 11 waits 70s, attempt 12 waits 80s, and attempt 100 waits 16
/// minutes. Burning all 100 attempts costs roughly 13 hours of wall clock.
///
/// WHAT THIS DOES AND DOES NOT DEFEND AGAINST — worth being clear about,
/// because the numbers look more impressive than they are:
///
/// This only slows down someone typing into the Lumos UI. An attacker who
/// copies vault.db and the header off the machine ignores every line of this
/// file and grinds Argon2id offline on a GPU at whatever rate their hardware
/// allows. The only real defence against THAT is KDF cost, which lives in
/// KdfParameters, not here. Treat this curve as protection against a person at
/// a borrowed laptop, not against a serious adversary with the files.
///
/// Pure function — the count is provided by the caller, who is also
/// responsible for resetting it on success and for the optional
/// self-destruct trigger.
/// </summary>
public static class UnlockBackoff
{
    /// <summary>
    /// Attempt count at which the optional self-destruct fires. Self-destruct
    /// is opt-in and OFF by default; raising this from 10 to 100 makes an
    /// accidental wipe far less likely for someone who simply cannot remember
    /// which password they used.
    /// </summary>
    public const int SelfDestructThreshold = 100;

    /// <summary>Attempt count after which the delay starts growing again.</summary>
    public const int LinearGrowthStartsAfter = 10;

    /// <summary>Added to the delay for each attempt past <see cref="LinearGrowthStartsAfter"/>.</summary>
    public static readonly TimeSpan LinearGrowthStep = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan[] _curve =
    {
        TimeSpan.Zero,                     // 1
        TimeSpan.FromSeconds(1),           // 2
        TimeSpan.FromSeconds(3),           // 3
        TimeSpan.FromSeconds(10),          // 4
        TimeSpan.FromSeconds(30),          // 5
    };

    /// <summary>Plateau delay for attempts 6 through 10.</summary>
    private static readonly TimeSpan _plateau = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Get the delay that should be enforced before the next attempt,
    /// given how many failed attempts have happened so far (1-indexed).
    /// </summary>
    public static TimeSpan GetDelayAfterFailure(int failedAttemptCount)
    {
        if (failedAttemptCount < 1)
            throw new ArgumentOutOfRangeException(nameof(failedAttemptCount));

        var index = failedAttemptCount - 1;
        if (index < _curve.Length) return _curve[index];

        if (failedAttemptCount <= LinearGrowthStartsAfter) return _plateau;

        // Linear growth past the plateau: +10s for every attempt beyond the 10th.
        var extraSteps = failedAttemptCount - LinearGrowthStartsAfter;
        return _plateau + (LinearGrowthStep * extraSteps);
    }

    /// <summary>
    /// True if the failed-attempt count has reached the self-destruct threshold.
    /// Self-destruct is opt-in and OFF by default — caller decides whether to act.
    /// </summary>
    public static bool ShouldTriggerSelfDestruct(int failedAttemptCount)
        => failedAttemptCount >= SelfDestructThreshold;
}
