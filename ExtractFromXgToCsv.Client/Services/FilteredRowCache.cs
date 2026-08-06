using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Web mode's live-preview row state: the extracted decision and diagram rows,
/// the materialized filter currently in effect, and the filtered projections
/// derived from the two. Extracted from <c>WebModePanel</c> so the projection
/// rules are a directly-testable unit instead of component-private fields.
///
/// <para>
/// Two rules live here — and only here:
/// </para>
/// <para>
/// <b>Materialization is cached by <see cref="FilterConfig"/> reference
/// identity.</b> The filter panel inside XgFilter_Razor's <c>FilterSurface</c>
/// mints a new <see cref="FilterConfig"/> instance on every Apply, so reference
/// inequality reliably signals "rebuild". <see cref="Refilter"/> calls
/// <see cref="FilterConfig.Build"/> only when handed a config instance it has
/// not built yet, keeping the build out of the per-row
/// <see cref="DecisionFilterSet.Matches"/> hot path.
/// </para>
/// <para>
/// <b>The projections always reflect the current rows.</b>
/// <see cref="ReplaceRows"/> re-projects the incoming rows through the
/// already-materialized set immediately (without rebuilding it), so rows that
/// arrive while a filter is in effect never sit unfiltered waiting for the
/// next Apply. Until the first <see cref="Refilter"/> there is no set and the
/// projections stay empty — the panel shows nothing filtered before the first
/// Apply.
/// </para>
/// </summary>
internal sealed class FilteredRowCache
{
    private readonly List<DecisionRow> _rows = new();
    private readonly List<BgDecisionData> _diagramRows = new();
    private List<DecisionRow> _filteredRows = new();
    private List<BgDecisionData> _filteredDiagramRows = new();

    // Cached materialization of the last Refilter config — see the class
    // remarks for the reference-identity rule these two implement.
    private DecisionFilterSet? _builtSet;
    private FilterConfig? _lastBuiltConfig;

    /// <summary>All loaded decision rows, unfiltered.</summary>
    public IReadOnlyList<DecisionRow> Rows => _rows;

    /// <summary>The decision rows passing the materialized filter; empty until the first <see cref="Refilter"/>.</summary>
    public IReadOnlyList<DecisionRow> FilteredRows => _filteredRows;

    /// <summary>The diagram rows passing the materialized filter; empty until the first <see cref="Refilter"/>.</summary>
    public IReadOnlyList<BgDecisionData> FilteredDiagramRows => _filteredDiagramRows;

    /// <summary>
    /// The materialized filter currently in effect — null until the first
    /// <see cref="Refilter"/>. Exposed so the identity-cache invariants
    /// (build once per config instance, never on row replacement) are
    /// observable without reflection.
    /// </summary>
    public DecisionFilterSet? BuiltSet => _builtSet;

    /// <summary>
    /// Empties the rows and both projections — a fresh file selection starts
    /// from nothing. The materialized filter is retained: it still describes
    /// the applied config, so rows loaded next re-project through it.
    /// </summary>
    public void Clear()
    {
        _rows.Clear();
        _diagramRows.Clear();
        _filteredRows = new();
        _filteredDiagramRows = new();
    }

    /// <summary>
    /// Replaces the loaded rows and immediately re-projects them through the
    /// materialized filter, if one is in effect. Never builds — row arrival
    /// must not trigger <see cref="FilterConfig.Build"/> (that belongs to
    /// <see cref="Refilter"/>, the Apply-driven path).
    /// </summary>
    public void ReplaceRows(
        IEnumerable<DecisionRow> rows, IEnumerable<BgDecisionData> diagramRows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(diagramRows);

        _rows.Clear();
        _rows.AddRange(rows);
        _diagramRows.Clear();
        _diagramRows.AddRange(diagramRows);
        Materialize();
    }

    /// <summary>
    /// Makes <paramref name="config"/> the filter in effect and recomputes
    /// both projections. Builds via <see cref="FilterConfig.Build"/> only when
    /// <paramref name="config"/> is not the instance already materialized —
    /// see the class remarks for why reference identity is the cache key.
    /// </summary>
    public void Refilter(FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!ReferenceEquals(_lastBuiltConfig, config))
        {
            _builtSet = config.Build();
            _lastBuiltConfig = config;
        }
        Materialize();
    }

    private void Materialize()
    {
        if (_builtSet is null)
        {
            // No filter in effect yet — the projections are empty by
            // definition, not left holding whatever a caller saw last.
            _filteredRows = new();
            _filteredDiagramRows = new();
            return;
        }
        _filteredRows = _rows.Where(_builtSet.Matches).ToList();
        _filteredDiagramRows = _diagramRows.Where(_builtSet.Matches).ToList();
    }
}
