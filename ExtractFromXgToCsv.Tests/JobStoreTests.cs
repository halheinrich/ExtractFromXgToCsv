using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The server's job store (the umbrella's <c>SPEC-notices.md</c> §7.5),
/// directly: a start is judged by its identity — a repeat returns its
/// admission and starts nothing, a changed request is refused, and no refusal
/// changes an admission — and two different identities both run. A status
/// read never removes a job; the acknowledgement releases only its result.
/// Deterministic: the launched work is the test's.
/// </summary>
public class JobStoreTests
{
    private readonly JobStore _store = new();
    private readonly List<JobEntry> _launched = [];

    private StartJudgement Start(StartRequest request) => _store.Start(request, _launched.Add);

    private static StartRequest Csv(string jobId) =>
        new(jobId, new ProcessRequest { FolderPath = @"D:\xg", OutputPath = @"D:\out.csv" });

    private static StartRequest Xgp(string jobId) =>
        new(jobId, new ProcessRequest
        {
            FolderPath = @"D:\xg",
            OutputPath = @"D:\positions",
            OutputFormat = OutputFormat.Xgp,
        });

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

    /// <summary>
    /// The snapshot a run reports is the one every status read returns, kept
    /// as reported — no holder can change it — until the terminal result
    /// replaces it.
    /// </summary>
    [Fact]
    public void TheReportedSnapshot_IsServedAsReported_UntilTheTerminalResultReplacesIt()
    {
        Start(Csv("a"));
        var reported = new ProcessingProgress { Current = 2, Total = 5, FileName = "b.xg", TotalRows = 9 };
        _store.Report(_launched[0], reported);
        Assert.Same(reported, _store.ReadStatus("a"));

        var terminal = new ProcessingProgress { Complete = true, Cancelled = true, Current = 3, Total = 5 };
        _store.Complete(_launched[0], terminal);

        Assert.Same(terminal, _store.ReadStatus("a"));
    }

    [Fact]
    public void TheAcknowledgement_ReleasesOnlyTheResult_TheAdmissionStands()
    {
        Start(Xgp("a"));
        _store.Complete(_launched[0], Ended(2));

        _store.Acknowledge("a");

        Assert.Null(_store.ReadStatus("a"));
        // A duplicate start after the acknowledgement: the admission stands.
        Assert.Equal(new StartJudgement.Admitted(Started: false), Start(Xgp("a")));
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

    // ── A start is judged by its identity ───────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARepeatedStart_ReturnsItsAdmission_AndStartsNothing(bool xgp)
    {
        var request = xgp ? Xgp("a") : Csv("a");
        var first = Start(request);

        var repeat = Start(request);

        Assert.Equal(new StartJudgement.Admitted(Started: true), first);
        Assert.Equal(new StartJudgement.Admitted(Started: false), repeat);
        Assert.Single(_launched);
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

    /// <summary>
    /// No refusal changes an admission: an admitted identity whose
    /// conflicting retry is refused stays admitted, so the original's retry is
    /// still answered with its job.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConflictingRetryRefused_LeavesTheAdmission_SoTheOriginalRetryIsStillAdmitted(bool xgp)
    {
        var original = xgp ? Xgp("a") : Csv("a");
        Start(original);
        var conflicting = original with { Run = new ProcessRequest { FolderPath = @"D:\elsewhere" } };

        Assert.Equal(new StartJudgement.Refused(RefusalReason.ChangedRequest), Start(conflicting));

        Assert.Equal(new StartJudgement.Admitted(Started: false), Start(original));
        Assert.Single(_launched);
    }

    /// <summary>
    /// Two XGP starts at once, under different identities: both are admitted,
    /// each registering its own job. Nothing admits one XGP job over another
    /// any more (§7.5): two exports into one folder write incremented names,
    /// which is the ruled cost.
    /// </summary>
    [Fact]
    public async Task TwoSimultaneousStarts_AreBothAdmitted()
    {
        for (var round = 0; round < 50; round++)
        {
            var store = new JobStore();
            using var go = new ManualResetEventSlim();
            var starts = Enumerable.Range(0, 2).Select(n => Task.Run(() =>
            {
                go.Wait();
                return store.Start(Xgp($"r{round}-{n}"), _ => { });
            })).ToArray();
            go.Set();
            var judgements = await Task.WhenAll(starts);

            Assert.All(judgements, judgement => Assert.Equal(new StartJudgement.Admitted(Started: true), judgement));
            Assert.All(Enumerable.Range(0, 2), n => Assert.NotNull(store.ReadStatus($"r{round}-{n}")));
        }
    }
}
