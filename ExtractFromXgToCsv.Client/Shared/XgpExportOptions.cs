namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Naming options for a batch of per-decision <c>.xgp</c> exports: the
/// user-editable <see cref="NamePattern"/> plus the counter state
/// (<see cref="StartNumber"/>/<see cref="SuffixLength"/>) that drives the
/// pattern's <c>{n}</c> token. Rendering lives in the naming engine — the
/// WASM zip builder and the server-side Local-mode processor both name files
/// through an <see cref="XgpNameAllocator"/> created from these options.
///
/// <para>
/// Permissive wire DTO like <see cref="ProcessRequest"/> — it crosses the
/// Local-mode POST boundary, so validation is a separate
/// <see cref="TryValidate"/> call (the UI gates on it; the export pathways
/// throw <see cref="ArgumentException"/> via
/// <see cref="XgpNameAllocator.Create"/>) rather than throwing property
/// initializers that would break deserialization.
/// </para>
/// </summary>
public sealed record XgpExportOptions
{
    /// <summary>
    /// The pattern a fresh install names files by: <c>pos001.xgp</c>,
    /// <c>pos002.xgp</c>, … — byte-identical to the pre-pattern
    /// <c>{prefix}{counter}</c> naming this pattern engine replaced.
    /// </summary>
    public const string DefaultNamePattern = "pos{n}";

    /// <summary>
    /// Filename pattern for each exported decision, without the <c>.xgp</c>
    /// extension: literal text plus <c>{token}</c> placeholders drawn from
    /// <see cref="XgpNameTokens.All"/> (e.g.
    /// <c>"Move{min-move}_{dice}_{score}"</c>). Duplicate rendered names
    /// within a run get Windows-style <c>" (2)"</c> suffixes.
    /// </summary>
    public string NamePattern { get; init; } = DefaultNamePattern;

    /// <summary>Number the <c>{n}</c> token assigns to the first exported decision (1-based counter).</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>
    /// Zero-padded width of the <c>{n}</c> token's value (1–9). Numbers wider
    /// than this grow naturally rather than truncating (e.g. width 3, number
    /// 1000 → <c>pos1000.xgp</c>).
    /// </summary>
    public int SuffixLength { get; init; } = 3;

    /// <summary>
    /// Validates the pattern (via <see cref="XgpNameTemplate.TryParse"/> —
    /// its error text is surfaced verbatim) and the option ranges. Returns
    /// <see langword="false"/> with a user-displayable
    /// <paramref name="error"/> on the first violation found.
    /// </summary>
    public bool TryValidate(out string error)
    {
        if (!XgpNameTemplate.TryParse(NamePattern, out _, out error))
            return false;
        if (StartNumber < 1)
        {
            error = "Next number must be at least 1.";
            return false;
        }
        if (SuffixLength is < 1 or > 9)
        {
            error = "Suffix length must be between 1 and 9.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
