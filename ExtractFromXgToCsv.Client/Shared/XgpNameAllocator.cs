using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Stateful per-export-run name source. Renders the batch's
/// <see cref="XgpNameTemplate"/> for each decision, appends <c>.xgp</c>, and
/// applies the Windows-style <c>" (2)"</c>, <c>" (3)"</c>, … suffix when a
/// base name repeats within the run. Uniquification is per-run only —
/// cross-run collisions on disk remain governed by the persisted counter
/// (the <c>{n}</c> token), by design.
///
/// <para>
/// The <see cref="Peek"/>/<see cref="Commit"/> split is load-bearing: the
/// Local-mode processor computes a decision's name before writing it and
/// consumes the slot only after the write succeeds, so a failed decision
/// doesn't burn a number (the client persists its counter from the reported
/// row total). The Web-mode zip builder uses <see cref="Next"/> — any
/// failure there aborts the whole zip, so there is no partial-success state
/// to protect.
/// </para>
///
/// <para>
/// <see cref="Create"/> is where the app's <see cref="XgpExportOptions"/>
/// DTO stops: it validates the options (the single throw point for both
/// export pathways) and copies the primitives out, so nothing below the
/// allocator couples to the app type and the engine stays lib-ready.
/// </para>
/// </summary>
public sealed class XgpNameAllocator
{
    private const string Extension = ".xgp";

    private readonly XgpNameTemplate _template;
    private readonly int _startNumber;
    private readonly int _suffixLength;
    private readonly FilterConfig _filters;

    // Windows filenames are case-insensitive, so the duplicate bookkeeping
    // is too. _issuedNames closes the residual hole the count alone leaves:
    // a pattern can render a literal "name (2)" that a generated uniquifier
    // has already claimed (or will claim) — issued names are never repeated.
    private readonly Dictionary<string, int> _baseNameCounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _issuedNames =
        new(StringComparer.OrdinalIgnoreCase);
    private int _index;

    private XgpNameAllocator(
        XgpNameTemplate template, int startNumber, int suffixLength, FilterConfig filters)
    {
        _template = template;
        _startNumber = startNumber;
        _suffixLength = suffixLength;
        _filters = filters;
    }

    /// <summary>
    /// Validates <paramref name="options"/> and builds an allocator for one
    /// export run over the batch's active <paramref name="filters"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> fails <see cref="XgpExportOptions.TryValidate"/> —
    /// the single validation throw point for both export pathways.
    /// </exception>
    public static XgpNameAllocator Create(XgpExportOptions options, FilterConfig filters)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(filters);
        if (!options.TryValidate(out var error))
            throw new ArgumentException(error, nameof(options));

        // TryValidate passing implies the pattern parses.
        XgpNameTemplate.TryParse(options.NamePattern, out var template, out _);
        return new XgpNameAllocator(
            template!, options.StartNumber, options.SuffixLength, filters);
    }

    /// <summary>
    /// The full filename (uniquifier and <c>.xgp</c> extension included) the
    /// current slot would assign to <paramref name="row"/>. No state change —
    /// idempotent until the slot is consumed by <see cref="Commit"/>.
    /// </summary>
    public string Peek(DecisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Resolve(row).FullName;
    }

    /// <summary>
    /// Consumes the current slot for <paramref name="row"/>: registers the
    /// name <see cref="Peek"/> reported and advances the counter index.
    /// </summary>
    public void Commit(DecisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var (fullName, baseName, attempt) = Resolve(row);
        _issuedNames.Add(fullName);
        _baseNameCounts[baseName] = attempt + 1;
        _index++;
    }

    /// <summary><see cref="Peek"/> then <see cref="Commit"/> in one call.</summary>
    public string Next(DecisionRow row)
    {
        var name = Peek(row);
        Commit(row);
        return name;
    }

    private (string FullName, string BaseName, int Attempt) Resolve(DecisionRow row)
    {
        var baseName = _template.Render(new XgpNameContext
        {
            StartNumber = _startNumber,
            SuffixLength = _suffixLength,
            Filters = _filters,
            Row = row,
            Index = _index,
        });

        // The k-th repeat of a base name takes the Windows-style " (k)"
        // suffix; the issued-name check then skips over any candidate a
        // colliding literal already produced.
        int attempt = _baseNameCounts.GetValueOrDefault(baseName);
        while (true)
        {
            var fullName =
                (attempt == 0 ? baseName : $"{baseName} ({attempt + 1})") + Extension;
            if (!_issuedNames.Contains(fullName))
                return (fullName, baseName, attempt);
            attempt++;
        }
    }
}
