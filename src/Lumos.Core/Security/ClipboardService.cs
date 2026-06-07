namespace Lumos.Core.Security;

/// <summary>
/// Copies a value to the clipboard and schedules an auto-clear after a
/// configurable timeout.
///
/// Verified clear: when the timer fires we read the clipboard back and only
/// clear it if the value still matches what we wrote. If the user copied
/// something else in the meantime — a paragraph of an email, a URL, etc. —
/// we leave it alone. Wiping the user's own clipboard would be a hostile
/// surprise.
///
/// Thread-safety: SetTextWithAutoClear may be called from any thread.
/// IClipboard implementations decide whether they need to marshal to a UI
/// thread (WPF does).
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private readonly IClipboard _clipboard;
    private readonly TimeSpan _defaultTimeout;
    private readonly object _gate = new();

    // The last value we wrote, and the timer that will clear it. We hold
    // these so a second SetTextWithAutoClear cancels the previous timer.
    private string? _lastWrittenValue;
    private CancellationTokenSource? _activeCts;

    /// <summary>
    /// Construct with a default clear timeout. Per-call timeouts can be
    /// passed to SetTextWithAutoClear.
    /// </summary>
    public ClipboardService(IClipboard clipboard, TimeSpan defaultClearTimeout)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (defaultClearTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be positive.", nameof(defaultClearTimeout));
        _clipboard = clipboard;
        _defaultTimeout = defaultClearTimeout;
    }

    /// <summary>The timeout used when SetTextWithAutoClear isn't given an explicit one.</summary>
    public TimeSpan DefaultClearTimeout => _defaultTimeout;

    /// <summary>
    /// Set the clipboard to <paramref name="value"/> and schedule a clear
    /// after <paramref name="timeout"/> (or the default if null).
    ///
    /// The clear is conditional: if the clipboard contents have changed by
    /// the time the timer fires, we leave the new contents alone.
    /// </summary>
    public void SetTextWithAutoClear(string value, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var window = timeout ?? _defaultTimeout;
        if (window <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be positive.", nameof(timeout));

        CancellationTokenSource newCts;
        lock (_gate)
        {
            // Cancel any pending clear from a previous copy.
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();
            newCts = _activeCts;
            _lastWrittenValue = value;
        }

        _clipboard.SetText(value);

        // Fire-and-forget background task that waits then verifies-and-clears.
        var token = newCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(window, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Read current clipboard, compare against the value we wrote.
            // If it matches, clear it. If not, the user copied something
            // else; do nothing.
            string current;
            try { current = _clipboard.GetText(); }
            catch { return; /* clipboard temporarily unavailable */ }

            lock (_gate)
            {
                if (token.IsCancellationRequested) return;
                if (!ReferenceEquals(_activeCts, newCts)) return;
                if (current != _lastWrittenValue) return;

                try { _clipboard.Clear(); } catch { /* best effort */ }
                _lastWrittenValue = null;
            }
        }, token);
    }

    /// <summary>
    /// Clear immediately if the clipboard still holds what we wrote.
    /// </summary>
    public void ClearNowIfOurs()
    {
        lock (_gate)
        {
            if (_lastWrittenValue is null) return;
            string current;
            try { current = _clipboard.GetText(); }
            catch { return; }
            if (current != _lastWrittenValue) return;
            try { _clipboard.Clear(); } catch { /* best effort */ }
            _lastWrittenValue = null;
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }
}
