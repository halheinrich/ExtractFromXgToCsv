using ExtractFromXgToCsv.Client.Shared;
using System.Collections.Concurrent;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// Singleton in-memory store of active processing jobs.
/// Keyed by jobId (short GUID). Jobs are cleaned up after completion.
/// </summary>
public class JobStore
{
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();

    public string CreateJob()
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        _jobs[jobId] = new JobEntry();
        return jobId;
    }

    public JobEntry? Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var entry) ? entry : null;

    public void Remove(string jobId) => _jobs.TryRemove(jobId, out _);
}

public class JobEntry
{
    public ProcessingProgress Progress { get; set; } = new();
    public CancellationTokenSource Cts { get; } = new();
}
