using System.Collections.Immutable;
using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Web mode's live-preview row state: the extracted decision rows and diagram
/// records, the materialized filter currently in effect, and the filtered
/// projections derived from them. Extracted from <c>WebModePanel</c> so the
/// projection rules are a directly-testable unit instead of component-private
/// fields.
///
/// <para>
/// Three rules live here — and only here:
/// </para>
/// <para>
/// <b>One run, one ranking.</b> A cache is built for one
/// <see cref="PlayRanking"/> (<see cref="Ranking"/>), and everything it holds
/// is interpreted under it: a row is accepted only if it was built for that
/// ranking (<see cref="DecisionRow.Ranking"/>), and a diagram record is
/// filtered through its view for that ranking
/// (<see cref="BgDecisionData.ViewFor"/>). So a run's rows and its records can
/// never be filtered under two rankings. The ranking's meaning is the
/// producer's; the cache only carries the one it was given.
/// </para>
/// <para>
/// <b>Materialization is cached by <see cref="FilterConfig"/> value.</b> The
/// host reads the filter in effect from XgFilter_Razor's <c>FilterSetup</c>,
/// whose <c>ConfigInEffectFor</c> returns a new instance on every call, so a
/// reference would differ on every render while the filter had not changed.
/// <see cref="Refilter"/> calls <see cref="FilterConfig.Build"/> only when
/// handed a config whose value differs from the one it last built
/// (<see cref="FilterConfig.Equals(FilterConfig)"/>), keeping the build out of
/// the per-row <see cref="DecisionFilterSet.Matches"/> hot path. The key is a
/// copy the cache owns, so a caller mutating the instance it handed in cannot
/// move the key out from under the built set.
/// </para>
/// <para>
/// <b>The projections always reflect the current rows.</b>
/// <see cref="ReplaceRows"/> re-projects the incoming rows through the
/// already-materialized set immediately (without rebuilding it), so rows that
/// arrive while a filter is in effect never sit unfiltered waiting for the
/// next Apply. Until the first <see cref="Refilter"/> there is no set and the
/// projections stay empty — the panel shows nothing filtered before a filter
/// is first in effect.
/// </para>
/// <para>
/// Every collection the cache hands out is immutable, and each replacement is
/// a new one: a holder can never change what the cache, or another holder,
/// reads (the collection rider of halheinrich/backgammon#273).
/// </para>
/// </summary>
internal sealed class FilteredRowCache
{
    // Each list is an ImmutableArray, boxed once where it is assigned, so the
    // IReadOnlyList properties hand out an immutable collection without boxing
    // on every read.
    private IReadOnlyList<DecisionRow> _rows = ImmutableArray<DecisionRow>.Empty;
    private IReadOnlyList<DecisionRow> _filteredRows = ImmutableArray<DecisionRow>.Empty;
    private IReadOnlyList<BgDecisionData> _filteredDiagramRows = ImmutableArray<BgDecisionData>.Empty;

    // Each record beside its view for Ranking, built once when the records
    // arrive, so a Refilter reads the views rather than rebuilding them.
    private ImmutableArray<(BgDecisionData Record, IDecisionFilterData View)> _diagramRows = [];

    // Cached materialization of the last Refilter config — see the class
    // remarks for the value-equality rule these two implement.
    private DecisionFilterSet? _builtSet;
    private FilterConfig? _lastBuiltConfig;

    /// <summary>Creates an empty cache whose rows and records are interpreted under <paramref name="ranking"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ranking"/> is not a defined ranking.</exception>
    public FilteredRowCache(PlayRanking ranking)
    {
        if (!Enum.IsDefined(ranking))
            throw new ArgumentOutOfRangeException(nameof(ranking), ranking, "Not a defined ranking.");
        Ranking = ranking;
    }

    /// <summary>
    /// The ranking this cache's rows were built for and its records are
    /// filtered under — the run's one ranking.
    /// </summary>
    public PlayRanking Ranking { get; }

