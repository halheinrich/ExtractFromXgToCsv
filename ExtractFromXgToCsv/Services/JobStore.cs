using ExtractFromXgToCsv.Client.Shared;
using System.Collections.Concurrent;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Singleton in-memory registry of Local-mode processing jobs, keyed by jobId
/// (short GUID). A job's terminal snapshot is served exactly once: reading its
/// status via <see cref="ReadStatus"/> returns the current snapshot and, when
/// that snapshot is terminal (<see cref="ProcessingProgress.Complete"/> —
/// success, cancellation, and failure alike), removes the entry and disposes its
/// <see cref="CancellationTokenSource"/> after capturing the snapshot to return.
/// Non-terminal reads leave the entry untouched. Nothing is removed on the
/// background job's completion itself, so the polling client always consumes the
/// terminal state before the entry vanishes — the cleanup rides the read that
/// observes completion, not the completion.
/// </summary>
public class JobStore
{
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();

    /// <summary>Registers a fresh job and returns its id.</summary>
    public string CreateJob()
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        _jobs[jobId] = new JobEntry();
        return jobId;
    }

    /// <summary>
    /// The job entry for <paramref name="jobId"/>, or <see langword="null"/> if
    /// unknown. A raw accessor for wiring a running job (its progress sink and
    /// cancellation token); status reads go through <see cref="ReadStatus"/>,
    /// which self-cleans on the terminal snapshot.
    /// </summary>
    public JobEntry? Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var entry) ? entry : null;

    /// <summary>
    /// Returns the job's current progress snapshot, or <see langword="null"/> if
    /// the job is unknown (or was already served its terminal snapshot). When the
    /// snapshot is terminal (<see cref="ProcessingProgress.Complete"/>) it is
    /// captured, then the job is removed and its CTS disposed — so the terminal
    /// state is delivered exactly once and cleanup rides the read that observes it.
    /// </summary>
    public ProcessingProgress? ReadStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
            return null;

        var progress = entry.Progress;
        if (progress.Complete)
            Remove(jobId);
        return progress;
    }

    /// <summary>
    /// Requests cancellation of a running job. Returns <see langword="true"/> when
    /// a live job was signalled, <see langword="false"/> when the job is unknown or
    /// has already reached (and been cleaned up by) its terminal snapshot. Never
    /// throws for a late cancel: a race with terminal cleanup disposing the CTS
    /// no-ops rather than surfacing an <see cref="ObjectDisposedException"/>.
    /// </summary>
    public bool Cancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
            return false;

        try
        {
            entry.Cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Terminal cleanup disposed the CTS between the lookup above and
            // this call — the job is already done, so the cancel is moot.
            return false;
        }
    }

    /// <summary>
    /// Removes the job and disposes its <see cref="CancellationTokenSource"/>.
    /// Idempotent under concurrency: only the caller that wins the removal
    /// disposes, so concurrent terminal reads never double-dispose.
    /// </summary>
    public void Remove(string jobId)
    {
        if (_jobs.TryRemove(jobId, out var entry))
            entry.Cts.Dispose();
    }
}

/// <summary>
/// One registered job: its latest <see cref="ProcessingProgress"/> snapshot
/// (replaced as the run reports) and the <see cref="CancellationTokenSource"/>
/// that cancels it. <see cref="JobStore"/> owns the CTS lifecycle and disposes
/// it on terminal cleanup.
/// </summary>
public class JobEntry
{
    /// <summary>The job's latest progress snapshot; replaced as the run reports.</summary>
    public ProcessingProgress Progress { get; set; } = new();

    /// <summary>Cancels the running job. Disposed by <see cref="JobStore"/> on terminal cleanup.</summary>
    public CancellationTokenSource Cts { get; } = new();
}
