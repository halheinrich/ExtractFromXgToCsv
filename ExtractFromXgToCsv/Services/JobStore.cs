using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// One server instance's Local-mode jobs (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): the jobs and their terminal results, the
/// start dispositions, and the XGP ledger, all under one lock, so every
/// judgement and every change of them is one step.
/// </summary>
/// <remarks>
/// <para>
/// <b>The job</b> is one Local run, in any format, and its terminal result.
/// Its identity is the one the browser minted for its start. It is kept until
/// the page that started it acknowledges it (<see cref="Acknowledge"/>), or the
/// instance ends; a status read never removes it.
/// </para>
/// <para>
/// <b>The start dispositions</b>, one for every identity the instance has
/// seen, are kept for its lifetime: <i>admitted</i>, holding the request a
/// repeat is compared against, or <i>retired</i>, meaning no start under that
/// identity may ever run. No acknowledgement removes one.
/// </para>
/// <para>
/// <b>The XGP ledger</b> holds one entry per admitted XGP job, numbered in
/// admission order: running, or ended with the accounting it committed. An
/// entry ends in the same step as its job's terminal result, and is kept for
/// the instance's lifetime.
/// </para>
/// </remarks>
public sealed class JobStore(ServerInstance instance)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, JobEntry> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Disposition> _dispositions = new(StringComparer.Ordinal);
    private readonly List<LedgerEntry> _ledger = [];

    /// <summary>The instance this store belongs to.</summary>
    public string Instance { get; } = instance.Id;

    /// <summary>
    /// Judges a start and, for an unseen identity it admits, registers the job
    /// — and for an XGP job its ledger entry — in the same step, then hands the
    /// job to <paramref name="launch"/> to run.
    /// </summary>
    /// <remarks>
    /// In order, under the lock: a start naming another instance is refused.
    /// Then its identity: an admitted one returns its disposition and starts
    /// nothing, unless the request differs, which is refused; a retired one is
    /// refused. Only an unseen identity goes on to admission, which for XGP is
    /// the ledger's: refused while any entry runs, and unless the start's mark
    /// names the latest entry or the instance has admitted none. A refused
    /// start starts nothing and leaves no disposition.
    /// </remarks>
    public StartJudgement Start(StartRequest request, Action<JobEntry> launch)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(launch);

        JobEntry job;
        lock (_gate)
        {
            if (request.Instance != Instance)
                return new StartJudgement.Refused(RefusalReason.OtherInstance);

            if (_dispositions.TryGetValue(request.JobId, out var seen))
            {
                return seen switch
                {
                    Disposition.Admitted admitted when admitted.Request == request =>
                        new StartJudgement.Admitted(admitted.LedgerNumber, Started: false),
                    Disposition.Admitted => new StartJudgement.Refused(RefusalReason.ChangedRequest),
                    _ => new StartJudgement.Refused(RefusalReason.Retired),
                };
            }

            int? ledgerNumber = null;
            if (request.Run.OutputFormat == OutputFormat.Xgp)
            {
                if (_ledger.Any(entry => entry.Running))
                    return new StartJudgement.Refused(RefusalReason.XgpRunning);
                if (_ledger.Count > 0 && request.Mark != new LedgerMark(Instance, _ledger.Count))
                    return new StartJudgement.Refused(RefusalReason.StaleMark);
                ledgerNumber = _ledger.Count + 1;
                _ledger.Add(new LedgerEntry(ledgerNumber.Value, request.JobId, Running: true, Commit: null));
            }

            job = new JobEntry(request.JobId, ledgerNumber);
            _jobs.Add(request.JobId, job);
            _dispositions.Add(request.JobId, new Disposition.Admitted(request, ledgerNumber));
        }

        launch(job);
        return new StartJudgement.Admitted(job.LedgerNumber, Started: true);
    }

    /// <summary>Records a running job's progress; a job that has reached its terminal result keeps it.</summary>
    public void Report(JobEntry job, ProcessingProgress snapshot)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (!job.IsTerminal)
                job.Progress = snapshot;
        }
    }

    /// <summary>
    /// Records a job's terminal result and, for an XGP job, ends its ledger
    /// entry with the accounting the result carries — in one step. The first
    /// terminal result stands.
    /// </summary>
    public void Complete(JobEntry job, ProcessingProgress terminal)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(terminal);
        lock (_gate)
        {
            if (job.IsTerminal)
                return;
            job.Progress = terminal;
            job.IsTerminal = true;
            if (job.LedgerNumber is { } number)
                _ledger[number - 1] = _ledger[number - 1] with { Running = false, Commit = terminal.XgpCommit };
        }
    }

    /// <summary>
    /// The job's current snapshot, or <see langword="null"/> when the store
    /// does not hold it — never started here, or released by its
    /// acknowledgement. Reading it removes nothing.
    /// </summary>
    public ProcessingProgress? ReadStatus(string jobId)
    {
        lock (_gate)
            return _jobs.TryGetValue(jobId, out var job) ? job.Progress : null;
    }

    /// <summary>
    /// Requests cancellation of a running job. Returns <see langword="true"/>
    /// when a running job was signalled, <see langword="false"/> when the job
    /// is unknown or has reached its terminal result.
    /// </summary>
    public bool Cancel(string jobId)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.IsTerminal)
                return false;
            cts = job.Cts;
        }

        // Outside the lock: the cancellation's callbacks run on this thread.
        // The job can end, and be acknowledged, in between; then the source
        // is disposed and the cancel is moot.
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The page that started a job acknowledges it once it has taken the
    /// terminal result: the job's result is released. It never touches a
    /// disposition or the ledger. Acknowledging a job the store no longer
    /// holds — or one still running, which has no result to release yet —
    /// changes nothing, so a retried acknowledgement is harmless.
    /// </summary>
    public void Acknowledge(string jobId)
    {
        JobEntry? released = null;
        lock (_gate)
        {
            if (_jobs.TryGetValue(jobId, out var job) && job.IsTerminal)
            {
                _jobs.Remove(jobId);
                released = job;
            }
        }
        released?.Cts.Dispose();
    }

    /// <summary>The instance and its whole XGP ledger, as they stand.</summary>
    public LedgerSnapshot Ledger()
    {
        lock (_gate)
            return new LedgerSnapshot(Instance, [.. _ledger]);
    }

    /// <summary>
    /// Settles an owed identity, in one step under the start lock: an identity
    /// never admitted is retired — it can never run, and the owed job is
    /// resolved — while one admitted, possibly after the page's last ledger
    /// read, stays the page's to follow. A settle naming another instance is
    /// refused.
    /// </summary>
    public SettleJudgement Settle(string jobId, string forInstance)
    {
        lock (_gate)
        {
            if (forInstance != Instance)
                return new SettleJudgement.Refused(RefusalReason.OtherInstance);
            if (_dispositions.TryGetValue(jobId, out var seen))
                return new SettleJudgement.Settled(seen is Disposition.Admitted ? SettleOutcome.Admitted : SettleOutcome.Retired);
            _dispositions.Add(jobId, Disposition.Retired.Value);
            return new SettleJudgement.Settled(SettleOutcome.Retired);
        }
    }

    /// <summary>A start disposition: the instance's judgement of one identity, kept for its lifetime.</summary>
    private abstract record Disposition
    {
        /// <summary>Admitted: the request a repeat is compared against, and its ledger entry for XGP.</summary>
        public sealed record Admitted(StartRequest Request, int? LedgerNumber) : Disposition;

        /// <summary>Retired: no start under the identity may ever run.</summary>
        public sealed record Retired : Disposition
        {
            public static Retired Value { get; } = new();
        }
    }
}

