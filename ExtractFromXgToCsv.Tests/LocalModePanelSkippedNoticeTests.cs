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

    /// <summary>
    /// A ready panel whose runs <paramref name="server"/> answers, with the
    /// filter of error minimum <paramref name="errorMin"/> applied, or the
    /// empty filter when null.
    /// </summary>
    private async Task<LocalPanelHost> PanelOverAsync(CapturingProcessHandler server, string? errorMin = null)
    {
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        var host = await LocalPanelHost.RenderReadyAsync(this);
        if (errorMin is not null)
            await host.ApplyErrorMinAsync(errorMin);
        return host;
    }

    /// <summary>A ready panel whose run against <paramref name="server"/> has completed.</summary>
    private async Task<IRenderedComponent<LocalModePanel>> CompletedRunAsync(CapturingProcessHandler server, string? errorMin = null)
    {
        var host = await PanelOverAsync(server, errorMin);
        await host.RunToCompletionAsync();
        return host.Panel;
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
        var cut = await CompletedRunAsync(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 40,
            Skipped = TwoFilesAndADecision,
        }));

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
        var cut = await CompletedRunAsync(
            new CapturingProcessHandler(new ProcessingProgress
            {
                Current = 1,
                Total = 1,
                Complete = true,
                TotalRows = 0,
                Skipped = OneFile,
            }),
            errorMin: "0.05");

        Assert.Contains("Done", cut.Find("span.text-success").TextContent);
        Assert.Empty(cut.FindAll("div.zero-match-notice"));
        Assert.Contains("broken.xg", cut.Find(Notice).TextContent);
    }

    [Fact]
    public async Task CancelledRunWithSkips_ShowsTheRecordSoFar_LabelledStopped()
    {
        var cut = await CompletedRunAsync(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 2,
            Total = 9,
            Complete = true,
            Cancelled = true,
            TotalRows = 11,
            Skipped = OneFile,
        }));

        Assert.Contains("Stopped", cut.Find("span.text-warning").TextContent);

        var notice = cut.Find(Notice);
        Assert.Equal("The last run (stopped)", notice.QuerySelector(".skip-list-label")!.TextContent);
        Assert.Contains("Skipped 1 file", notice.TextContent);
        Assert.DoesNotContain("decision", notice.TextContent);
        Assert.Contains("broken.xg", Assert.Single(notice.QuerySelectorAll("li")).TextContent);
    }

    /// <summary>
    /// A stopped run whose output file then failed to close is still stopped
    /// (SPEC-notices.md §7.1): its line and its record say so, and the close
    /// failure is shown beside them, never as the run's error.
    /// </summary>
    [Fact]
    public async Task AStoppedRunWhoseCloseFailed_IsShownStopped_WithTheCloseFailureBesideIt()
    {
        const string closeFailure = "Closing the unfinished out.csv failed: The network name is no longer available.";
        var cut = await CompletedRunAsync(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 2,
            Total = 9,
            Complete = true,
            Cancelled = true,
            CloseFailureMessage = closeFailure,
            Skipped = OneFile,
        }));

        Assert.Contains("Stopped", cut.Find("span.text-warning").TextContent);
        Assert.Equal(closeFailure, cut.Find("#closeFailure").TextContent.Trim());
        Assert.Empty(cut.FindAll("span.text-danger"));
        Assert.Equal("The last run (stopped)", cut.Find($"{Notice} .skip-list-label").TextContent);
    }

    [Fact]
    public async Task FailedRunWithSkips_ShowsItsErrorAndItsRecord_LabelledFailed()
    {
        var cut = await CompletedRunAsync(new CapturingProcessHandler(new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "Access to the path is denied.",
            Skipped = OneFile,
        }));

        Assert.Contains("Access to the path is denied.", cut.Find("span.text-danger").TextContent);
        Assert.Equal("The last run (failed)", cut.Find($"{Notice} .skip-list-label").TextContent);
    }

    [Fact]
    public async Task CompletedRunWithoutSkips_ShowsNoNotice()
    {
        var cut = await CompletedRunAsync(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 40,
        }));

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
        var host = await PanelOverAsync(server);
        var cut = host.Panel;

        var run = host.ClickRunAsync();
        await server.HeldPollReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        cut.WaitForAssertion(() => Assert.Contains("File 2 of 9", cut.Markup));
        Assert.Empty(cut.FindAll(Notice));

        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("broken.xg", cut.Find(Notice).TextContent);
    }
}
