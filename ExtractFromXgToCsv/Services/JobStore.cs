using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// The server's Local-mode jobs (the umbrella's <c>SPEC-notices.md</c> §7.5):
/// the jobs and their terminal results, and the starts admitted, all under
/// one lock, so every judgement and every change of them is one step.
/// </summary>
/// <remarks>
/// <para>
/// <b>The job</b> is one Local run, in any format, and its terminal result.
/// Its identity is the one the browser minted for its start. It is kept until
/// the page that started it acknowledges it (<see cref="Acknowledge"/>), or the
/// process ends; a status read never removes it.
/// </para>
/// <para>
/// <b>The admitted starts</b>, one request for every identity the store has
/// admitted, are kept for the process's lifetime, so a repeated start finds
/// its job's admission after the job is acknowledged too. No acknowledgement
/// removes one, and no refusal changes one.
/// </para>
/// <para>
/// <b>Snapshots are immutable</b> (<see cref="ProcessingProgress"/>), so the
/// store keeps the one a run reported and serves it to every status read, as
/// it is: no holder can change one another holds, inside the lock or out of
/// it.
/// </para>
/// </remarks>
public sealed class JobStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, HeldJob> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StartRequest> _admitted = new(StringComparer.Ordinal);

    /// <summary>
    /// Judges a start by its identity and, for an unseen identity, registers
    /// the job in the same step, then hands the job to
    /// <paramref name="launch"/> to run.
    /// </summary>
    /// <remarks>
    /// Under the lock: an admitted identity returns its admission and starts
    /// nothing, unless the request differs, which is refused and changes
    /// nothing. An unseen identity is admitted.
    /// </remarks>
    public StartJudgement Start(StartRequest request, Action<JobEntry> launch)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(launch);

        JobEntry job;
        lock (_gate)
        {
            if (_admitted.TryGetValue(request.JobId, out var admitted))
            {
                return admitted == request
                    ? new StartJudgement.Admitted(Started: false)
                    : new StartJudgement.Refused(RefusalReason.ChangedRequest);
            }

            var held = new HeldJob(request.JobId);
            _jobs.Add(request.JobId, held);
            _admitted.Add(request.JobId, request);
            job = held.Entry;
        }

        launch(job);
        return new StartJudgement.Admitted(Started: true);
    }

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

    /// <summary>Records a job's terminal result. The first terminal result stands.</summary>
    public void Complete(JobEntry job, ProcessingProgress terminal)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(terminal);
        lock (_gate)
        {
            if (Held(job) is not { IsTerminal: false } held)
                return;
            held.Progress = terminal;
            held.IsTerminal = true;
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
    /// terminal result: the job's result is released. It never touches an
    /// admission. Acknowledging a job the store no longer holds — or one still
    /// running, which has no result to release yet — changes nothing, so a
    /// retried acknowledgement is harmless.
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
        public HeldJob(string jobId) => Entry = new JobEntry(jobId, Cts.Token);

        public CancellationTokenSource Cts { get; } = new();

        public JobEntry Entry { get; }

        public ProcessingProgress Progress { get; set; } = new();

        public bool IsTerminal { get; set; }
    }
}

/// <summary>What <see cref="JobStore.Start"/> judged.</summary>
public abstract record StartJudgement
{
    /// <summary>Admitted: a new job (<paramref name="Started"/>), or a repeat of one, which started nothing.</summary>
    /// <param name="Started">Whether this start registered the job, rather than repeating one already admitted.</param>
    public sealed record Admitted(bool Started) : StartJudgement;

    /// <summary>Refused, for <paramref name="Reason"/>; nothing started.</summary>
    public sealed record Refused(RefusalReason Reason) : StartJudgement;
}

/// <summary>
/// One registered job, as its runner holds it: its identity and the token its
/// work runs under. Its progress, its terminal result and its cancellation
/// belong to the <see cref="JobStore"/>, which changes them under its lock.
/// This handle changes nothing.
/// </summary>
public sealed class JobEntry
{
    internal JobEntry(string jobId, CancellationToken cancellation)
    {
        JobId = jobId;
        Cancellation = cancellation;
    }

    /// <summary>The identity the browser minted for the job's start.</summary>
    public string JobId { get; }

    /// <summary>The token the job's work runs under.</summary>
    public CancellationToken Cancellation { get; }
}
