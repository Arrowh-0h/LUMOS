using System.Security.Cryptography;
using System.Text;
using Lumos.Core.Licensing;

// =============================================================================
//  Lumos key generator (PRIVATE TOOL — do not distribute)
// =============================================================================
//  Generates product keys that the Lumos app will accept. Uses the same
//  ProductKey algorithm + secret as the app, so keep this in sync with
//  src/Lumos.Core/Licensing/ProductKey.cs.
//
//  USAGE:
//    dotnet run --project tools/keygen            -> 1 key
//    dotnet run --project tools/keygen -- 10      -> 10 keys
//
//  When someone emails you for a key, run this, copy one line, send it.
// =============================================================================

const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

int count = 1;
if (args.Length > 0 && int.TryParse(args[0], out var n) && n > 0)
    count = Math.Min(n, 1000);

Console.WriteLine($"Generating {count} Lumos product key(s):");
Console.WriteLine();

for (int i = 0; i < count; i++)
{
    var serial = RandomSerial(8);
    var key = ProductKey.FromSerial(serial);

    // Self-check: every key we print must validate, or the secret drifted.
    if (!ProductKey.IsValid(key))
    {
        Console.Error.WriteLine("ERROR: generated a key that fails validation. " +
                                "The ProductKey secret/algorithm is out of sync.");
        return 1;
    }

    Console.WriteLine("  " + key);
}

Console.WriteLine();
Console.WriteLine("These validate against the current app build. If you change the");
Console.WriteLine("secret in ProductKey.cs, previously issued keys stop working.");
return 0;

static string RandomSerial(int length)
{
    var sb = new StringBuilder(length);
    Span<byte> buf = stackalloc byte[length];
    RandomNumberGenerator.Fill(buf);
    for (int i = 0; i < length; i++)
        sb.Append(Alphabet[buf[i] % Alphabet.Length]);
    return sb.ToString();
}
