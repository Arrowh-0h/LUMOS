namespace Lumos.Core.Vault;

/// <summary>
/// Backoff policy for wrong master password attempts.
///
/// Spec curve:
///   attempt 1 -> 0s
///   attempt 2 -> 1s
///   attempt 3 -> 3s
///   attempt 4 -> 10s
///   attempt 5 -> 30s
///   attempt 6+ -> 60s
///
/// Pure function — the count is provided by the caller, who is also
/// responsible for resetting it on success and for the optional
/// self-destruct trigger at attempt 10.
/// </summary>
public static class UnlockBackoff
{
    public const int SelfDestructThreshold = 10;

    private static readonly TimeSpan[] _curve =
    {
        TimeSpan.Zero,                     // 1
        TimeSpan.FromSeconds(1),           // 2
        TimeSpan.FromSeconds(3),           // 3
        TimeSpan.FromSeconds(10),          // 4
        TimeSpan.FromSeconds(30),          // 5
    };

    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Get the delay that should be enforced before the next attempt,
    /// given how many failed attempts have happened so far (1-indexed).
    /// </summary>
    public static TimeSpan GetDelayAfterFailure(int failedAttemptCount)
    {
        if (failedAttemptCount < 1)
            throw new ArgumentOutOfRangeException(nameof(failedAttemptCount));

        var index = failedAttemptCount - 1;
        return index < _curve.Length ? _curve[index] : _ceiling;
    }

    /// <summary>
    /// True if the failed-attempt count has reached the self-destruct threshold.
    /// Self-destruct is opt-in and OFF by default — caller decides whether to act.
    /// </summary>
    public static bool ShouldTriggerSelfDestruct(int failedAttemptCount)
        => failedAttemptCount >= SelfDestructThreshold;
}
