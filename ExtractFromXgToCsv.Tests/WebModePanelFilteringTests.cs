using BgDataTypes_Lib;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wire tests pinning that <see cref="WebModePanel"/> routes its live
/// filtering through <see cref="FilteredRowCache"/> under its one gate,
/// <c>FilterInEffect</c>: no materialization while none is in effect;
/// materialization when one is; a re-render with an <em>equal</em> config is a
/// cache hit although the instance is new — the host reads the filter from
/// <c>FilterSetup</c>, whose <c>ConfigInEffectFor</c> returns a new instance
/// on every call — and a different value rebuilds; a filter no longer in
/// effect leaves the cache untouched. The projection rules themselves are
/// pinned by <c>FilteredRowCacheTests</c> — here only the panel→cache routing
/// is under test, observed through the panel's internal <c>RowCache</c> seam
/// (no reflection). The migration failure mode this guards: a panel that
/// compiles but filters beside the cache instead of through it.
/// </summary>
public class WebModePanelFilteringTests : BunitContext
{
    public WebModePanelFilteringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    [Fact]
    public void NotInEffect_LeavesBuiltSetUnpopulated()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, (FilterConfig?)null));

        Assert.Null(cut.Instance.RowCache.BuiltSet);
    }

    [Fact]
    public void InEffect_BuildsAndCachesByFilterConfigValue()
    {
        // Initial render: nothing in effect — no build expected.
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, (FilterConfig?)null));

        Assert.Null(cut.Instance.RowCache.BuiltSet);

        // Alice comes into effect — build fires.
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { Players = ["Alice"] }));
        var built1 = cut.Instance.RowCache.BuiltSet;
        Assert.NotNull(built1);

        // A NEW instance of the SAME value — what every host render hands
        // down — is a cache hit: the built set is unchanged.
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { Players = ["Alice"] }));
        Assert.Same(built1, cut.Instance.RowCache.BuiltSet);

        // A different value rebuilds.
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { Players = ["Bob"] }));
        Assert.NotSame(built1, cut.Instance.RowCache.BuiltSet);
        Assert.NotNull(cut.Instance.RowCache.BuiltSet);
    }

    [Fact]
    public void NoLongerInEffect_DoesNotRebuild()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig()));

        var built1 = cut.Instance.RowCache.BuiltSet;
        Assert.NotNull(built1);

        // The shape an edit arrives in: the selection on screen no longer
        // equals what is in effect, so the host hands down null. The cache
        // keeps the set built from the last filter in effect rather than build
        // the pending one — a rebuild here would filter the preview by a
        // filter the user has not applied.
        cut.Render(p => p.Add(c => c.FilterInEffect, (FilterConfig?)null));
        Assert.Same(built1, cut.Instance.RowCache.BuiltSet);
    }

    /// <summary>
    /// One run, one ranking, through the panel (halheinrich/backgammon#273):
    /// a real file selection loads rows built for the application's ranking
    /// into a cache for that same ranking, so the preview's rows and the
    /// Diagram JSON's records are filtered under one ranking. The cache's own
    /// test pins that it filters both under its ranking; this pins that the
    /// panel builds it for the application's, and extracts for it.
    /// </summary>
    [Fact]
    public void FileSelection_LoadsAndFiltersUnderTheAppsOneRanking()
    {
        const string fixture = "MTCH4064.xg";
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig()));

        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(fixture), fixture));

        var cache = cut.Instance.RowCache;
        Assert.Equal(AppRanking.Ranking, cache.Ranking);
        Assert.NotEmpty(cache.Rows);
        Assert.All(cache.Rows, r => Assert.Equal(AppRanking.Ranking, r.Ranking));
        Assert.Equal(
            cache.FilteredRows.Select(r => r.Id),
            cache.FilteredDiagramRows.Select(d => d.Id));
    }
}
