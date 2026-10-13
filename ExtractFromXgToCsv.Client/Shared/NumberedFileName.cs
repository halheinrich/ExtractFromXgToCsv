namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// A file name numbered in the Windows style a browser gives a file downloaded
/// twice: the stem alone first, then <c>"stem (2)"</c>, <c>"stem (3)"</c>, …,
/// each before the extension. The one statement of that rule. An XGP run's
/// <see cref="XgpNameAllocator"/> numbers a name repeated within the run by it,
/// and a Local run numbers an output whose name a file already holds by it
/// (the umbrella's <c>SPEC-notices.md</c> §7.5: Local output files are never
/// overwritten).
/// </summary>
/// <remarks>
/// Immutable. A name is made only by <see cref="Create"/>, <see cref="FirstOf"/>
/// or <see cref="Next"/>, so its ordinal is never below 1, and
/// <see cref="Name"/> is always the rule's rendering of its parts.
/// </remarks>
public sealed record NumberedFileName
{
    private NumberedFileName(string stem, int ordinal, string extension)
    {
        Stem = stem;
        Ordinal = ordinal;
        Extension = extension;
    }

    /// <summary>The name before any number and the extension.</summary>
    public string Stem { get; }

    /// <summary>The name's place in its stem's sequence: 1 for the stem alone, then 2, 3, ….</summary>
    public int Ordinal { get; }

    /// <summary>The extension, its dot included; empty for none.</summary>
    public string Extension { get; }

    /// <summary>
    /// The file name: <c>stem.ext</c> for the first, <c>stem (n).ext</c> for
    /// the n-th.
    /// </summary>
    public string Name => Ordinal == 1 ? Stem + Extension : $"{Stem} ({Ordinal}){Extension}";

    /// <summary>The <paramref name="ordinal"/>-th name of <paramref name="stem"/> with <paramref name="extension"/>.</summary>
    /// <param name="stem">The name before any number and the extension.</param>
    /// <param name="ordinal">The name's place in the sequence, from 1.</param>
    /// <param name="extension">The extension, its dot included; empty for none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is below 1.</exception>
    public static NumberedFileName Create(string stem, int ordinal, string extension)
    {
        ArgumentNullException.ThrowIfNull(stem);
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        return new NumberedFileName(stem, ordinal, extension);
    }

    /// <summary>
    /// <paramref name="fileName"/> as the first of its sequence: its stem and
    /// extension as <see cref="Path"/> splits them. A number it already
    /// carries is part of its stem, so the name after <c>"report (2).csv"</c>
    /// is <c>"report (2) (2).csv"</c>.
    /// </summary>
    public static NumberedFileName FirstOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return new NumberedFileName(
            Path.GetFileNameWithoutExtension(fileName), 1, Path.GetExtension(fileName));
    }

    /// <summary>The next name of the sequence: the same stem and extension, numbered one higher.</summary>
    /// <exception cref="OverflowException">The ordinal is already <see cref="int.MaxValue"/>.</exception>
    public NumberedFileName Next() => new(Stem, checked(Ordinal + 1), Extension);

    /// <summary>The file name, <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}
