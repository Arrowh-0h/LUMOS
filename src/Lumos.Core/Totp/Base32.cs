namespace Lumos.Core.Totp;

/// <summary>
/// RFC 4648 Base32 codec. TOTP secrets are transmitted as Base32 because
/// it's the convention authenticator apps use (Google Authenticator,
/// Authy, 1Password, etc.). No padding on output — Google Authenticator
/// is strict about that.
///
/// RFC 4648 base32 codec. Self-contained in Lumos.Core; no external deps.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return "";

        var output = new char[(data.Length * 8 + 4) / 5];
        var outIdx = 0;
        var buf = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buf = (buf << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output[outIdx++] = Alphabet[(buf >> bits) & 0x1F];
            }
        }
        if (bits > 0)
        {
            output[outIdx++] = Alphabet[(buf << (5 - bits)) & 0x1F];
        }
        return new string(output, 0, outIdx);
    }

    public static byte[] Decode(string base32)
    {
        ArgumentNullException.ThrowIfNull(base32);
        var s = base32.Replace(" ", "").Replace("=", "").ToUpperInvariant();
        if (s.Length == 0) return Array.Empty<byte>();

        var output = new byte[s.Length * 5 / 8];
        var outIdx = 0;
        var buf = 0;
        var bits = 0;
        foreach (var c in s)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException($"Invalid base32 character: '{c}'");
            buf = (buf << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[outIdx++] = (byte)((buf >> bits) & 0xFF);
            }
        }
        return output;
    }

    /// <summary>Cheap validity check used by the UI to refuse junk secrets at save time.</summary>
    public static bool IsValid(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { Decode(text); return true; }
        catch { return false; }
    }
}
