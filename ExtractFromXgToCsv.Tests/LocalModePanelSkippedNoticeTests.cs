using BgDataTypes_Lib;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the client half of halheinrich/backgammon#223: a run that ends with
/// skips shows how many and which, each with its reason, labelled with the
/// run and how it ended, and one that skipped nothing shows nothing. A skip
/// never ends a run, so without the notice the terminal line reads as a clean
/// result over a folder that lost files — which is exactly how the skips went
/// unseen before. Every case is a real run against a stub server: the record
/// is the run's, published when the run ends. How long it then stands is
/// <see cref="LocalModePanelRunRecordLifetimeTests"/>'.
/// </summary>
public class LocalModePanelSkippedNoticeTests : BunitContext
{
    private const string Notice = "div.skipped-notice";

    public LocalModePanelSkippedNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    private IRenderedComponent<LocalModePanel> PanelOver(CapturingProcessHandler server, FilterConfig? filter = null)
    {
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        return this.RenderReadyLocalPanel(filter ?? new FilterConfig());
    }

    // Names as the processor records them: paths under the input folder.
    private static readonly string NestedFile = Path.Combine("2026", "truncated.xgp");
    private static readonly string NestedMatch = Path.Combine("club", "match.xg");

    private static readonly SkipRecord TwoFilesAndADecision =
    [
        new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic."),
        new SkippedItem(NestedFile, null, "Unexpected end of stream."),
        new SkippedItem(
            NestedMatch, new XgDecisionId("match.xg", 3, 12, IsCube: true), "Access denied."),
    ];

    private static readonly SkipRecord OneFile = [new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic.")];

    [Fact]
    public async Task CompletedRunWithSkips_ShowsTheCountsAndEachNameWithItsReason()
    {
        var cut = PanelOver(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 40,
            Skipped = TwoFilesAndADecision,
        }));
        await cut.RunToCompletionAsync();

        // Beside the Done line, not in place of it: the rows that were written
        // are still the run's result.
        Assert.Contains("Done", cut.Find("span.text-success").TextContent);

        var notice = cut.Find(Notice);
        Assert.Equal("The last run", notice.QuerySelector(".skip-list-label")!.TextContent);
        Assert.Contains("Skipped 2 files and 1 decision", notice.TextContent);

        var entries = notice.QuerySelectorAll("li").Select(li => li.TextContent).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains("broken.xg", entries[0]);
        Assert.Contains("Not a valid XG file: bad magic.", entries[0]);
        Assert.Contains(NestedFile, entries[1]);
        Assert.Contains("Unexpected end of stream.", entries[1]);
        // A decision shows its file's path under the input folder, then the
        // library's canonical id — which names the file by bare name only.
        Assert.Contains(NestedMatch, entries[2]);
        Assert.Contains("decision match.xg:g3:m12:cube", entries[2]);
        Assert.Contains("Access denied.", entries[2]);
    }

    [Fact]
    public async Task CompletedRunWithZeroRowsAndSkips_TheZeroMatchNoticeYieldsToTheSkips()
    {
        // Zero rows through an active filter would normally blame the filter,
        // but that inference assumes every file was read. With skips the zero
        // may be theirs, so only the skip notice speaks.
        var cut = PanelOver(
            new CapturingProcessHandler(new ProcessingProgress
            {
                Current = 1,
                Total = 1,
                Complete = true,
                TotalRows = 0,
                Skipped = OneFile,
            }),
            new FilterConfig { Players = ["Alice"] });
        await cut.RunToCompletionAsync();

        Assert.Contains("Done", cut.Find("span.text-success").TextContent);
        Assert.Empty(cut.FindAll("div.zero-match-notice"));
        Assert.Contains("broken.xg", cut.Find(Notice).TextContent);
    }

    [Fact]
    public async Task CancelledRunWithSkips_ShowsTheRecordSoFar_LabelledStopped()
    {
        var cut = PanelOver(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 2,
            Total = 9,
            Complete = true,
            Cancelled = true,
            TotalRows = 11,
            Skipped = OneFile,
        }));
        await cut.RunToCompletionAsync();

        Assert.Contains("Stopped", cut.Find("span.text-warning").TextContent);

        var notice = cut.Find(Notice);
        Assert.Equal("The last run (stopped)", notice.QuerySelector(".skip-list-label")!.TextContent);
        Assert.Contains("Skipped 1 file", notice.TextContent);
        Assert.DoesNotContain("decision", notice.TextContent);
        Assert.Contains("broken.xg", Assert.Single(notice.QuerySelectorAll("li")).TextContent);
    }

    [Fact]
    public async Task FailedRunWithSkips_ShowsItsErrorAndItsRecord_LabelledFailed()
    {
        var cut = PanelOver(new CapturingProcessHandler(new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "Access to the path is denied.",
            Skipped = OneFile,
        }));
        await cut.RunToCompletionAsync();

        Assert.Contains("Access to the path is denied.", cut.Find("span.text-danger").TextContent);
        Assert.Equal("The last run (failed)", cut.Find($"{Notice} .skip-list-label").TextContent);
    }

    [Fact]
    public async Task CompletedRunWithoutSkips_ShowsNoNotice()
    {
        var cut = PanelOver(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 40,
        }));
        await cut.RunToCompletionAsync();

        Assert.Contains("Done", cut.Find("span.text-success").TextContent);
        Assert.Empty(cut.FindAll(Notice));
    }

    [Fact]
    public async Task RunInFlightWithSkips_ShowsNoNoticeUntilItEnds()
    {
        // The record is on every snapshot, but the notice is the finished
        // run's: mid-run the list is still growing.
        var server = new CapturingProcessHandler(
            new ProcessingProgress
            {
                Current = 2,
                Total = 9,
                FileName = "next.xg",
                TotalRows = 11,
                Skipped = OneFile,
            },
            new ProcessingProgress
            {
                Current = 9,
                Total = 9,
                Complete = true,
                TotalRows = 30,
                Skipped = OneFile,
            });
        server.HoldPoll(2);
        var cut = PanelOver(server);

        var run = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Run")
            .ClickAsync(new MouseEventArgs());
        await server.HeldPollReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        cut.WaitForAssertion(() => Assert.Contains("File 2 of 9", cut.Markup));
        Assert.Empty(cut.FindAll(Notice));

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("broken.xg", cut.Find(Notice).TextContent);
    }
}
