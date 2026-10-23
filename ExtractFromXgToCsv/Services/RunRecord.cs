using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// One Local-mode run's record: every unit it skipped, as the shared
/// <see cref="SkipRecord"/>; for an XGP run, what it has committed; and where
/// the run stands — its clock, the file it has reached, the rows it has
/// produced, and the one output file it has created.
/// <see cref="LocalFolderProcessor"/> writes it — skips only through its one
/// skip seam — and every snapshot it reports carries it as it stands.
/// </summary>
/// <remarks>
/// <para>
/// A caller that must know what a run did after a failure or a stop has ended
/// it passes its own instance to the processor and reads it once the call has
/// thrown. The snapshots cannot serve that caller: a run-ending exception
/// reports nothing, and the pathways report only every tenth file, so the
/// last snapshot can trail the record. A typed handoff read after the throw
/// needs no extra catch in the pathways and no promise from
/// <see cref="IProgress{T}"/> about when a report is delivered. The job runner
/// is that caller: its terminal snapshots are built here, from the record
/// alone.
/// </para>
/// <para>
/// One instance per run (the processor rejects one that already holds
/// anything). Not thread-safe: the run writes on its own flow, and the caller
/// reads after the call has completed or thrown.
/// </para>
/// </remarks>
public sealed class RunRecord
{
    private readonly TimeProvider _clock;

    // The clock's timestamp when the walk began; null before.
    private long? _began;

    // The one output file the run created, and how it is filled; null before
    // it is created, and on an XGP run, whose files its commit accounts for.
    private string? _outputPath;
    private OutputFill _outputFill;

    /// <summary>A record on the system clock.</summary>
    public RunRecord()
        : this(TimeProvider.System)
    {
    }

    /// <summary>A record whose elapsed time is read from <paramref name="clock"/> — a test's, to pin the figures at a stop.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    internal RunRecord(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>What the run has skipped so far, in order; immutable, replaced on each skip.</summary>
    public SkipRecord Skips { get; private set; } = SkipRecord.Empty;

    /// <summary>
    /// For an XGP run, the files it has committed so far and the options it
    /// names them by — set once its options have validated, replaced as each
    /// file is written; <see langword="null"/> for any other run.
    /// </summary>
    public XgpExportCommit? XgpCommit { get; private set; }

    /// <summary>The input files the run walks; zero until it begins.</summary>
    internal int Total { get; private set; }

    /// <summary>
    /// The file the run has reached, from 1: the one it is on, or, between
    /// files, the last it finished; zero before the first.
    /// </summary>
    internal int FileReached { get; private set; }

    /// <summary>The input files the run has finished — read and committed, or skipped.</summary>
    internal int FilesDone { get; private set; }

    /// <summary>
    /// What the run has produced so far: a CSV's rows written to its file,
    /// Diagram JSON records or deck decisions collected, XGP files written.
    /// </summary>
    internal int Rows { get; private set; }

    /// <summary>Seconds since the walk began, read now; zero before it began.</summary>
    internal double ElapsedSec => _began is { } began ? _clock.GetElapsedTime(began).TotalSeconds : 0;

    /// <summary>Whether the run has recorded nothing yet.</summary>
    internal bool IsFresh => Skips.IsEmpty && XgpCommit is null && _began is null && _outputPath is null;

    /// <summary>Records one skip. The processor's skip seam is the only caller.</summary>
    internal void Skip(SkippedItem item) => Skips = Skips.With(item);

    /// <summary>Records what the XGP run has committed, as it stands.</summary>
    internal void Commit(XgpExportCommit commit) => XgpCommit = commit;

    /// <summary>The walk begins, over <paramref name="total"/> files: the run's clock starts.</summary>
    /// <exception cref="InvalidOperationException">The walk has already begun: a record belongs to one run.</exception>
    internal void Begin(int total)
    {
        if (_began is not null)
            throw new InvalidOperationException("The run's walk has already begun.");
        Total = total;
        _began = _clock.GetTimestamp();
    }

    /// <summary>The walk is between files, <paramref name="done"/> of them finished: it has reached the last of them.</summary>
    internal void BetweenFiles(int done) => FileReached = FilesDone = done;

    /// <summary>The walk takes up file <paramref name="number"/>, from 1.</summary>
    internal void TakeUpFile(int number) => FileReached = number;

    /// <summary>Counts <paramref name="count"/> more rows, records, decisions or files produced.</summary>
    internal void Add(int count) => Rows += count;

    /// <summary>
    /// The run created its one output file at <paramref name="path"/> — the
    /// user's path, or the next free name beside it — to be filled as
    /// <paramref name="fill"/> says.
    /// </summary>
    internal void OutputCreated(string path, OutputFill fill)
    {
        _outputPath = path;
        _outputFill = fill;
    }

    /// <summary>
    /// The terminal snapshot of a run that was stopped, from this record
    /// alone, never from a progress report: the file it reached, the elapsed
    /// time and throughput at the stop, what it skipped and committed, and
    /// what it left at its output (<see cref="ProcessingProgress.StoppedOutput"/>).
    /// An XGP run's count is its commit's, so the line the client shows agrees
    /// with the accounting. A failure closing the output file the stop left
    /// unfinished is reported beside it (<paramref name="closeFailureMessage"/>);
    /// the run is still stopped, and what the file holds is then uncertain.
    /// </summary>
    internal ProcessingProgress CancelledSnapshot(string? closeFailureMessage = null)
    {
        var elapsed = ElapsedSec;
        return new ProcessingProgress
        {
            Current = FileReached,
            Total = Total,
            TotalRows = XgpCommit?.Count ?? Rows,
            ElapsedSec = elapsed,
            FilesPerSec = elapsed > 0 ? (int)(FilesDone / elapsed) : 0,
            Complete = true,
            Cancelled = true,
            CloseFailureMessage = closeFailureMessage,
            StoppedOutput = _outputPath is null
                ? null
                // An appended file that closed holds every row counted: a
                // file's rows are committed whole. A file written whole after
                // the walk was stopped during that write, so it is never
                // known to be whole; nor is any file whose close failed.
                : new StoppedOutput(_outputPath, _outputFill == OutputFill.Whole || closeFailureMessage is not null),
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

/// <summary>How a CSV, Diagram JSON or deck run fills its one output file.</summary>
internal enum OutputFill
{
    /// <summary>A CSV: each file's rows are appended as the file commits, so a closed file holds every row counted.</summary>
    Appended,

    /// <summary>Diagram JSON, PPTX or PDF: written whole once every file is read, so it holds the output only once written and closed.</summary>
    Whole,
}
