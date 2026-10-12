using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// One server instance's job store (the umbrella's <c>SPEC-notices.md</c>
/// §7.5), directly: a start is judged by its identity first — a repeat returns
/// its disposition and starts nothing, a changed request and a retired
/// identity are refused, and so is a start naming another instance — and only
/// then, for XGP, by the ledger: refused while any entry runs, and unless its
/// mark names the latest entry. A status read never removes a job; the
/// acknowledgement releases only its result. Settling retires an identity
/// never admitted and answers admitted for one that was. Deterministic: the
/// launched work is the test's.
/// </summary>
public class JobStoreTests
{
    private const string Here = "instance-here";

    private readonly JobStore _store = new(new ServerInstance(Here));
    private readonly List<JobEntry> _launched = [];

    private StartJudgement Start(StartRequest request) => _store.Start(request, _launched.Add);

    private static StartRequest Csv(string jobId, string instance = Here) =>
        new(jobId, instance, Mark: null, new ProcessRequest { FolderPath = @"D:\xg", OutputPath = @"D:\out.csv" });

    private static StartRequest Xgp(string jobId, LedgerMark? mark = null, string instance = Here) =>
        new(jobId, instance, mark, new ProcessRequest
        {
            FolderPath = @"D:\xg",
            OutputPath = @"D:\positions",
            OutputFormat = OutputFormat.Xgp,
        });

    private static LedgerMark Mark(int number) => new(Here, number);

    private static ProcessingProgress Ended(int committed) => new()
    {
        Complete = true,
        TotalRows = committed,
        XgpCommit = new XgpExportCommit(new XgpExportOptions(), committed),
    };

    // ── The job and its result ──────────────────────────────────────────────

    [Fact]
    public void AStatusRead_NeverRemovesTheJob_ItsTerminalResultIsServedAsOftenAsAsked()
    {
        Start(Csv("a"));
        var terminal = new ProcessingProgress { Complete = true, TotalRows = 7 };
        _store.Complete(_launched[0], terminal);

        Assert.Same(terminal, _store.ReadStatus("a"));
        Assert.Same(terminal, _store.ReadStatus("a"));
    }

    [Fact]
    public void TheAcknowledgement_ReleasesOnlyTheResult_TheDispositionAndTheLedgerStand()
    {
        Start(Xgp("a"));
        _store.Complete(_launched[0], Ended(2));

        _store.Acknowledge("a");

        Assert.Null(_store.ReadStatus("a"));
        Assert.Equal(new LedgerEntry(1, "a", Running: false, new XgpExportCommit(new XgpExportOptions(), 2)),
            Assert.Single(_store.Ledger().Entries));
        // A duplicate start after the acknowledgement: the disposition stands.
        Assert.Equal(new StartJudgement.Admitted(1, Started: false), Start(Xgp("a")));
        Assert.Single(_launched);
    }

    [Fact]
    public void AnAcknowledgement_OfAJobNotHeld_OrStillRunning_ChangesNothing()
    {
        _store.Acknowledge("never-started");

        Start(Csv("running"));
        _store.Acknowledge("running");

        Assert.NotNull(_store.ReadStatus("running"));
    }

    [Fact]
    public void TheFirstTerminalResultStands_AndAReportAfterItIsIgnored()
    {
        Start(Xgp("a"));
        var job = _launched[0];
        var first = Ended(1);
        _store.Complete(job, first);

        _store.Complete(job, Ended(5));
        _store.Report(job, new ProcessingProgress { Current = 3, Total = 9 });

        Assert.Same(first, _store.ReadStatus("a"));
        Assert.Equal(1, _store.Ledger().Entries[0].Commit!.Count);
    }

    [Fact]
    public void Cancel_SignalsARunningJob_AndNoOpsForAnEndedOrUnknownOne()
    {
        Start(Csv("a"));
        var token = _launched[0].Cancellation;

        Assert.True(_store.Cancel("a"));
        Assert.True(token.IsCancellationRequested);

        _store.Complete(_launched[0], new ProcessingProgress { Complete = true, Cancelled = true });
        Assert.False(_store.Cancel("a"));
        Assert.False(_store.Cancel("nope"));
    }

