using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// One Local-mode run's skip record: every input the run skipped, in order.
/// <see cref="LocalFolderProcessor"/> writes it, only through its one skip
/// seam, and copies it onto each snapshot it reports.
/// </summary>
/// <remarks>
/// <para>
/// A caller that must know what was skipped after a failure has ended the run
/// passes its own instance to the processor and reads it once the call has
/// thrown. The snapshots cannot serve that caller: a run-ending exception
/// reports nothing, and the streaming pathways report only every tenth file,
/// so the last snapshot can trail the record. A typed handoff read after the
/// throw needs no extra catch in the pathways and no promise from
/// <see cref="IProgress{T}"/> about when a report is delivered.
/// </para>
/// <para>
/// One instance per run (the processor rejects one that already holds
/// entries). Not thread-safe: the run appends on its own flow, and the caller
/// reads after the call has completed or thrown.
/// </para>
/// </remarks>
public sealed class SkipRecord
{
    private readonly List<SkippedItem> _items = [];

    /// <summary>Whether nothing has been recorded.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>
    /// A copy of the record as it stands; later entries never reach a copy
    /// already taken, so a snapshot that carries it does not change.
    /// </summary>
    public IReadOnlyList<SkippedItem> Snapshot() => [.. _items];

    /// <summary>Appends one entry. The processor's skip seam is the only caller.</summary>
    internal void Add(SkippedItem item) => _items.Add(item);
}
