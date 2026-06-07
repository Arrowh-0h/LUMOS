using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Lumos.Core;
using Lumos.Desktop.Common;

namespace Lumos.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // (VelopackApp.Build().Run() runs in Program.Main, before WPF starts.)

        // CRITICAL: hook every unhandled exception path before anything else runs.
        // Without these, a single throw on the dispatcher thread (or in a fire-and-forget
        // Task.Run) closes the app silently — no error, no log, no chance to react.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        LumosCoreBootstrap.Initialize();
        AppServices.Initialize();

        // v2: Lumos is fully offline. The only network use is the explicit,
        // user-initiated update check (Phase 14), which talks to GitHub
        // Releases via Velopack and never runs without the user asking.

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Make sure any open vault is closed and the clipboard's pending
        // clear timer is cancelled before the process exits.
        AppServices.ShutDown();
        base.OnExit(e);
    }

    // ---- Global exception sinks ----

    // Track recently-shown exceptions so we don't pop a MessageBox cascade
    // when a render-loop or binding error keeps re-firing. Same message
    // within 5 seconds → just log it.
    private string? _lastShownMessage;
    private DateTimeOffset _lastShownAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan _suppressWindow = TimeSpan.FromSeconds(5);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Things on the UI thread: binding errors that throw, command handlers,
        // any code reached from a button click.
        LogAndShow("UI thread exception", e.Exception);
        e.Handled = true;   // keep the app alive
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Background threads that didn't go through a Task. By the time we get
        // here the runtime is usually already shutting down (IsTerminating == true),
        // but we still log so the next launch can show what happened.
        if (e.ExceptionObject is Exception ex) LogToDisk("Domain exception", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Fire-and-forget Task.Run failures that nobody awaited. We log and
        // mark observed so the runtime doesn't escalate to a process kill.
        LogToDisk("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void LogAndShow(string context, Exception ex)
    {
        // TargetInvocationException, AggregateException, and other wrapper
        // exceptions hide the actual cause. Unwrap to find the deepest non-
        // wrapper exception — that's what the user actually needs to see.
        var root = UnwrapException(ex);

        LogToDisk(context, ex);  // log the whole tree, not just the root

        // Coalesce a flood: if the same message has fired in the last 5s,
        // don't bother the user again. We still log every occurrence.
        var key = $"{context}|{root.GetType().Name}|{root.Message}";
        var now = DateTimeOffset.UtcNow;
        if (_lastShownMessage == key && now - _lastShownAt < _suppressWindow)
            return;
        _lastShownMessage = key;
        _lastShownAt = now;

        try
        {
            MessageBox.Show(
                $"{context}:\n\n{root.GetType().Name}: {root.Message}\n\n" +
                $"Full stack trace written to crash.log next to the vault file.",
                "Lumos — unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If even the MessageBox throws (e.g. no UI thread left), we've
            // already logged to disk — nothing more we can do.
        }
    }

    /// <summary>
    /// Drill through wrapper exceptions to the real cause. Stops at the first
    /// non-wrapper or when InnerException is null.
    /// </summary>
    private static Exception UnwrapException(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null &&
               (current is System.Reflection.TargetInvocationException
                || current is AggregateException
                || current is System.Windows.Markup.XamlParseException))
        {
            current = current.InnerException;
        }
        return current;
    }

    private static void LogToDisk(string context, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(AppPaths.VaultPath) ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "crash.log");
            // Note: this log captures exception type, message, and stack trace.
            // By design Lumos never puts secret material (master password,
            // derived keys, decrypted entry fields) into exception messages, so
            // this file should not contain secrets — but it is PLAINTEXT and
            // sits next to the vault, so treat it as potentially sensitive
            // (it can reveal file paths and app internals). Safe to delete.
            var entry =
                $"=== {DateTimeOffset.UtcNow:O} — {context} ===\n" +
                $"{ex}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Logging failures must never themselves throw — we're already in
            // a degenerate state. Swallow.
        }
    }
}
