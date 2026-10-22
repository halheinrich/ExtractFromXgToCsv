using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// How long Local mode's run record stands (the umbrella's
/// <c>SPEC-notices.md</c> §7.2, ruled "Option B" on halheinrich/backgammon#374):
/// the record belongs to the run on the source it ran against, so a filter
/// change keeps it, labelled with its run, while "Done" and the zero-match
/// notice — the run's current-input result — go; replacing or removing the
/// source clears it, and a run from the former source that completes later
/// publishes none; and the next run replaces it. Before v1.12.2 the panel
/// dropped the skip list with the result on any source or filter change.
/// Every case is a real run against a stub server, the inputs moved by the
/// gestures that move them.
/// </summary>
public class LocalModePanelRunRecordLifetimeTests : BunitContext
{
    private const string Notice = "div.skipped-notice";
    private const string FolderB = @"D:\xg\b";

    private static readonly SkipRecord BrokenA = [new SkippedItem("broken-a.xg", null, "Not a valid XG file.")];
    private static readonly SkipRecord BrokenB = [new SkippedItem("broken-b.xg", null, "Unexpected end of stream.")];

    private static ProcessingProgress CompletedWith(SkipRecord skips, int rows = 5) =>
        new() { Current = 2, Total = 2, Complete = true, TotalRows = rows, Skipped = skips };

    public LocalModePanelRunRecordLifetimeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    /// <summary>
    /// A ready panel over <see cref="LocalPanelHost.Folder"/> in
    /// <paramref name="format"/>, its runs answered by
    /// <paramref name="server"/>, with the filter of error minimum
    /// <paramref name="errorMin"/> applied, or the empty filter when null.
    /// </summary>
    private async Task<LocalPanelHost> PanelOverAsync(
        CapturingProcessHandler server, string? errorMin = null, OutputFormat format = OutputFormat.Csv)
    {
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        var host = await LocalPanelHost.RenderReadyAsync(this, format);
        if (errorMin is not null)
            await host.ApplyErrorMinAsync(errorMin);
        return host;
    }

    // ── A filter change keeps the record ────────────────────────────────────

    [Fact]
    public async Task AFilterChange_KeepsTheRecordLabelledWithItsRun_AndDropsDone()
    {
        var host = await PanelOverAsync(new CapturingProcessHandler(CompletedWith(BrokenA)));
        var cut = host.Panel;
        await host.RunToCompletionAsync();
        Assert.Contains("Done", cut.Markup);

        await host.ApplyErrorMinAsync("0.05");

        Assert.DoesNotContain("Done", cut.Markup);
        var notice = cut.Find(Notice);
        Assert.Contains("broken-a.xg", notice.TextContent);
        Assert.Equal("The last run", notice.QuerySelector(".skip-list-label")!.TextContent);

        // An edit not yet applied — nothing in effect — keeps it too.
        await host.EditErrorMinAsync("0.06");
        Assert.Contains("broken-a.xg", cut.Find(Notice).TextContent);
    }

    [Fact]
    public async Task AFilterChange_DropsTheZeroMatchNotice()
    {
        // A run through an active filter that wrote nothing and skipped
        // nothing: the zero-match notice is its current-input result.
        var host = await PanelOverAsync(
            new CapturingProcessHandler(CompletedWith(SkipRecord.Empty, rows: 0)), errorMin: "0.05");
        var cut = host.Panel;
        await host.RunToCompletionAsync();
        Assert.Single(cut.FindAll("div.zero-match-notice"));

        await host.ApplyErrorMinAsync("0.10");

        Assert.Empty(cut.FindAll("div.zero-match-notice"));
    }

    // ── A source change clears it ───────────────────────────────────────────

    [Fact]
    public async Task ReplacingTheSource_ClearsTheRecord()
    {
        var host = await PanelOverAsync(new CapturingProcessHandler(CompletedWith(BrokenA)));
        await host.RunToCompletionAsync();
        Assert.Single(host.Panel.FindAll(Notice));

        await host.CommitFolderAsync(FolderB);

        Assert.Empty(host.Panel.FindAll(Notice));
    }

    [Fact]
    public async Task RemovingTheSource_ClearsTheRecord()
    {
        var host = await PanelOverAsync(new CapturingProcessHandler(CompletedWith(BrokenA)));
        await host.RunToCompletionAsync();
        Assert.Single(host.Panel.FindAll(Notice));

        await host.CommitFolderAsync(string.Empty);

        Assert.Empty(host.Panel.FindAll(Notice));
    }

