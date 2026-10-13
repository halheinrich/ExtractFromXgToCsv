using System.Net.Http.Json;
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
        var keeper = new XgpNumberingKeeper(new XgpNumberingStore(new SettingsStorage(
            new BrowserStorage(_storage), new BrowserStorageCondition(NullLogger<BrowserStorageCondition>.Instance))));
        await keeper.RestoreAsync();
        return keeper;
    }

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
        Assert.Equal([1], Assert.Single(_takings).Taken.Select(entry => entry.Number));

        await reconciler.ReconcileAsync();
        await reconciler.ReconcileAsync();
        Assert.Single(_takings);

        await _server.ExportAsync("elsewhere-2", _instance, new LedgerMark(_instance, 1));
        await reconciler.ReconcileAsync();
        Assert.Equal(2, _takings.Count);
        Assert.Equal([2], _takings[1].Taken.Select(entry => entry.Number));
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
