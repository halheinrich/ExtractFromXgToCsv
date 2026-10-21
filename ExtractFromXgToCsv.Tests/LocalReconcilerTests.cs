using System.Net.Http.Json;
using BgDataTypes_Lib.TestSupport;
using BgUiPrimitives_Razor;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The page's reconciliation with the Local server (the umbrella's
/// <c>SPEC-notices.md</c> §7.5), driven directly over the real server and a
/// browser storage in memory, for what no page can show: the takings it
/// offers the controls — each entry the record takes, once, and never a
/// replay.
/// </summary>
public class LocalReconcilerTests : IAsyncLifetime
{
    private readonly LocalServer _server = new();
    private readonly MemoryBrowserStorage _storage = new();
    private readonly List<LedgerTaking> _takings = [];
    private string _instance = default!;

    public async Task InitializeAsync()
    {
        using var client = _server.Client();
        _instance = (await client.GetFromJsonAsync<LedgerSnapshot>("/api/process/ledger"))!.Instance;
    }

    public Task DisposeAsync()
    {
        _server.Dispose();
        return Task.CompletedTask;
    }

    private async Task<XgpNumberingKeeper> RestoredKeeperAsync()
    {
        var keeper = new XgpNumberingKeeper(new XgpNumberingStore(new ReportingBrowserStorage(
            new BrowserStorage(_storage), new BrowserStorageCondition(), NullLogger<ReportingBrowserStorage>.Instance)));
        await keeper.RestoreAsync();
        return keeper;
    }

    /// <summary>The numbers of the entries an ordered taking took.</summary>
    private static int[] Taken(LedgerTaking taking) =>
        [.. Assert.IsType<LedgerTaking.Ordered>(taking).Taken.Select(entry => entry.Number)];

    private LocalReconciler ReconcilerOver(XgpNumberingKeeper keeper, Func<LedgerTaking, Task>? followRecord = null, ILogger? logger = null) =>
        new(new LocalJobsClient(_server.Client()), keeper, LocalPage.FastTiming,
            followRecord ?? (taking =>
            {
                _takings.Add(taking);
                return Task.CompletedTask;
            }),
            logger ?? NullLogger.Instance, CancellationToken.None);

    /// <summary>
    /// The event the controls follow is the record's taking: each new entry
    /// is offered once, with exactly the entries taken, and a reconciliation
    /// that finds them again — a replay — offers nothing.
    /// </summary>
    [Fact]
    public async Task EachEntryTheRecordTakes_IsOfferedOnce_AndAReplayOffersNothing()
    {
        await _server.ExportAsync("elsewhere-1", _instance, mark: null);
        var reconciler = ReconcilerOver(await RestoredKeeperAsync());

        await reconciler.StartAsync();
        Assert.Equal([1], Taken(Assert.Single(_takings)));

        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();
        Assert.Single(_takings);

        await _server.ExportAsync("elsewhere-2", _instance, new LedgerMark(_instance, 1));
        await reconciler.ReconcileAsync();
        Assert.Equal(2, _takings.Count);
        Assert.Equal([2], Taken(_takings[1]));
    }

    /// <summary>
    /// A held ledger response, then a newer stored checkpoint: the pass's
    /// ledger shows E1 ended; while its response is held, another page applies
    /// E1 and E2 and stores the record holding E2, which the pass merges as it
    /// applies E1. The offer moves only over what a ledger shows ended, so E1
    /// is offered with its own numbering, and E2 — not consumed with it — once
    /// a later pass's ledger shows it, with its own; and each only once.
    /// </summary>
    [Fact]
    public async Task ARecordMergedPastTheLedgersEnd_IsOfferedOnceALedgerShowsIt_EachTakingWithItsOwnNumbering()
    {
        var reconciler = ReconcilerOver(await RestoredKeeperAsync());
        await reconciler.StartAsync();
        await _server.ExportAsync("elsewhere-1", _instance, mark: null);
        var held = _server.Wire.HoldResponse(ProtocolWire.Ledger);
        var reconciling = reconciler.ReconcileAsync();
        await held.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        var anotherPage = ReconcilerOver(await RestoredKeeperAsync(), _ => Task.CompletedTask);
        await anotherPage.StartAsync();
        var second = new XgpExportOptions { StartNumber = 100 };
        await _server.ExportAsync("elsewhere-2", _instance, new LedgerMark(_instance, 1), second);
        await anotherPage.ReconcileAsync();

        held.Release();
        await reconciling.WaitAsync(NoticeTesting.OperationBound);
        Assert.True(SpinWait.SpinUntil(() => reconciler.State.XgpBlock is null && _takings.Count == 2, NoticeTesting.OperationBound));

        var decisions = Assert.IsType<LedgerTaking.Ordered>(_takings[0]).Export!.Commit!.Count;
        Assert.Equal([1], Taken(_takings[0]));
        Assert.Equal(new XgpNumbering(XgpExportOptions.DefaultNamePattern, decisions, 3), _takings[0].Numbering);
        Assert.Equal([2], Taken(_takings[1]));
        Assert.Equal(new XgpNumbering(XgpExportOptions.DefaultNamePattern, 99 + decisions, 3), _takings[1].Numbering);

        await reconciler.ReconcileAsync();
        Assert.Equal(2, _takings.Count);
    }

