using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Services;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Direct tests of <see cref="FilteredRowCache"/> — the class that owns Web
/// mode's loaded rows, the materialized filter, and the filtered projections.
/// These pin the invariants <c>WebModePanelFilteringTests</c> used to reach
/// through the panel's private fields by reflection: no build before the
/// first Refilter; build on the first Refilter; cache hit on the same
/// <see cref="FilterConfig"/> reference; rebuild on a new reference; row
/// replacement re-projects without rebuilding. The panel-side routing is
/// pinned separately by <c>WebModePanelFilteringTests</c>.
/// </summary>
public class FilteredRowCacheTests
{
    private static DecisionRow Row(string player, string file = "a.xgp") =>
        new() { Id = new XgpDecisionId(file), Player = player };

    private static BgDecisionData DiagramRow(string player, string file = "a.xgp") =>
        new()
        {
            Id = new XgpDecisionId(file),
            Descriptive = new DescriptiveData { OnRollName = player },
        };

    private static FilterConfig AliceOnly() =>
        new() { Players = new List<string> { "Alice" } };

    [Fact]
    public void ReplaceRows_BeforeAnyRefilter_DoesNotBuild_AndProjectsNothing()
    {
        var cache = new FilteredRowCache();

        cache.ReplaceRows(new[] { Row("Alice") }, new[] { DiagramRow("Alice") });

        Assert.Null(cache.BuiltSet);
        Assert.Single(cache.Rows);
        Assert.Empty(cache.FilteredRows);
        Assert.Empty(cache.FilteredDiagramRows);
    }

    [Fact]
    public void FirstRefilter_Builds_AndProjectsBothRowKinds()
    {
        var cache = new FilteredRowCache();
        cache.ReplaceRows(
            new[] { Row("Alice"), Row("Bob") },
            new[] { DiagramRow("Alice"), DiagramRow("Bob") });

        cache.Refilter(AliceOnly());

        Assert.NotNull(cache.BuiltSet);
        Assert.Equal("Alice", Assert.Single(cache.FilteredRows).Player);
        Assert.Equal("Alice", Assert.Single(cache.FilteredDiagramRows).Player);
    }

    [Fact]
    public void Refilter_SameConfigReference_ReusesBuiltSet()
    {
        var cache = new FilteredRowCache();
        var cfg = AliceOnly();

        cache.Refilter(cfg);
        var built = cache.BuiltSet;
        cache.Refilter(cfg);

        Assert.Same(built, cache.BuiltSet);
    }

    [Fact]
    public void Refilter_NewConfigReference_Rebuilds()
    {
        var cache = new FilteredRowCache();
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
        var cache = new FilteredRowCache();
        cache.Refilter(AliceOnly());
        var built = cache.BuiltSet;

        cache.ReplaceRows(
            new[] { Row("Alice"), Row("Bob"), Row("Alice") },
            new[] { DiagramRow("Bob") });

        Assert.Same(built, cache.BuiltSet);
        Assert.Equal(2, cache.FilteredRows.Count);
        Assert.All(cache.FilteredRows, r => Assert.Equal("Alice", r.Player));
        Assert.Empty(cache.FilteredDiagramRows);
    }

    [Fact]
    public void ReplaceRows_ReplacesRatherThanAccumulates()
    {
        var cache = new FilteredRowCache();
        cache.Refilter(new FilterConfig());
        cache.ReplaceRows(new[] { Row("Alice") }, Array.Empty<BgDecisionData>());

        cache.ReplaceRows(new[] { Row("Bob"), Row("Cara") }, Array.Empty<BgDecisionData>());

        Assert.Equal(new[] { "Bob", "Cara" }, cache.Rows.Select(r => r.Player));
        Assert.Equal(new[] { "Bob", "Cara" }, cache.FilteredRows.Select(r => r.Player));
    }

    [Fact]
    public void Clear_EmptiesRowsAndProjections_ButKeepsTheMaterializedFilter()
    {
        var cache = new FilteredRowCache();
        cache.Refilter(AliceOnly());
        var built = cache.BuiltSet;
        cache.ReplaceRows(new[] { Row("Alice") }, new[] { DiagramRow("Alice") });

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
}
