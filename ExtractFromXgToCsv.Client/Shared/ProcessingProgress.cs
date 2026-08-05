namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// Progress snapshot for a Local-mode processing job, polled by the client via
/// <c>GET /api/process/{jobId}/status</c> once per second. Lives in the Client
/// project so both server and client code share the one wire shape.
/// </summary>
public class ProcessingProgress
{
    /// <summary>1-based index of the file currently being processed.</summary>
    public int Current { get; set; }

    /// <summary>Total number of input files discovered for the run.</summary>
    public int Total { get; set; }

    /// <summary>Name of the file being processed, or a terminal marker such as <c>"Done"</c>.</summary>
    /// <remarks>
    /// Presentation only — a line to show the user. Which stage the run is in is
    /// <see cref="Phase"/>; never re-derive it by inspecting this string.
    /// </remarks>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Which stage of the run this snapshot describes. Drives the client's
    /// choice of a determinate or indeterminate busy affordance — see
    /// <see cref="JobPhase"/>.
    /// </summary>
    public JobPhase Phase { get; set; } = JobPhase.Processing;

    /// <summary>Rows/decisions written so far; on a terminal snapshot, the run's final count.</summary>
    public int TotalRows { get; set; }

    /// <summary>Whether the job has reached a terminal state (success, cancellation, or failure).</summary>
    public bool Complete { get; set; }

    /// <summary>Whether the terminal state was a cancellation.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Wall-clock seconds elapsed since the run started.</summary>
    public double ElapsedSec { get; set; }

    /// <summary>Throughput, in input files processed per second.</summary>
    public int FilesPerSec { get; set; }

    /// <summary>Failure message; non-null only on the terminal error state.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Percent complete, derived from <see cref="Current"/> over <see cref="Total"/>.</summary>
    public int PercentComplete => Total == 0 ? 0 : (int)((Current / (double)Total) * 100);
}
