using Lumos.Core.Crypto;
using Xunit;

namespace Lumos.Core.Tests;

public class SecureMemoryTests
{
    [Fact]
    public void Zero_clears_all_bytes()
    {
        var buffer = new byte[] { 1, 2, 3, 4, 5, 0xFF, 0xAA };
        SecureMemory.Zero(buffer);
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Zero_handles_null_and_empty()
    {
        SecureMemory.Zero(null);
        SecureMemory.Zero(Array.Empty<byte>());
        // Should not throw.
    }

    [Fact]
    public void RandomBytes_returns_requested_length()
    {
        var b = SecureMemory.RandomBytes(32);
        Assert.Equal(32, b.Length);
    }

    [Fact]
    public void RandomBytes_returns_distinct_values()
    {
        var a = SecureMemory.RandomBytes(32);
        var b = SecureMemory.RandomBytes(32);
        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(b));
    }

    [Fact]
    public void RandomBytes_rejects_invalid_length()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SecureMemory.RandomBytes(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecureMemory.RandomBytes(-1));
    }

    [Fact]
    public void ConstantTimeEquals_works()
    {
        var a = new byte[] { 1, 2, 3, 4 };
        var b = new byte[] { 1, 2, 3, 4 };
        var c = new byte[] { 1, 2, 3, 5 };

        Assert.True(SecureMemory.ConstantTimeEquals(a, b));
        Assert.False(SecureMemory.ConstantTimeEquals(a, c));
    }
}
