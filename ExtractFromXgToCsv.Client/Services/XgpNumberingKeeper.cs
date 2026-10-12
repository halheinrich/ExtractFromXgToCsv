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
/// <b>A record that cannot be read</b> is kept as it is (<see cref="IsUnreadable"/>):
/// neither migration nor a checkpoint replaces it, and XGP export waits on the
/// user. Their resolution (<see cref="ResolveUnreadableAsync"/>) authorizes
/// replacing that document with a fresh record, and nothing more.
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

    // The unreadable document this page last read and the user has not
    // resolved; null while there is none.
    private string? _unreadable;

    // The unreadable document the user resolved: replacing it is authorized,
    // so a read that finds it still stored reads as nothing stored. Null until
    // a resolution.
    private string? _replaceable;

    /// <summary>This page's record.</summary>
    public XgpNumberingRecord Record { get; private set; } = XgpNumberingRecord.Empty;

    /// <summary>
    /// Whether the stored record cannot be read and the user has not resolved
    /// it: it may hold owed exports, so XGP export is blocked, and nothing
    /// replaces the document until <see cref="ResolveUnreadableAsync"/>.
    /// </summary>
    public bool IsUnreadable => _unreadable is not null;

    /// <summary>
    /// Takes the record a page starts from: the stored one; while none is
    /// stored, the one migrated from the keys it replaces; under a refusal,
    /// nothing, the page working from memory; and when what is stored cannot
    /// be read, nothing either, the document kept and XGP blocked.
    /// </summary>
    public async Task RestoreAsync()
    {
        var read = await ReadAsync();
        if (read is NumberingRead.Missing && await store.MigrateAsync() is { } migrated)
        {
            Record = migrated.Record;
            _unsaved = !migrated.Stored;
        }
        else if (read is NumberingRead.Valid valid)
        {
            Record = valid.Record;
        }
    }

    /// <summary>
    /// Reads the stored record afresh and merges it into this page's, never
    /// undoing what this page has applied or resolved.
    /// </summary>
    /// <param name="currentInstance">The server's instance as this page knows it; <see langword="null"/> when unknown.</param>
    public async Task RefreshAsync(string? currentInstance)
    {
        if (await ReadAsync() is NumberingRead.Valid valid)
            Record = Merge(valid.Record, currentInstance);
    }

    /// <summary>
    /// The user's deliberate resolution of a record that cannot be read: it
    /// authorizes replacing that document with a fresh record, and nothing
    /// more. The fresh record is written as every record is, from a fresh
    /// read; whatever the page then may do is the ordinary admission's to
    /// decide.
    /// </summary>
    public async Task ResolveUnreadableAsync()
    {
        if (_unreadable is not { } document)
            return;
        _replaceable = document;
        _unreadable = null;
        Record = XgpNumberingRecord.Empty;
        await CheckpointAsync(record => record, currentInstance: null);
    }

    /// <summary>
    /// One checkpoint: the stored record read afresh and merged with this
    /// page's, <paramref name="change"/> applied, and the result written whole
    /// in one storage write. The result is this page's record whether or not
    /// storage stored it. A stored record that cannot be read is never
    /// replaced: the change stands in this page's record alone.
    /// </summary>
    /// <param name="change">The change, applied to the merged record.</param>
    /// <param name="currentInstance">The server's instance as this page knows it; <see langword="null"/> when unknown.</param>
    /// <returns>Whether storage stored the record.</returns>
    public async Task<bool> CheckpointAsync(Func<XgpNumberingRecord, XgpNumberingRecord> change, string? currentInstance)
    {
        ArgumentNullException.ThrowIfNull(change);

        var read = await ReadAsync();
        var merged = read is NumberingRead.Valid valid ? Merge(valid.Record, currentInstance) : Record;
        var changed = change(merged);
        foreach (var job in merged.Owed)
        {
            if (!changed.Owed.Any(kept => kept.JobId == job.JobId))
                _resolved.Add(job.JobId);
        }
        Record = changed;

        if (read is NumberingRead.Unreadable)
        {
            _unsaved = true;
            return false;
        }
        var stored = await store.WriteAsync(changed);
        _unsaved = !stored;
        return stored;
    }

    // Reads the stored record: one of its four outcomes, the document the user
    // authorized replacing reading as nothing stored. A readable read ends a
    // standing unreadable one — the document has been replaced since — and a
    // refused one establishes nothing.
    private async Task<NumberingRead> ReadAsync()
    {
        var read = await store.ReadAsync();
        switch (read)
        {
            case NumberingRead.Unreadable unreadable when unreadable.Document == _replaceable:
                return NumberingRead.Missing.Value;
            case NumberingRead.Unreadable unreadable:
                _unreadable = unreadable.Document;
                break;
            case NumberingRead.Missing or NumberingRead.Valid:
                _unreadable = null;
                break;
        }
        return read;
    }

    private XgpNumberingRecord Merge(XgpNumberingRecord stored, string? currentInstance)
    {
        var page = Record;

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
