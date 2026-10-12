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
/// <c>SPEC-notices.md</c> §7.5), over a strict storage plan: every checkpoint
/// reads the stored record afresh and writes the whole merged record in one
/// write. A stored record unchanged since this page saw it leaves this page's
/// record standing; a changed one supplies its numbering and mark together —
/// within an instance only when its mark is later, across instances, which no
/// mark orders, always. Owed jobs this page learned from storage are dropped
/// when a readable record no longer holds them; ones it recorded survive
/// until seen stored. A pass writes an entry of another instance only from
/// the state it started with, while both halves stand. A record that cannot
/// be read is kept until the user's resolution replaces it.
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

    private static string OwingDocument(params string[] ids) =>
        "{\"version\":1,\"owed\":[" + string.Join(",", ids.Select(JobDocument)) + "]}";

    private static BrowserStorageReadAnswer Stored(string document) => BrowserStorageReadAnswer.Stored(document);

    private static LedgerEntry Ended(int number, string jobId, int committed) =>
        new(number, jobId, Running: false, new XgpExportCommit(Pos, committed));

    private void ExpectRead(BrowserStorageReadAnswer answer) =>
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, answer);

    private async Task RestoreFromAsync(string document)
    {
        ExpectRead(Stored(document));
        await _keeper.RestoreAsync();
    }

    // ── Checkpoints ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ACheckpoint_ReadsAfresh_AndWritesTheWholeRecordInOneWrite()
    {
        // Another page exported after this one loaded: its numbering is the
        // stored one, and this page's change goes on top of it.
        var theirs = HomeStoragePlan.NumberingDocument("pos{n}", 9, 3);
        _storage.ExpectNumberingCheckpoint(Stored(theirs),
            HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, null, JobDocument("j1")));

        Assert.True(await _keeper.OweAsync(Job("j1")));

        Assert.Equal(new XgpNumbering("pos{n}", 9, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    [Fact]
    public async Task ARefusedCheckpoint_StandsInThisPage_AndTheNextCheckpointKeepsIt()
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent,
            HomeStoragePlan.NumberingDocument("pos{n}", 4, 3), BrowserStorageWriteAnswer.Refused);
        Assert.False(await _keeper.RecordExportAsync(new XgpExportCommit(Pos, 4)));
        Assert.Equal(new XgpNumbering("pos{n}", 4, 3), _keeper.Record.Numbering);

        // The stored record is an older one; this page's numbering, which
        // storage never took, is not undone by it.
        _storage.ExpectNumberingCheckpoint(Stored(HomeStoragePlan.NumberingDocument("pos{n}", 1, 3)),
            HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, null, JobDocument("j1")));
        Assert.True(await _keeper.OweAsync(Job("j1")));
        _storage.Verify();
    }

    [Fact]
    public async Task AnExportThatCommittedNothing_WritesNothing()
    {
        Assert.True(await _keeper.RecordExportAsync(new XgpExportCommit(Pos, 0)));

        Assert.Null(_keeper.Record.Numbering);
        _storage.Verify();
    }

    // ── The merge: whose numbering and mark ─────────────────────────────────

    [Fact]
    public async Task AChangedStoredRecordWithALaterMarkOfTheSameInstance_SuppliesTheNumberingAndTheMark()
    {
        await RestoreFromAsync(HomeStoragePlan.NumberingDocument("pos{n}", 2, 3, (Current, 1)));

        ExpectRead(Stored(HomeStoragePlan.NumberingDocument("pos{n}", 7, 3, (Current, 2))));
        await _keeper.BeginPassAsync();

        Assert.Equal(new LedgerMark(Current, 2), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 7, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    [Fact]
    public async Task AChangedStoredRecordWithAnEarlierMarkOfTheSameInstance_UndoesNothing()
    {
        await RestoreFromAsync(HomeStoragePlan.NumberingDocument("pos{n}", 5, 3, (Current, 2)));

        ExpectRead(Stored(HomeStoragePlan.NumberingDocument("old{n}", 1, 3, (Current, 1))));
        await _keeper.BeginPassAsync();

        Assert.Equal(new LedgerMark(Current, 2), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 5, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    /// <summary>
    /// Marks of different instances are not ordered — whatever this page
    /// believes the current instance is — so a stored record another page
    /// changed to another instance's mark is taken, its numbering and its mark
    /// together, never one record's mark with the other's numbering.
    /// </summary>
    [Fact]
    public async Task AChangedStoredRecordOfAnotherInstance_IsTaken_MarkAndNumberingTogether()
    {
        await RestoreFromAsync(HomeStoragePlan.NumberingDocument("pos{n}", 5, 3, (Earlier, 2)));

        ExpectRead(Stored(HomeStoragePlan.NumberingDocument("new{n}", 30, 4, (Current, 1))));
        await _keeper.BeginPassAsync();

        Assert.Equal(new LedgerMark(Current, 1), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("new{n}", 30, 4), _keeper.Record.Numbering);
        _storage.Verify();
    }

    /// <summary>
    /// A stored record unchanged since this page saw it holds nothing new:
    /// this page's record stands, mark and numbering, its pending change with
    /// it — here one of another instance, which the stored record's mark does
    /// not order.
    /// </summary>
    [Fact]
    public async Task AStoredRecordUnchangedSinceThisPageSawIt_LeavesThisPagesRecordStanding()
    {
        var before = HomeStoragePlan.NumberingDocument("pos{n}", 5, 3, (Earlier, 2));
        await RestoreFromAsync(before);
        var start = await BeginPassOverAsync(before);
        // Applied in this page alone: storage refused the write.
        ExpectRead(Stored(before));
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey,
            HomeStoragePlan.NumberingDocument("pos{n}", 7, 3, (Current, 1)), BrowserStorageWriteAnswer.Refused);
        Assert.Equal(EntryApplied.Taken, await _keeper.ApplyAsync(start, Current, Ended(1, "j1", 2) with
        {
            Commit = new XgpExportCommit(Pos with { StartNumber = 6 }, 2),
        }));

        ExpectRead(Stored(before));
        await _keeper.BeginPassAsync();

        Assert.Equal(new LedgerMark(Current, 1), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 7, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    // ── Owed jobs ───────────────────────────────────────────────────────────

    [Fact]
    public async Task OwedJobs_AreTheStoredOnesAndThisPagesTogether_LessThoseThisPageResolved()
    {
        // This page owes j1; another page has stored that it owes j2.
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent, OwingDocument("j1"));
        await _keeper.OweAsync(Job("j1"));

        // This page resolves j1, over a stored record holding both: j2 stays.
        _storage.ExpectNumberingCheckpoint(Stored(OwingDocument("j2", "j1")), OwingDocument("j2"));
        await _keeper.ResolveAsync("j1");

        // A stale write elsewhere still lists j1: it is not owed here again.
        ExpectRead(Stored(OwingDocument("j2", "j1")));
        await _keeper.BeginPassAsync();
        Assert.Equal(["j2"], _keeper.Record.Owed.Select(j => j.JobId));
        _storage.Verify();
    }

    /// <summary>
    /// An owed job this page learned from storage is resolved when a later
    /// readable record no longer holds it: another page resolved it.
    /// </summary>
    [Fact]
    public async Task AnOwedJobLearnedFromStorage_IsDropped_WhenAReadableRecordNoLongerHoldsIt()
    {
        await RestoreFromAsync(OwingDocument("j2"));
        Assert.Equal(["j2"], _keeper.Record.Owed.Select(j => j.JobId));

        ExpectRead(Stored(OwingDocument()));
        await _keeper.BeginPassAsync();

        Assert.Empty(_keeper.Record.Owed);
        _storage.Verify();
    }

    /// <summary>A refused read, or one of a record that cannot be read, establishes nothing: the learned job stays owed.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReadThatEstablishesNothing_DropsNoLearnedOwedJob(bool refused)
    {
        await RestoreFromAsync(OwingDocument("j2"));

        ExpectRead(refused ? BrowserStorageReadAnswer.Refused : Stored("not json"));
        await _keeper.BeginPassAsync();

        Assert.Equal(["j2"], _keeper.Record.Owed.Select(j => j.JobId));
        _storage.Verify();
    }

    /// <summary>
    /// A job this page recorded and has not seen stored survives a read that
    /// lacks it — storage may never have taken it; once it has been seen
    /// stored, a read that lacks it means another page resolved it.
    /// </summary>
    [Fact]
    public async Task AnOwedJobThisPageRecorded_SurvivesAReadThatLacksIt_UntilItHasBeenSeenStored()
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent, OwingDocument("j1"), BrowserStorageWriteAnswer.Refused);
        await _keeper.OweAsync(Job("j1"));

        ExpectRead(Stored(OwingDocument()));
        await _keeper.BeginPassAsync();
        Assert.Equal(["j1"], _keeper.Record.Owed.Select(j => j.JobId));

        _storage.ExpectNumberingCheckpoint(Stored(OwingDocument()), OwingDocument("j1"));
        await _keeper.CheckpointAsync(record => record);

        ExpectRead(Stored(OwingDocument()));
        await _keeper.BeginPassAsync();
        Assert.Empty(_keeper.Record.Owed);
        _storage.Verify();
    }

    [Fact]
    public async Task ARefusedPassRead_KeepsThisPagesRecord()
    {
        _storage.ExpectNumberingCheckpoint(BrowserStorageReadAnswer.Absent, HomeStoragePlan.NumberingDocument("pos{n}", 3, 3));
        await _keeper.RecordExportAsync(new XgpExportCommit(Pos, 3));
        var record = _keeper.Record;

        ExpectRead(BrowserStorageReadAnswer.Refused);
        await _keeper.BeginPassAsync();

        Assert.Equal(record, _keeper.Record);
        _storage.Verify();
    }

    // ── A pass's authority over another instance's mark ─────────────────────

    private static readonly string EarlierRecord = HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, (Earlier, 4));

    private async Task<XgpNumberingKeeper.PassStart> BeginPassOverAsync(string document)
    {
        ExpectRead(Stored(document));
        return await _keeper.BeginPassAsync();
    }

    [Fact]
    public async Task AnEntryOfAnotherInstance_IsWrittenFromThePassesStart_WhileBothHalvesStand()
    {
        await RestoreFromAsync(EarlierRecord);
        var start = await BeginPassOverAsync(EarlierRecord);

        _storage.ExpectNumberingCheckpoint(Stored(EarlierRecord), HomeStoragePlan.NumberingDocument("pos{n}", 2, 3, (Current, 1)));
        Assert.Equal(EntryApplied.Taken, await _keeper.ApplyAsync(start, Current, Ended(1, "j1", 2)));

        Assert.Equal(new LedgerMark(Current, 1), _keeper.Record.Mark);
        _storage.Verify();
    }

    /// <summary>
    /// Another page wrote after the pass read the stored record: the pass
    /// writes nothing, takes the changed state — mark and numbering together —
    /// and reconciles again.
    /// </summary>
    [Fact]
    public async Task WhenTheStoredRecordChangedSinceThePassBegan_ThePassWritesNothing_AndTakesTheChangedState()
    {
        await RestoreFromAsync(EarlierRecord);
        var start = await BeginPassOverAsync(EarlierRecord);

        ExpectRead(Stored(HomeStoragePlan.NumberingDocument("new{n}", 40, 3, ("instance-newer", 2))));
        Assert.Equal(EntryApplied.StateChanged, await _keeper.ApplyAsync(start, Current, Ended(1, "j1", 2)));

        Assert.Equal(new LedgerMark("instance-newer", 2), _keeper.Record.Mark);
        Assert.Equal(new XgpNumbering("new{n}", 40, 3), _keeper.Record.Numbering);
        _storage.Verify();
    }

    /// <summary>
    /// This page's own record changed in memory while the pass waited — a
    /// change storage refused — so the pass does not write over it: it writes
    /// nothing, and reconciles again.
    /// </summary>
    [Fact]
    public async Task WhenThisPagesRecordChangedSinceThePassBegan_ThePassWritesNothing()
    {
        await RestoreFromAsync(HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, (Earlier, 4), JobDocument("lost")));
        var start = await BeginPassOverAsync(HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, (Earlier, 4), JobDocument("lost")));
        _storage.ExpectNumberingCheckpoint(
            Stored(HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, (Earlier, 4), JobDocument("lost"))),
            EarlierRecord, BrowserStorageWriteAnswer.Refused);
        await _keeper.ResolveAsync("lost");

        ExpectRead(Stored(HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, (Earlier, 4), JobDocument("lost"))));
        Assert.Equal(EntryApplied.StateChanged, await _keeper.ApplyAsync(start, Current, Ended(1, "j1", 2)));

        Assert.Equal(new LedgerMark(Earlier, 4), _keeper.Record.Mark);
        Assert.Empty(_keeper.Record.Owed);
        _storage.Verify();
    }

    /// <summary>
    /// A refused fresh read cannot show the stored record unchanged: nothing
    /// is written, and the entry is applied in this page's memory alone —
    /// while this page's record is unchanged, and not otherwise.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnderARefusedRead_AnEntryOfAnotherInstance_IsAppliedInMemoryAlone_OnlyWhileThisPagesRecordStands(bool pageChanged)
    {
        await RestoreFromAsync(EarlierRecord);
        var start = await BeginPassOverAsync(EarlierRecord);
        if (pageChanged)
        {
            _storage.ExpectNumberingCheckpoint(Stored(EarlierRecord), OwingAppendedTo(EarlierRecord, "j9"), BrowserStorageWriteAnswer.Refused);
            await _keeper.OweAsync(Job("j9"));
        }

        ExpectRead(BrowserStorageReadAnswer.Refused);
        var applied = await _keeper.ApplyAsync(start, Current, Ended(1, "j1", 2));

        Assert.Equal(pageChanged ? EntryApplied.StateChanged : EntryApplied.Taken, applied);
        Assert.Equal(pageChanged ? new LedgerMark(Earlier, 4) : new LedgerMark(Current, 1), _keeper.Record.Mark);
        _storage.Verify();
    }

    private static string OwingAppendedTo(string document, string jobId) =>
        document.Replace("\"owed\":[]", "\"owed\":[" + JobDocument(jobId) + "]");

    /// <summary>An entry of the mark's instance is ordered by number: one at or before the mark writes nothing.</summary>
    [Fact]
    public async Task AnEntryOfTheMarksInstance_AtOrBeforeTheMark_WritesNothing()
    {
        var record = HomeStoragePlan.NumberingDocument("pos{n}", 9, 3, (Current, 2));
        await RestoreFromAsync(record);
        var start = await BeginPassOverAsync(record);

        ExpectRead(Stored(record));
        Assert.Equal(EntryApplied.NotNewer, await _keeper.ApplyAsync(start, Current, Ended(2, "j2", 4)));
        _storage.Verify();
    }

    // ── A record that cannot be read ────────────────────────────────────────

    private const string FreshDocument = "{\"version\":1,\"owed\":[]}";

    /// <summary>
    /// A malformed document, and one a newer version wrote, are unreadable:
    /// the page starts from no numbering, and the document is kept — the
    /// strict plan admits no migration read after it, a checkpoint reads it
    /// and writes nothing, and a pass applies nothing over it.
    /// </summary>
    [Theory]
    [InlineData("{\"version\":1,\"owed\":")]
    [InlineData("{\"version\":2,\"numbering\":{\"pattern\":\"pos{n}\",\"lastNumber\":40,\"suffixLength\":3},\"owed\":[]}")]
    public async Task AnUnreadableRecord_IsKept_NeitherMigrationNorACheckpointNorAPassReplacesIt(string unreadable)
    {
        await RestoreFromAsync(unreadable);

        Assert.True(_keeper.IsUnreadable);
        Assert.Null(_keeper.Record.Numbering);

        ExpectRead(Stored(unreadable));
        Assert.False(await _keeper.RecordExportAsync(new XgpExportCommit(Pos, 4)));
        var start = await BeginPassOverAsync(unreadable);
        ExpectRead(Stored(unreadable));
        Assert.Equal(EntryApplied.Unreadable, await _keeper.ApplyAsync(start, Current, Ended(1, "j1", 2)));
        Assert.True(_keeper.IsUnreadable);
        _storage.Verify();
    }

    /// <summary>
    /// The user's resolution authorizes replacing the unreadable document with
    /// a fresh record, and nothing more: the fresh record is written over it,
    /// and the page no longer reads it as unreadable.
    /// </summary>
    [Fact]
    public async Task TheResolution_ReplacesTheDocumentWithAFreshRecord_AndNothingMore()
    {
        const string unreadable = "not json";
        await RestoreFromAsync(unreadable);

        _storage.ExpectNumberingCheckpoint(Stored(unreadable), FreshDocument);
        await _keeper.ResolveUnreadableAsync();

        Assert.False(_keeper.IsUnreadable);
        Assert.Equal(XgpNumberingRecord.Empty, _keeper.Record);
        _storage.Verify();
    }

    /// <summary>
    /// The resolution authorizes replacing the document the user resolved,
    /// not whatever is stored later: another unreadable document is unreadable
    /// again, and kept.
    /// </summary>
    [Fact]
    public async Task AnotherUnreadableDocument_AfterAResolution_IsUnreadableAgain()
    {
        await RestoreFromAsync("not json");
        _storage.ExpectNumberingCheckpoint(Stored("not json"), FreshDocument, BrowserStorageWriteAnswer.Refused);
        await _keeper.ResolveUnreadableAsync();
        Assert.False(_keeper.IsUnreadable);

        ExpectRead(Stored("{\"version\":3}"));
        Assert.False(await _keeper.CheckpointAsync(r => r));

        Assert.True(_keeper.IsUnreadable);
        _storage.Verify();
    }

    /// <summary>A refused read is not an unreadable record: it gates nothing, and migrates nothing.</summary>
    [Fact]
    public async Task ARefusedRead_IsNotUnreadable()
    {
        ExpectRead(BrowserStorageReadAnswer.Refused);

        await _keeper.RestoreAsync();

        Assert.False(_keeper.IsUnreadable);
        Assert.Equal(XgpNumberingRecord.Empty, _keeper.Record);
        _storage.Verify();
    }
}
