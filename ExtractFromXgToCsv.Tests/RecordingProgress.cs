using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The test project's one synchronous progress sink: each snapshot the
/// processor reports is recorded, in order, on the reporting thread, and then
/// handed to <c>onReport</c>, if any, inside <see cref="Report"/>.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts its callback — to the synchronization
/// context captured at its construction, or to the thread pool — and awaiting
/// the processor does not wait for what it posted, so a test reading what its
/// callbacks wrote can race a report still landing ("Collection was
/// modified"), and a test cancelling from a callback lets the processor run
/// past the point it cancels at. Here nothing is posted: when the processor's
/// call returns, every report has been recorded and every callback has run.
/// A callback's exception is not caught; it propagates out of
/// <see cref="Report"/> to the processor, as the call it made.
/// </remarks>
/// <param name="onReport">Runs on each snapshot after it is recorded; null for none.</param>
internal sealed class RecordingProgress(Action<ProcessingProgress>? onReport = null) : IProgress<ProcessingProgress>
{
    private readonly List<ProcessingProgress> _reports = [];

    /// <summary>Every snapshot reported, in the order reported.</summary>
    public IReadOnlyList<ProcessingProgress> Reports => _reports;

    /// <summary>The last snapshot reported; null before any.</summary>
    public ProcessingProgress? Last => _reports.Count == 0 ? null : _reports[^1];

    /// <inheritdoc />
    public void Report(ProcessingProgress value)
    {
        _reports.Add(value);
        onReport?.Invoke(value);
    }
}
