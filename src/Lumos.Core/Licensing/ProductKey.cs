using System.Security.Cryptography;
using System.Text;

namespace Lumos.Core.Licensing;

/// <summary>
/// Product-key format and validation.
///
/// Format:  LUMOS-XXXX-XXXX-XXXX
///   - 12 payload characters from an unambiguous Crockford-style base32
///     alphabet (no 0/O, 1/I/L, U).
///   - First 8 chars = "serial" (random per key).
///   - Last 4 chars  = truncated HMAC-SHA256(serial, secret), mapped into the
///     same alphabet. This is the partial-signature: only someone with the
///     secret can produce a serial+signature pair that validates.
///
/// HONEST LIMITATION (see docs/DECISIONS.md, D-V2-07): the secret ships inside
/// the application, and .NET IL is decompilable. A determined reverse-engineer
/// can recover the secret and forge keys. This scheme makes that *effort*
/// necessary (it resists casual guessing and looks like a real product key),
/// but it is deliberately NOT presented as unbreakable. Lumos is free; the key
/// is a "feels official" gate, not a security control.
/// </summary>
public static class ProductKey
{
    public const string Prefix = "LUMOS";

    // Crockford base32 minus a couple extra ambiguous chars. 32 symbols.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    // ^ 30 chars; we index modulo this length. Unambiguous for humans typing.

    // The shared secret. The in-app validator and the offline key generator
    // must use the SAME value. Change it to invalidate all previously issued
    // keys (a "new batch"). This is intentionally a single point of control.
    //
    // NOTE: keep this in sync with tools/keygen. Changing it here without
    // regenerating keys will reject every key issued under the old secret.
    private static readonly byte[] Secret =
        Encoding.UTF8.GetBytes("lumos-rel1-0e6c0dfb2da6404e6fd835d1c0b482e6485f84716b70497a");

    private const int SerialLength = 8;
    private const int SignatureLength = 4;

    /// <summary>
    /// Validate a product key string. Accepts any casing and tolerates missing
    /// or extra dashes/spaces. Returns true only if the signature matches.
    /// </summary>
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        var normalized = Normalize(key);
        if (normalized is null) return false;

        var (serial, signature) = normalized.Value;
        var expected = ComputeSignature(serial);
        // Constant-time compare on the signature chars.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(signature),
            Encoding.ASCII.GetBytes(expected));
    }

    /// <summary>
    /// Produce a full key string for a given 8-char serial. Used by the offline
    /// generator tool. The serial must already be in the alphabet.
    /// </summary>
    public static string FromSerial(string serial)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        serial = serial.Trim().ToUpperInvariant();
        if (serial.Length != SerialLength || !serial.All(c => Alphabet.Contains(c)))
            throw new ArgumentException(
                $"Serial must be {SerialLength} chars from the key alphabet.", nameof(serial));

        var sig = ComputeSignature(serial);
        var payload = serial + sig; // 12 chars
        return $"{Prefix}-{payload[..4]}-{payload[4..8]}-{payload[8..12]}";
    }

    /// <summary>
    /// Format a raw 12-char payload (serial+signature) into the dashed display
    /// form. Exposed mostly for tests / tooling.
    /// </summary>
    public static string Format(string payload12)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload12);
        if (payload12.Length != SerialLength + SignatureLength)
            throw new ArgumentException("Payload must be 12 chars.", nameof(payload12));
        var p = payload12.ToUpperInvariant();
        return $"{Prefix}-{p[..4]}-{p[4..8]}-{p[8..12]}";
    }

    // --- internals -----------------------------------------------------------

    /// <summary>
    /// Strip the prefix, dashes, and spaces; upper-case; verify the alphabet;
    /// split into (serial, signature). Returns null if the shape is wrong.
    /// </summary>
    private static (string Serial, string Signature)? Normalize(string key)
    {
        var cleaned = new StringBuilder();
        foreach (var ch in key.Trim().ToUpperInvariant())
        {
            if (ch is '-' or ' ') continue;
            cleaned.Append(ch);
        }
        var s = cleaned.ToString();

        // Drop an optional leading "LUMOS".
        if (s.StartsWith(Prefix, StringComparison.Ordinal))
            s = s[Prefix.Length..];

        if (s.Length != SerialLength + SignatureLength) return null;
        if (!s.All(c => Alphabet.Contains(c))) return null;

        return (s[..SerialLength], s[SerialLength..]);
    }

    /// <summary>HMAC-SHA256(serial, secret) truncated and mapped to 4 alphabet chars.</summary>
    private static string ComputeSignature(string serial)
    {
        using var hmac = new HMACSHA256(Secret);
        var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(serial));

        var sb = new StringBuilder(SignatureLength);
        for (int i = 0; i < SignatureLength; i++)
            sb.Append(Alphabet[hash[i] % Alphabet.Length]);
        return sb.ToString();
    }
}
