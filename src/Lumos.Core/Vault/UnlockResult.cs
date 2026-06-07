namespace Lumos.Core.Vault;

public enum UnlockStatus
{
    Success,
    WrongPassword,
    VaultMissing,
    VaultCorrupted,
    BackoffRequired,
    SelfDestructed,
    UnexpectedError,
}

/// <summary>
/// Result of an unlock attempt.
///
///   - On Success, Service is set. Caller owns it (must Dispose).
///   - On WrongPassword, Backoff is the delay before the next attempt is permitted.
///       FailedAttemptCount is the new total (>= 1).
///   - On BackoffRequired, the caller tried to unlock during a forced wait.
///       RemainingBackoff is how long to wait.
///   - On SelfDestructed, the vault file has been deleted because the
///       self-destruct setting was enabled and the threshold was reached.
/// </summary>
public sealed record UnlockResult(
    UnlockStatus Status,
    VaultService? Service = null,
    TimeSpan Backoff = default,
    TimeSpan RemainingBackoff = default,
    int FailedAttemptCount = 0,
    string? ErrorMessage = null);
