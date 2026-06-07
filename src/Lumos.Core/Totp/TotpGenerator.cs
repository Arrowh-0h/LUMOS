using System.Security.Cryptography;

namespace Lumos.Core.Totp;

/// <summary>
/// A snapshot of a TOTP at a moment in time. The UI uses
/// <see cref="SecondsRemaining"/> and <see cref="FractionRemaining"/> to
/// drive the countdown ring without doing the maths itself.
/// </summary>
public sealed record TotpSnapshot(
    string Code,
    double SecondsRemaining,
    double FractionRemaining);   // 1.0 = just rolled, 0.0 = about to roll

/// <summary>
/// RFC 6238 client-side TOTP. 30-second steps, 6-digit codes, HMAC-SHA1.
/// Used by the entry detail view to show the current code with a countdown.
///
/// Client-side RFC 6238 TOTP. Fully offline — no network, no backend.
/// </summary>
public static class TotpGenerator
{
    public const int CodeDigits = 6;
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Compute the current code + countdown info for the given secret at
    /// <paramref name="at"/>.
    /// </summary>
    public static TotpSnapshot Snapshot(string base32Secret, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(base32Secret);
        var secret = Base32.Decode(base32Secret);
        var unixSeconds = (at - DateTimeOffset.UnixEpoch).TotalSeconds;
        var step = (long)(unixSeconds / Period.TotalSeconds);
        var code = ComputeForStep(secret, step);

        var secondsInStep = unixSeconds - (step * Period.TotalSeconds);
        var secondsRemaining = Period.TotalSeconds - secondsInStep;
        var fraction = secondsRemaining / Period.TotalSeconds;
        return new TotpSnapshot(code, secondsRemaining, fraction);
    }

    /// <summary>Try to extract a base32 secret from arbitrary user input.</summary>
    /// <remarks>
    /// Accepts either:
    ///   - A bare base32 string (with optional spaces / case differences)
    ///   - An <c>otpauth://totp/...</c> URI from which we pull the
    ///     <c>secret</c> parameter.
    /// Returns null if the input doesn't parse to a valid secret.
    /// </remarks>
    public static string? TryParseSecret(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();

        // otpauth:// URI form
        if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&'))
            {
                var idx = pair.IndexOf('=');
                if (idx <= 0) continue;
                var key = pair.Substring(0, idx);
                var value = Uri.UnescapeDataString(pair.Substring(idx + 1));
                if (string.Equals(key, "secret", StringComparison.OrdinalIgnoreCase))
                {
                    return Base32.IsValid(value) ? Normalize(value) : null;
                }
            }
            return null;
        }

        // Plain base32 form
        return Base32.IsValid(trimmed) ? Normalize(trimmed) : null;
    }

    private static string Normalize(string secret) =>
        secret.Replace(" ", "").Replace("=", "").ToUpperInvariant();

    private static string ComputeForStep(byte[] secret, long step)
    {
        Span<byte> message = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { message[i] = (byte)(step & 0xFF); step >>= 8; }

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, message, hash);

        // RFC 4226 §5.3 dynamic truncation.
        var offset = hash[19] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);
        var code = binary % 1_000_000;
        return code.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
