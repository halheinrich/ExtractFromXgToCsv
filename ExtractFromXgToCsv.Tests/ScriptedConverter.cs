using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The application's <see cref="XgSourceConverter"/>, wrapped so a test can
/// see and steer Web mode's conversions: it counts every call per
/// representation, can fail a chosen file's conversion, can rewrite a file's
/// rows, and can hold one rows or records conversion pending — the seam's
/// purpose (<see cref="ISourceConverter"/>). Everything it does not steer is
/// the real conversion over the real decoded file.
/// </summary>
internal sealed class ScriptedConverter : ISourceConverter
{
    private readonly XgSourceConverter _real = new();
    private readonly CallHold _rowsHold = new();
    private readonly CallHold _recordsHold = new();

    /// <summary>Every rows conversion, in order, by file name and the book it ran under.</summary>
    public List<(string File, OpeningBook? Book)> RowsCalls { get; } = [];

    /// <summary>Every records conversion, in order, by file name and the book it ran under.</summary>
    public List<(string File, OpeningBook? Book)> RecordsCalls { get; } = [];

    /// <summary>Fails a file's rows conversion when it returns an exception for it.</summary>
    public Func<DecodedSource, OpeningBook?, Exception?> FailRows { get; set; } = (_, _) => null;

    /// <summary>Fails a file's records conversion when it returns an exception for it.</summary>
    public Func<DecodedSource, OpeningBook?, Exception?> FailRecords { get; set; } = (_, _) => null;

    /// <summary>Rewrites a file's converted rows.</summary>
    public Func<DecodedSource, IReadOnlyList<DecisionRow>, IReadOnlyList<DecisionRow>> RewriteRows { get; set; } = (_, rows) => rows;

    /// <summary>
    /// Hold rows conversion number <paramref name="call"/> (1-based, counted
    /// from the first rows call) pending until <see cref="ReleaseRows"/>;
    /// <see cref="RowsHeld"/> completes when it arrives.
    /// </summary>
    public void HoldRowsCall(int call) => _rowsHold.Hold(call);

    /// <summary>Completes when the held rows conversion has arrived.</summary>
    public Task RowsHeld => _rowsHold.Held;

    /// <summary>Lets the held rows conversion finish.</summary>
    public void ReleaseRows() => _rowsHold.Release();

    /// <summary>
    /// Hold records conversion number <paramref name="call"/> (1-based) pending
    /// until <see cref="ReleaseRecords"/>; <see cref="RecordsHeld"/> completes
    /// when it arrives.
    /// </summary>
    public void HoldRecordsCall(int call) => _recordsHold.Hold(call);

    /// <summary>Completes when the held records conversion has arrived.</summary>
    public Task RecordsHeld => _recordsHold.Held;

    /// <summary>Lets the held records conversion finish.</summary>
    public void ReleaseRecords() => _recordsHold.Release();

    public async Task<IReadOnlyList<DecisionRow>> RowsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken)
    {
        RowsCalls.Add((source.Name, book));
        await _rowsHold.ArriveAsync();
        if (FailRows(source, book) is { } failure)
            throw failure;
        return RewriteRows(source, await _real.RowsAsync(source, ranking, book, cancellationToken));
    }

    public async Task<IReadOnlyList<BgDecisionData>> RecordsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken)
    {
        RecordsCalls.Add((source.Name, book));
        await _recordsHold.ArriveAsync();
        if (FailRecords(source, book) is { } failure)
            throw failure;
        var records = await _real.RecordsAsync(source, ranking, book, cancellationToken);
        RecordsResults.Add((book, records));
        return records;
    }

    /// <summary>What each records conversion that finished returned, with its book, in the order they finished.</summary>
    public List<(OpeningBook? Book, IReadOnlyList<BgDecisionData> Records)> RecordsResults { get; } = [];

    /// <summary>One representation's calls, and the one call among them held pending.</summary>
    private sealed class CallHold
    {
        private readonly object _gate = new();
        private int _calls;
        private int _holdCall = int.MaxValue;
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Held => _held.Task;

        public void Hold(int call)
        {
            lock (_gate)
            {
                _holdCall = call;
                _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Release() => _release.TrySetResult();

        /// <summary>Counts a call; the held one waits here until released.</summary>
        public Task ArriveAsync()
        {
            lock (_gate)
            {
                if (++_calls != _holdCall)
                    return Task.CompletedTask;
                _held.TrySetResult();
                return _release.Task;
            }
        }
    }
}
