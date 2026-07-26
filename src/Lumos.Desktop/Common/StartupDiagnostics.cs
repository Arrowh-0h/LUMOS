using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Lumos.Core;

namespace Lumos.Desktop.Common;

/// <summary>
/// Startup-time diagnostics and last-resort crash reporting.
///
/// App.xaml.cs already hooks DispatcherUnhandledException, AppDomain
/// UnhandledException and TaskScheduler.UnobservedTaskException — but all of
/// those are registered inside App.OnStartup. Anything that throws in
/// Program.Main before that point (Velopack hooks, `new App()`, XAML resource
/// dictionary parsing in InitializeComponent, or OnStartup itself running
/// before the dispatcher loop begins) escapes every one of them and kills the
/// process with a bare 0xE0434352 in the Windows event log.
///
/// This class covers that window, and writes an environment banner so bug
/// reports arrive with the machine details already attached.
/// </summary>
public static class StartupDiagnostics
{
    private const string LogFileName = "crash.log";

    /// <summary>
    /// Resolve the crash log path, degrading gracefully. AppPaths touches
    /// %APPDATA% and creates directories, either of which can fail on a locked
    /// down or redirected profile — so fall back to TEMP rather than throwing
    /// from inside the error handler.
    /// </summary>
    public static string ResolveLogPath()
    {
        try
        {
            return Path.Combine(AppPaths.AppDataDirectory, LogFileName);
        }
        catch
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "Lumos");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, LogFileName);
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "lumos-" + LogFileName);
            }
        }
    }

    /// <summary>
    /// Append an entry to crash.log. Never throws — we are frequently already
    /// in a degenerate state when this is called.
    /// </summary>
    public static void Log(string context, Exception? ex = null, string? detail = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("=== ").Append(DateTimeOffset.UtcNow.ToString("O"))
              .Append(" — ").Append(context).AppendLine(" ===");
            if (detail is not null) sb.AppendLine(detail);
            if (ex is not null) sb.AppendLine(ex.ToString());
            sb.AppendLine();

            File.AppendAllText(ResolveLogPath(), sb.ToString());
        }
        catch
        {
            // Logging must never be the thing that crashes us.
        }
    }

    /// <summary>
    /// Write machine + build details once per launch, including a live test of
    /// the native encryption stack. This is what turns "it crashes on some
    /// laptops" into a report you can act on without a back-and-forth.
    ///
    /// Deliberately contains NO secret material — no vault contents, no key
    /// material, no master password. Paths and versions only.
    /// </summary>
    public static void WriteEnvironmentBanner()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- environment ---");
            sb.Append("lumos      : ").AppendLine(GetAppVersion());
            sb.Append("os         : ").AppendLine(RuntimeInformation.OSDescription);
            sb.Append("os arch    : ").AppendLine(RuntimeInformation.OSArchitecture.ToString());
            sb.Append("proc arch  : ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString());
            sb.Append("runtime    : ").AppendLine(RuntimeInformation.FrameworkDescription);
            sb.Append("base dir   : ").AppendLine(AppContext.BaseDirectory);
            sb.Append("ram (avail): ").AppendLine(FormatBytes(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));

            // Is the native SQLite3MC binary actually on disk? If an antivirus
            // quarantined it during install, this is where that becomes visible.
            sb.Append("native libs: ").AppendLine(DescribeNativeFiles());

            // And does it actually work?
            var probe = LumosCoreBootstrap.SelfTest();
            sb.Append("self-test  : ").AppendLine(DescribeProbe(probe));

            Log("startup", detail: sb.ToString());
        }
        catch (Exception ex)
        {
            Log("startup banner failed", ex);
        }
    }

    /// <summary>
    /// Last-resort handler for a fatal exception in Program.Main. Logs the full
    /// exception, shows the user something they can act on, then exits with a
    /// non-zero code.
    /// </summary>
    public static void FatalStartupFailure(Exception ex)
    {
        Log("FATAL startup failure", ex);

        var logPath = ResolveLogPath();
        var advice = Explain(ex);

        var message =
            "Lumos couldn't start.\n\n" +
            advice + "\n\n" +
            $"Technical details were written to:\n{logPath}\n\n" +
            "Please attach that file to a bug report at\n" +
            "https://github.com/Arrowh-0h/LUMOS/issues";

        try
        {
            MessageBox.Show(message, "Lumos — startup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // No UI available (very early failure). The log is still on disk.
        }

        Environment.Exit(1);
    }

    /// <summary>
    /// Translate the exceptions we actually expect into plain language. Anything
    /// unrecognised falls through to a generic message — we never pretend to
    /// know a cause we haven't identified.
    /// </summary>
    private static string Explain(Exception ex)
    {
        // Unwrap the usual suspects so we classify on the real cause.
        var root = ex;
        while (root.InnerException is not null &&
               (root is TypeInitializationException
                || root is TargetInvocationException
                || root is AggregateException))
        {
            root = root.InnerException;
        }

        var text = root.ToString();
        var mentionsNativeSqlite =
            text.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SQLitePCL", StringComparison.OrdinalIgnoreCase);

        if (root is DllNotFoundException || (mentionsNativeSqlite && root is not OutOfMemoryException))
        {
            return
                "Lumos's encryption library (e_sqlite3mc.dll) could not be loaded.\n\n" +
                "This is most often caused by antivirus software quarantining the file " +
                "during installation — Lumos is not code-signed, which some scanners treat " +
                "as suspicious.\n\n" +
                "Try: reinstall Lumos, and if your antivirus reports anything, add an " +
                "exclusion for the Lumos install folder:\n" +
                $"{AppContext.BaseDirectory}";
        }

        if (root is BadImageFormatException)
        {
            return
                "Lumos's encryption library was found but is built for a different CPU " +
                $"architecture than this machine ({RuntimeInformation.OSArchitecture}).\n\n" +
                "Please download the installer matching your processor type.";
        }

        if (root is OutOfMemoryException)
        {
            return
                "Lumos ran out of memory during startup. Key derivation (Argon2id) " +
                "reserves 64 MB — closing other applications and retrying may help.";
        }

        if (root is UnauthorizedAccessException || root is IOException)
        {
            return
                "Lumos could not read or write its data folder:\n" +
                $"{TryGetAppDataDir()}\n\n" +
                "Check that the folder exists and that your user account has permission " +
                "to write to it.";
        }

        return $"An unexpected error occurred: {root.GetType().Name}: {root.Message}";
    }

    // ---- small helpers ----

    private static string GetAppVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            var informational = asm?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational ?? asm?.GetName().Version?.ToString() ?? "unknown";
        }
        catch { return "unknown"; }
    }

    /// <summary>
    /// One-line summary of the native self-test. Failures can arrive either as
    /// an exception or as a plain explanation (e.g. encryption silently not
    /// applied), so handle both rather than printing a bare "FAILED".
    /// </summary>
    private static string DescribeProbe(NativeSelfTestResult probe)
    {
        if (probe.Success)
            return $"ok ({probe.Detail})";

        var cause = probe.Failure is not null
            ? $"{probe.Failure.GetType().Name}: {probe.Failure.Message}"
            : probe.Detail ?? "no further detail";

        return $"FAILED at stage '{probe.Stage}' — {cause}";
    }

    private static string DescribeNativeFiles()
    {
        try
        {
            var files = Directory.GetFiles(AppContext.BaseDirectory, "e_sqlite3*.dll",
                SearchOption.AllDirectories);
            if (files.Length == 0) return "NONE FOUND (expected e_sqlite3mc.dll)";

            var sb = new StringBuilder();
            foreach (var f in files)
            {
                var info = new FileInfo(f);
                sb.Append(Path.GetFileName(f)).Append(" (").Append(info.Length).Append(" bytes) ");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"could not enumerate: {ex.GetType().Name}";
        }
    }

    private static string TryGetAppDataDir()
    {
        try { return AppPaths.AppDataDirectory; }
        catch { return "%APPDATA%\\Lumos"; }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "unknown";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
