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

    /// <summary>
    /// A recovery operation was attempted on a vault that has no recovery
    /// envelope. Distinct from WrongPassword: nothing the user types will work,
    /// so the UI must say so rather than inviting another attempt.
    /// </summary>
    RecoveryNotConfigured,

    /// <summary>
    /// The supplied recovery code was well-formed but did not unwrap the cipher
    /// key. Kept separate from WrongPassword so the UI can name the right
    /// credential in the error.
    /// </summary>
    WrongRecoveryCode,

    /// <summary>The supplied recovery code was not a valid code shape at all.</summary>
    MalformedRecoveryCode,
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
/// <param name="RequiresRecoverySetup">
/// Set on a successful unlock when this vault has no recovery envelope yet.
/// The UI uses this to show the one-time recovery-code screen. It can only be
/// acted on here — at unlock — because writing the recovery wrap needs the
/// cipher key, which needs the master password.
/// </param>
/// <param name="HeaderUpgraded">
/// True when this unlock rewrote a pre-v3 header into the current format.
/// Informational; useful in logs and tests.
/// </param>
public sealed record UnlockResult(
    UnlockStatus Status,
    VaultService? Service = null,
    TimeSpan Backoff = default,
    TimeSpan RemainingBackoff = default,
    int FailedAttemptCount = 0,
    string? ErrorMessage = null,
    bool RequiresRecoverySetup = false,
    bool HeaderUpgraded = false);

/// <summary>
/// Outcome of issuing a recovery code.
///
/// <paramref name="Code"/> is the ONLY time the plaintext code exists. It is
/// never persisted and cannot be re-read afterwards — if the user loses it,
/// their only option is to generate a new one while they still know the master
/// password. Display it once, let them save or print it, then drop it.
/// </summary>
public sealed record RecoverySetupResult(
    bool Success,
    string? Code = null,
    string? ErrorMessage = null);
