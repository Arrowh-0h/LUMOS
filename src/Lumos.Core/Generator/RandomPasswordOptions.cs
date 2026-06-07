namespace Lumos.Core.Generator;

/// <summary>
/// Options for character-based random password generation.
/// </summary>
public sealed record RandomPasswordOptions
{
    public int Length { get; init; } = 24;
    public bool IncludeUppercase { get; init; } = true;
    public bool IncludeLowercase { get; init; } = true;
    public bool IncludeDigits { get; init; } = true;
    public bool IncludeSymbols { get; init; } = true;

    /// <summary>
    /// When true, excludes characters that are easy to confuse visually:
    /// 0 O o 1 l I | ` ' "
    /// </summary>
    public bool ExcludeAmbiguous { get; init; } = false;

    /// <summary>
    /// When true, guarantee at least one character from each enabled set.
    /// Useful for sites that require "at least one digit" etc.
    /// </summary>
    public bool RequireOneOfEachClass { get; init; } = true;

    public void Validate()
    {
        if (Length < 4 || Length > 128)
            throw new ArgumentException($"Length must be between 4 and 128 (got {Length}).");
        if (!(IncludeUppercase || IncludeLowercase || IncludeDigits || IncludeSymbols))
            throw new ArgumentException("At least one character class must be enabled.");
        if (RequireOneOfEachClass)
        {
            var classCount =
                (IncludeUppercase ? 1 : 0) +
                (IncludeLowercase ? 1 : 0) +
                (IncludeDigits ? 1 : 0) +
                (IncludeSymbols ? 1 : 0);
            if (Length < classCount)
                throw new ArgumentException(
                    $"Length ({Length}) must be at least the number of enabled classes ({classCount}) " +
                    "when RequireOneOfEachClass is true.");
        }
    }
}
