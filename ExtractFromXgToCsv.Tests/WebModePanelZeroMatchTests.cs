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
/// Pins the Web-mode zero-match notice: when files are loaded and an active
/// filter set excludes every row, the panel surfaces an explicit notice
/// instead of the bare "0 of N rows match" muted line — the silent-zero the
/// BgQuiz A1 finding flagged.
///
/// The activeness signal here is emergent, not a re-inspection of
/// <see cref="FilterConfig"/> fields: an empty (inactive) set passes every
/// row, so <c>_rows.Count &gt; 0 &amp;&amp; _filteredRows.Count == 0</c> can only
/// happen when the built set is non-empty. No lib-side emptiness check is
/// needed on this path.
/// </summary>
public class WebModePanelZeroMatchTests : BunitContext
{
    private const string XgFixture = "MTCH4064.xg";

    public WebModePanelZeroMatchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    [Fact]
    public void FilterExcludesEveryRow_RendersZeroMatchNotice()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));

        // A filter naming a player who never appears rejects every row: the
        // built set is non-empty, _rows.Count > 0, _filteredRows.Count == 0.
        cut.Render(p => p
            .Add(c => c.FilterConfig,
                new FilterConfig { Players = new List<string> { "__no_such_player__" } })
            .Add(c => c.FilterApplied, true));

        Assert.True(
            bUnitTestHelpers.GetPrivateField<List<BgDataTypes_Lib.DecisionRow>>(
                cut.Instance, "_rows").Count > 0,
            "fixture should load rows");
        Assert.Empty(
            bUnitTestHelpers.GetPrivateField<List<BgDataTypes_Lib.DecisionRow>>(
                cut.Instance, "_filteredRows"));

        var notice = cut.Find(".zero-match-notice");
        Assert.Contains("No decisions matched", notice.TextContent);
    }

    [Fact]
    public void FilterMatchesRows_DoesNotRenderZeroMatchNotice()
    {
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));

        // An empty (inactive) filter passes every row → filtered == loaded > 0.
        cut.Render(p => p.Add(c => c.FilterApplied, true));

        Assert.True(
            bUnitTestHelpers.GetPrivateField<List<BgDataTypes_Lib.DecisionRow>>(
                cut.Instance, "_filteredRows").Count > 0,
            "empty filter should pass every loaded row");
        Assert.Empty(cut.FindAll(".zero-match-notice"));
    }
}
