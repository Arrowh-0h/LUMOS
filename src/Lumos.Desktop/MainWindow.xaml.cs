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
        Loaded += (_, _) => RouteVaultView();
        StateChanged += OnStateChanged;
        Closed += (_, _) => TearDownShell();
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
        vm.VaultUnlocked += OnVaultUnlocked;
        vm.ForgotPasswordRequested += (_, _) => ShowRecoveryReset();
        var view = new UnlockView { DataContext = vm };
        MainContent.Content = view;
    }

    private void OnVaultUnlocked(object? sender, VaultUnlockedEventArgs e)
    {
        // A vault with no recovery code gets the one-time explainer before the
        // shell. This can only happen here: writing the recovery wrap needs the
        // cipher key, which needs the master password, which only exists at
        // unlock time.
        if (e.RequiresRecoverySetup)
        {
            ShowRecoverySetupPrompt();
            return;
        }
        ShowShell();
    }

    private void ShowCreateVault()
    {
        var vm = new CreateVaultViewModel();
        vm.VaultCreated += OnVaultCreated;
        var view = new CreateVaultView { DataContext = vm };
        MainContent.Content = view;
    }

    private void OnVaultCreated(object? sender, VaultCreatedEventArgs e)
    {
        // The code was issued during creation, while the master password was
        // still in hand. If that failed, the vault is still fine — go straight
        // to the shell and prompt again at the next unlock.
        if (e.RecoveryCode is null)
        {
            ShowShell();
            return;
        }
        ShowRecoveryCode(e.RecoveryCode, isNewVault: true);
    }

    // -------- Recovery flows --------

    /// <summary>
    /// The one-time migration prompt for vaults that predate recovery codes.
    /// Declining is a real option: locking someone out of their own vault with
    /// a nag screen is worse than letting them postpone.
    /// </summary>
    private void ShowRecoverySetupPrompt()
    {
        var vm = new RecoverySetupPromptViewModel();
        vm.RecoveryIssued += (_, args) => ShowRecoveryCode(args.Code, isNewVault: false);
        vm.Deferred += (_, _) => ShowShell();
        MainContent.Content = new RecoverySetupPromptView { DataContext = vm };
    }

    /// <summary>
    /// Display a freshly issued recovery code. This is the only time it exists
    /// in plaintext, so the view gates Continue behind an explicit
    /// acknowledgement.
    /// </summary>
    private void ShowRecoveryCode(string code, bool isNewVault)
    {
        var vm = new RecoveryCodeViewModel(code, new WindowsFileDialogService(), isNewVault);
        vm.Acknowledged += (_, _) => ShowShell();
        MainContent.Content = new RecoveryCodeView { DataContext = vm };
    }

    /// <summary>The "I forgot my master password" route, reached from the unlock screen.</summary>
    private void ShowRecoveryReset()
    {
        var vm = new RecoveryResetViewModel();
        vm.ResetSucceeded += (_, _) => ShowShell();
        vm.Cancelled += (_, _) => ShowUnlock();
        MainContent.Content = new RecoveryResetView { DataContext = vm };
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

            // Vault contents are on screen from here on — exclude the window
            // from screenshots and screen sharing. Deliberately not applied to
            // the unlock or error screens, so users can still capture a bug
            // report.
            ScreenCaptureProtection.Apply(this, enabled: true);
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

        // Nothing sensitive is on screen once we're back at the unlock prompt,
        // and leaving capture blocked there would prevent bug-report screenshots.
        ScreenCaptureProtection.Apply(this, enabled: false);

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
