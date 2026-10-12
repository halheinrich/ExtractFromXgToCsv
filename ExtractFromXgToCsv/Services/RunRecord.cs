using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Services;

/// <summary>
/// One Local-mode run's record: every unit it skipped, as the shared
/// <see cref="SkipRecord"/>. <see cref="LocalFolderProcessor"/> writes it, only
/// through its one skip seam, and every snapshot it reports carries the record
/// as it stands.
/// </summary>
/// <remarks>
/// <para>
/// A caller that must know what a run did after a failure has ended it passes
/// its own instance to the processor and reads it once the call has thrown.
/// The snapshots cannot serve that caller: a run-ending exception reports
/// nothing, and the streaming pathways report only every tenth file, so the last
/// snapshot can trail the record. A typed handoff read after the throw needs
/// no extra catch in the pathways and no promise from
/// <see cref="IProgress{T}"/> about when a report is delivered.
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

    /// <summary>Whether the run has recorded nothing yet.</summary>
    internal bool IsFresh => Skips.IsEmpty;

    /// <summary>Records one skip. The processor's skip seam is the only caller.</summary>
    internal void Skip(SkippedItem item) => Skips = Skips.With(item);
}
