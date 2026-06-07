using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumos.Core.Vault;

/// <summary>
/// Tracks consecutive failed master-password attempts and persists the count
/// to disk so it survives app restarts. Without persistence, an attacker
/// could simply quit and relaunch the app to bypass our backoff.
///
/// File: vault.db.attempts.json (sidecar next to the vault).
/// Not encrypted — just a counter. An attacker who can edit this file can
/// already access the encrypted vault, and editing it doesn't help them.
/// </summary>
public sealed class FailedAttemptTracker
{
    private readonly string _path;

    public FailedAttemptTracker(string vaultDbPath)
    {
        ArgumentNullException.ThrowIfNull(vaultDbPath);
        _path = vaultDbPath + ".attempts.json";
    }

    public int GetCount()
    {
        if (!File.Exists(_path)) return 0;
        try
        {
            var json = File.ReadAllText(_path);
            var state = JsonSerializer.Deserialize<State>(json);
            return state?.FailedAttempts ?? 0;
        }
        catch
        {
            // If the file is corrupt, treat it as 0. A tampered counter
            // doesn't help an attacker — they still need the password.
            return 0;
        }
    }

    public DateTimeOffset? GetLastFailureUtc()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var json = File.ReadAllText(_path);
            var state = JsonSerializer.Deserialize<State>(json);
            return state?.LastFailureUtc;
        }
        catch
        {
            return null;
        }
    }

    public void RecordFailure()
    {
        var current = GetCount();
        Write(new State
        {
            FailedAttempts = current + 1,
            LastFailureUtc = DateTimeOffset.UtcNow,
        });
    }

    public void Reset()
    {
        if (File.Exists(_path))
        {
            try { File.Delete(_path); }
            catch { /* best effort */ }
        }
    }

    private void Write(State state)
    {
        var json = JsonSerializer.Serialize(state, _jsonOpts);
        File.WriteAllText(_path, json);
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private sealed class State
    {
        [JsonPropertyName("failedAttempts")]
        public int FailedAttempts { get; set; }

        [JsonPropertyName("lastFailureUtc")]
        public DateTimeOffset? LastFailureUtc { get; set; }
    }
}
