using System.Text;
using Lumos.Core;
using Lumos.Core.Attachments;
using Lumos.Core.Crypto;
using Lumos.Core.Entries;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class AttachmentRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VaultService _vault;
    private readonly EntryRepository _entries;
    private readonly AttachmentRepository _attachments;

    public AttachmentRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-attach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
        var key = SecureMemory.RandomBytes(32);
        _vault = VaultService.Create(Path.Combine(_tempDir, "v.db"), key);
        _entries = new EntryRepository(_vault);
        _attachments = new AttachmentRepository(_vault);
    }

    public void Dispose()
    {
        _vault.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private Entry SeedEntry()
    {
        var e = Entry.NewLogin("GitHub",
            new LoginPayload(Username: "alice", Password: "p", Url: "https://github.com"));
        _entries.Insert(e);
        return e;
    }

    [Fact]
    public void Add_then_list_returns_metadata_without_bytes()
    {
        var e = SeedEntry();
        var bytes = Encoding.UTF8.GetBytes("hello world");
        var info = _attachments.Add(e.Id, "note.txt", "text/plain", bytes);

        Assert.Equal(e.Id, info.EntryId);
        Assert.Equal("note.txt", info.FileName);
        Assert.Equal(bytes.LongLength, info.SizeBytes);

        var list = _attachments.ListForEntry(e.Id);
        Assert.Single(list);
        Assert.Equal("note.txt", list[0].FileName);
        Assert.Equal(11, list[0].SizeBytes);
    }

    [Fact]
    public void GetContent_round_trips_the_exact_bytes()
    {
        var e = SeedEntry();
        var bytes = new byte[5000];
        new Random(42).NextBytes(bytes);
        var info = _attachments.Add(e.Id, "blob.bin", "application/octet-stream", bytes);

        var fetched = _attachments.GetContent(info.Id);
        Assert.NotNull(fetched);
        Assert.Equal(bytes, fetched!.Bytes);
        Assert.Equal("blob.bin", fetched.Info.FileName);
    }

    [Fact]
    public void Multiple_attachments_per_entry_are_supported()
    {
        var e = SeedEntry();
        _attachments.Add(e.Id, "a.txt", "text/plain", Encoding.UTF8.GetBytes("a"));
        _attachments.Add(e.Id, "b.txt", "text/plain", Encoding.UTF8.GetBytes("bb"));
        _attachments.Add(e.Id, "c.txt", "text/plain", Encoding.UTF8.GetBytes("ccc"));

        Assert.Equal(3, _attachments.CountForEntry(e.Id));
        Assert.Equal(3, _attachments.ListForEntry(e.Id).Count);
    }

    [Fact]
    public void Delete_removes_one_attachment()
    {
        var e = SeedEntry();
        var info = _attachments.Add(e.Id, "x.txt", "text/plain", Encoding.UTF8.GetBytes("x"));
        Assert.Equal(1, _attachments.CountForEntry(e.Id));

        var removed = _attachments.Delete(info.Id);
        Assert.True(removed);
        Assert.Equal(0, _attachments.CountForEntry(e.Id));
    }

    [Fact]
    public void Deleting_the_entry_cascades_to_its_attachments()
    {
        var e = SeedEntry();
        _attachments.Add(e.Id, "x.txt", "text/plain", Encoding.UTF8.GetBytes("x"));
        _attachments.Add(e.Id, "y.txt", "text/plain", Encoding.UTF8.GetBytes("y"));
        Assert.Equal(2, _attachments.CountForEntry(e.Id));

        _entries.Delete(e.Id);

        // ON DELETE CASCADE should have removed the attachment rows.
        Assert.Equal(0, _attachments.CountForEntry(e.Id));
    }

    [Fact]
    public void File_over_the_cap_is_rejected()
    {
        var e = SeedEntry();
        var tooBig = new byte[AttachmentRepository.MaxFileSizeBytes + 1];

        var ex = Assert.Throws<AttachmentTooLargeException>(
            () => _attachments.Add(e.Id, "big.bin", "application/octet-stream", tooBig));
        Assert.Equal(tooBig.LongLength, ex.ActualBytes);
        Assert.Equal(AttachmentRepository.MaxFileSizeBytes, ex.MaxBytes);

        // Nothing should have been stored.
        Assert.Equal(0, _attachments.CountForEntry(e.Id));
    }

    [Fact]
    public void File_exactly_at_the_cap_is_accepted()
    {
        var e = SeedEntry();
        var atCap = new byte[AttachmentRepository.MaxFileSizeBytes];
        var info = _attachments.Add(e.Id, "max.bin", "application/octet-stream", atCap);
        Assert.Equal(AttachmentRepository.MaxFileSizeBytes, info.SizeBytes);
    }

    [Fact]
    public void IsImage_detects_image_types_by_mime_or_extension()
    {
        var e = SeedEntry();
        var png = _attachments.Add(e.Id, "pic.png", "image/png", new byte[] { 1, 2, 3 });
        var byExt = _attachments.Add(e.Id, "pic.JPG", "", new byte[] { 1 });
        var doc = _attachments.Add(e.Id, "report.pdf", "application/pdf", new byte[] { 1 });

        Assert.True(png.IsImage);
        Assert.True(byExt.IsImage);
        Assert.False(doc.IsImage);
    }

    [Fact]
    public void GetContent_for_missing_id_returns_null()
    {
        Assert.Null(_attachments.GetContent("does-not-exist"));
    }

    [Fact]
    public void Attachments_are_isolated_per_entry()
    {
        var e1 = SeedEntry();
        var e2 = Entry.NewSecureNote("Note", new SecureNotePayload(Body: "x"));
        _entries.Insert(e2);

        _attachments.Add(e1.Id, "one.txt", "text/plain", Encoding.UTF8.GetBytes("1"));
        _attachments.Add(e2.Id, "two.txt", "text/plain", Encoding.UTF8.GetBytes("2"));

        Assert.Equal(1, _attachments.CountForEntry(e1.Id));
        Assert.Equal(1, _attachments.CountForEntry(e2.Id));
        Assert.Equal("one.txt", _attachments.ListForEntry(e1.Id)[0].FileName);
    }

    [Fact]
    public void Size_formatting_is_human_readable()
    {
        Assert.Equal("512 B", AttachmentInfo.FormatSize(512));
        Assert.Equal("1 KB", AttachmentInfo.FormatSize(1024));
        Assert.Equal("1.5 KB", AttachmentInfo.FormatSize(1536));
        Assert.Equal("1 MB", AttachmentInfo.FormatSize(1024 * 1024));
    }
}
