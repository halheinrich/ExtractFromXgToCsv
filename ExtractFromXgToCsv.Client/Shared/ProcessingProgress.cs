namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Progress snapshot for a Local-mode processing job, polled by the client via
/// <c>GET /api/process/{jobId}/status</c> once per second. Lives in the Client
/// project so both server and client code share the one wire shape.
/// </summary>
/// <remarks>
/// Immutable: every property is set when the snapshot is made, and a changed
/// snapshot is a new one (<c>with</c>). So the job store can keep the snapshot
/// a run reported and serve it to every status read, and no holder — the run,
/// the store, a reader — can change what another reads, inside the store's
/// lock or out of it.
/// </remarks>
public sealed record ProcessingProgress
{
    /// <summary>1-based index of the file currently being processed; on a stopped snapshot, the file the run reached.</summary>
    public int Current { get; init; }

    /// <summary>Total number of input files discovered for the run.</summary>
    public int Total { get; init; }

    /// <summary>Name of the file being processed, or a terminal marker such as <c>"Done"</c>.</summary>
    /// <remarks>
    /// Presentation only — a line to show the user. Which stage the run is in is
    /// <see cref="Phase"/>; never re-derive it by inspecting this string.
    /// </remarks>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Which stage of the run this snapshot describes. Drives the client's
    /// choice of a determinate or indeterminate busy affordance — see
    /// <see cref="JobPhase"/>.
    /// </summary>
    public JobPhase Phase { get; init; } = JobPhase.Processing;

    /// <summary>Rows/decisions written so far; on a terminal snapshot, the run's final count.</summary>
    public int TotalRows { get; init; }

    /// <summary>Whether the job has reached a terminal state (success, cancellation, or failure).</summary>
    public bool Complete { get; init; }

    /// <summary>Whether the terminal state was a cancellation.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Wall-clock seconds elapsed since the run started.</summary>
    public double ElapsedSec { get; init; }

    /// <summary>Throughput, in input files processed per second.</summary>
    public int FilesPerSec { get; init; }

    /// <summary>
    /// On a successful terminal snapshot, where the run wrote its output, as
    /// the code that created it reports it (the umbrella's
    /// <c>SPEC-notices.md</c> §7.5): for a CSV, Diagram JSON, PPTX or PDF run,
    /// the one file's path — the path the user named, or the next free name
    /// beside it where a file of that name existed; for an XGP run, the
    /// output folder. <see langword="null"/> on every other snapshot: it is
    /// reported only once the output is written and closed.
    /// </summary>
    /// <remarks>
    /// An output fact, never the request's path: the client shows it as it
    /// is, and neither rebuilds a numbered name nor looks for the file.
    /// </remarks>
    public string? WrittenPath { get; init; }

    /// <summary>Failure message; non-null only on the terminal error state.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// On a cancelled terminal snapshot, a failure closing the output file the
    /// cancellation left unfinished, as the run reports it: beside the
    /// cancellation, which stands, never in its place. <see langword="null"/>
    /// otherwise; a failed run reports such a failure within
    /// <see cref="ErrorMessage"/>.
    /// </summary>
    public string? CloseFailureMessage { get; init; }

    /// <summary>
    /// On a cancelled terminal snapshot of a CSV, Diagram JSON, PPTX or PDF
    /// run, the output file the run had created when it stopped, from the
    /// run's record; <see langword="null"/> when it stopped before creating
    /// one — nothing written — and on every other snapshot. An XGP run's
    /// files are its <see cref="XgpCommit"/>'s.
    /// </summary>
    public StoppedOutput? StoppedOutput { get; init; }

    /// <summary>
    /// Every unit the run has skipped so far, in the order skipped — a file it
    /// could not read or convert, or a decision whose payload it could not
    /// prepare; on a terminal snapshot, the run's whole record. Empty when
    /// nothing was skipped.
    /// </summary>
    /// <remarks>
    /// A skip never ends a run — one bad file must not kill a batch — so this is
    /// the only place a dropped input shows. A <see cref="SkipRecord"/> is
    /// immutable, so no holder can change the record another reads (the
    /// collection rider of halheinrich/backgammon#273), and its counts are
    /// derived from its entries.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Set to <see langword="null"/>.</exception>
    public SkipRecord Skipped
    {
        get => _skipped;
        init => _skipped = value ?? throw new ArgumentNullException(nameof(value));
    }

    private SkipRecord _skipped = SkipRecord.Empty;

    /// <summary>
    /// For an XGP run, what it has committed so far — the files written to
    /// the output folder, and the naming options it captured; on a terminal
    /// snapshot, completed, cancelled or failed alike, the run's whole commit.
    /// <see langword="null"/> for every other format.
    /// </summary>
    /// <remarks>
    /// The authoritative accounting the client advances its numbering from,
    /// never reconstructed from <see cref="TotalRows"/> or any other progress
    /// figure: the job runner takes it from the run's own record, so a run that
    /// ends between two progress reports, or by an exception, still accounts
    /// for every file it wrote.
    /// </remarks>
    public XgpExportCommit? XgpCommit { get; init; }

    /// <summary>Percent complete, derived from <see cref="Current"/> over <see cref="Total"/>.</summary>
    public int PercentComplete => Total == 0 ? 0 : (int)((Current / (double)Total) * 100);
}
