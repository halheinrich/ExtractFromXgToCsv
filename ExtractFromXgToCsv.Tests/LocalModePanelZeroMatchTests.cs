using BgUiPrimitives_Razor.TestSupport;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;
using Bunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the Local-mode filtered-to-zero notice. The panel has no browser-side
/// rows to count, and <c>TotalRows == 0</c> alone is ambiguous (empty folder
/// vs. filtered-to-zero). The distinguisher is the lib seam <see cref="DecisionFilterSet.IsEmpty"/> — an empty set admits
/// every row, so zero rows through a non-empty set is a genuine filter miss.
///
/// The notice hangs off the completed-success branch only: it must appear
/// alongside the "✓ Done" line, never on error or cancelled terminations
/// where it would mislead. And the set it asks is the <em>run's</em> filter,
/// never the one on screen now: the box describes the run. It stands down for
/// any entry on the run's skip record, an omission's as much as a file's: the
/// zero may be theirs, and the list says so. Each case runs a real run against
/// a server that writes zero rows, because the run is where the coordinator
/// records which filter it ran.
/// </summary>
public class LocalModePanelZeroMatchTests : BunitContext
{
    public LocalModePanelZeroMatchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    private void ServerAnswers(ProcessingProgress terminal) =>
        Services.AddSingleton(new HttpClient(new CapturingProcessHandler(terminal))
        {
            BaseAddress = new Uri("http://localhost/"),
        });

    private static readonly SkipRecord OnlyOmissions =
    [
        SkippedItem.ForGame("match.xg", "Invalid standing in match.xg, game 1: refused."),
        SkippedItem.ForDecision("match.xg", decisionId: null, "Corrupt candidate in match.xg, game 2, move 1, roll 31: bad."),
    ];

    /// <summary>
    /// The walk left out what the run might have kept, and nothing else was
    /// skipped: zero rows through an active filter is then not the filter's to
    /// answer for (halheinrich/backgammon#514). The notice stands down for an
    /// omission as it does for a file, and the list says what was lost.
    /// </summary>
    [Fact]
    public async Task CompleteZeroRows_ActiveFilter_OnlyOmissions_ListsThem_AndBlamesNoFilter()
    {
        var cut = (await RunAsync(errorMin: "0.05", skipped: OnlyOmissions)).Panel;

        Assert.Empty(cut.FindAll("div.zero-match-notice"));

        var box = NoticeBox.ByMarkerClass(cut, "skipped-notice");
        Assert.Contains("Skipped 1 game and 1 decision — not in the output:", box.Content.TextContent);
        Assert.Equal(2, box.Content.QuerySelectorAll("li").Length);
    }

    /// <summary>An XGP run writes files, so its line keeps that word at zero, with its omissions listed.</summary>
    [Fact]
    public async Task CompleteZeroFiles_OnlyOmissions_KeepsTheFilesWrittenWording()
    {
        ServerAnswers(new ProcessingProgress
        {
            Complete = true,
            XgpCommit = new XgpExportCommit(new XgpExportOptions(), 0),
            Skipped = OnlyOmissions,
        });
        var host = await LocalPanelHost.RenderReadyAsync(this, OutputFormat.Xgp);
        await host.ApplyErrorMinAsync("0.05");
        await host.RunToCompletionAsync();

        Assert.Contains("0 files written", host.Panel.Find("#doneLine").TextContent);
        Assert.Empty(host.Panel.FindAll("div.zero-match-notice"));
        NoticeBox.ByMarkerClass(host.Panel, "skipped-notice");
    }

    /// <summary>
    /// Complete + no error + <c>TotalRows == 0</c> through an active filter
    /// set renders the zero-match notice alongside the Done line.
    /// </summary>
    [Fact]
    public async Task CompleteZeroRows_ActiveFilter_RendersNoticeAlongsideDone()
    {
        var cut = (await RunAsync(errorMin: "0.05")).Panel;

        // Notice renders...
        var notice = cut.Find("div.zero-match-notice");
        Assert.Contains("No decisions matched these filters", notice.TextContent);

        // ...alongside the success Done line, not in place of it.
        var done = cut.Find("span.text-success");
        Assert.Contains("Done", done.TextContent);
    }

    /// <summary>
    /// Complete + no error + <c>TotalRows == 0</c> through an empty
    /// (inactive) filter set renders the plain Done line only — a folder that
    /// simply held zero decisions is not a filter miss.
    /// </summary>
    [Fact]
    public async Task CompleteZeroRows_EmptyFilter_RendersDoneLineOnly()
    {
        var cut = (await RunAsync(errorMin: null)).Panel;

        Assert.Empty(cut.FindAll("div.zero-match-notice"));

        var done = cut.Find("span.text-success");
        Assert.Contains("Done", done.TextContent);
    }

    /// <summary>
    /// A run through the empty filter wrote zero rows — an empty folder, not a
    /// filter miss. An active filter then comes into effect for the same
    /// folder. The previous run's zero must not be read against the filter now
    /// on screen: that would blame a filter the run never applied.
    /// </summary>
    [Fact]
    public async Task ZeroRowsThroughTheEmptyFilter_ThenAnActiveFilter_BlamesNoFilter()
    {
        var host = await RunAsync(errorMin: null);

        await host.ApplyErrorMinAsync("0.05");

        Assert.Empty(host.Panel.FindAll("div.zero-match-notice"));
    }

    // A panel over one folder with the filter in effect — the error minimum
    // applied, or the empty filter when null — and an output path, whose Run
    // has completed against the zero-row server, which reports skipped.
    private async Task<LocalPanelHost> RunAsync(string? errorMin, SkipRecord? skipped = null)
    {
        ServerAnswers(new ProcessingProgress { Complete = true, Skipped = skipped ?? SkipRecord.Empty });
        var host = await LocalPanelHost.RenderReadyAsync(this);
        if (errorMin is not null)
            await host.ApplyErrorMinAsync(errorMin);
        await host.RunToCompletionAsync();
        return host;
    }
}
