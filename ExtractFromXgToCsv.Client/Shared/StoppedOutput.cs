using System.Text.Json.Serialization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The one output file a stopped CSV, Diagram JSON, PPTX or PDF run had
/// created when it stopped, as the run's record holds it: the path it was
/// created at, and whether what it holds is known.
/// </summary>
/// <remarks>
/// A stopped run that created no file has none (nothing written), and an XGP
/// run's files are its <see cref="XgpExportCommit"/>'s to account for.
/// </remarks>
public sealed record StoppedOutput
{
    /// <summary>A stopped run's output file at <paramref name="path"/>.</summary>
    /// <param name="path">The path the run created it at: the user's, or the next free name beside it.</param>
    /// <param name="mayBeIncomplete">Whether what the file holds is uncertain (<see cref="MayBeIncomplete"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    [JsonConstructor]
    public StoppedOutput(string path, bool mayBeIncomplete)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
        MayBeIncomplete = mayBeIncomplete;
    }

    /// <summary>The path the run created the file at, as the run reports it: shown as it is, never rebuilt.</summary>
    public string Path { get; }

    /// <summary>
    /// <see langword="true"/> when the run was stopped while writing the file,
    /// or closing it failed, so what it holds is uncertain;
    /// <see langword="false"/> for a CSV that closed after the stop, which
    /// holds exactly the rows its snapshot counts.
    /// </summary>
    public bool MayBeIncomplete { get; }
}