    // ── A start is judged by its identity first ─────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARepeatedStart_ReturnsItsDisposition_AndStartsNothing(bool xgp)
    {
        var request = xgp ? Xgp("a") : Csv("a");
        var first = Start(request);

        var repeat = Start(request);

        Assert.Equal(new StartJudgement.Admitted(xgp ? 1 : null, Started: true), first);
        Assert.Equal(new StartJudgement.Admitted(xgp ? 1 : null, Started: false), repeat);
        Assert.Single(_launched);
        Assert.Equal(xgp ? 1 : 0, _store.Ledger().Entries.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSameIdentityWithAChangedRequest_IsRefused(bool xgp)
    {
        Start(xgp ? Xgp("a") : Csv("a"));
        var changed = (xgp ? Xgp("a") : Csv("a")) with { Run = new ProcessRequest { FolderPath = @"D:\elsewhere" } };

        Assert.Equal(new StartJudgement.Refused(RefusalReason.ChangedRequest), Start(changed));
        Assert.Single(_launched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStartNamingAnotherInstance_IsRefused_BeforeItsIdentityIsJudged(bool xgp)
    {
        var elsewhere = xgp ? Xgp("a", instance: "instance-before") : Csv("a", instance: "instance-before");

        Assert.Equal(new StartJudgement.Refused(RefusalReason.OtherInstance), Start(elsewhere));
        Assert.Empty(_launched);
        Assert.Empty(_store.Ledger().Entries);
    }

    // ── Settling ────────────────────────────────────────────────────────────

    [Fact]
    public void SettlingAnIdentityNeverAdmitted_RetiresIt_AndItsStartIsRefusedForever()
    {
        Assert.Equal(new SettleJudgement.Settled(SettleOutcome.Retired), _store.Settle("a", Here));

        Assert.Equal(new StartJudgement.Refused(RefusalReason.Retired), Start(Xgp("a")));
        Assert.Equal(new StartJudgement.Refused(RefusalReason.Retired), Start(Csv("a")));
        Assert.Empty(_launched);
        Assert.Empty(_store.Ledger().Entries);
        Assert.Equal(new SettleJudgement.Settled(SettleOutcome.Retired), _store.Settle("a", Here));
    }

    [Fact]
    public void SettlingAnAdmittedIdentity_AnswersAdmitted_AndChangesNothing()
    {
        Start(Xgp("a"));

        Assert.Equal(new SettleJudgement.Settled(SettleOutcome.Admitted), _store.Settle("a", Here));
        Assert.True(_store.Ledger().Entries[0].Running);
        Assert.NotNull(_store.ReadStatus("a"));
    }

    [Fact]
    public void ASettleNamingAnotherInstance_IsRefused()
    {
        Assert.Equal(new SettleJudgement.Refused(RefusalReason.OtherInstance), _store.Settle("a", "instance-before"));
        // And it retired nothing here.
        Assert.IsType<StartJudgement.Admitted>(Start(Csv("a")));
    }

    // ── XGP admission is the ledger's ───────────────────────────────────────

    [Fact]
    public void AnInstanceThatHasAdmittedNone_AdmitsAnyMark_AndNumbersItsEntriesInOrder()
    {
        Assert.Equal(new StartJudgement.Admitted(1, Started: true), Start(Xgp("a", new LedgerMark("instance-before", 9))));
        _store.Complete(_launched[0], Ended(3));

        Assert.Equal(new StartJudgement.Admitted(2, Started: true), Start(Xgp("b", Mark(1))));
        Assert.Equal([1, 2], _store.Ledger().Entries.Select(entry => entry.Number));
    }

    [Fact]
    public void AStartWhoseMarkIsNotTheLatestEntry_IsRefused_AndStartsNothing()
    {
        Start(Xgp("a"));
        _store.Complete(_launched[0], Ended(3));
        Start(Xgp("b", Mark(1)));
        _store.Complete(_launched[1], Ended(1));

        Assert.Equal(new StartJudgement.Refused(RefusalReason.StaleMark), Start(Xgp("stale", Mark(1))));
        Assert.Equal(new StartJudgement.Refused(RefusalReason.StaleMark), Start(Xgp("none", mark: null)));
        Assert.Equal(2, _launched.Count);
        Assert.Equal(2, _store.Ledger().Entries.Count);
    }

    /// <summary>
    /// A start whose mark names the latest entry is still refused while that
    /// entry runs: admission checks the running entry itself, not the mark
    /// alone.
    /// </summary>
    [Fact]
    public void AStartNamingTheLatestEntry_IsStillRefused_WhileAnyEntryRuns()
    {
        Start(Xgp("a"));
        _store.Complete(_launched[0], Ended(1));
        Start(Xgp("b", Mark(1)));

        Assert.Equal(new StartJudgement.Refused(RefusalReason.XgpRunning), Start(Xgp("c", Mark(2))));
        Assert.Equal(new StartJudgement.Refused(RefusalReason.XgpRunning), Start(Xgp("d", Mark(1))));
        Assert.Equal(2, _store.Ledger().Entries.Count);
    }

    [Fact]
    public void OtherFormats_AreNotAdmittedByTheLedger()
    {
        Start(Xgp("a"));

        Assert.IsType<StartJudgement.Admitted>(Start(Csv("b")));
        Assert.Single(_store.Ledger().Entries);
    }

    /// <summary>
    /// Two XGP starts at once, each naming the same latest entry: exactly one
    /// is admitted, under the lock, and the other is refused.
    /// </summary>
    [Fact]
    public async Task OfTwoSimultaneousStarts_ExactlyOneIsAdmitted()
    {
        for (var round = 0; round < 50; round++)
        {
            var store = new JobStore(new ServerInstance(Here));
            using var go = new ManualResetEventSlim();
            var starts = Enumerable.Range(0, 2).Select(n => Task.Run(() =>
            {
                go.Wait();
                return store.Start(Xgp($"r{round}-{n}"), _ => { });
            })).ToArray();
            go.Set();
            var judgements = await Task.WhenAll(starts);

            Assert.Single(judgements, judgement => judgement is StartJudgement.Admitted);
            Assert.Single(store.Ledger().Entries);
        }
    }
}
