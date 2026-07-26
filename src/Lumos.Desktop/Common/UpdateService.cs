using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Lumos.Desktop.Common;

/// <summary>
/// Result of an update check.
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>A newer version is available (see <see cref="UpdateCheckResult.NewVersion"/>).</summary>
    UpdateAvailable,
    /// <summary>Already on the latest version.</summary>
    UpToDate,
    /// <summary>The app isn't running from a Velopack install (e.g. dev `dotnet run`).</summary>
    NotInstalled,
    /// <summary>The check failed (no network, GitHub unreachable, etc.).</summary>
    Failed,
    /// <summary>
    /// This build's architecture is not distributed through the update feed.
    /// Currently ARM64: only the x64 build is published to GitHub Releases, so
    /// an ARM64 install polling that feed would be offered an x64 package and
    /// would overwrite itself with the wrong architecture.
    /// </summary>
    UnsupportedArchitecture,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? NewVersion, string? Error);

/// <summary>
/// Thin wrapper over Velopack's UpdateManager. Lumos is offline by default;
/// this is the ONLY component that touches the network, and only when the user
/// explicitly asks (a manual "Check for updates" action). There is no automatic
/// or background checking.
///
/// Updates come from GitHub Releases. The vault in %APPDATA%\Lumos is never
/// touched by an update — only the application files under %LocalAppData%\Lumos
/// are replaced.
/// </summary>
public sealed class UpdateService
{
    // The GitHub repository that hosts Lumos releases. The updater reads the
    // Releases of this repo to find newer versions. Use the repo URL (not the
    // .git clone URL). Until a release is published there, checks return
    // UpToDate or Failed, which is harmless.
    public const string RepositoryUrl = "https://github.com/Arrowh-0h/LUMOS";

    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    /// <summary>
    /// Check GitHub for a newer release. Network call — only invoked from the
    /// user's explicit "Check for updates" action.
    /// </summary>
    /// <summary>
    /// True when this build must not use the GitHub update feed.
    ///
    /// v2.0.0 ships only the x64 installer to GitHub Releases; the ARM64 build
    /// is distributed from the website instead. Both architectures otherwise
    /// poll the same default Velopack channel, so an ARM64 install left to its
    /// own devices would find the x64 release, accept it as "newer", and
    /// replace itself with a package for the wrong CPU.
    ///
    /// A note in the docs is not enough protection for something that breaks a
    /// user's installation, so the check is blocked in code and the user is
    /// pointed at the website download instead.
    /// </summary>
    public static bool UpdatesAvailableForThisBuild
        => RuntimeInformation.ProcessArchitecture != Architecture.Arm64;

    public async Task<UpdateCheckResult> CheckAsync()
    {
        if (!UpdatesAvailableForThisBuild)
            return new UpdateCheckResult(UpdateCheckStatus.UnsupportedArchitecture, null, null);

        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));

            // IsInstalled is false under `dotnet run` / unpacked dev builds.
            if (!_manager.IsInstalled)
                return new UpdateCheckResult(UpdateCheckStatus.NotInstalled, null, null);

            _pendingUpdate = await _manager.CheckForUpdatesAsync();
            if (_pendingUpdate is null)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);

            var version = _pendingUpdate.TargetFullRelease.Version.ToString();
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, version, null);
        }
        catch (Exception ex)
        {
            // Any failure (offline, GitHub unreachable, not a Velopack install,
            // etc.) is reported as a non-fatal check failure. The IsInstalled
            // guard above already handles the normal dev-run case.
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, ex.Message);
        }
    }

    /// <summary>
    /// Download the pending update and apply it, restarting the app. Only valid
    /// after a successful <see cref="CheckAsync"/> that returned UpdateAvailable.
    /// On success this does not return — the app exits and relaunches.
    /// </summary>
    public async Task<string?> DownloadAndApplyAsync()
    {
        if (!UpdatesAvailableForThisBuild)
            return "This ARM64 build updates from the Lumos website, not from here.";

        if (_manager is null || _pendingUpdate is null)
            return "No update is pending. Run a check first.";

        try
        {
            await _manager.DownloadUpdatesAsync(_pendingUpdate);
            // Replaces the app and relaunches. The vault in %APPDATA% is untouched.
            _manager.ApplyUpdatesAndRestart(_pendingUpdate);
            return null; // unreachable on success
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
