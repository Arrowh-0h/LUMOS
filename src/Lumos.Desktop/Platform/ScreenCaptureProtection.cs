using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lumos.Desktop.Platform;

/// <summary>
/// Asks Windows to exclude a window from screen capture.
///
/// Uses SetWindowDisplayAffinity with WDA_EXCLUDEFROMCAPTURE (Windows 10 2004
/// and later). The window stays fully visible to the person sitting at the
/// machine, but screenshots, screen recorders, and remote-desktop/screen-share
/// tools see an empty region where it should be.
///
/// HONEST SCOPE — this is a guard against the ordinary accidents (an
/// unattended screen recorder, a shared Teams call, malware doing naive
/// GDI captures), NOT a defence against a compromised machine. Anything with
/// the privileges to read another process's memory can read the vault contents
/// directly and never needs to take a screenshot. It also cannot stop someone
/// photographing the monitor with a phone.
///
/// Applied only while the vault is unlocked. Blanketing the unlock and error
/// screens too would mean users could not screenshot a crash or an error
/// message to send in a bug report — which, given we are actively chasing a
/// crash, would cost us more than it protects.
/// </summary>
public static class ScreenCaptureProtection
{
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>
    /// Turn capture protection on or off for a window.
    /// Returns false if the platform refused (older Windows builds return an
    /// error for WDA_EXCLUDEFROMCAPTURE) or the window has no handle yet.
    /// Never throws: this is a hardening measure, not a functional requirement,
    /// so failing to apply it must not stop the app from working.
    /// </summary>
    public static bool Apply(Window window, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;   // not shown yet

            return SetWindowDisplayAffinity(
                handle, enabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }
        catch
        {
            return false;
        }
    }
}
