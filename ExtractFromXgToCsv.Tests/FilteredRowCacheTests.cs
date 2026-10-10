using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ExtractFromXgToCsv.Client.Services;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Direct tests of <see cref="FilteredRowCache"/> — the class that owns Web
/// mode's loaded rows, the materialized filter, and the filtered projections.
/// These pin the invariants <c>WebModePanelFilteringTests</c> used to reach
/// through the panel's private fields by reflection: no build before the
/// first Refilter; build on the first Refilter; cache hit on an equal
/// <see cref="FilterConfig"/> value, whatever the instance; rebuild on a
/// different value; row replacement re-projects without rebuilding. And the
/// run's one ranking:
/// the rows and the records are filtered under the cache's ranking, never
/// two. The panel-side routing is pinned separately by
/// <c>WebModePanelFilteringTests</c>.
/// </summary>
public class FilteredRowCacheTests
{
    private static CheckerPlayDecision Record(string player) =>
        TestRecords.CheckerPlay(descriptive: TestRecords.Descriptive(onRollName: player));

    private static DecisionRow Row(string player) => TestRecords.Row(Record(player));

    private static FilteredRowCache NewCache() => new(PlayRanking.Equity);

    private static FilterConfig AliceOnly() =>
        new() { Players = new List<string> { "Alice" } };

    [Fact]
    public void ReplaceRows_BeforeAnyRefilter_DoesNotBuild_AndProjectsNothing()
    {
        var cache = NewCache();

        cache.ReplaceRows(new[] { Row("Alice") }, new[] { Record("Alice") });

        Assert.Null(cache.BuiltSet);
        Assert.Single(cache.Rows);
        Assert.Empty(cache.FilteredRows);
        Assert.Empty(cache.FilteredDiagramRows);
    }

    [Fact]
    public void FirstRefilter_Builds_AndProjectsBothRowKinds()
    {
        var cache = NewCache();
        cache.ReplaceRows(
            new[] { Row("Alice"), Row("Bob") },
            new[] { Record("Alice"), Record("Bob") });

        cache.Refilter(AliceOnly());

        Assert.NotNull(cache.BuiltSet);
        Assert.Equal("Alice", Assert.Single(cache.FilteredRows).Player);
        Assert.Equal("Alice", Assert.Single(cache.FilteredDiagramRows).Descriptive.OnRollName);
    }

    [Fact]
    public void Refilter_SameConfigReference_ReusesBuiltSet()
    {
        var cache = NewCache();
        var cfg = AliceOnly();

        cache.Refilter(cfg);
        var built = cache.BuiltSet;
        cache.Refilter(cfg);

        Assert.Same(built, cache.BuiltSet);
    }

    /// <summary>
    /// The case the value key exists for: the host reads the filter in effect
    /// from <c>FilterSetup.ConfigInEffectFor</c>, a new instance on every call,
    /// so every render hands the cache a new reference to an unchanged filter.
    /// </summary>
    [Fact]
    public void Refilter_EqualConfigNewInstance_ReusesBuiltSet()
    {
        var cache = NewCache();

        cache.Refilter(AliceOnly());
        var built = cache.BuiltSet;
        cache.Refilter(AliceOnly());

        Assert.Same(built, cache.BuiltSet);
    }

    /// <summary>
    /// The key is the cache's own copy: mutating the instance handed in does
    /// not move the key, so the next Refilter with that instance sees a
    /// different value and rebuilds rather than keep a set built for a value
    /// no longer asked for.
    /// </summary>
    [Fact]
    public void Refilter_TheHandedInInstanceMutatedAfterwards_RebuildsForItsNewValue()
    {
        var cache = NewCache();
        cache.ReplaceRows(new[] { Row("Alice"), Row("Bob") }, Array.Empty<BgDecisionData>());
        var cfg = AliceOnly();
        cache.Refilter(cfg);
        var built = cache.BuiltSet;

        cfg.Players = new List<string> { "Bob" };
        cache.Refilter(cfg);

        Assert.NotSame(built, cache.BuiltSet);
        Assert.Equal("Bob", Assert.Single(cache.FilteredRows).Player);
    }

    [Fact]
    public void Refilter_DifferentValue_Rebuilds()
    {
        var cache = NewCache();
        cache.ReplaceRows(new[] { Row("Alice"), Row("Bob") }, Array.Empty<BgDecisionData>());

        cache.Refilter(AliceOnly());
        var built = cache.BuiltSet;
        cache.Refilter(new FilterConfig { Players = new List<string> { "Bob" } });

        Assert.NotSame(built, cache.BuiltSet);
        Assert.Equal("Bob", Assert.Single(cache.FilteredRows).Player);
    }

    [Fact]
    public void ReplaceRows_WithFilterInEffect_ProjectsImmediately_WithoutRebuilding()
    {
        // The seam the file-load site depends on: rows that arrive while a
        // filter is applied are filtered through it in the same call — no
        // second Refilter, and no Build.
        var cache = NewCache();
        cache.Refilter(AliceOnly());
        var built = cache.BuiltSet;

        cache.ReplaceRows(
            new[] { Row("Alice"), Row("Bob"), Row("Alice") },
            new[] { Record("Bob") });

        Assert.Same(built, cache.BuiltSet);
        Assert.Equal(2, cache.FilteredRows.Count);
        Assert.All(cache.FilteredRows, r => Assert.Equal("Alice", r.Player));
        Assert.Empty(cache.FilteredDiagramRows);
    }

