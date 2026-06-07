using Lumos.Core.Crypto;
using Lumos.Core.Vault;
using Xunit;

namespace Lumos.Core.Tests;

public class VaultHeaderTests
{
    [Fact]
    public void Round_trip_preserves_everything()
    {
        var kdf = KdfParameters.CreateDefault();
        var wrapped = SecureMemory.RandomBytes(60);  // arbitrary blob

        var header = VaultHeader.Build(kdf, wrapped);
        var json = header.ToJson();
        var parsed = VaultHeader.FromJson(json);

        var kdf2 = parsed.ToKdfParameters();
        Assert.Equal(kdf.MemoryKb, kdf2.MemoryKb);
        Assert.Equal(kdf.Iterations, kdf2.Iterations);
        Assert.Equal(kdf.Parallelism, kdf2.Parallelism);
        Assert.Equal(kdf.KeyLengthBytes, kdf2.KeyLengthBytes);
        Assert.Equal(Convert.ToHexString(kdf.Salt), Convert.ToHexString(kdf2.Salt));
        Assert.Equal(Convert.ToHexString(wrapped), Convert.ToHexString(parsed.GetWrappedCipherKey()));
    }

    [Fact]
    public void Json_is_human_readable_and_contains_expected_fields()
    {
        var kdf = KdfParameters.CreateDefault();
        var wrapped = SecureMemory.RandomBytes(60);
        var json = VaultHeader.Build(kdf, wrapped).ToJson();

        Assert.Contains("\"kdfAlgorithm\": \"argon2id\"", json);
        Assert.Contains("\"kdfMemoryKb\": 65536", json);
        Assert.Contains("\"kdfIterations\": 3", json);
        Assert.Contains("\"kdfParallelism\": 4", json);
        Assert.Contains("\"wrappedCipherKey\":", json);
        Assert.Contains("\"cipher\": \"sqlite3mc-sqlcipher-v4\"", json);
        Assert.Contains("\"schemaVersion\": 2", json);
    }

    [Fact]
    public void Invalid_json_throws()
    {
        Assert.Throws<InvalidOperationException>(() => VaultHeader.FromJson("null"));
    }

    [Fact]
    public void Loaded_kdf_params_are_validated()
    {
        // Tampered: memory below minimum.
        var header = new VaultHeader
        {
            KdfMemoryKb = 1024,
            KdfIterations = 1,
            KdfParallelism = 1,
            KdfSaltBase64 = Convert.ToBase64String(new byte[16]),
            KdfKeyLengthBytes = 32,
            WrappedCipherKeyBase64 = Convert.ToBase64String(new byte[60]),
        };
        var kdf = header.ToKdfParameters();
        Assert.Throws<InvalidOperationException>(() => kdf.Validate());
    }
}
