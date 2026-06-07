using System.Globalization;
using System.Text;
using System.Text.Json;
using Lumos.Core.Entries;

namespace Lumos.Core.PortableExport;

public enum ExportFormat
{
    /// <summary>Lumos native, encrypted with the LXP1 envelope.</summary>
    LumosEncrypted,
    /// <summary>Lumos native, plaintext JSON (intended for re-import or audit).</summary>
    LumosJson,
    /// <summary>Bitwarden plaintext JSON.</summary>
    BitwardenJson,
    /// <summary>Bitwarden's encrypted-export shape (we use the same LXP1 envelope).</summary>
    BitwardenEncrypted,
    /// <summary>Flat CSV. Export only.</summary>
    Csv,
}

/// <summary>
/// Turns a vault (entries + folders) into bytes in a chosen export format.
/// The class is stateless — each Export call reads fresh from the repos.
/// </summary>
public sealed class VaultExporter
{
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public VaultExporter(EntryRepository entries, FolderRepository folders)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folders);
        _entries = entries;
        _folders = folders;
    }

    /// <summary>
    /// Serialize the vault into bytes. <paramref name="passphrase"/> is
    /// required for any *Encrypted format and ignored otherwise.
    /// </summary>
    public byte[] Export(ExportFormat format, string? passphrase = null)
    {
        switch (format)
        {
            case ExportFormat.LumosJson:
                return Encoding.UTF8.GetBytes(BuildLumosJson(indented: true));

            case ExportFormat.LumosEncrypted:
                RequirePassphrase(passphrase);
                return ExportEnvelope.Encode(
                    Encoding.UTF8.GetBytes(BuildLumosJson(indented: false)),
                    passphrase!);

            case ExportFormat.BitwardenJson:
                return Encoding.UTF8.GetBytes(BuildBitwardenJson(encrypted: false, indented: true));

            case ExportFormat.BitwardenEncrypted:
                RequirePassphrase(passphrase);
                return ExportEnvelope.Encode(
                    Encoding.UTF8.GetBytes(BuildBitwardenJson(encrypted: false, indented: false)),
                    passphrase!);

            case ExportFormat.Csv:
                return Encoding.UTF8.GetBytes(BuildCsv());

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format.");
        }
    }

    // ---- Lumos native ----

    private string BuildLumosJson(bool indented)
    {
        var export = new LumosExport
        {
            Folders = _folders.ListAll().Select(f => new LumosExportFolder
            {
                Id = f.Id,
                Name = f.Name,
                ParentId = f.ParentId,
            }).ToList(),
            Entries = _entries.ListAll().Select(EntryToLumosExport).ToList(),
        };
        var opts = indented ? JsonOpts : new JsonSerializerOptions(JsonOpts) { WriteIndented = false };
        return JsonSerializer.Serialize(export, opts);
    }

    private static LumosExportEntry EntryToLumosExport(Entry e)
    {
        var dto = new LumosExportEntry
        {
            Id = e.Id,
            Title = e.Title,
            Type = e.Type,
            Notes = e.Notes,
            FolderId = e.FolderId,
            Tags = e.Tags.ToList(),
            CreatedUtc = e.CreatedUtc,
            ModifiedUtc = e.ModifiedUtc,
            LastUsedUtc = e.LastUsedUtc,
        };

        switch (e.Payload)
        {
            case LoginPayload p:
                dto.Username = p.Username;
                dto.Password = p.Password;
                dto.Url = p.Url;
                dto.TotpSecret = p.TotpSecret;
                break;
            case CardPayload p:
                dto.CardholderName = p.CardholderName;
                dto.CardNumber = p.Number;
                dto.Cvv = p.Cvv;
                dto.ExpiryMonth = p.ExpiryMonth;
                dto.ExpiryYear = p.ExpiryYear;
                dto.CardBrand = p.Brand;
                break;
            case IdentityPayload p:
                // Split FullName into first/last (best-effort: split on first space).
                var sp = p.FullName.IndexOf(' ');
                dto.FirstName = sp > 0 ? p.FullName.Substring(0, sp) : p.FullName;
                dto.LastName = sp > 0 ? p.FullName.Substring(sp + 1) : null;
                dto.Email = p.Email;
                dto.Phone = p.Phone;
                dto.Address = p.Address;
                dto.City = p.City;
                dto.Country = p.Country;
                break;
            case SecureNotePayload p:
                // Body lives in Notes for SecureNote — we copy it there so the
                // export format treats it uniformly with the other types'
                // top-level Notes field.
                if (!string.IsNullOrEmpty(p.Body) && string.IsNullOrEmpty(dto.Notes))
                    dto.Notes = p.Body;
                break;
        }

        return dto;
    }

    // ---- Bitwarden ----

    private string BuildBitwardenJson(bool encrypted, bool indented)
    {
        var bw = new BitwardenExport
        {
            Encrypted = encrypted,
            Folders = _folders.ListAll().Select(f => new BitwardenFolder
            {
                Id = f.Id,
                Name = f.Name,
            }).ToList(),
            Items = _entries.ListAll().Select(EntryToBitwarden).ToList(),
        };
        var opts = indented ? JsonOpts : new JsonSerializerOptions(JsonOpts) { WriteIndented = false };
        return JsonSerializer.Serialize(bw, opts);
    }

    private static BitwardenItem EntryToBitwarden(Entry e)
    {
        var item = new BitwardenItem
        {
            Id = e.Id,
            Name = e.Title,
            Notes = string.IsNullOrEmpty(e.Notes) ? null : e.Notes,
            FolderId = e.FolderId,
            CreationDate = e.CreatedUtc,
            RevisionDate = e.ModifiedUtc,
        };

        switch (e.Payload)
        {
            case LoginPayload p:
                item.Type = BitwardenItemType.Login;
                item.Login = new BitwardenLogin
                {
                    Username = NullIfEmpty(p.Username),
                    Password = NullIfEmpty(p.Password),
                    Totp = p.TotpSecret,
                    Uris = string.IsNullOrEmpty(p.Url)
                        ? new List<BitwardenUri>()
                        : new List<BitwardenUri> { new() { Uri = p.Url } },
                };
                break;
            case CardPayload p:
                item.Type = BitwardenItemType.Card;
                item.Card = new BitwardenCard
                {
                    CardholderName = NullIfEmpty(p.CardholderName),
                    Brand = p.Brand,
                    Number = NullIfEmpty(p.Number),
                    ExpMonth = NullIfEmpty(p.ExpiryMonth),
                    ExpYear = NullIfEmpty(p.ExpiryYear),
                    Code = NullIfEmpty(p.Cvv),
                };
                break;
            case IdentityPayload p:
                item.Type = BitwardenItemType.Identity;
                var sp = p.FullName.IndexOf(' ');
                item.Identity = new BitwardenIdentity
                {
                    FirstName = sp > 0 ? p.FullName.Substring(0, sp) : NullIfEmpty(p.FullName),
                    LastName = sp > 0 ? p.FullName.Substring(sp + 1) : null,
                    Email = NullIfEmpty(p.Email),
                    Phone = NullIfEmpty(p.Phone),
                    Address1 = NullIfEmpty(p.Address),
                    City = NullIfEmpty(p.City),
                    Country = NullIfEmpty(p.Country),
                };
                break;
            case SecureNotePayload p:
                item.Type = BitwardenItemType.SecureNote;
                item.SecureNote = new BitwardenSecureNote { Type = 0 };
                // Bitwarden puts secure note body in the item's Notes field.
                if (string.IsNullOrEmpty(item.Notes) && !string.IsNullOrEmpty(p.Body))
                    item.Notes = p.Body;
                break;
        }

        return item;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    // ---- CSV ----

    private string BuildCsv()
    {
        var sb = new StringBuilder();
        // Header — wide table, mostly-null columns per type. CSV is for
        // spreadsheet workflows; importers should use JSON instead.
        sb.AppendLine("type,title,folder,notes,username,password,url,totp,cardholder,card_number,cvv,exp_month,exp_year,full_name,email,phone,address,city,country");

        var folderById = _folders.ListAll().ToDictionary(f => f.Id, f => f.Name);
        foreach (var e in _entries.ListAll())
        {
            var folderName = e.FolderId is not null && folderById.TryGetValue(e.FolderId, out var n) ? n : "";
            var row = new List<string?>
            {
                e.Type.ToString(),
                e.Title,
                folderName,
                e.Notes,
            };

            switch (e.Payload)
            {
                case LoginPayload p:
                    row.AddRange(new[] { p.Username, p.Password, p.Url, p.TotpSecret });
                    Pad(row, 8);   // pad to card+identity columns count
                    break;
                case CardPayload p:
                    Pad(row, 4);
                    row.AddRange(new[] { p.CardholderName, p.Number, p.Cvv, p.ExpiryMonth, p.ExpiryYear });
                    Pad(row, 9);
                    break;
                case IdentityPayload p:
                    Pad(row, 9);
                    row.AddRange(new[] { p.FullName, p.Email, p.Phone, p.Address, p.City, p.Country });
                    break;
                case SecureNotePayload p:
                    // Body went into Notes column.
                    if (string.IsNullOrEmpty(e.Notes)) row[3] = p.Body;
                    Pad(row, 15);
                    break;
            }

            sb.AppendLine(string.Join(",", row.Select(CsvEscape)));
        }
        return sb.ToString();
    }

    private static void Pad(List<string?> row, int targetLength)
    {
        while (row.Count < targetLength) row.Add(null);
    }

    /// <summary>Escape a CSV field per RFC 4180.</summary>
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void RequirePassphrase(string? p)
    {
        if (string.IsNullOrEmpty(p))
            throw new ArgumentException("Passphrase is required for encrypted export formats.", nameof(p));
    }
}
