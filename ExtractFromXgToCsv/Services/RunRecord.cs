using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// One Local-mode run's record: every unit it skipped, as the shared
/// <see cref="SkipRecord"/>, and, for an XGP run, what it has committed.
/// <see cref="LocalFolderProcessor"/> writes it — skips only through its one
/// skip seam — and every snapshot it reports carries it as it stands.
/// </summary>
/// <remarks>
/// <para>
/// A caller that must know what a run did after a failure has ended it passes
/// its own instance to the processor and reads it once the call has thrown.
/// The snapshots cannot serve that caller: a run-ending exception reports
/// nothing, and the pathways report only every tenth file, so the last
/// snapshot can trail the record. A typed handoff read after the throw needs
/// no extra catch in the pathways and no promise from
/// <see cref="IProgress{T}"/> about when a report is delivered. The job runner
/// is that caller: its terminal snapshots take both facts from here.
/// </para>
/// <para>
/// One instance per run (the processor rejects one that already holds
/// anything). Not thread-safe: the run writes on its own flow, and the caller
/// reads after the call has completed or thrown.
/// </para>
/// </remarks>
public sealed class RunRecord
{
    /// <summary>What the run has skipped so far, in order; immutable, replaced on each skip.</summary>
    public SkipRecord Skips { get; private set; } = SkipRecord.Empty;

    /// <summary>
    /// For an XGP run, the files it has committed so far and the options it
    /// names them by — set once its options have validated, replaced as each
    /// file is written; <see langword="null"/> for any other run.
    /// </summary>
    public XgpExportCommit? XgpCommit { get; private set; }

    /// <summary>Whether the run has recorded nothing yet.</summary>
    internal bool IsFresh => Skips.IsEmpty && XgpCommit is null;

    /// <summary>Records one skip. The processor's skip seam is the only caller.</summary>
    internal void Skip(SkippedItem item) => Skips = Skips.With(item);

    /// <summary>Records what the XGP run has committed, as it stands.</summary>
    internal void Commit(XgpExportCommit commit) => XgpCommit = commit;

    /// <summary>
    /// The terminal snapshot of a run that was cancelled: where it stood comes
    /// from <paramref name="lastReported"/>, its last report; what it skipped
    /// and committed comes from this record, which the last report can trail.
    /// An XGP run's row count is its commit's, so the line the client shows
    /// agrees with the accounting. A failure closing the output file the
    /// cancellation left unfinished is reported beside it
    /// (<paramref name="closeFailureMessage"/>); the run is still stopped.
    /// </summary>
    internal ProcessingProgress CancelledSnapshot(ProcessingProgress lastReported, string? closeFailureMessage = null)
    {
        ArgumentNullException.ThrowIfNull(lastReported);
        return new ProcessingProgress
        {
            Current = lastReported.Current,
            Total = lastReported.Total,
            FileName = lastReported.FileName,
            Phase = lastReported.Phase,
            TotalRows = XgpCommit?.Count ?? lastReported.TotalRows,
            ElapsedSec = lastReported.ElapsedSec,
            FilesPerSec = lastReported.FilesPerSec,
            Complete = true,
            Cancelled = true,
            CloseFailureMessage = closeFailureMessage,
            Skipped = Skips,
            XgpCommit = XgpCommit,
        };
    }

    /// <summary>
    /// The terminal snapshot of a run that failed with
    /// <paramref name="errorMessage"/>: its error, and what it skipped and
    /// committed before the failure, from this record.
    /// </summary>
    internal ProcessingProgress FailedSnapshot(string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(errorMessage);
        return new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = errorMessage,
            TotalRows = XgpCommit?.Count ?? 0,
            Skipped = Skips,
            XgpCommit = XgpCommit,
        };
    }
}
