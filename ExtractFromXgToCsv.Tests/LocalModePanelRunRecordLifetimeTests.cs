using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
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
/// Every case is a real run against a stub server.
/// </summary>
public class LocalModePanelRunRecordLifetimeTests : BunitContext
{
    private const string Notice = "div.skipped-notice";
    private const string FolderA = @"D:\xg\a";
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

    private IRenderedComponent<LocalModePanel> PanelOver(
        CapturingProcessHandler server,
        FilterConfig filter,
        OutputFormat format = OutputFormat.Csv,
        Action? ended = null)
    {
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, format)
            .Add(c => c.Source, FilterSourceToken.FromPath(FolderA))
            .Add(c => c.FilterInEffect, filter)
            .Add(c => c.FolderPath, FolderA)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.StartState, LocalPanelRuns.Ready)
            .Add(c => c.XgpAnonymize, false)
            .Add(c => c.OnXgpJobEnded, () => ended?.Invoke()));
        cut.Find("#outputPath").Input(new ChangeEventArgs { Value = format == OutputFormat.Xgp ? @"D:\xg\positions" : @"D:\xg\out.csv" });
        return cut;
    }

    private static Task RunAsync(IRenderedComponent<LocalModePanel> cut) =>
        RunButton(cut).ClickAsync(new MouseEventArgs());

    private static AngleSharp.Dom.IElement RunButton(IRenderedComponent<LocalModePanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Run");

    private static void ChangeSource(IRenderedComponent<LocalModePanel> cut, string? folder) =>
        cut.Render(p => p
            .Add(c => c.Source, folder is null ? null : FilterSourceToken.FromPath(folder))
            .Add(c => c.FilterInEffect, folder is null ? null : new FilterConfig())
            .Add(c => c.FolderPath, folder ?? string.Empty));

    // ── A filter change keeps the record ────────────────────────────────────

    [Fact]
    public async Task AFilterChange_KeepsTheRecordLabelledWithItsRun_AndDropsDone()
    {
        var cut = PanelOver(new CapturingProcessHandler(CompletedWith(BrokenA)), new FilterConfig());
        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Contains("Done", cut.Markup);

        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { ErrorMin = 0.05 }));

        Assert.DoesNotContain("Done", cut.Markup);
        var notice = cut.Find(Notice);
        Assert.Contains("broken-a.xg", notice.TextContent);
        Assert.Equal("The last run", notice.QuerySelector(".skip-list-label")!.TextContent);

        // An edit not yet applied — nothing in effect — keeps it too.
        cut.Render(p => p.Add(c => c.FilterInEffect, (FilterConfig?)null));
        Assert.Contains("broken-a.xg", cut.Find(Notice).TextContent);
    }

    [Fact]
    public async Task AFilterChange_DropsTheZeroMatchNotice()
    {
        // A run through an active filter that wrote nothing and skipped
        // nothing: the zero-match notice is its current-input result.
        var cut = PanelOver(
            new CapturingProcessHandler(CompletedWith(SkipRecord.Empty, rows: 0)),
            new FilterConfig { Players = ["Alice"] });
        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Single(cut.FindAll("div.zero-match-notice"));

        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { Players = ["Bob"] }));

        Assert.Empty(cut.FindAll("div.zero-match-notice"));
    }

    // ── A source change clears it ───────────────────────────────────────────

    [Fact]
    public async Task ReplacingTheSource_ClearsTheRecord()
    {
        var cut = PanelOver(new CapturingProcessHandler(CompletedWith(BrokenA)), new FilterConfig());
        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Single(cut.FindAll(Notice));

        ChangeSource(cut, FolderB);

        Assert.Empty(cut.FindAll(Notice));
    }

    [Fact]
    public async Task RemovingTheSource_ClearsTheRecord()
    {
        var cut = PanelOver(new CapturingProcessHandler(CompletedWith(BrokenA)), new FilterConfig());
        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Single(cut.FindAll(Notice));

        ChangeSource(cut, null);

        Assert.Empty(cut.FindAll(Notice));
    }

    [Fact]
    public async Task ARunCompletingAfterItsSourceWasReplaced_PublishesNoRecord()
    {
        var server = new CapturingProcessHandler(CompletedWith(BrokenA));
        var cut = PanelOver(server, new FilterConfig());
        server.HoldStatus();
        var run = RunAsync(cut);
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        ChangeSource(cut, FolderB);
        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Empty(cut.FindAll(Notice));
        Assert.DoesNotContain("Done", cut.Markup);
    }

    /// <summary>
    /// A→B→A: the held XGP run's folder is replaced and then put back before
    /// it completes. Coming back does not undo the replacement — the return is
    /// a new visit — so the run publishes neither its record nor "Done". What
    /// it committed still reaches the host: clearing a record never discards
    /// accounting. Before the correction the panel judged by the folder's
    /// value at completion, so the old run's "Done" and record came back.
    /// </summary>
    [Fact]
    public async Task ARunWhoseSourceWasReplacedAndRestored_PublishesNothing_AndItsAccountingStillReachesTheHost()
    {
        var commit = new XgpExportCommit(new XgpExportOptions(), 2);
        var server = new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 2, Total = 2, Complete = true, TotalRows = 2, Skipped = BrokenA, XgpCommit = commit,
        });
        // How many acknowledgements the server had when the host was told
        // the job ended: none, since the job is acknowledged only after.
        var acknowledgedWhenEnded = new List<int>();
        var cut = PanelOver(server, new FilterConfig(), OutputFormat.Xgp, () => acknowledgedWhenEnded.Add(server.Acknowledged.Count));
        server.HoldStatus();
        var run = RunAsync(cut);
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        ChangeSource(cut, FolderB);
        ChangeSource(cut, FolderA);
        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Empty(cut.FindAll(Notice));
        Assert.DoesNotContain("Done", cut.Markup);
        // The host is told the job ended, to apply its entry from the
        // ledger, and the job is acknowledged after.
        Assert.Equal([0], acknowledgedWhenEnded);
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
        var cut = PanelOver(server, new FilterConfig());
        server.HoldStatus();
        var run = RunAsync(cut);
        await server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { ErrorMin = 0.05 }));
        server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("broken-a.xg", cut.Find(Notice).TextContent);
        Assert.DoesNotContain("Done", cut.Markup);
    }

    // ── The next run replaces it ────────────────────────────────────────────

    [Fact]
    public async Task TheNextRun_ReplacesTheRecord_FromItsStart()
    {
        var server = new CapturingProcessHandler(CompletedWith(BrokenA), CompletedWith(BrokenB));
        var cut = PanelOver(server, new FilterConfig());
        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Contains("broken-a.xg", cut.Find(Notice).TextContent);

        // The second run's poll is held: from its start the first run's
        // record is gone, and its own is not yet shown.
        server.HoldPoll(2);
        var second = RunAsync(cut);
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
        var cut = PanelOver(server, new FilterConfig());
        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Single(cut.FindAll(Notice));

        await RunAsync(cut).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Empty(cut.FindAll(Notice));
        Assert.Contains("Done", cut.Markup);
    }
}
