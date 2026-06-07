using System.Windows;
using Lumos.Core.Entries;
using Lumos.Core.Security;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;
using Lumos.Desktop.ViewModels;
using Lumos.Desktop.Views;

namespace Lumos.Desktop;

public partial class MainWindow : Window
{
    private ShellViewModel? _shellVm;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RouteInitialView();
        StateChanged += OnStateChanged;
        Closed += (_, _) => TearDownShell();
    }

    private readonly LicenseStore _license = new();

    /// <summary>
    /// First gate: require product-key activation. Once activated (now or
    /// previously), fall through to the normal create/unlock routing.
    /// </summary>
    private void RouteInitialView()
    {
        if (!_license.IsActivated())
        {
            ShowActivation();
            return;
        }
        RouteVaultView();
    }

    private void ShowActivation()
    {
        var vm = new ActivationViewModel(_license);
        vm.Activated += (_, _) => RouteVaultView();
        MainContent.Content = new ActivationView { DataContext = vm };
    }

    /// <summary>
    /// Decide whether to show "create vault" or "unlock" based on whether
    /// a vault already exists on disk.
    /// </summary>
    private void RouteVaultView()
    {
        if (AppServices.VaultManager.VaultExists)
        {
            ShowUnlock();
        }
        else
        {
            ShowCreateVault();
        }
    }

    private void ShowUnlock()
    {
        var vm = new UnlockViewModel();
        vm.VaultUnlocked += (_, _) => ShowShell();
        var view = new UnlockView { DataContext = vm };
        MainContent.Content = view;
    }

    private void ShowCreateVault()
    {
        var vm = new CreateVaultViewModel();
        vm.VaultCreated += (_, _) => ShowShell();
        var view = new CreateVaultView { DataContext = vm };
        MainContent.Content = view;
    }

    private void ShowShell()
    {
        try
        {
            var openVault = AppServices.OpenVault
                ?? throw new InvalidOperationException("ShowShell called with no open vault.");

            var entryRepo = new EntryRepository(openVault);
            var folderRepo = new FolderRepository(openVault);
            var attachmentRepo = new Lumos.Core.Attachments.AttachmentRepository(openVault);

            // Build the platform-specific event sources. The system event source
            // hooks the main window's StateChanged so it can detect minimize.
            var idleMonitor = new WindowsIdleMonitor();
            var systemEvents = new WindowsSystemEventSource(this,
                AutoLockSettings.Default.MinimizeThreshold);

            // Keep lock-on-minimize OFF by default.
            var settings = AutoLockSettings.Default with { LockOnMinimize = false };

            _shellVm = new ShellViewModel(
                entryRepo,
                folderRepo,
                attachmentRepo,
                idleMonitor,
                systemEvents,
                settings,
                new WindowsFileDialogService());
            _shellVm.LockRequested += OnShellLockRequested;

            MainContent.Content = new ShellView { DataContext = _shellVm };
        }
        catch (Exception ex)
        {
            // Surface the error to the user instead of failing silently.
            // Without this, an exception in shell construction just hides the
            // app — the unlock screen stays put with no feedback.
            MessageBox.Show(
                $"Failed to open the vault shell:\n\n{ex}",
                "Lumos — shell error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OnShellLockRequested(object? sender, LockRequestedEventArgs e)
    {
        // Lock the vault: clear clipboard if we own it, dispose the open
        // vault, drop the shell, route back to UnlockView.
        try
        {
            AppServices.Clipboard.ClearNowIfOurs();
        }
        catch { /* best effort */ }

        TearDownShell();
        AppServices.OpenVault?.Dispose();
        AppServices.OpenVault = null;

        ShowUnlock();
    }

    private void TearDownShell()
    {
        if (_shellVm is not null)
        {
            _shellVm.LockRequested -= OnShellLockRequested;
            _shellVm.Dispose();
            _shellVm = null;
        }
    }

    // -------- Window chrome handlers --------

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Swap the glyph between maximize / restore so the icon matches state.
        MaximizeBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }
}
