using Bunit;
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
/// while <c>FilterApplied &amp;&amp; !FilterDirty</c> is (still) true are
/// projected through the set in effect by <see cref="FilteredRowCache"/>'s
/// <c>ReplaceRows</c> — no rebuild, no second Apply at this layer.
/// <para>
/// <b>The end-to-end user contract this once pinned is superseded</b> (#78):
/// in the real app a file selection bumps Home's selection generation, the
/// hosted <c>FilterSurface</c> ends the setup, and the panel receives
/// <c>FilterApplied = false</c> — so a post-Apply selection now blanks the
/// preview until re-Apply (pinned at Home level by
/// <see cref="HomeWiringTests"/>). These tests drive the panel directly with
/// the applied parameters held true, which is exactly what isolates the
/// cache-routing half: if a host ever keeps the applied state across a
/// selection again, this is the projection it gets.
/// </para>
/// </summary>
public class WebModePanelRefilterOnLoadTests : BunitContext
{
    private const string FixtureA = "MTCH4064.xg";
    private const string FixtureB = "ajhhBG0407.xg";

    public WebModePanelRefilterOnLoadTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    private IRenderedComponent<WebModePanel> Panel() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false));

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
        var rowsB = svc.ExtractDecisions(FixtureHelper.ReadFixture(FixtureB), FixtureB);
        var player = rowsB[0].Player;
        var cfg = new FilterConfig { Players = new List<string> { player } };
        var built = cfg.Build();
        var expected = rowsB.Count(r => built.Matches(r));
        Assert.True(expected > 0 && expected < rowsB.Count,
            "the filter must be selective on fixture B for this test to prove anything");

        var cut = Panel();
        Upload(cut, FixtureA);
        cut.Render(p => p
            .Add(c => c.FilterConfig, cfg)
            .Add(c => c.FilterApplied, true));

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
        var rowsA = svc.ExtractDecisions(FixtureHelper.ReadFixture(FixtureA), FixtureA);
        var rowsB = svc.ExtractDecisions(FixtureHelper.ReadFixture(FixtureB), FixtureB);
        var playerA = rowsA[0].Player;
        Assert.DoesNotContain(rowsB, r => r.Player == playerA);
        var cfg = new FilterConfig { Players = new List<string> { playerA } };

        var cut = Panel();
        Upload(cut, FixtureA);
        cut.Render(p => p
            .Add(c => c.FilterConfig, cfg)
            .Add(c => c.FilterApplied, true));
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
