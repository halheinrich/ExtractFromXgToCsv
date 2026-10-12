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
/// admitted, settled or refused at admission, are kept for its lifetime:
/// <i>admitted</i>, holding the request a repeat is compared against, or
/// <i>retired</i>, meaning no start under that identity may ever run. No
/// acknowledgement removes one, and no refusal changes one.
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
    private readonly Dictionary<string, HeldJob> _jobs = new(StringComparer.Ordinal);
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
    /// In order, under the lock: a start naming another instance is refused,
    /// and changes nothing in this one. Then its identity: an admitted one
    /// returns its disposition and starts nothing, unless the request differs,
    /// which is refused; a retired one is refused. No refusal changes a
    /// disposition an identity already has. Only an unseen identity goes on to
    /// admission, which for XGP is the ledger's: refused while any entry runs,
    /// and unless the start's mark names the latest entry or the instance has
    /// admitted none. A start refused at admission starts nothing, and its
    /// identity is retired: no start under it may ever run, and settling it
    /// answers retired.
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

            if (AdmissionRefusal(request) is { } refusal)
            {
                _dispositions.Add(request.JobId, Disposition.Retired.Value);
                return new StartJudgement.Refused(refusal);
            }

            int? ledgerNumber = null;
            if (request.Run.OutputFormat == OutputFormat.Xgp)
            {
                ledgerNumber = _ledger.Count + 1;
                _ledger.Add(new LedgerEntry(ledgerNumber.Value, request.JobId, Running: true, Commit: null));
            }

            var held = new HeldJob(request.JobId, ledgerNumber);
            _jobs.Add(request.JobId, held);
            _dispositions.Add(request.JobId, new Disposition.Admitted(request, ledgerNumber));
            job = held.Entry;
        }

        launch(job);
        return new StartJudgement.Admitted(job.LedgerNumber, Started: true);
    }

    // Why admission refuses an unseen identity's start; null when it admits
    // it. Other formats are not admitted by the ledger: they consume no
    // numbers. Called under the lock.
    private RefusalReason? AdmissionRefusal(StartRequest request) =>
        request.Run.OutputFormat != OutputFormat.Xgp ? null
        : _ledger.Any(entry => entry.Running) ? RefusalReason.XgpRunning
        : _ledger.Count > 0 && request.Mark != new LedgerMark(Instance, _ledger.Count) ? RefusalReason.StaleMark
        : null;

    /// <summary>Records a running job's progress; a job that has reached its terminal result keeps it.</summary>
    public void Report(JobEntry job, ProcessingProgress snapshot)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (Held(job) is { IsTerminal: false } held)
                held.Progress = snapshot;
        }
    }

    /// <summary>
    /// Records a job's terminal result and, for an XGP job, ends its ledger
    /// entry with the accounting the result carries — in one step. The first
    /// terminal result stands.
    /// </summary>
    public void Complete(JobEntry job, ProcessingProgress terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        Complete(job, _ => terminal);
    }

    /// <summary>
    /// Records a job's terminal result built from the last snapshot it
    /// reported, as <see cref="Complete(JobEntry, ProcessingProgress)"/> does,
    /// in the same step, so no report can land between the two.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="terminalFrom">Builds the terminal result from the job's last reported snapshot.</param>
    public void Complete(JobEntry job, Func<ProcessingProgress, ProcessingProgress> terminalFrom)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(terminalFrom);
        lock (_gate)
        {
            if (Held(job) is not { IsTerminal: false } held)
                return;
            var terminal = terminalFrom(held.Progress);
            held.Progress = terminal;
            held.IsTerminal = true;
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
            return _jobs.TryGetValue(jobId, out var held) ? held.Progress : null;
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
            if (!_jobs.TryGetValue(jobId, out var held) || held.IsTerminal)
                return false;
            cts = held.Cts;
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
        HeldJob? released = null;
        lock (_gate)
        {
            if (_jobs.TryGetValue(jobId, out var held) && held.IsTerminal)
            {
                _jobs.Remove(jobId);
                released = held;
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

    // The store's own record of a job it registered; null once the job's
    // result is released. Called under the lock.
    private HeldJob? Held(JobEntry job) =>
        _jobs.TryGetValue(job.JobId, out var held) && ReferenceEquals(held.Entry, job) ? held : null;

    /// <summary>
    /// A job as the store holds it: the runner's handle, the latest snapshot,
    /// which is the terminal result once <see cref="IsTerminal"/> is set, and
    /// the source that cancels it. Private, so only the store changes it,
    /// under its lock.
    /// </summary>
    private sealed class HeldJob
    {
        public HeldJob(string jobId, int? ledgerNumber) => Entry = new JobEntry(jobId, ledgerNumber, Cts.Token);

        public CancellationTokenSource Cts { get; } = new();

        public JobEntry Entry { get; }

        public ProcessingProgress Progress { get; set; } = new();

        public bool IsTerminal { get; set; }
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
/// One registered job, as its runner holds it: its identity, its XGP ledger
/// entry's number, and the token its work runs under. Its progress, its
/// terminal result and its cancellation belong to the <see cref="JobStore"/>,
/// which changes them under its lock. This handle changes nothing.
/// </summary>
public sealed class JobEntry
{
    internal JobEntry(string jobId, int? ledgerNumber, CancellationToken cancellation)
    {
        JobId = jobId;
        LedgerNumber = ledgerNumber;
        Cancellation = cancellation;
    }

    /// <summary>The identity the browser minted for the job's start.</summary>
    public string JobId { get; }

    /// <summary>For an XGP job, its ledger entry's number; <see langword="null"/> for any other.</summary>
    public int? LedgerNumber { get; }

    /// <summary>The token the job's work runs under.</summary>
    public CancellationToken Cancellation { get; }
}
