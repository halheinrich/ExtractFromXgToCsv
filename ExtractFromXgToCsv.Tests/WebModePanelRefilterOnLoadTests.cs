using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the <em>panel-layer</em> projection rule: rows handed to the cache
/// while a filter is (still) in effect are projected through the set in
/// effect by <see cref="FilteredRowCache"/>'s <c>ReplaceRows</c> — no rebuild,
/// no second Apply at this layer.
/// <para>
/// In the real app a new selection carries a new source token, so the filter
/// setup ends: an applied selection is dropped and the preview waits for
/// Apply, while the empty selection stays in effect without one
/// (halheinrich/backgammon#266) — both pinned at Home level by
/// <see cref="HomeWiringTests"/>. These tests drive the panel directly with a
/// filter held in effect, which is exactly what isolates the cache-routing
/// half: whatever the host keeps in effect across a selection, this is the
/// projection it gets.
/// </para>
/// </summary>
public class WebModePanelRefilterOnLoadTests : BunitContext
{
    private const string FixtureA = "MTCH4064.xg";
    private const string FixtureB = "ajhhBG0407.xg";

    public WebModePanelRefilterOnLoadTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    private IRenderedComponent<WebModePanel> Panel() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, (FilterConfig?)null));

    private static void Upload(IRenderedComponent<WebModePanel> cut, string fixture) =>
        // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(fixture), fixture));

    [Fact]
    public void FilesSelectedAfterApply_AreFilteredWithoutSecondApply()
    {
        // The expected outcome comes from outside the panel: fixture B's rows
        // put through a genuinely selective filter (B's first player only).
        var svc = new XgProcessingService();
        var rowsB = svc.ExtractDecisions(FixtureHelper.ReadFixture(FixtureB), FixtureB, AppRanking.Ranking);
        var player = rowsB[0].Player!;
        var cfg = new FilterConfig { Players = new List<string> { player } };
        var built = cfg.Build();
        var expected = rowsB.Count(r => built.Matches(r));
        Assert.True(expected > 0 && expected < rowsB.Count,
            "the filter must be selective on fixture B for this test to prove anything");

        var cut = Panel();
        Upload(cut, FixtureA);
        cut.Render(p => p
            .Add(c => c.FilterInEffect, cfg));

        // Select B while the filter is applied — no second Apply follows.
        Upload(cut, FixtureB);

        Assert.Equal(expected, cut.Instance.RowCache.FilteredRows.Count);
        Assert.All(cut.Instance.RowCache.FilteredRows,
            r => Assert.Equal(player, r.Player));
        // And the rendered count line reflects the fresh selection, filtered.
        Assert.Contains(
            $"{expected} of {rowsB.Count} rows match current filters",
            cut.Markup);
    }

    [Fact]
    public void FilesSelectedAfterApply_AllRejected_LandInZeroMatchNotice()
    {
        // A filter naming a player from fixture A rejects everything in
        // fixture B — first proven, not assumed, so a future fixture change
        // fails loudly here rather than silently weakening the test.
        var svc = new XgProcessingService();
        var rowsA = svc.ExtractDecisions(FixtureHelper.ReadFixture(FixtureA), FixtureA, AppRanking.Ranking);
        var rowsB = svc.ExtractDecisions(FixtureHelper.ReadFixture(FixtureB), FixtureB, AppRanking.Ranking);
        var playerA = rowsA[0].Player!;
        Assert.DoesNotContain(rowsB, r => r.Player == playerA);
        var cfg = new FilterConfig { Players = new List<string> { playerA } };

        var cut = Panel();
        Upload(cut, FixtureA);
        cut.Render(p => p
            .Add(c => c.FilterInEffect, cfg));
        Assert.Empty(cut.FindAll(".zero-match-notice"));

        // Select B while A's player filter is applied: every new row fails,
        // and that surfaces as the explicit zero-match notice — not as a
        // stale preview or a silent success line.
        Upload(cut, FixtureB);

        Assert.True(cut.Instance.RowCache.Rows.Count > 0, "fixture B should load rows");
        Assert.Empty(cut.Instance.RowCache.FilteredRows);
        var notice = cut.Find(".zero-match-notice");
        Assert.Contains("No decisions matched", notice.TextContent);
    }
}
