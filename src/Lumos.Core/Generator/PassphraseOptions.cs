namespace Lumos.Core.Generator;

/// <summary>
/// Options for diceware-style passphrase generation.
/// </summary>
public sealed record PassphraseOptions
{
    public int WordCount { get; init; } = 5;
    public string Separator { get; init; } = "-";
    public bool CapitalizeWords { get; init; } = false;

    /// <summary>
    /// If true, appends a 2-4 digit number at the end of the passphrase.
    /// Some sites require a digit; this satisfies that without breaking
    /// the human-memorable structure.
    /// </summary>
    public bool AppendDigits { get; init; } = false;

    public int DigitCount { get; init; } = 3;

    public void Validate()
    {
        if (WordCount < 3 || WordCount > 12)
            throw new ArgumentException($"WordCount must be between 3 and 12 (got {WordCount}).");
        if (Separator is null)
            throw new ArgumentException("Separator cannot be null.");
        if (AppendDigits && (DigitCount < 1 || DigitCount > 8))
            throw new ArgumentException($"DigitCount must be between 1 and 8 (got {DigitCount}).");
    }
}