    [Fact]
    public async Task ARunCompletingAfterItsSourceWasReplaced_PublishesNoRecord()
    {
        var server = new CapturingProcessHandler(CompletedWith(BrokenA));
        var host = await PanelOverAsync(server);
        server.HoldStatus();
        var run = host.ClickRunAsync();
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        await host.CommitFolderAsync(FolderB);
        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Empty(host.Panel.FindAll(Notice));
        Assert.DoesNotContain("Done", host.Panel.Markup);
    }

    /// <summary>
    /// A→B→A: the held XGP run's folder is replaced and then put back before
    /// it completes. Coming back does not undo the replacement — the return is
    /// a new visit — so the run publishes neither its record nor "Done". What
    /// it committed is still followed — the next number moves past it — before
    /// the job is acknowledged: clearing a record never discards accounting.
    /// Before the correction the panel judged by the folder's value at
    /// completion, so the old run's "Done" and record came back.
    /// </summary>
    [Fact]
    public async Task ARunWhoseSourceWasReplacedAndRestored_PublishesNothing_AndItsAccountingStillReachesTheHost()
    {
        var commit = new XgpExportCommit(new XgpExportOptions(), 2);
        var server = new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 2, Total = 2, Complete = true, TotalRows = 2, Skipped = BrokenA, XgpCommit = commit,
        });
        var host = await PanelOverAsync(server, format: OutputFormat.Xgp);
        // The next number each time it moved, and how many acknowledgements
        // the server had then: none, since the job is acknowledged only after.
        var followed = new List<(int NextNumber, int Acknowledged)>();
        var last = host.App.XgpOptions.StartNumber;
        host.App.Changed += () =>
        {
            if (host.App.XgpOptions.StartNumber == last) return;
            last = host.App.XgpOptions.StartNumber;
            followed.Add((last, server.Acknowledged.Count));
        };
        server.HoldStatus();
        var run = host.ClickRunAsync();
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        await host.CommitFolderAsync(FolderB);
        await host.CommitFolderAsync(LocalPanelHost.Folder);
        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Empty(host.Panel.FindAll(Notice));
        Assert.DoesNotContain("Done", host.Panel.Markup);
        // The run's commit is followed, and the job is acknowledged after.
        Assert.Equal([(commit.NextNumber, 0)], followed);
        Assert.Single(server.Acknowledged);
    }

    /// <summary>
    /// The control for the case above: the same held run, its filter changed
    /// instead of its source. Its result is not shown — the filter it ran is
    /// no longer the one in effect — but its record is: it belongs to the run
    /// on its source, and the source stood.
    /// </summary>
    [Fact]
    public async Task ARunCompletingAfterItsFilterChanged_PublishesItsRecordButNotItsResult()
    {
        var server = new CapturingProcessHandler(CompletedWith(BrokenA));
        var host = await PanelOverAsync(server);
        server.HoldStatus();
        var run = host.ClickRunAsync();
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        await host.ApplyErrorMinAsync("0.05");
        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("broken-a.xg", host.Panel.Find(Notice).TextContent);
        Assert.DoesNotContain("Done", host.Panel.Markup);
    }

    // ── The next run replaces it ────────────────────────────────────────────

    [Fact]
    public async Task TheNextRun_ReplacesTheRecord_FromItsStart()
    {
        var server = new CapturingProcessHandler(CompletedWith(BrokenA), CompletedWith(BrokenB));
        var host = await PanelOverAsync(server);
        var cut = host.Panel;
        await host.RunToCompletionAsync();
        Assert.Contains("broken-a.xg", cut.Find(Notice).TextContent);

        // The second run's poll is held: from its start the first run's
        // record is gone, and its own is not yet shown.
        server.HoldPoll(2);
        var second = host.ClickRunAsync();
        await server.HeldPollReached.WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Empty(cut.FindAll(Notice));

        server.ReleaseStatus();
        await second.WaitAsync(CapturingProcessHandler.RunTimeout);
        var notice = cut.Find(Notice);
        Assert.Contains("broken-b.xg", notice.TextContent);
        Assert.DoesNotContain("broken-a.xg", notice.TextContent);
    }

    [Fact]
    public async Task ANextRunThatSkipsNothing_LeavesNoRecord()
    {
        var server = new CapturingProcessHandler(CompletedWith(BrokenA), CompletedWith(SkipRecord.Empty));
        var host = await PanelOverAsync(server);
        await host.RunToCompletionAsync();
        Assert.Single(host.Panel.FindAll(Notice));

        await host.RunToCompletionAsync();

        Assert.Empty(host.Panel.FindAll(Notice));
        Assert.Contains("Done", host.Panel.Markup);
    }
}
