using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The application's <see cref="XgSourceConverter"/>, wrapped so a test can
/// see and steer Web mode's conversions: it counts every call per
/// representation, can fail a chosen file's conversion, can rewrite a file's
/// rows, and can hold one records conversion pending — the seam's purpose
/// (<see cref="ISourceConverter"/>). Everything it does not steer is the real
/// conversion over the real decoded file.
/// </summary>
internal sealed class ScriptedConverter : ISourceConverter
{
    private readonly XgSourceConverter _real = new();
    private readonly object _gate = new();
    private int _recordsCalls;
    private int _holdRecordsCall = int.MaxValue;
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    /// Hold records conversion number <paramref name="call"/> (1-based) pending
    /// until <see cref="ReleaseRecords"/>; <see cref="RecordsHeld"/> completes
    /// when it arrives.
    /// </summary>
    public void HoldRecordsCall(int call)
    {
        lock (_gate)
        {
            _holdRecordsCall = call;
            _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Completes when the held records conversion has arrived.</summary>
    public Task RecordsHeld => _held.Task;

    /// <summary>Lets the held records conversion finish.</summary>
    public void ReleaseRecords() => _release.TrySetResult();

    public async Task<IReadOnlyList<DecisionRow>> RowsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken)
    {
        RowsCalls.Add((source.Name, book));
        if (FailRows(source, book) is { } failure)
            throw failure;
        return RewriteRows(source, await _real.RowsAsync(source, ranking, book, cancellationToken));
    }

    public async Task<IReadOnlyList<BgDecisionData>> RecordsAsync(
        DecodedSource source, PlayRanking ranking, OpeningBook? book, CancellationToken cancellationToken)
    {
        RecordsCalls.Add((source.Name, book));
        Task? hold = null;
        lock (_gate)
        {
            if (++_recordsCalls == _holdRecordsCall)
            {
                _held.TrySetResult();
                hold = _release.Task;
            }
        }
        if (hold is not null)
            await hold;
        if (FailRecords(source, book) is { } failure)
            throw failure;
        var records = await _real.RecordsAsync(source, ranking, book, cancellationToken);
        RecordsResults.Add((book, records));
        return records;
    }

    /// <summary>What each records conversion that finished returned, with its book, in the order they finished.</summary>
    public List<(OpeningBook? Book, IReadOnlyList<BgDecisionData> Records)> RecordsResults { get; } = [];
}
