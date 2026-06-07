using System.Security.Cryptography;
using System.Text;
using Lumos.Core.Crypto;
using Xunit;

namespace Lumos.Core.Tests;

public class AesGcmCryptoTests
{
    [Fact]
    public void Round_trip_returns_original_plaintext()
    {
        var key = SecureMemory.RandomBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("the wand chooses the wizard, Mr. Potter");

        var envelope = AesGcmCrypto.Encrypt(key, plaintext);
        var decrypted = AesGcmCrypto.Decrypt(key, envelope);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Wrong_key_fails_authentication()
    {
        var key1 = SecureMemory.RandomBytes(32);
        var key2 = SecureMemory.RandomBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("secret");

        var envelope = AesGcmCrypto.Encrypt(key1, plaintext);

        // In .NET 8, AesGcm.Decrypt throws AuthenticationTagMismatchException,
        // which inherits from CryptographicException. Assert the base class so
        // both .NET 8 and possible older targets are covered.
        var ex = Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(key2, envelope));
        Assert.IsAssignableFrom<CryptographicException>(ex);
    }

    [Fact]
    public void Tampered_ciphertext_fails_authentication()
    {
        var key = SecureMemory.RandomBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("secret");

        var envelope = AesGcmCrypto.Encrypt(key, plaintext);
        envelope[envelope.Length / 2] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(key, envelope));
    }

    [Fact]
    public void Associated_data_must_match_on_decrypt()
    {
        var key = SecureMemory.RandomBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("secret");
        var ad1 = Encoding.UTF8.GetBytes("user-42");
        var ad2 = Encoding.UTF8.GetBytes("user-99");

        var envelope = AesGcmCrypto.Encrypt(key, plaintext, ad1);

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCrypto.Decrypt(key, envelope, ad2));
        var ok = AesGcmCrypto.Decrypt(key, envelope, ad1);
        Assert.Equal(plaintext, ok);
    }

    [Fact]
    public void Each_encrypt_uses_fresh_nonce()
    {
        var key = SecureMemory.RandomBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("same input");

        var a = AesGcmCrypto.Encrypt(key, plaintext);
        var b = AesGcmCrypto.Encrypt(key, plaintext);

        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(b));
    }

    [Fact]
    public void Rejects_wrong_size_key()
    {
        var shortKey = new byte[16];
        var plaintext = Encoding.UTF8.GetBytes("x");
        Assert.Throws<ArgumentException>(() => AesGcmCrypto.Encrypt(shortKey, plaintext));
    }
}
