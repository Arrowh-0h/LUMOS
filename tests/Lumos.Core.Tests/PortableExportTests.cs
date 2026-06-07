using System.Text;
using System.Text.Json;
using Lumos.Core;
using Lumos.Core.Crypto;
using Lumos.Core.Entries;
using Lumos.Core.PortableExport;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class PortableExportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VaultService _vault;
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;

    public PortableExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
        var key = SecureMemory.RandomBytes(32);
        _vault = VaultService.Create(Path.Combine(_tempDir, "v.db"), key);
        _entries = new EntryRepository(_vault);
        _folders = new FolderRepository(_vault);
    }

    public void Dispose()
    {
        _vault.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // Helper: build a second vault to import INTO so dedupe and folder
    // mapping can be verified end-to-end.
    private (VaultService Vault, EntryRepository Entries, FolderRepository Folders, string Dir) BuildSecondVault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumos-export2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var key = SecureMemory.RandomBytes(32);
        var vault = VaultService.Create(Path.Combine(dir, "v.db"), key);
        return (vault, new EntryRepository(vault), new FolderRepository(vault), dir);
    }

    private void SeedSampleVault()
    {
        var folder = _folders.Create("Work");
        _entries.Insert(Entry.NewLogin("GitHub",
            new LoginPayload(Username: "alice", Password: "p@ss!", Url: "https://github.com",
                TotpSecret: "JBSWY3DPEHPK3PXP"),
            folderId: folder.Id));
        _entries.Insert(Entry.NewCard("Visa",
            new CardPayload(CardholderName: "Alice", Number: "4111111111111111",
                ExpiryMonth: "12", ExpiryYear: "2030", Cvv: "123", Brand: "Visa")));
        _entries.Insert(Entry.NewIdentity("Personal",
            new IdentityPayload(FullName: "Alice Liddell", Email: "[email protected]",
                Phone: "+15551234567", Address: "1 Wonderland Ave",
                City: "Oxford", Country: "GB")));
        _entries.Insert(Entry.NewSecureNote("WiFi password",
            new SecureNotePayload(Body: "lumos-wifi-2024")));
    }

    // ---- Native format round trips ----

    [Fact]
    public void Lumos_plaintext_export_then_import_into_fresh_vault_restores_all_entries()
    {
        SeedSampleVault();

        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.LumosJson);
        Assert.NotEmpty(bytes);

        var (v2, e2, f2, dir) = BuildSecondVault();
        try
        {
            var importer = new VaultImporter(e2, f2);
            var load = importer.Load(bytes, passphrase: null);
            Assert.Equal(ImportFailureReason.None, load.Failure);
            Assert.NotNull(load.Preview);
            Assert.Equal(4, load.Preview!.NewEntryCount);
            Assert.Equal(0, load.Preview.DuplicateCount);
            Assert.Equal(1, load.Preview.NewFolderCount);

            var inserted = importer.Commit(load.Preview);
            Assert.Equal(4, inserted);

            // Verify the data round-tripped, not just the count.
            var imported = e2.ListAll();
            var login = imported.Single(e => e.Title == "GitHub");
            var loginPayload = Assert.IsType<LoginPayload>(login.Payload);
            Assert.Equal("alice", loginPayload.Username);
            Assert.Equal("p@ss!", loginPayload.Password);
            Assert.Equal("https://github.com", loginPayload.Url);
            Assert.Equal("JBSWY3DPEHPK3PXP", loginPayload.TotpSecret);

            // Folder was created and the login filed under it.
            var workFolder = f2.ListAll().Single(folder => folder.Name == "Work");
            Assert.Equal(workFolder.Id, login.FolderId);

            var card = imported.Single(e => e.Title == "Visa");
            var cardPayload = Assert.IsType<CardPayload>(card.Payload);
            Assert.Equal("4111111111111111", cardPayload.Number);
            Assert.Equal("123", cardPayload.Cvv);

            var identity = imported.Single(e => e.Title == "Personal");
            var idPayload = Assert.IsType<IdentityPayload>(identity.Payload);
            Assert.Equal("Alice Liddell", idPayload.FullName);

            var note = imported.Single(e => e.Title == "WiFi password");
            var notePayload = Assert.IsType<SecureNotePayload>(note.Payload);
            Assert.Equal("lumos-wifi-2024", notePayload.Body);
        }
        finally
        {
            v2.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ---- Encrypted envelope ----

    [Fact]
    public void Lumos_encrypted_round_trip_with_correct_passphrase()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders)
            .Export(ExportFormat.LumosEncrypted, passphrase: "correct-horse-battery-staple");

        // Must start with the LXP1 magic.
        Assert.Equal((byte)'L', bytes[0]);
        Assert.Equal((byte)'X', bytes[1]);
        Assert.Equal((byte)'P', bytes[2]);
        Assert.Equal((byte)'1', bytes[3]);

        // The ciphertext should not contain "alice" (the username) as readable text.
        var asText = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("alice", asText);
        Assert.DoesNotContain("p@ss!", asText);

        var (v2, e2, f2, dir) = BuildSecondVault();
        try
        {
            var importer = new VaultImporter(e2, f2);
            var load = importer.Load(bytes, "correct-horse-battery-staple");
            Assert.Equal(ImportFailureReason.None, load.Failure);
            Assert.Equal(4, load.Preview!.NewEntryCount);
        }
        finally
        {
            v2.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Lumos_encrypted_with_wrong_passphrase_returns_failure()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders)
            .Export(ExportFormat.LumosEncrypted, passphrase: "real-passphrase");

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, "WRONG");
        Assert.Equal(ImportFailureReason.WrongPassphrase, load.Failure);
        Assert.Null(load.Preview);
    }

    [Fact]
    public void Lumos_encrypted_with_missing_passphrase_returns_failure()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders)
            .Export(ExportFormat.LumosEncrypted, passphrase: "real-passphrase");

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, passphrase: null);
        Assert.Equal(ImportFailureReason.WrongPassphrase, load.Failure);
    }

    [Fact]
    public void Encrypted_export_requires_passphrase()
    {
        var exporter = new VaultExporter(_entries, _folders);
        Assert.Throws<ArgumentException>(() =>
            exporter.Export(ExportFormat.LumosEncrypted, passphrase: null));
        Assert.Throws<ArgumentException>(() =>
            exporter.Export(ExportFormat.BitwardenEncrypted, passphrase: ""));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders)
            .Export(ExportFormat.LumosEncrypted, passphrase: "p");

        // Flip a byte in the ciphertext region.
        bytes[bytes.Length - 5] ^= 0xFF;

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, "p");
        Assert.NotEqual(ImportFailureReason.None, load.Failure);
    }

    [Fact]
    public void Bad_magic_is_detected_before_passphrase_check()
    {
        var bytes = new byte[64];
        // Won't start with LXP1, but won't parse as JSON either.
        bytes[0] = 0xFF;

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, "anything");
        // Falls through to JSON sniff path → invalid JSON → Corrupt
        Assert.NotEqual(ImportFailureReason.None, load.Failure);
    }

    // ---- Dedupe ----

    [Fact]
    public void Importing_into_a_vault_with_overlapping_entries_skips_duplicates()
    {
        // Seed the source vault.
        _entries.Insert(Entry.NewLogin("GitHub",
            new LoginPayload(Username: "alice", Password: "p", Url: "https://github.com")));
        _entries.Insert(Entry.NewLogin("Stripe",
            new LoginPayload(Username: "alice", Password: "x", Url: "https://stripe.com")));

        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.LumosJson);

        var (v2, e2, f2, dir) = BuildSecondVault();
        try
        {
            // Pre-populate the target with one of the same entries (same title+username+url).
            e2.Insert(Entry.NewLogin("GitHub",
                new LoginPayload(Username: "alice", Password: "different", Url: "https://github.com")));

            var importer = new VaultImporter(e2, f2);
            var load = importer.Load(bytes, null);
            Assert.Equal(ImportFailureReason.None, load.Failure);
            Assert.Equal(1, load.Preview!.NewEntryCount);
            Assert.Equal(1, load.Preview.DuplicateCount);

            var inserted = importer.Commit(load.Preview);
            Assert.Equal(1, inserted);
            Assert.Equal(2, e2.ListAll().Count);

            // The pre-existing GitHub entry should still have its original password.
            var existing = e2.ListAll().Single(e => e.Title == "GitHub");
            var p = Assert.IsType<LoginPayload>(existing.Payload);
            Assert.Equal("different", p.Password);
        }
        finally
        {
            v2.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Dedupe_is_case_insensitive_for_login_signature()
    {
        var e1 = Entry.NewLogin("GitHub", new LoginPayload("Alice", "p", "https://github.com"));
        var e2 = Entry.NewLogin("github", new LoginPayload("alice", "x", "HTTPS://GITHUB.COM"));
        Assert.Equal(VaultImporter.BuildDedupeKey(e1), VaultImporter.BuildDedupeKey(e2));
    }

    // ---- Bitwarden interop ----

    [Fact]
    public void Bitwarden_json_exports_with_documented_type_integers()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.BitwardenJson);

        using var doc = JsonDocument.Parse(bytes);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("type").GetInt32() == 1); // Login
        Assert.Contains(items, i => i.GetProperty("type").GetInt32() == 2); // SecureNote
        Assert.Contains(items, i => i.GetProperty("type").GetInt32() == 3); // Card
        Assert.Contains(items, i => i.GetProperty("type").GetInt32() == 4); // Identity
    }

    [Fact]
    public void Bitwarden_json_round_trips_into_a_fresh_vault()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.BitwardenJson);

        var (v2, e2, f2, dir) = BuildSecondVault();
        try
        {
            var importer = new VaultImporter(e2, f2);
            var load = importer.Load(bytes, null);
            Assert.Equal(ImportFailureReason.None, load.Failure);
            Assert.Equal(ImportSourceFormat.BitwardenJson, load.Preview!.Format);
            Assert.Equal(4, load.Preview.NewEntryCount);

            var inserted = importer.Commit(load.Preview);
            Assert.Equal(4, inserted);

            // Check the login's TOTP survived the Bitwarden round trip.
            var login = e2.ListAll().Single(e => e.Title == "GitHub");
            var lp = Assert.IsType<LoginPayload>(login.Payload);
            Assert.Equal("JBSWY3DPEHPK3PXP", lp.TotpSecret);
        }
        finally
        {
            v2.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Bitwarden_encrypted_their_own_format_is_rejected_with_clear_failure()
    {
        // Construct a Bitwarden JSON with encrypted=true.
        var json = "{\"encrypted\":true,\"folders\":[],\"items\":[]}";
        var bytes = Encoding.UTF8.GetBytes(json);

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, null);
        // We refuse — users should decrypt with Bitwarden first.
        Assert.Equal(ImportFailureReason.BadFormat, load.Failure);
    }

    // ---- CSV ----

    [Fact]
    public void Csv_export_writes_header_and_one_row_per_entry()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.Csv);
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + 4 entries.
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("type,title,folder,notes,", lines[0]);
    }

    [Fact]
    public void Csv_escapes_commas_and_quotes_in_values()
    {
        _entries.Insert(Entry.NewLogin("Has, comma",
            new LoginPayload(Username: "with \"quotes\"", Password: "p", Url: "")));

        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.Csv);
        var text = Encoding.UTF8.GetString(bytes);

        // The title with a comma should be quoted; quotes inside should be doubled.
        Assert.Contains("\"Has, comma\"", text);
        Assert.Contains("\"with \"\"quotes\"\"\"", text);
    }

    // ---- Format detection ----

    [Fact]
    public void Plain_lumos_json_is_detected_correctly()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.LumosJson);

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, null);
        Assert.Equal(ImportSourceFormat.LumosJson, load.Preview!.Format);
    }

    [Fact]
    public void Plain_bitwarden_json_is_detected_correctly()
    {
        SeedSampleVault();
        var bytes = new VaultExporter(_entries, _folders).Export(ExportFormat.BitwardenJson);

        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, null);
        Assert.Equal(ImportSourceFormat.BitwardenJson, load.Preview!.Format);
    }

    [Fact]
    public void Empty_file_returns_empty_file_failure()
    {
        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(Array.Empty<byte>(), null);
        Assert.Equal(ImportFailureReason.EmptyFile, load.Failure);
    }

    [Fact]
    public void Garbage_json_returns_corrupt()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"this\":\"is not an export\"}");
        var importer = new VaultImporter(_entries, _folders);
        var load = importer.Load(bytes, null);
        Assert.Equal(ImportFailureReason.BadFormat, load.Failure);
    }
}
