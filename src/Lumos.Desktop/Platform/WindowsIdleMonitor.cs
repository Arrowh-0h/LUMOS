using System.Runtime.InteropServices;
using Lumos.Core.Security;

namespace Lumos.Desktop.Platform;

/// <summary>
/// Reports system-wide user idle time using the Win32 GetLastInputInfo API.
/// Idle time is measured from the most recent keyboard or mouse input.
/// </summary>
public sealed class WindowsIdleMonitor : IIdleMonitor
{
    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>(),
        };
        if (!GetLastInputInfo(ref info))
        {
            // P/Invoke failed; report zero idle to avoid false locks.
            return TimeSpan.Zero;
        }

        // Environment.TickCount and dwTime are both 32-bit unsigned millisecond
        // counters that wrap every ~49.7 days. Subtract as uint to handle
        // wrap-around correctly.
        uint nowTicks = (uint)Environment.TickCount;
        uint idleMs = nowTicks - info.dwTime;
        return TimeSpan.FromMilliseconds(idleMs);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