    /// <summary>All loaded decision rows, unfiltered.</summary>
    public IReadOnlyList<DecisionRow> Rows => _rows;

    /// <summary>The decision rows passing the materialized filter; empty until the first <see cref="Refilter"/>.</summary>
    public IReadOnlyList<DecisionRow> FilteredRows => _filteredRows;

    /// <summary>
    /// The diagram records passing the materialized filter, each judged through
    /// its view for <see cref="Ranking"/>; empty until the first
    /// <see cref="Refilter"/>.
    /// </summary>
    public IReadOnlyList<BgDecisionData> FilteredDiagramRows => _filteredDiagramRows;

    /// <summary>
    /// The materialized filter currently in effect — null until the first
    /// <see cref="Refilter"/>. Exposed so the cache's invariants (build once
    /// per config value, never on row replacement) are observable without
    /// reflection.
    /// </summary>
    public DecisionFilterSet? BuiltSet => _builtSet;

    /// <summary>
    /// Empties the rows and both projections — a fresh file selection starts
    /// from nothing. The materialized filter is retained: it still describes
    /// the applied config, so rows loaded next re-project through it.
    /// </summary>
    public void Clear()
    {
        _rows = ImmutableArray<DecisionRow>.Empty;
        _diagramRows = [];
        _filteredRows = ImmutableArray<DecisionRow>.Empty;
        _filteredDiagramRows = ImmutableArray<BgDecisionData>.Empty;
    }

    /// <summary>
    /// Replaces the loaded rows and records and immediately re-projects them
    /// through the materialized filter, if one is in effect. Never builds — row
    /// arrival must not trigger <see cref="FilterConfig.Build"/> (that belongs
    /// to <see cref="Refilter"/>, the filter-in-effect path).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A row in <paramref name="rows"/> was built for a ranking other than
    /// <see cref="Ranking"/>. Nothing is replaced.
    /// </exception>
    public void ReplaceRows(
        IEnumerable<DecisionRow> rows, IEnumerable<BgDecisionData> diagramRows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(diagramRows);

        var newRows = rows.ToImmutableArray();
        foreach (var row in newRows)
        {
            if (row.Ranking != Ranking)
                throw new ArgumentException(
                    $"Row {row.Id} was built for the {row.Ranking} ranking; this run is under {Ranking}.",
                    nameof(rows));
        }
        var newDiagramRows = diagramRows.Select(r => (r, r.ViewFor(Ranking))).ToImmutableArray();

        _rows = newRows;
        _diagramRows = newDiagramRows;
        Materialize();
    }

    /// <summary>
    /// Makes <paramref name="config"/> the filter in effect and recomputes
    /// both projections. Builds via <see cref="FilterConfig.Build"/> only when
    /// <paramref name="config"/>'s value differs from the one already
    /// materialized — see the class remarks for why value is the cache key.
    /// </summary>
    public void Refilter(FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (_lastBuiltConfig is null || !_lastBuiltConfig.Equals(config))
        {
            _builtSet = config.Build();
            // The canonical JSON round-trip is the copy: FilterConfig is
            // mutable and has no other, and equality survives it.
            _lastBuiltConfig = FilterConfig.FromJson(config.ToJson());
        }
        Materialize();
    }

    private void Materialize()
    {
        if (_builtSet is not { } set)
        {
            // No filter in effect yet — the projections are empty by
            // definition, not left holding whatever a caller saw last.
            _filteredRows = ImmutableArray<DecisionRow>.Empty;
            _filteredDiagramRows = ImmutableArray<BgDecisionData>.Empty;
            return;
        }
        _filteredRows = _rows.Where(set.Matches).ToImmutableArray();
        _filteredDiagramRows = _diagramRows
            .Where(d => set.Matches(d.View))
            .Select(d => d.Record)
            .ToImmutableArray();
    }
}
