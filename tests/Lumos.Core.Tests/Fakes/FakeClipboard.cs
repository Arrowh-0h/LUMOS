using Lumos.Core.Security;

namespace Lumos.Core.Tests.Fakes;

/// <summary>
/// In-memory clipboard for tests. Thread-safe.
/// </summary>
internal sealed class FakeClipboard : IClipboard
{
    private readonly object _gate = new();
    private string _value = "";
    public int ClearCount { get; private set; }
    public int SetTextCount { get; private set; }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            _value = text;
            SetTextCount++;
        }
    }

    public string GetText()
    {
        lock (_gate) return _value;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _value = "";
            ClearCount++;
        }
    }
}
