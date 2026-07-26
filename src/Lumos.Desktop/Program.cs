using System;
using Lumos.Desktop.Common;
using Velopack;

namespace Lumos.Desktop;

/// <summary>
/// Explicit application entry point. Velopack STRONGLY recommends that
/// VelopackApp.Build().Run() be the very first thing executed — before WPF
/// initializes — so that install/update/uninstall hooks (which launch the
/// app with special arguments) are handled and exit before any UI loads.
///
/// We therefore define our own Main here instead of letting WPF generate one.
/// (App.xaml's Build Action is set to ApplicationDefinition, but the project's
/// StartupObject points at this class, which suppresses the generated Main.
/// See Lumos.Desktop.csproj.)
///
/// v2 change: the whole body is now guarded. App.OnStartup registers the
/// global exception sinks, but everything BEFORE that point — Velopack hooks,
/// `new App()`, InitializeComponent() parsing the theme dictionaries, and
/// OnStartup itself (which runs before the dispatcher loop starts, so
/// DispatcherUnhandledException does not reliably cover it) — was completely
/// unprotected. A throw in that window killed the process with nothing but a
/// 0xE0434352 entry in the Windows event log and no crash.log at all.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 1) Velopack first. Fast no-op on a normal launch; on install/update
            //    hooks it does its work and exits before we ever start WPF.
            //    Still guarded separately so a dev `dotnet run` (no Velopack
            //    metadata) proceeds — but now we record it instead of swallowing
            //    it silently, because a genuine Velopack failure here is exactly
            //    the kind of thing behind an "Install Partially Succeeded".
            try
            {
                VelopackApp.Build().Run();
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log("Velopack init (non-fatal — expected in dev)", ex);
            }

            // 2) Record the environment and probe the native encryption stack
            //    BEFORE any UI exists. If e_sqlite3mc.dll is missing or broken,
            //    this is where we find out — with a message that names the cause.
            StartupDiagnostics.WriteEnvironmentBanner();

            // 3) Now start WPF normally.
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            // Anything that reaches here bypassed every handler in App.xaml.cs.
            // Log it, tell the user something useful, exit non-zero.
            StartupDiagnostics.FatalStartupFailure(ex);
        }
    }
}
