using System.Collections.Immutable;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Home's numbering record as its one owner keeps it (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): this page's record, and every write of it.
/// Home owns one, and nothing else writes the record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always whole, from a fresh read, in one write.</b> A checkpoint reads
/// the stored record afresh, merges it with this page's, applies its change,
/// and writes the result as one document. The numbering, the mark and the
/// owed jobs are saved together or not at all.
/// </para>
/// <para>
/// <b>A stored record never undoes what this page has applied or
/// resolved.</b> The numbering and the mark come from whichever record's
/// mark is newer (<see cref="ExtractFromXgToCsv.Client.Shared.LedgerMark.IsNewerThan"/>);
/// with neither newer, the stored numbering — another page may have exported
/// since — unless this page holds a change storage refused. The owed jobs are
/// the stored ones and this page's together, less those this page has
/// resolved, so another page's owed job is never dropped and a job this page
/// resolved is never owed again.
/// </para>
/// <para>
/// <b>A refusal never stops the gesture.</b> When storage refuses a
/// checkpoint, the change stands in this page's record, and export goes on
/// (the storage condition reports the refusal). A job durably recorded as
/// owed stays owed in storage until a later checkpoint succeeds.
/// </para>
/// <para>
/// Browser storage offers no compare-and-swap across tabs, so two pages
/// writing at once can lose one write; §7.5 states that limit.
/// </para>
/// </remarks>
internal sealed class XgpNumberingKeeper(XgpNumberingStore store)
{
    // The owed jobs this page has resolved: never owed again here, whatever a
    // stored record still says.
    private readonly HashSet<string> _resolved = new(StringComparer.Ordinal);

    // Whether this page's record holds a change storage refused, which a
    // stored record must not undo.
    private bool _unsaved;

    /// <summary>This page's record.</summary>
    public XgpNumberingRecord Record { get; private set; } = XgpNumberingRecord.Empty;

    /// <summary>Takes the record a page starts from: the stored one, or the one migrated from the keys it replaces.</summary>
    public async Task RestoreAsync() => Record = await store.RestoreAsync();

    /// <summary>
    /// Reads the stored record afresh and merges it into this page's, never
    /// undoing what this page has applied or resolved.
    /// </summary>
    /// <param name="currentInstance">The server's instance as this page knows it; <see langword="null"/> when unknown.</param>
    public async Task RefreshAsync(string? currentInstance) =>
        Record = Merge(await store.ReadAsync(), currentInstance);

    /// <summary>
    /// One checkpoint: the stored record read afresh and merged with this
    /// page's, <paramref name="change"/> applied, and the result written whole
    /// in one storage write. The result is this page's record whether or not
    /// storage stored it.
    /// </summary>
    /// <param name="change">The change, applied to the merged record.</param>
    /// <param name="currentInstance">The server's instance as this page knows it; <see langword="null"/> when unknown.</param>
    /// <returns>Whether storage stored the record.</returns>
    public async Task<bool> CheckpointAsync(Func<XgpNumberingRecord, XgpNumberingRecord> change, string? currentInstance)
    {
        ArgumentNullException.ThrowIfNull(change);

        var merged = Merge(await store.ReadAsync(), currentInstance);
        var changed = change(merged);
        foreach (var job in merged.Owed)
        {
            if (!changed.Owed.Any(kept => kept.JobId == job.JobId))
                _resolved.Add(job.JobId);
        }
        Record = changed;

        var stored = await store.WriteAsync(changed);
        _unsaved = !stored;
        return stored;
    }

    private XgpNumberingRecord Merge(XgpNumberingRecord? stored, string? currentInstance)
    {
        var page = Record;
        if (stored is null)
            return page;

        var (mark, numbering) =
            stored.Mark is { } storedMark && storedMark.IsNewerThan(page.Mark, currentInstance) ? (stored.Mark, stored.Numbering)
            : page.Mark is { } pageMark && pageMark.IsNewerThan(stored.Mark, currentInstance) ? (page.Mark, page.Numbering)
            : (page.Mark ?? stored.Mark, _unsaved ? page.Numbering : stored.Numbering);

        var owed = stored.Owed
            .Concat(page.Owed.Where(job => !stored.Owed.Any(other => other.JobId == job.JobId)))
            .Where(job => !_resolved.Contains(job.JobId))
            .ToImmutableArray();

        return new XgpNumberingRecord { Numbering = numbering, Mark = mark, Owed = owed };
    }
}
