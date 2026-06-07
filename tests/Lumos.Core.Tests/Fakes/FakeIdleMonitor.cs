using Lumos.Core.Security;

namespace Lumos.Core.Tests.Fakes;

internal sealed class FakeIdleMonitor : IIdleMonitor
{
    public TimeSpan IdleTime { get; set; } = TimeSpan.Zero;
    public TimeSpan GetIdleTime() => IdleTime;
}
