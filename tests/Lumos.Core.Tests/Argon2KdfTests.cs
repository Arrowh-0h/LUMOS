using System.Text;
using Lumos.Core.Crypto;
using Xunit;

namespace Lumos.Core.Tests;

public class Argon2KdfTests
{
    // Use light parameters for fast tests. The real defaults (64 MB / 3 iter)
    // are tested by the vault round-trip test and are slow.
    private static KdfParameters LightParams(byte[] salt) => new(
        MemoryKb: 8 * 1024,   // 8 MB - minimum allowed
        Iterations: 1,
        Parallelism: 1,
        Salt: salt,
        KeyLengthBytes: 32);

    [Fact]
    public void Same_password_and_salt_produces_same_key()
    {
        var salt = SecureMemory.RandomBytes(16);
        var pw = Encoding.UTF8.GetBytes("correct horse battery staple");

        var k1 = Argon2Kdf.DeriveKey(pw, LightParams(salt));
        var k2 = Argon2Kdf.DeriveKey(pw, LightParams(salt));

        Assert.Equal(Convert.ToHexString(k1), Convert.ToHexString(k2));
        Assert.Equal(32, k1.Length);
    }

    [Fact]
    public void Different_password_produces_different_key()
    {
        var salt = SecureMemory.RandomBytes(16);
        var pw1 = Encoding.UTF8.GetBytes("password-one");
        var pw2 = Encoding.UTF8.GetBytes("password-two");

        var k1 = Argon2Kdf.DeriveKey(pw1, LightParams(salt));
        var k2 = Argon2Kdf.DeriveKey(pw2, LightParams(salt));

        Assert.NotEqual(Convert.ToHexString(k1), Convert.ToHexString(k2));
    }

    [Fact]
    public void Different_salt_produces_different_key()
    {
        var salt1 = SecureMemory.RandomBytes(16);
        var salt2 = SecureMemory.RandomBytes(16);
        var pw = Encoding.UTF8.GetBytes("same password");

        var k1 = Argon2Kdf.DeriveKey(pw, LightParams(salt1));
        var k2 = Argon2Kdf.DeriveKey(pw, LightParams(salt2));

        Assert.NotEqual(Convert.ToHexString(k1), Convert.ToHexString(k2));
    }

    [Fact]
    public void Empty_password_throws()
    {
        var salt = SecureMemory.RandomBytes(16);
        Assert.Throws<ArgumentException>(
            () => Argon2Kdf.DeriveKey(Array.Empty<byte>(), LightParams(salt)));
    }

    [Fact]
    public void Default_parameters_validate()
    {
        var kdf = KdfParameters.CreateDefault();
        kdf.Validate(); // should not throw
        Assert.Equal(64 * 1024, kdf.MemoryKb);
        Assert.Equal(3, kdf.Iterations);
        Assert.Equal(4, kdf.Parallelism);
        Assert.Equal(32, kdf.KeyLengthBytes);
        Assert.Equal(16, kdf.Salt.Length);
    }
}
