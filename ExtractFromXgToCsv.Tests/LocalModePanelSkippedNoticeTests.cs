using BgDataTypes_Lib;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the client half of halheinrich/backgammon#223: a terminal snapshot
/// that carries skips shows how many and which, each with its reason, and one
/// that carries none shows nothing. A skip never ends a run, so without the
/// notice the terminal line reads as a clean result over a folder that lost
/// files — which is exactly how the skips went unseen before.
/// </summary>
public class LocalModePanelSkippedNoticeTests : BunitContext
{
    private const string Notice = "div.skipped-notice";

    public LocalModePanelSkippedNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
    }

    private IRenderedComponent<LocalModePanel> RenderWith(ProcessingProgress snapshot)
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Xgp)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", snapshot);
        cut.Render();
        return cut;
    }

    private static IReadOnlyList<SkippedItem> TwoFilesAndADecision() =>
    [
        new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic."),
        new SkippedItem("truncated.xgp", null, "Unexpected end of stream."),
        new SkippedItem(
            "match.xg", new XgDecisionId("match.xg", 3, 12, IsCube: true), "Access denied."),
    ];

    [Fact]
    public void CompletedRunWithSkips_ShowsTheCountsAndEachNameWithItsReason()
    {
        var cut = RenderWith(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 40,
            Skipped = TwoFilesAndADecision(),
        });

        // Beside the Done line, not in place of it: the rows that were written
        // are still the run's result.
        Assert.Contains("Done", cut.Find("span.text-success").TextContent);

        var notice = cut.Find(Notice);
        Assert.Contains("Skipped 2 files and 1 decision", notice.TextContent);

        var entries = notice.QuerySelectorAll("li").Select(li => li.TextContent).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains("broken.xg", entries[0]);
        Assert.Contains("Not a valid XG file: bad magic.", entries[0]);
        Assert.Contains("truncated.xgp", entries[1]);
        Assert.Contains("Unexpected end of stream.", entries[1]);
        // A decision is named by the library's canonical id, which carries its file.
        Assert.Contains("match.xg:g3:m12:cube", entries[2]);
        Assert.Contains("Access denied.", entries[2]);
    }

    [Fact]
    public void CancelledRunWithSkips_ShowsTheRecordSoFar()
    {
        var cut = RenderWith(new ProcessingProgress
        {
            Current = 2,
            Total = 9,
            Complete = true,
            Cancelled = true,
            TotalRows = 11,
            Skipped = [new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic.")],
        });

        Assert.Contains("Stopped", cut.Find("span.text-warning").TextContent);

        var notice = cut.Find(Notice);
        Assert.Contains("Skipped 1 file", notice.TextContent);
        Assert.DoesNotContain("decision", notice.TextContent);
        Assert.Contains("broken.xg", Assert.Single(notice.QuerySelectorAll("li")).TextContent);
    }

    [Fact]
    public void CompletedRunWithoutSkips_ShowsNoNotice()
    {
        var cut = RenderWith(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 40,
        });

        Assert.Contains("Done", cut.Find("span.text-success").TextContent);
        Assert.Empty(cut.FindAll(Notice));
    }

    [Fact]
    public void RunInFlightWithSkips_ShowsNoNoticeYet()
    {
        // The record is on every snapshot, but the notice is the terminal
        // snapshot's: mid-run the list is still growing.
        var cut = RenderWith(new ProcessingProgress
        {
            Current = 2,
            Total = 9,
            FileName = "next.xg",
            TotalRows = 11,
            Skipped = [new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic.")],
        });

        Assert.Empty(cut.FindAll(Notice));
    }
}
