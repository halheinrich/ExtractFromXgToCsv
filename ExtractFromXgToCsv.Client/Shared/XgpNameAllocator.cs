using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Stateful per-export-run name source. Renders the batch's
/// <see cref="XgpNameTemplate"/> for each decision, appends <c>.xgp</c>, and
/// numbers a base name that repeats within the run by
/// <see cref="NumberedFileName"/>'s rule: <c>" (2)"</c>, <c>" (3)"</c>, ….
/// Uniquification here is per-run only. A Local run that meets a file of the
/// issued name on disk carries on the issued name's sequence there
/// (<see cref="NumberedFileName.Next"/>); the persisted counter (the
/// <c>{n}</c> token) keeps such meetings rare, but never prevents them.
///
/// <para>
/// The <see cref="Peek"/>/<see cref="Commit"/> split is load-bearing: the
/// Local-mode processor computes a decision's name before writing it and
/// consumes the slot only after the write succeeds, so a decision that was
/// skipped, or whose write ended the run, doesn't burn a number.
/// <see cref="CommittedCount"/> — the numbers a run has used — is the count
/// the run's <see cref="XgpExportCommit"/> carries to the persisted counter.
/// The Web-mode archive builder uses <see cref="Next"/> once a decision's
/// payload has been prepared — a skipped preparation takes no slot — and its
/// allocator is provisional: the download commits the archive's entries only
/// at the browser hand-off.
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
    /// The file name (uniquifier and <c>.xgp</c> extension included) the
    /// current slot would assign to <paramref name="row"/>, numbered so that a
    /// Local run can carry its sequence on past a file on disk. No state change —
    /// idempotent until the slot is consumed by <see cref="Commit"/>.
    /// </summary>
    public NumberedFileName Peek(DecisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Resolve(row);
    }

    /// <summary>
    /// Consumes the current slot for <paramref name="row"/>: registers the
    /// name <see cref="Peek"/> reported and advances the counter index.
    /// </summary>
    public void Commit(DecisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var name = Resolve(row);
        _issuedNames.Add(name.Name);
        _baseNameCounts[name.Stem] = name.Ordinal;
        _index++;
    }

    /// <summary>
    /// The slots committed so far — the number of names this allocator has
    /// issued, and so the count of <c>{n}</c> values the run has used.
    /// </summary>
    public int CommittedCount => _index;

    /// <summary><see cref="Peek"/> then <see cref="Commit"/> in one call.</summary>
    public NumberedFileName Next(DecisionRow row)
    {
        var name = Peek(row);
        Commit(row);
        return name;
    }

    private NumberedFileName Resolve(DecisionRow row)
    {
        var baseName = _template.Render(new XgpNameContext
        {
            StartNumber = _startNumber,
            SuffixLength = _suffixLength,
            Filters = _filters,
            Row = row,
            Index = _index,
        });

        // A base name's next number is one past the last it took; the
        // issued-name check then skips over any candidate a colliding
        // literal already produced.
        var name = NumberedFileName.Create(
            baseName, _baseNameCounts.GetValueOrDefault(baseName) + 1, Extension);
        while (_issuedNames.Contains(name.Name))
            name = name.Next();
        return name;
    }
}
