using System.Collections.Immutable;
using BgDataTypes_Lib;
using XgFilter_Lib.Filtering;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Web mode's projections: the converted decision rows and diagram records,
/// each retained unfiltered, the materialized filter currently in effect, and
/// the filtered projections derived from them. Rows and records arrive
/// separately — rows convert on a pick or a book change, records only when an
/// output needs them — and each set is replaced whole. The owner of when they
/// arrive is <c>WebSession</c>; this type owns only how they are filtered.
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
/// <b>The projections always reflect the current rows and records.</b>
/// <see cref="ReplaceRows"/> and <see cref="ReplaceRecords"/> re-project the
/// incoming set through the already-materialized filter immediately (without
/// rebuilding it), so items that arrive while a filter is in effect never sit
/// unfiltered waiting for the next Apply. Until the first
/// <see cref="Refilter"/> there is no set and the projections stay empty — the
/// panel shows nothing filtered before a filter is first in effect. Refiltering
/// reads only what is retained: it converts and reads nothing.
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
    private IReadOnlyList<BgDecisionData> _records = ImmutableArray<BgDecisionData>.Empty;

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

    /// <summary>All converted decision rows, unfiltered — the unfiltered row total is their count.</summary>
    public IReadOnlyList<DecisionRow> Rows => _rows;

    /// <summary>All converted diagram records, unfiltered.</summary>
    public IReadOnlyList<BgDecisionData> Records => _records;

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
    /// Empties the rows, the records and both projections — a fresh file
    /// selection starts from nothing. The materialized filter is retained: it
    /// still describes the applied config, so items loaded next re-project
    /// through it.
    /// </summary>
    public void Clear()
    {
        ReplaceRows([]);
        ReplaceRecords([]);
    }

    /// <summary>
    /// Replaces the converted rows and immediately re-projects them through
    /// the materialized filter, if one is in effect. Never builds — arrival
    /// must not trigger <see cref="FilterConfig.Build"/> (that belongs to
    /// <see cref="Refilter"/>, the filter-in-effect path). The records are
    /// untouched.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A row in <paramref name="rows"/> was built for a ranking other than
    /// <see cref="Ranking"/>. Nothing is replaced.
    /// </exception>
    public void ReplaceRows(IEnumerable<DecisionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var newRows = rows.ToImmutableArray();
        foreach (var row in newRows)
        {
            if (row.Ranking != Ranking)
                throw new ArgumentException(
                    $"Row {row.Id} was built for the {row.Ranking} ranking; this run is under {Ranking}.",
                    nameof(rows));
        }

        _rows = newRows;
        Materialize();
    }

    /// <summary>
    /// Replaces the converted records — each filtered through its view for
    /// <see cref="Ranking"/>, built once here — and immediately re-projects
    /// them, as <see cref="ReplaceRows"/> does the rows. The rows are
    /// untouched.
    /// </summary>
    public void ReplaceRecords(IEnumerable<BgDecisionData> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var newRecords = records.ToImmutableArray();
        _diagramRows = newRecords.Select(r => (r, r.ViewFor(Ranking))).ToImmutableArray();
        _records = newRecords;
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
