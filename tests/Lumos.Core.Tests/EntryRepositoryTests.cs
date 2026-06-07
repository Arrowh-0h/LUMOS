using Lumos.Core;
using Lumos.Core.Crypto;
using Lumos.Core.Entries;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class EntryRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VaultService _vault;
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;
    private readonly TagRepository _tags;

    public EntryRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-entries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
        var key = SecureMemory.RandomBytes(32);
        _vault = VaultService.Create(Path.Combine(_tempDir, "v.db"), key);
        _entries = new EntryRepository(_vault);
        _folders = new FolderRepository(_vault);
        _tags = new TagRepository(_vault);
    }

    public void Dispose()
    {
        _vault.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ---------- Round trip per type ----------

    [Fact]
    public void Login_round_trip()
    {
        var entry = Entry.NewLogin("GitHub",
            new LoginPayload(Username: "alice", Password: "p@ssw0rd!", Url: "https://github.com",
                             TotpSecret: "JBSWY3DPEHPK3PXP"));

        var inserted = _entries.Insert(entry);
        var loaded = _entries.GetById(inserted.Id);

        Assert.NotNull(loaded);
        Assert.Equal(EntryType.Login, loaded!.Type);
        Assert.Equal("GitHub", loaded.Title);
        var p = Assert.IsType<LoginPayload>(loaded.Payload);
        Assert.Equal("alice", p.Username);
        Assert.Equal("p@ssw0rd!", p.Password);
        Assert.Equal("https://github.com", p.Url);
        Assert.Equal("JBSWY3DPEHPK3PXP", p.TotpSecret);
    }

    [Fact]
    public void Secure_note_round_trip()
    {
        var entry = Entry.NewSecureNote("Server SSH key", new SecureNotePayload(Body: "-----BEGIN OPENSSH PRIVATE KEY-----..."));
        _entries.Insert(entry);
        var loaded = _entries.GetById(entry.Id);
        var p = Assert.IsType<SecureNotePayload>(loaded!.Payload);
        Assert.StartsWith("-----BEGIN", p.Body);
    }

    [Fact]
    public void Card_round_trip()
    {
        var entry = Entry.NewCard("Visa Personal",
            new CardPayload(CardholderName: "ALICE EXAMPLE", Number: "4111111111111111",
                            ExpiryMonth: "12", ExpiryYear: "2028", Cvv: "123", Brand: "Visa"));
        _entries.Insert(entry);
        var p = Assert.IsType<CardPayload>(_entries.GetById(entry.Id)!.Payload);
        Assert.Equal("4111111111111111", p.Number);
        Assert.Equal("Visa", p.Brand);
    }

    [Fact]
    public void Identity_round_trip()
    {
        var entry = Entry.NewIdentity("Home",
            new IdentityPayload(FullName: "Alice Example", Email: "[email protected]",
                                Phone: "+1-555-0100", City: "Brooklyn", Country: "US"));
        _entries.Insert(entry);
        var p = Assert.IsType<IdentityPayload>(_entries.GetById(entry.Id)!.Payload);
        Assert.Equal("Alice Example", p.FullName);
        Assert.Equal("Brooklyn", p.City);
    }

    // ---------- Tags ----------

    [Fact]
    public void Tags_are_persisted_and_returned()
    {
        var entry = Entry.NewLogin("Site",
            new LoginPayload(Username: "u", Password: "p"),
            tags: new[] { "work", "Important" });
        _entries.Insert(entry);

        var loaded = _entries.GetById(entry.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Tags.Count);
        Assert.Contains("work", loaded.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Important", loaded.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Same_tag_name_case_insensitive_is_deduped_in_tag_table()
    {
        _entries.Insert(Entry.NewLogin("A", new LoginPayload(), tags: new[] { "Work" }));
        _entries.Insert(Entry.NewLogin("B", new LoginPayload(), tags: new[] { "WORK" }));
        _entries.Insert(Entry.NewLogin("C", new LoginPayload(), tags: new[] { "work" }));

        // Only one tag row should exist.
        var all = _tags.ListAll();
        Assert.Single(all);
    }

    [Fact]
    public void ListByTag_finds_entries_case_insensitively()
    {
        _entries.Insert(Entry.NewLogin("A", new LoginPayload(), tags: new[] { "Personal" }));
        _entries.Insert(Entry.NewLogin("B", new LoginPayload(), tags: new[] { "Personal" }));
        _entries.Insert(Entry.NewLogin("C", new LoginPayload(), tags: new[] { "Work" }));

        var hits = _entries.ListByTag("personal");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Tags_can_be_added_and_removed_on_update()
    {
        var entry = Entry.NewLogin("E", new LoginPayload(), tags: new[] { "a", "b" });
        _entries.Insert(entry);

        var updated = entry with { Tags = new[] { "b", "c" } };
        _entries.Update(updated);

        var loaded = _entries.GetById(entry.Id)!;
        Assert.Equal(2, loaded.Tags.Count);
        Assert.Contains("b", loaded.Tags);
        Assert.Contains("c", loaded.Tags);
        Assert.DoesNotContain("a", loaded.Tags);
    }

    // ---------- Folders ----------

    [Fact]
    public void Entry_can_be_filed_into_folder()
    {
        var folder = _folders.Create("Work");
        var entry = Entry.NewLogin("Office WiFi", new LoginPayload(), folderId: folder.Id);
        _entries.Insert(entry);

        var inFolder = _entries.ListByFolder(folder.Id);
        Assert.Single(inFolder);
        Assert.Equal(entry.Id, inFolder[0].Id);
    }

    [Fact]
    public void Listing_no_folder_returns_only_unfiled()
    {
        var folder = _folders.Create("F");
        _entries.Insert(Entry.NewLogin("filed", new LoginPayload(), folderId: folder.Id));
        _entries.Insert(Entry.NewLogin("unfiled", new LoginPayload()));

        var unfiled = _entries.ListByFolder(null);
        Assert.Single(unfiled);
        Assert.Equal("unfiled", unfiled[0].Title);
    }

    [Fact]
    public void Deleting_folder_unlinks_entries()
    {
        var folder = _folders.Create("Doomed");
        var entry = Entry.NewLogin("Stays", new LoginPayload(), folderId: folder.Id);
        _entries.Insert(entry);

        _folders.Delete(folder.Id);

        var loaded = _entries.GetById(entry.Id);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.FolderId);
    }

    // ---------- Update / TouchLastUsed ----------

    [Fact]
    public void Update_changes_modified_utc()
    {
        var entry = Entry.NewLogin("X", new LoginPayload(Username: "old"));
        _entries.Insert(entry);
        var before = _entries.GetById(entry.Id)!.ModifiedUtc;

        Thread.Sleep(50);  // ensure timestamp difference is observable
        _entries.Update(entry with { Payload = new LoginPayload(Username: "new") });

        var after = _entries.GetById(entry.Id)!.ModifiedUtc;
        Assert.True(after > before, $"modified_utc should advance; was {before:o}, now {after:o}");
    }

    [Fact]
    public void TouchLastUsed_updates_last_used_without_touching_modified()
    {
        var entry = Entry.NewLogin("X", new LoginPayload());
        _entries.Insert(entry);
        var modBefore = _entries.GetById(entry.Id)!.ModifiedUtc;

        Thread.Sleep(50);
        _entries.TouchLastUsed(entry.Id);

        var loaded = _entries.GetById(entry.Id)!;
        Assert.Equal(modBefore, loaded.ModifiedUtc);
        Assert.NotNull(loaded.LastUsedUtc);
    }

    [Fact]
    public void Update_unknown_entry_throws()
    {
        var ghost = Entry.NewLogin("ghost", new LoginPayload());
        Assert.Throws<InvalidOperationException>(() => _entries.Update(ghost));
    }

    // ---------- Delete ----------

    [Fact]
    public void Delete_removes_entry_and_tag_links()
    {
        var entry = Entry.NewLogin("doomed", new LoginPayload(), tags: new[] { "x" });
        _entries.Insert(entry);
        _entries.Delete(entry.Id);

        Assert.Null(_entries.GetById(entry.Id));
        // Tag itself remains (might be used elsewhere) but link is gone.
        Assert.Empty(_entries.ListByTag("x"));
    }

    // ---------- Search ----------

    [Fact]
    public void Search_finds_by_title()
    {
        _entries.Insert(Entry.NewLogin("GitHub", new LoginPayload()));
        _entries.Insert(Entry.NewLogin("GitLab", new LoginPayload()));
        _entries.Insert(Entry.NewLogin("AWS", new LoginPayload()));

        var hits = _entries.Search("Git");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Search_finds_login_by_username()
    {
        _entries.Insert(Entry.NewLogin("Some Site",
            new LoginPayload(Username: "alice", Password: "secret")));
        _entries.Insert(Entry.NewLogin("Other",
            new LoginPayload(Username: "bob", Password: "secret")));

        var hits = _entries.Search("alice");
        Assert.Single(hits);
    }

    [Fact]
    public void Search_finds_login_by_url_substring_via_prefix()
    {
        _entries.Insert(Entry.NewLogin("Acct",
            new LoginPayload(Username: "u", Password: "p", Url: "https://example.com")));
        var hits = _entries.Search("example");
        Assert.Single(hits);
    }

    [Fact]
    public void Search_never_finds_passwords()
    {
        // Sensitive fields must NOT be indexed. Search for the password
        // string itself; we must get zero hits.
        _entries.Insert(Entry.NewLogin("X",
            new LoginPayload(Username: "u", Password: "very-distinctive-pwd-token")));

        var hits = _entries.Search("very-distinctive-pwd-token");
        Assert.Empty(hits);
    }

    [Fact]
    public void Search_never_finds_cvv()
    {
        _entries.Insert(Entry.NewCard("Card",
            new CardPayload(CardholderName: "Alice", Number: "4111111111111111",
                            Cvv: "987654321")));

        var hits = _entries.Search("987654321");
        Assert.Empty(hits);
    }

    [Fact]
    public void Search_finds_by_notes()
    {
        _entries.Insert(Entry.NewSecureNote("My note",
            new SecureNotePayload(Body: "a unique note body sentinel")));
        var hits = _entries.Search("sentinel");
        Assert.Single(hits);
    }

    [Fact]
    public void Search_on_deleted_entry_returns_nothing()
    {
        var e = Entry.NewLogin("FindMe", new LoginPayload());
        _entries.Insert(e);
        _entries.Delete(e.Id);
        Assert.Empty(_entries.Search("FindMe"));
    }

    [Fact]
    public void Search_after_update_uses_new_text()
    {
        var e = Entry.NewLogin("OldTitle", new LoginPayload());
        _entries.Insert(e);
        Assert.Single(_entries.Search("OldTitle"));

        _entries.Update(e with { Title = "FreshTitle" });
        Assert.Empty(_entries.Search("OldTitle"));
        Assert.Single(_entries.Search("FreshTitle"));
    }

    [Fact]
    public void Empty_search_query_returns_nothing()
    {
        _entries.Insert(Entry.NewLogin("anything", new LoginPayload()));
        Assert.Empty(_entries.Search(""));
        Assert.Empty(_entries.Search("   "));
    }

    // ---------- Listing ----------

    [Fact]
    public void ListAll_orders_by_modified_desc()
    {
        var a = Entry.NewLogin("a", new LoginPayload());
        _entries.Insert(a);
        Thread.Sleep(20);
        var b = Entry.NewLogin("b", new LoginPayload());
        _entries.Insert(b);

        var list = _entries.ListAll();
        Assert.Equal("b", list[0].Title);
        Assert.Equal("a", list[1].Title);
    }

    [Fact]
    public void ListByType_filters_by_entry_type()
    {
        _entries.Insert(Entry.NewLogin("login1", new LoginPayload()));
        _entries.Insert(Entry.NewLogin("login2", new LoginPayload()));
        _entries.Insert(Entry.NewSecureNote("note1", new SecureNotePayload()));
        _entries.Insert(Entry.NewCard("card1", new CardPayload()));

        Assert.Equal(2, _entries.ListByType(EntryType.Login).Count);
        Assert.Single(_entries.ListByType(EntryType.SecureNote));
        Assert.Single(_entries.ListByType(EntryType.Card));
        Assert.Empty(_entries.ListByType(EntryType.Identity));
    }
}
