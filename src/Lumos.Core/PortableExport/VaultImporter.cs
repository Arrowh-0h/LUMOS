using System.Text;
using System.Text.Json;
using Lumos.Core.Entries;

namespace Lumos.Core.PortableExport;

public enum ImportSourceFormat
{
    LumosJson,
    LumosEncrypted,
    BitwardenJson,
    BitwardenEncrypted,
    Unknown,
}

/// <summary>
/// What an import would do, surfaced before commit so the user can decide
/// to proceed or cancel.
/// </summary>
public sealed record ImportPreview(
    ImportSourceFormat Format,
    int TotalCandidates,
    int NewEntryCount,
    int DuplicateCount,
    int NewFolderCount,
    IReadOnlyList<string> PreviewTitles,    // up to ~10 sample titles
    IReadOnlyList<ParsedEntry> ParsedEntries,
    IReadOnlyList<ParsedFolder> ParsedFolders);

/// <summary>One folder, intermediate form.</summary>
public sealed record ParsedFolder(string SourceId, string Name);

/// <summary>One entry, parsed but not yet inserted.</summary>
public sealed record ParsedEntry(Entry Entry, string DedupeKey);

public enum ImportFailureReason
{
    None,
    BadFormat,
    WrongPassphrase,
    Corrupt,
    EmptyFile,
}

public sealed record ImportLoadResult(
    ImportPreview? Preview,
    ImportFailureReason Failure);

