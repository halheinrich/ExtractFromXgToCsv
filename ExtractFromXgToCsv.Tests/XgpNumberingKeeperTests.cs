using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Home's numbering record as its one owner keeps it (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): every checkpoint reads the stored record
/// afresh and writes the whole merged record in one write; a stored record
/// never undoes what this page has applied or resolved; another page's owed
/// job is never dropped; and a refused write leaves the change standing in
/// this page, kept by the next checkpoint.
/// </summary>
public class XgpNumberingKeeperTests : BunitContext
{
    private const string Current = "instance-now";
    private const string Earlier = "instance-before";

    private readonly BrowserStoragePlan _storage;
    private readonly XgpNumberingKeeper _keeper;

    public XgpNumberingKeeperTests()
    {
        Services.AddClientServices();
        _storage = BrowserStoragePlan.On(JSInterop);
        _keeper = new XgpNumberingKeeper(new XgpNumberingStore(Services.GetRequiredService<SettingsStorage>()));
    }

    private static readonly XgpExportOptions Pos = new();

    private static OwedXgpJob Job(string id) => new(id, Current, Pos, @"D:\xg\out");

    private static string JobDocument(string id) => HomeStoragePlan.OwedJob(id, Current, "pos{n}", 1, 3, @"D:\xg\out");

    private static BrowserStorageReadAnswer Stored(string document) => BrowserStorageReadAnswer.Stored(document);

    [Fact]
    public async Task ACheckpoint_ReadsAfresh_AndWritesTheWholeRecordInOneWrite()
    {
        // Another page exported after this one loaded: its numbering is the
        // stored one, and this page's change goes on top of it.
        var theirs = HomeStoragePlan.NumberingDocument("pos{n}", 9, 3);
        _storage.ExpectNumberingCheckpoint(Stored(theirs),
            HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, null, JobDocument("j1")));

        Assert.True(await _keeper.CheckpointAsync(r => r with { Owed = [.. r.Owed, Job("j1")] }, Current));

        Assert.Equal(new XgpNumbering("pos{n}", 9, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    [Fact]
    public async Task ARefusedCheckpoint_StandsInThisPage_AndTheNextCheckpointKeepsIt()
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent,
            HomeStoragePlan.NumberingDocument("pos{n}", 4, 3), BrowserStorageWriteAnswer.Refused);
        Assert.False(await _keeper.CheckpointAsync(r => r.WithCommit(new XgpExportCommit(Pos, 4)), Current));
        Assert.Equal(new XgpNumbering("pos{n}", 4, 3), _keeper.Record.Numbering);

        // The stored record is the older one the refusal left whole; this
        // page's numbering, which storage never took, is not undone by it.
        _storage.ExpectNumberingCheckpoint(Stored(HomeStoragePlan.NumberingDocument("pos{n}", 1, 3)),
            HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, null, JobDocument("j1")));
        Assert.True(await _keeper.CheckpointAsync(r => r with { Owed = [Job("j1")] }, Current));
        _storage.Verify();
    }

    [Fact]
    public async Task AStoredRecordWithANewerMark_SuppliesTheNumberingAndTheMark()
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent,
            HomeStoragePlan.NumberingDocument("pos{n}", 2, 3, (Current, 1)));
        await _keeper.CheckpointAsync(
            r => r.WithCommit(new XgpExportCommit(Pos, 2)) with { Mark = new LedgerMark(Current, 1) }, Current);

        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey,
            Stored(HomeStoragePlan.NumberingDocument("pos{n}", 7, 3, (Current, 2))));
        await _keeper.RefreshAsync(Current);

        Assert.Equal(new LedgerMark(Current, 2), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 7, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    /// <summary>
    /// A stored record never undoes what this page applied: a stored mark
    /// older than this page's — a lower entry of the same instance, or any
    /// entry of an earlier instance — supplies neither the mark nor the
    /// numbering.
    /// </summary>
    [Theory]
    [InlineData(Current, 1)]
    [InlineData(Earlier, 9)]
    public async Task AStoredRecordWithAnOlderMark_UndoesNothing(string storedInstance, int storedNumber)
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent,
            HomeStoragePlan.NumberingDocument("pos{n}", 5, 3, (Current, 2)));
        await _keeper.CheckpointAsync(
            r => r.WithCommit(new XgpExportCommit(Pos, 5)) with { Mark = new LedgerMark(Current, 2) }, Current);

        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey,
            Stored(HomeStoragePlan.NumberingDocument("old{n}", 1, 3, (storedInstance, storedNumber))));
        await _keeper.RefreshAsync(Current);

        Assert.Equal(new LedgerMark(Current, 2), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 5, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    [Fact]
    public async Task OwedJobs_AreTheStoredOnesAndThisPagesTogether_LessThoseThisPageResolved()
    {
        // This page owes j1; another page has stored that it owes j2.
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent,
            "{\"version\":1,\"owed\":[" + JobDocument("j1") + "]}");
        await _keeper.CheckpointAsync(r => r with { Owed = [Job("j1")] }, Current);

        // This page resolves j1, over a stored record holding both: j2 stays.
        var both = "{\"version\":1,\"owed\":[" + JobDocument("j2") + "," + JobDocument("j1") + "]}";
        _storage.ExpectNumberingCheckpoint(Stored(both), "{\"version\":1,\"owed\":[" + JobDocument("j2") + "]}");
        await _keeper.CheckpointAsync(r => r with { Owed = [.. r.Owed.Where(j => j.JobId != "j1")] }, Current);

        // A stale write elsewhere still lists j1: it is not owed here again.
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, Stored(both));
        await _keeper.RefreshAsync(Current);
        Assert.Equal(["j2"], _keeper.Record.Owed.Select(j => j.JobId));
        _storage.Verify();
    }

    [Fact]
    public async Task ARefreshThatIsRefused_KeepsThisPagesRecord()
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent,
            HomeStoragePlan.NumberingDocument("pos{n}", 3, 3));
        await _keeper.CheckpointAsync(r => r.WithCommit(new XgpExportCommit(Pos, 3)), Current);
        var record = _keeper.Record;

        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, BrowserStorageReadAnswer.Refused);
        await _keeper.RefreshAsync(Current);

        Assert.Equal(record, _keeper.Record);
        _storage.Verify();
    }
}
