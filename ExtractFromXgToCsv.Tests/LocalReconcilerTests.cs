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