/// <summary>
/// Reads a portable export back into the vault. Merge-only: existing
/// entries with the same (Title, Username, URL) signature are kept and
/// duplicates from the import are skipped.
/// </summary>
public sealed class VaultImporter
{
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public VaultImporter(EntryRepository entries, FolderRepository folders)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folders);
        _entries = entries;
        _folders = folders;
    }

    /// <summary>
    /// Load a file, detect the format, decrypt if needed, parse, and return
    /// a preview describing what would happen on commit.
    /// </summary>
    public ImportLoadResult Load(byte[] fileBytes, string? passphrase)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        if (fileBytes.Length == 0)
            return new ImportLoadResult(null, ImportFailureReason.EmptyFile);

        // Detect by the first few bytes. LXP1 = encrypted Lumos envelope.
        // Otherwise we try JSON and look at its shape.
        byte[] jsonBytes;
        ImportSourceFormat detectedFormat;

        if (LooksLikeEncryptedEnvelope(fileBytes))
        {
            if (string.IsNullOrEmpty(passphrase))
                return new ImportLoadResult(null, ImportFailureReason.WrongPassphrase);

            var decoded = ExportEnvelope.Decode(fileBytes, passphrase);
            if (!decoded.Success)
            {
                return new ImportLoadResult(null, decoded.Status switch
                {
                    DecodeStatus.WrongPassphrase => ImportFailureReason.WrongPassphrase,
                    DecodeStatus.BadMagic => ImportFailureReason.BadFormat,
                    _ => ImportFailureReason.Corrupt,
                });
            }
            jsonBytes = decoded.PlaintextBytes!;
            // We'll set the exact subtype after sniffing the JSON below.
            detectedFormat = ImportSourceFormat.LumosEncrypted;
        }
        else
        {
            jsonBytes = fileBytes;
            detectedFormat = ImportSourceFormat.LumosJson;  // tentative
        }

        // Sniff for "lumos-export" vs Bitwarden shape.
        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            var root = doc.RootElement;
            if (root.TryGetProperty("format", out var fmtEl) &&
                fmtEl.ValueKind == JsonValueKind.String &&
                fmtEl.GetString() == "lumos-export")
            {
                detectedFormat = detectedFormat == ImportSourceFormat.LumosEncrypted
                    ? ImportSourceFormat.LumosEncrypted
                    : ImportSourceFormat.LumosJson;
                return BuildPreview(JsonSerializer.Deserialize<LumosExport>(jsonBytes, JsonOpts)!, detectedFormat);
            }

            // Bitwarden has "items" and "folders" arrays at top level.
            if (root.TryGetProperty("items", out _))
            {
                var bwFormat = detectedFormat == ImportSourceFormat.LumosEncrypted
                    ? ImportSourceFormat.BitwardenEncrypted
                    : ImportSourceFormat.BitwardenJson;
                return BuildPreview(JsonSerializer.Deserialize<BitwardenExport>(jsonBytes, JsonOpts)!, bwFormat);
            }

            return new ImportLoadResult(null, ImportFailureReason.BadFormat);
        }
        catch (JsonException)
        {
            return new ImportLoadResult(null, ImportFailureReason.Corrupt);
        }
    }

    /// <summary>
    /// Apply the preview to the vault. Returns the number of entries actually
    /// inserted (which equals NewEntryCount unless something goes wrong).
    /// </summary>
    public int Commit(ImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        // First, create any folders that don't exist by name. We map source
        // folder ids → new folder ids so entries can be filed correctly.
        var existingByName = _folders.ListAll()
            .ToDictionary(f => f.Name, f => f.Id, StringComparer.OrdinalIgnoreCase);
        var sourceToTargetFolderId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pf in preview.ParsedFolders)
        {
            if (existingByName.TryGetValue(pf.Name, out var existingId))
            {
                sourceToTargetFolderId[pf.SourceId] = existingId;
            }
            else
            {
                var created = _folders.Create(pf.Name);
                sourceToTargetFolderId[pf.SourceId] = created.Id;
                existingByName[created.Name] = created.Id;
            }
        }

        // Build the dedupe set from what's already in the vault.
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries.ListAll())
            existingKeys.Add(BuildDedupeKey(e));

        var inserted = 0;
        foreach (var pe in preview.ParsedEntries)
        {
            if (existingKeys.Contains(pe.DedupeKey)) continue;

            // Remap folder id from source-space to vault-space.
            var folderId = pe.Entry.FolderId is not null && sourceToTargetFolderId.TryGetValue(pe.Entry.FolderId, out var mapped)
                ? mapped
                : null;
            var entryToInsert = pe.Entry with { FolderId = folderId };

            _entries.Insert(entryToInsert);
            existingKeys.Add(pe.DedupeKey);
            inserted++;
        }
        return inserted;
    }

    // ---- detection ----

    private static bool LooksLikeEncryptedEnvelope(byte[] bytes)
    {
        if (bytes.Length < ExportEnvelope.HeaderLength) return false;
        for (var i = 0; i < ExportEnvelope.Magic.Length; i++)
            if (bytes[i] != ExportEnvelope.Magic[i]) return false;
        return true;
    }

    // ---- Lumos parsing ----

    private ImportLoadResult BuildPreview(LumosExport src, ImportSourceFormat format)
    {
        var parsedFolders = src.Folders
            .Select(f => new ParsedFolder(f.Id, f.Name))
            .ToList();

        var parsedEntries = new List<ParsedEntry>();
        foreach (var dto in src.Entries)
        {
            var entry = LumosDtoToEntry(dto);
            if (entry is null) continue;
            parsedEntries.Add(new ParsedEntry(entry, BuildDedupeKey(entry)));
        }

        return BuildPreview(parsedFolders, parsedEntries, format);
    }

    private static Entry? LumosDtoToEntry(LumosExportEntry dto)
    {
        EntryPayload payload = dto.Type switch
        {
            EntryType.Login => new LoginPayload(
                Username: dto.Username ?? "",
                Password: dto.Password ?? "",
                Url: dto.Url ?? "",
                TotpSecret: dto.TotpSecret),

            EntryType.Card => new CardPayload(
                CardholderName: dto.CardholderName ?? "",
                Number: dto.CardNumber ?? "",
                ExpiryMonth: dto.ExpiryMonth ?? "",
                ExpiryYear: dto.ExpiryYear ?? "",
                Cvv: dto.Cvv ?? "",
                Brand: dto.CardBrand),

            EntryType.Identity => new IdentityPayload(
                FullName: BuildFullName(dto.FirstName, dto.LastName),
                Email: dto.Email ?? "",
                Phone: dto.Phone ?? "",
                Address: dto.Address ?? "",
                City: dto.City ?? "",
                Country: dto.Country ?? ""),

            EntryType.SecureNote => new SecureNotePayload(Body: dto.Notes),

            _ => null!,
        };
        if (payload is null) return null;

        return new Entry
        {
            Id = Guid.NewGuid().ToString("N"),   // fresh id; the source id is for folder mapping only
            Type = dto.Type,
            Title = dto.Title,
            Notes = dto.Type == EntryType.SecureNote ? "" : dto.Notes,
            FolderId = dto.FolderId,
            Tags = dto.Tags ?? new List<string>(),
            Payload = payload,
            CreatedUtc = dto.CreatedUtc == default ? DateTimeOffset.UtcNow : dto.CreatedUtc,
            ModifiedUtc = dto.ModifiedUtc == default ? DateTimeOffset.UtcNow : dto.ModifiedUtc,
            LastUsedUtc = dto.LastUsedUtc,
        };
    }

    // ---- Bitwarden parsing ----

    private ImportLoadResult BuildPreview(BitwardenExport src, ImportSourceFormat format)
    {
        if (src.Encrypted)
        {
            // We don't support Bitwarden's *own* encrypted format (it uses
            // their account-key derivation). Users should decrypt with
            // Bitwarden first, then export plaintext, then re-encrypt with
            // our LXP1 envelope.
            return new ImportLoadResult(null, ImportFailureReason.BadFormat);
        }

        var parsedFolders = src.Folders
            .Select(f => new ParsedFolder(f.Id, f.Name))
            .ToList();

        var parsedEntries = new List<ParsedEntry>();
        foreach (var item in src.Items)
        {
            var entry = BitwardenItemToEntry(item);
            if (entry is null) continue;
            parsedEntries.Add(new ParsedEntry(entry, BuildDedupeKey(entry)));
        }

        return BuildPreview(parsedFolders, parsedEntries, format);
    }

    private static Entry? BitwardenItemToEntry(BitwardenItem item)
    {
        EntryType type;
        EntryPayload payload;

        switch (item.Type)
        {
            case BitwardenItemType.Login:
                type = EntryType.Login;
                payload = new LoginPayload(
                    Username: item.Login?.Username ?? "",
                    Password: item.Login?.Password ?? "",
                    Url: item.Login?.Uris.FirstOrDefault()?.Uri ?? "",
                    TotpSecret: item.Login?.Totp);
                break;

            case BitwardenItemType.Card:
                type = EntryType.Card;
                payload = new CardPayload(
                    CardholderName: item.Card?.CardholderName ?? "",
                    Number: item.Card?.Number ?? "",
                    ExpiryMonth: item.Card?.ExpMonth ?? "",
                    ExpiryYear: item.Card?.ExpYear ?? "",
                    Cvv: item.Card?.Code ?? "",
                    Brand: item.Card?.Brand);
                break;

            case BitwardenItemType.Identity:
                type = EntryType.Identity;
                payload = new IdentityPayload(
                    FullName: BuildFullName(item.Identity?.FirstName, item.Identity?.LastName),
                    Email: item.Identity?.Email ?? "",
                    Phone: item.Identity?.Phone ?? "",
                    Address: item.Identity?.Address1 ?? "",
                    City: item.Identity?.City ?? "",
                    Country: item.Identity?.Country ?? "");
                break;

            case BitwardenItemType.SecureNote:
                type = EntryType.SecureNote;
                payload = new SecureNotePayload(Body: item.Notes ?? "");
                break;

            default:
                return null;
        }

        return new Entry
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Title = item.Name,
            Notes = item.Type == BitwardenItemType.SecureNote ? "" : (item.Notes ?? ""),
            FolderId = item.FolderId,
            Tags = Array.Empty<string>(),
            Payload = payload,
            CreatedUtc = item.CreationDate ?? DateTimeOffset.UtcNow,
            ModifiedUtc = item.RevisionDate ?? DateTimeOffset.UtcNow,
            LastUsedUtc = null,
        };
    }

    // ---- helpers ----

    private ImportLoadResult BuildPreview(
        IReadOnlyList<ParsedFolder> folders,
        IReadOnlyList<ParsedEntry> entries,
        ImportSourceFormat format)
    {
        var existingKeys = new HashSet<string>(
            _entries.ListAll().Select(BuildDedupeKey),
            StringComparer.OrdinalIgnoreCase);
        var existingFolderNames = new HashSet<string>(
            _folders.ListAll().Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);

        var newCount = entries.Count(e => !existingKeys.Contains(e.DedupeKey));
        var dupCount = entries.Count - newCount;
        var newFolderCount = folders.Count(f => !existingFolderNames.Contains(f.Name));
        var sample = entries
            .Where(e => !existingKeys.Contains(e.DedupeKey))
            .Take(10)
            .Select(e => e.Entry.Title)
            .ToList();

        var preview = new ImportPreview(
            Format: format,
            TotalCandidates: entries.Count,
            NewEntryCount: newCount,
            DuplicateCount: dupCount,
            NewFolderCount: newFolderCount,
            PreviewTitles: sample,
            ParsedEntries: entries,
            ParsedFolders: folders);

        return new ImportLoadResult(preview, ImportFailureReason.None);
    }

    /// <summary>
    /// Dedupe signature. Same key = same entry. Per Phase 9 plan: based on
    /// (Title, Username, URL) for logins, and (Title, type) for the rest.
    /// </summary>
    public static string BuildDedupeKey(Entry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Type).Append('|').Append(e.Title.Trim().ToLowerInvariant()).Append('|');
        if (e.Payload is LoginPayload p)
        {
            sb.Append(p.Username.Trim().ToLowerInvariant()).Append('|');
            sb.Append(p.Url.Trim().ToLowerInvariant());
        }
        return sb.ToString();
    }

    private static string BuildFullName(string? first, string? last)
    {
        var f = (first ?? "").Trim();
        var l = (last ?? "").Trim();
        if (f.Length == 0) return l;
        if (l.Length == 0) return f;
        return f + " " + l;
    }
}