    [Fact]
    public void ReplaceRows_ReplacesRatherThanAccumulates()
    {
        var cache = NewCache();
        cache.Refilter(new FilterConfig());
        cache.ReplaceRows(new[] { Row("Alice") }, Array.Empty<BgDecisionData>());

        cache.ReplaceRows(new[] { Row("Bob"), Row("Cara") }, Array.Empty<BgDecisionData>());

        Assert.Equal(new[] { "Bob", "Cara" }, cache.Rows.Select(r => r.Player));
        Assert.Equal(new[] { "Bob", "Cara" }, cache.FilteredRows.Select(r => r.Player));
    }

    [Fact]
    public void Clear_EmptiesRowsAndProjections_ButKeepsTheMaterializedFilter()
    {
        var cache = NewCache();
        cache.Refilter(AliceOnly());
        var built = cache.BuiltSet;
        cache.ReplaceRows(new[] { Row("Alice") }, new[] { Record("Alice") });

        cache.Clear();

        Assert.Empty(cache.Rows);
        Assert.Empty(cache.FilteredRows);
        Assert.Empty(cache.FilteredDiagramRows);
        // The applied filter survives a selection reset: rows loaded next
        // still re-project through it (the load-after-Apply pathway).
        Assert.Same(built, cache.BuiltSet);
        cache.ReplaceRows(new[] { Row("Alice"), Row("Bob") }, Array.Empty<BgDecisionData>());
        Assert.Equal("Alice", Assert.Single(cache.FilteredRows).Player);
    }

    // -----------------------------------------------------------------------
    //  One run, one ranking
    // -----------------------------------------------------------------------

    /// <summary>
    /// A 3-1 whose best play differs by ranking: the player played 8/5 6/5,
    /// at 4-ply +0.25, beside 13/10 6/5 at 2-ply +0.50. Under equity the
    /// shallower play is best, so the player erred by 0.25; under depth first
    /// the deeper play is best, and the player's is it, erring by 0.
    /// </summary>
    private static CheckerPlayDecision RankingSplit() =>
        TestRecords.CheckerPlay(decision: TestRecords.CheckerPlayData(
            plays:
            [
                TestRecords.Candidate(
                    play: [new(8, 5), new(6, 5)], analysisLevel: AnalysisLevel.Ply4, equity: 0.25),
                TestRecords.Candidate(
                    play: [new(13, 10), new(6, 5)], analysisLevel: AnalysisLevel.Ply2, equity: 0.50),
            ],
            userPlayIndex: 0));

    /// <summary>
    /// The pin for halheinrich/backgammon#273's one-run-one-ranking rule: a
    /// run's rows and its diagram records are filtered under the same ranking,
    /// the cache's. "Erred by at least 0.1" admits the split decision under
    /// equity and not under depth first, so each case shows the row and the
    /// record judged alike — and the depth-first case shows the record was
    /// judged under the cache's ranking, since a view under the equity default
    /// would have admitted it.
    /// </summary>
    [Theory]
    [InlineData(PlayRanking.Equity, 1)]
    [InlineData(PlayRanking.DepthFirst, 0)]
    public void OneRun_RowsAndRecords_AreFilteredUnderTheCachesRanking(
        PlayRanking ranking, int expectedMatches)
    {
        var record = RankingSplit();
        var cache = new FilteredRowCache(ranking);
        cache.ReplaceRows([DecisionRow.From(record, ranking)], [record]);

        cache.Refilter(new FilterConfig { ErrorMin = 0.1 });

        Assert.Equal(ranking, cache.Ranking);
        Assert.Equal(expectedMatches, cache.FilteredRows.Count);
        Assert.Equal(expectedMatches, cache.FilteredDiagramRows.Count);
    }

    [Fact]
    public void ReplaceRows_RefusesARowBuiltForAnotherRanking_AndReplacesNothing()
    {
        var cache = NewCache();
        cache.ReplaceRows([Row("Alice")], [Record("Alice")]);

        var ex = Assert.Throws<ArgumentException>(() => cache.ReplaceRows(
            [TestRecords.Row(Record("Bob"), PlayRanking.DepthFirst)], [Record("Bob")]));

        Assert.Equal("rows", ex.ParamName);
        Assert.Equal("Alice", Assert.Single(cache.Rows).Player);
    }

    [Fact]
    public void Constructor_RefusesAnUndefinedRanking()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FilteredRowCache((PlayRanking)99));
    }

    // -----------------------------------------------------------------------
    //  The collection rider
    // -----------------------------------------------------------------------

    /// <summary>
    /// What the cache hands out is immutable, so no holder can change what
    /// the cache or another holder reads (the collection rider of
    /// halheinrich/backgammon#273): each list refuses a write through the
    /// mutable interface it might be cast to.
    /// </summary>
    [Fact]
    public void HandedOutCollections_AreImmutable()
    {
        var cache = NewCache();
        cache.ReplaceRows([Row("Alice")], [Record("Alice")]);
        cache.Refilter(new FilterConfig());

        AssertImmutable(cache.Rows);
        AssertImmutable(cache.FilteredRows);
        AssertImmutable(cache.FilteredDiagramRows);
    }

    private static void AssertImmutable<T>(IReadOnlyList<T> list)
    {
        var asList = Assert.IsAssignableFrom<IList<T>>(list);
        Assert.True(asList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asList.Add(list[0]));
        Assert.Throws<NotSupportedException>(() => asList[0] = list[0]);
    }
}
