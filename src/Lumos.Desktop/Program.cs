using System;
using Velopack;

namespace Lumos.Desktop;

/// <summary>
/// Explicit application entry point. Velopack STRONGLY recommends that
/// VelopackApp.Build().Run() be the very first thing executed — before WPF
/// initializes — so that install/update/uninstall hooks (which launch the
/// app with special arguments) are handled and exit before any UI loads.
///
/// We therefore define our own Main here instead of letting WPF generate one.
/// (App.xaml's Build Action is set to ApplicationDefinition, but the project
/// sets EnableDefaultApplicationDefinition=false so this Main is the entry
/// point. See Lumos.Desktop.csproj.)
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1) Velopack first. Fast no-op on a normal launch; on install/update
        //    hooks it does its work and exits before we ever start WPF. Guarded
        //    so a dev `dotnet run` (no Velopack metadata) still proceeds.
        try
        {
            VelopackApp.Build().Run();
        }
        catch
        {
            // Not running under a Velopack install — fine in dev.
        }

        // 2) Now start WPF normally.
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
