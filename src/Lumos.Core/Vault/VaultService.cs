using Microsoft.Data.Sqlite;

namespace Lumos.Core.Vault;

/// <summary>
/// Owns the SQLite3MC connection for an unlocked vault.
///
/// IMPORTANT: This layer takes the CIPHER KEY directly — the 32 raw bytes
/// that SQLite3MC uses to encrypt the database. It does NOT do password-to-key
/// derivation. That's VaultManager's job, which derives a WRAPPING key from
/// the user password and uses it to decrypt the cipher key stored in the
/// vault header.
///
/// This split exists because SQLite3MC's PRAGMA rekey path applies the
/// cipher's internal KDF to whatever you give it — there's no way to set a
/// new raw cipher key. By keeping the cipher key fixed for the life of the
/// vault and changing only the wrapping key on password change, we get
/// instant password changes and avoid SQLite3MC's rekey quirks entirely.
/// </summary>
public sealed class VaultService : IDisposable
{
    private SqliteConnection? _connection;
    private byte[]? _cipherKey;

    public string DatabasePath { get; }
    public string HeaderPath => DatabasePath + ".header.json";
    public bool IsOpen => _connection is not null;

    private VaultService(string dbPath)
    {
        DatabasePath = dbPath;
    }

    /// <summary>
    /// Create a brand-new vault DB file, encrypted under <paramref name="cipherKey"/>.
    /// Caller owns cipherKey and must zero it when done — we hold a copy
    /// internally so the caller can dispose theirs safely.
    /// </summary>
    public static VaultService Create(string dbPath, byte[] cipherKey)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(cipherKey);
        if (cipherKey.Length != 32)
            throw new ArgumentException("Cipher key must be 32 bytes.", nameof(cipherKey));
        if (File.Exists(dbPath))
            throw new InvalidOperationException($"Vault DB already exists at {dbPath}.");

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var service = new VaultService(dbPath);
        try
        {
            service.OpenEncryptedConnection(cipherKey, isNewVault: true);
            service.VerifyEncryptionEngaged();
            using (var fk = service._connection!.CreateCommand())
            {
                fk.CommandText = "PRAGMA foreign_keys = ON;";
                fk.ExecuteNonQuery();
            }
            service.InitializeSchema();
            service._cipherKey = (byte[])cipherKey.Clone();
            return service;
        }
        catch
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
            service.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Open an existing vault DB with the given cipher key. The key was
    /// recovered by VaultManager from the header envelope using the user's
    /// master password.
    ///
    /// Returns null on wrong-cipher-key (which means the wrapped key was
    /// decrypted incorrectly — shouldn't happen in normal flow because the
    /// AES-GCM auth tag catches that earlier — but defense in depth).
    /// </summary>
    public static VaultService? Open(string dbPath, byte[] cipherKey)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(cipherKey);
        if (cipherKey.Length != 32)
            throw new ArgumentException("Cipher key must be 32 bytes.", nameof(cipherKey));
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Vault DB not found.", dbPath);

        var service = new VaultService(dbPath);
        try
        {
            service.OpenEncryptedConnection(cipherKey, isNewVault: false);

            // Probe to verify the key. If the cipher key is wrong, SQLite3MC
            // returns NOTADB (26) on the first real query.
            using (var cmd = service._connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
                _ = cmd.ExecuteScalar();
            }

            // Key was good. Enable FK enforcement (per-connection, off by default)
            // and run any pending schema migrations for vaults created with
            // an older app version.
            using (var fk = service._connection!.CreateCommand())
            {
                fk.CommandText = "PRAGMA foreign_keys = ON;";
                fk.ExecuteNonQuery();
            }
            SchemaMigrator.EnsureUpToDate(service._connection!);

            service._cipherKey = (byte[])cipherKey.Clone();
            return service;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26 || ex.SqliteErrorCode == 23)
        {
            service.Dispose();
            return null;
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    private void OpenEncryptedConnection(byte[] key, bool isNewVault)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = isNewVault ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
        }.ToString();

        _connection = new SqliteConnection(connStr);
        _connection.Open();

        // ORDER MATTERS: cipher PRAGMA first, then key PRAGMA.
        // If we set the key before the cipher, SQLite3MC locks in the
        // default cipher and our SQLCipher-v4 config won't apply.
        ExecutePragma("cipher='sqlcipher'");
        ExecutePragma("legacy=0");
        ExecutePragma($"key=\"x'{Convert.ToHexString(key)}'\"");
    }

    private void ExecutePragma(string pragmaBody)
    {
        if (_connection is null) throw new InvalidOperationException();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {pragmaBody};";
        cmd.ExecuteNonQuery();
    }

    private void VerifyEncryptionEngaged()
    {
        if (_connection is null) throw new InvalidOperationException();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT sqlite3mc_version();";
        try
        {
            var version = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(
                    "SQLite3MC version function returned empty — encryption layer is not loaded.");
            }
        }
        catch (SqliteException)
        {
            throw new InvalidOperationException(
                "SQLite3MC is not loaded (sqlite3mc_version() unavailable). " +
                "Check that SQLitePCLRaw.bundle_e_sqlite3mc is referenced.");
        }
    }

    private void InitializeSchema()
    {
        if (_connection is null) throw new InvalidOperationException();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_version', '1');
                INSERT OR IGNORE INTO meta(key, value) VALUES ('created_utc', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """;
            cmd.ExecuteNonQuery();
        }

        // Apply any pending schema migrations.
        SchemaMigrator.EnsureUpToDate(_connection);
    }

    internal SqliteConnection RequireConnection()
    {
        return _connection ?? throw new InvalidOperationException("Vault is not open.");
    }

    /// <summary>
    /// Encrypt arbitrary plaintext under the vault's cipher key.
    /// Used to seal sensitive data (like backend session tokens) that lives
    /// outside the vault DB but must only be readable while the vault is
    /// unlocked. Returns a self-contained AES-GCM envelope (nonce + ciphertext + tag).
    /// </summary>
    public byte[] SealUnderVaultKey(byte[] plaintext, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (_cipherKey is null) throw new InvalidOperationException("Vault is not unlocked.");
        return Crypto.AesGcmCrypto.Encrypt(_cipherKey, plaintext, associatedData);
    }

    /// <summary>
    /// Decrypt an envelope previously produced by <see cref="SealUnderVaultKey"/>.
    /// Throws CryptographicException if the envelope is corrupt or the
    /// associated data doesn't match.
    /// </summary>
    public byte[] OpenUnderVaultKey(byte[] envelope, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_cipherKey is null) throw new InvalidOperationException("Vault is not unlocked.");
        return Crypto.AesGcmCrypto.Decrypt(_cipherKey, envelope, associatedData);
    }

    public void Dispose()
    {
        try
        {
            if (_connection is not null)
            {
                _connection.Close();
                _connection.Dispose();
                SqliteConnection.ClearAllPools();
            }
        }
        finally
        {
            _connection = null;
            if (_cipherKey is not null)
            {
                Crypto.SecureMemory.Zero(_cipherKey);
                _cipherKey = null;
            }
        }
    }
}
