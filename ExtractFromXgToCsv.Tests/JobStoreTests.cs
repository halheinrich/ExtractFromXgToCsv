using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the JobStore cleanup contract: a terminal snapshot is served exactly
/// once, and serving it removes the job and disposes its CTS. These are the
/// invariants the polling client relies on — it must observe the terminal
/// snapshot (Done line, XGP counter advance) before the entry vanishes, which
/// holds because cleanup rides the read that delivers completion, not the
/// background job's completion. Deterministic: no timers, no wall-clock.
/// </summary>
public class JobStoreTests
{
    [Fact]
    public void ReadStatus_UnknownJob_ReturnsNull()
    {
        var store = new JobStore();
        Assert.Null(store.ReadStatus("nope"));
    }

    [Fact]
    public void ReadStatus_NonTerminalSnapshot_LeavesEntryAndCtsAlive()
    {
        var store = new JobStore();
        var jobId = store.CreateJob();
        var entry = store.Get(jobId)!;
        entry.Progress = new ProcessingProgress { Current = 3, Total = 10 };

        var snapshot = store.ReadStatus(jobId);

        Assert.Same(entry.Progress, snapshot);
        Assert.NotNull(store.Get(jobId));            // entry untouched
        _ = entry.Cts.Token;                          // CTS not disposed (Token getter would throw)
    }

    [Fact]
    public void ReadStatus_TerminalSnapshot_RemovesEntryAndDisposesCts()
    {
        var store = new JobStore();
        var jobId = store.CreateJob();
        var entry = store.Get(jobId)!;
        var terminal = new ProcessingProgress { Complete = true, TotalRows = 7 };
        entry.Progress = terminal;

        var snapshot = store.ReadStatus(jobId);

        Assert.Same(terminal, snapshot);              // terminal state still delivered
        Assert.Null(store.Get(jobId));                // …and the entry is gone
        Assert.Throws<ObjectDisposedException>(() => _ = entry.Cts.Token); // CTS disposed
    }

    public static IEnumerable<object[]> TerminalSnapshots() =>
    [
        [new ProcessingProgress { Complete = true, TotalRows = 5 }],                   // success
        [new ProcessingProgress { Complete = true, Cancelled = true, TotalRows = 3 }], // cancellation
        [new ProcessingProgress { Complete = true, ErrorMessage = "boom" }],           // failure
    ];

    [Theory]
    [MemberData(nameof(TerminalSnapshots))]
    public void ReadStatus_AnyTerminalShape_CleansUp(ProcessingProgress terminal)
    {
        // All three terminal shapes are distinguished only by Cancelled /
        // ErrorMessage; Complete is the sole terminal signal, so each must clean
        // up. Guards against an impl that only self-cleans on a plain success.
        var store = new JobStore();
        var jobId = store.CreateJob();
        var entry = store.Get(jobId)!;
        entry.Progress = terminal;

        var snapshot = store.ReadStatus(jobId);

        Assert.Same(terminal, snapshot);
        Assert.Null(store.Get(jobId));
        Assert.Throws<ObjectDisposedException>(() => _ = entry.Cts.Token);
    }

    [Fact]
    public void Cancel_LiveJob_SignalsTokenAndReturnsTrue()
    {
        var store = new JobStore();
        var jobId = store.CreateJob();
        var token = store.Get(jobId)!.Cts.Token;

        Assert.True(store.Cancel(jobId));
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_AfterTerminalCleanup_NoOps()
    {
        var store = new JobStore();
        var jobId = store.CreateJob();
        store.Get(jobId)!.Progress = new ProcessingProgress { Complete = true };

        store.ReadStatus(jobId);                      // terminal read removes + disposes

        // Late cancel must no-op — not throw ObjectDisposedException — and report
        // that no live job was signalled.
        Assert.False(store.Cancel(jobId));
    }

    [Fact]
    public void Cancel_UnknownJob_ReturnsFalse()
    {
        var store = new JobStore();
        Assert.False(store.Cancel("nope"));
    }
}
