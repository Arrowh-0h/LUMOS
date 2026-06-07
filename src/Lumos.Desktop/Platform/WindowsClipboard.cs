using System.Windows;
using System.Windows.Threading;
using Lumos.Core.Security;

namespace Lumos.Desktop.Platform;

/// <summary>
/// IClipboard implementation backed by System.Windows.Clipboard. All clipboard
/// access must happen on the UI thread on Windows, so each call marshals via
/// the captured Dispatcher.
/// </summary>
public sealed class WindowsClipboard : IClipboard
{
    private readonly Dispatcher _dispatcher;

    public WindowsClipboard(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher
            ?? Application.Current?.Dispatcher
            ?? throw new InvalidOperationException(
                "No WPF Dispatcher available. Construct WindowsClipboard from the UI thread, " +
                "or pass a Dispatcher explicitly.");
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _dispatcher.Invoke(() =>
        {
            // Retry a few times — Clipboard.SetDataObject occasionally throws
            // ExternalException / COMException when another app has it locked.
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, copy: true);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(20);
                }
            }
            Clipboard.SetDataObject(text, copy: true);
        });
    }

    public string GetText()
    {
        return _dispatcher.Invoke(() =>
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() ?? "" : "";
            }
            catch (System.Runtime.InteropServices.COMException) { return ""; }
        });
    }

    public void Clear()
    {
        _dispatcher.Invoke(() =>
        {
            try { Clipboard.Clear(); }
            catch (System.Runtime.InteropServices.COMException) { /* best effort */ }
        });
    }
}