/// <summary>What <see cref="JobStore.Start"/> judged.</summary>
public abstract record StartJudgement
{
    /// <summary>Admitted: a new job (<paramref name="Started"/>), or a repeat of one, which started nothing.</summary>
    /// <param name="LedgerNumber">For an XGP job, its ledger entry's number.</param>
    /// <param name="Started">Whether this start registered the job, rather than repeating one already admitted.</param>
    public sealed record Admitted(int? LedgerNumber, bool Started) : StartJudgement;

    /// <summary>Refused, for <paramref name="Reason"/>; nothing started.</summary>
    public sealed record Refused(RefusalReason Reason) : StartJudgement;
}

/// <summary>What <see cref="JobStore.Settle"/> judged.</summary>
public abstract record SettleJudgement
{
    /// <summary>Settled, with <paramref name="Outcome"/>.</summary>
    public sealed record Settled(SettleOutcome Outcome) : SettleJudgement;

    /// <summary>Refused, for <paramref name="Reason"/>.</summary>
    public sealed record Refused(RefusalReason Reason) : SettleJudgement;
}

/// <summary>
/// One registered job: its identity, its XGP ledger entry's number, its latest
/// snapshot — replaced as the run reports, and final once terminal — and the
/// source that cancels it. <see cref="JobStore"/> owns every change of it.
/// </summary>
public sealed class JobEntry
{
    internal JobEntry(string jobId, int? ledgerNumber)
    {
        JobId = jobId;
        LedgerNumber = ledgerNumber;
    }

    /// <summary>The identity the browser minted for the job's start.</summary>
    public string JobId { get; }

    /// <summary>For an XGP job, its ledger entry's number; <see langword="null"/> for any other.</summary>
    public int? LedgerNumber { get; }

    /// <summary>The job's latest snapshot; its terminal result once <see cref="IsTerminal"/>.</summary>
    public ProcessingProgress Progress { get; internal set; } = new();

    /// <summary>Whether the job has reached its terminal result, which then stands.</summary>
    public bool IsTerminal { get; internal set; }

    /// <summary>Cancels the running job; disposed when its result is released.</summary>
    internal CancellationTokenSource Cts { get; } = new();

    /// <summary>The token the job's work runs under.</summary>
    public CancellationToken Cancellation => Cts.Token;
}
