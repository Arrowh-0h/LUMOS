using System.Reflection;

namespace Lumos.Core.Generator;

/// <summary>
/// Loads the EFF Large Wordlist as an embedded resource. We use this list
/// because it's the recognized standard for diceware passphrases: 7,776
/// words, deliberately filtered to be memorable, distinct, and free of
/// offensive entries.
///
/// The list is loaded once on first access and cached for the process
/// lifetime. ~60 KB, ~10 ms to parse on a normal machine.
///
/// Source: https://www.eff.org/dice
/// </summary>
internal static class EffWordList
{
    public const string ResourcePath = "Lumos.Core.Resources.eff_large_wordlist.txt";
    public const int ExpectedWordCount = 7776;

    private static readonly Lazy<IReadOnlyList<string>> _words = new(LoadList);
    public static IReadOnlyList<string> Words => _words.Value;

    private static IReadOnlyList<string> LoadList()
    {
        var asm = typeof(EffWordList).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePath)
            ?? throw new InvalidOperationException(
                $"EFF wordlist embedded resource not found at '{ResourcePath}'. " +
                $"This usually means the file 'Resources/eff_large_wordlist.txt' is missing " +
                $"from Lumos.Core or isn't marked as an embedded resource in the .csproj. " +
                $"Get the canonical file from https://www.eff.org/dice (eff_large_wordlist.txt).");

        using var reader = new StreamReader(stream);
        var list = new List<string>(ExpectedWordCount);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // EFF format: "11111\tabacus" — dice roll prefix and tab, then word.
            // We accept either format: dice-prefixed or just the bare word.
            line = line.Trim();
            if (line.Length == 0) continue;
            var tabIndex = line.IndexOf('\t');
            var word = tabIndex >= 0 ? line[(tabIndex + 1)..].Trim() : line;
            // A few EFF entries are hyphenated (drop-down, felt-tip, t-shirt,
            // yo-yo). Hyphen is also the default passphrase separator, so a
            // hyphenated word would create ambiguous word boundaries (and
            // break any split-on-separator logic). Strip internal hyphens so
            // every word is a single clean token: "drop-down" -> "dropdown".
            if (word.Contains('-'))
                word = word.Replace("-", "");
            if (word.Length > 0) list.Add(word);
        }

        if (list.Count < 1000)
            throw new InvalidOperationException(
                $"EFF wordlist appears truncated or malformed: only {list.Count} words loaded. " +
                $"Expected {ExpectedWordCount}.");

        return list;
    }
}