    /// <summary>
    /// The offer is consumed only once the controls have followed it: a
    /// taking whose follow throws fails the pass, which is tried again and
    /// offers the same taking, and once followed it is not offered again.
    /// </summary>
    [Fact]
    public async Task ATakingWhoseFollowThrows_IsOfferedAgain_UntilItIsFollowed()
    {
        await _server.ExportAsync("elsewhere-1", _instance, mark: null);
        var reconciler = ReconcilerOver(await RestoredKeeperAsync(), taking =>
        {
            _takings.Add(taking);
            return _takings.Count == 1 ? throw new InvalidOperationException("The controls failed to follow.") : Task.CompletedTask;
        });

        await reconciler.StartAsync();
        Assert.True(SpinWait.SpinUntil(() => reconciler.State.XgpBlock is null, NoticeTesting.OperationBound));

        Assert.Equal(2, _takings.Count);
        Assert.Equal([1], Taken(_takings[0]));
        Assert.Equal([1], Taken(_takings[1]));
        await reconciler.ReconcileAsync();
        Assert.Equal(2, _takings.Count);
    }

    /// <summary>
    /// A pass that fails for any reason but the server's absence leaves the
    /// state unknown and is tried again — both settled before the failure is
    /// logged — and a logger that throws stops neither: the reconciliation
    /// itself completes normally, and the next pass succeeds.
    /// </summary>
    [Fact]
    public async Task APassThatFails_WithALoggerThatThrows_LeavesTheStateUnknown_AndIsTriedAgain()
    {
        await _server.ExportAsync("elsewhere-1", _instance, mark: null);
        var logger = new ThrowingLogger<LocalReconciler>();
        var follows = 0;
        var reconciler = ReconcilerOver(await RestoredKeeperAsync(),
            _ => ++follows == 1 ? throw new InvalidOperationException("The controls failed to follow.") : Task.CompletedTask,
            logger);

        await reconciler.StartAsync();

        Assert.Equal(1, logger.Attempts);
        Assert.Equal(XgpStartBlock.Checking, reconciler.State.XgpBlock);
        Assert.True(SpinWait.SpinUntil(() => reconciler.State.XgpBlock is null, NoticeTesting.OperationBound));
        Assert.Equal(_instance, reconciler.State.Instance);
    }

    /// <summary>
    /// The owe step — the record's half of admission (§7.5) — holds an XGP
    /// start back while the state is unknown: before the page has reconciled,
    /// the job is resolved as never sent. Once it has, a job of this instance
    /// that another page recorded holds nothing back: it is the server's half.
    /// </summary>
    [Fact]
    public async Task TheOweStep_HoldsAStartBackWhileTheStateIsUnknown_ButNotForAnotherPagesJobOfThisInstance()
    {
        var keeper = await RestoredKeeperAsync();
        var reconciler = ReconcilerOver(keeper);

        Assert.False(await reconciler.RecordOwedAsync(OwedJob("job-1")));
        Assert.Empty(keeper.Record.Owed);

        await reconciler.StartAsync();
        Assert.Null(reconciler.State.XgpBlock);
        var anotherPage = await RestoredKeeperAsync();
        Assert.True(await anotherPage.OweAsync(OwedJob("another-pages")));

        Assert.True(await reconciler.RecordOwedAsync(OwedJob("job-2")));
        Assert.Equal(["another-pages", "job-2"], keeper.Record.Owed.Select(job => job.JobId));
    }

    /// <summary>
    /// The state is unknown though the instance is known: a pass has learned
    /// it, but an owed job of it cannot be settled, so reconciliation has not
    /// succeeded, and the owe step holds a start back. Once the settle is
    /// reached the job is retired and the state known again.
    /// </summary>
    [Fact]
    public async Task TheOweStep_WhileAnOwedJobCannotBeSettled_HoldsTheStartBack()
    {
        var anotherPage = await RestoredKeeperAsync();
        Assert.True(await anotherPage.OweAsync(OwedJob("never-started")));
        var failing = _server.Wire.Fail(ProtocolWire.Settle);
        var keeper = await RestoredKeeperAsync();
        var reconciler = ReconcilerOver(keeper);
        await reconciler.StartAsync();
        Assert.Equal(new LocalStartState(_instance, null, XgpStartBlock.Checking), reconciler.State);

        Assert.False(await reconciler.RecordOwedAsync(OwedJob("job-1")));
        Assert.DoesNotContain(keeper.Record.Owed, job => job.JobId == "job-1");

        failing.Stop();
        Assert.True(SpinWait.SpinUntil(() => reconciler.State.XgpBlock is null, NoticeTesting.OperationBound));
    }

    private OwedXgpJob OwedJob(string jobId) =>
        new(jobId, _instance, new XgpExportOptions(), _server.OutputFolder);

    /// <summary>
    /// A page restored from a record that already holds the entries offers
    /// nothing: its controls were restored from that record.
    /// </summary>
    [Fact]
    public async Task ARecordRestoredHoldingTheEntries_OffersNothing()
    {
        await _server.ExportAsync("elsewhere-1", _instance, mark: null);
        await ReconcilerOver(await RestoredKeeperAsync(), _ => Task.CompletedTask).StartAsync();

        await ReconcilerOver(await RestoredKeeperAsync()).StartAsync();

        Assert.Empty(_takings);
    }
}
