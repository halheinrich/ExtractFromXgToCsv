using System.Collections.Immutable;
using ExtractFromXgToCsv.Client.Shared;

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
/// owed jobs are saved together or not at all. Without a fresh read that
/// shows what is stored — a record, or nothing — nothing is written: a
/// refused read shows nothing, and the change stands in this page's record,
/// as a refused write's does.
/// </para>
/// <para>
/// <b>One operation at a time.</b> Every operation — a restore, a pass's
/// first read, an entry applied, a checkpoint, the user's resolution — runs
/// alone: none starts until the one before it has ended. Each awaits storage
/// between its read and its write, and other work interleaves there, so
/// without this a pass's guard could compare a copy of this page's record that
/// a resolution had since changed, and a write could mark as seen a job
/// another operation had just recorded. The page's work outside the keeper
/// still interleaves, which a pass's guard is for.
/// </para>
/// <para>
/// <b>The merge.</b> The page remembers what it last saw stored — read, or
/// written by it. A stored record unchanged since then holds nothing new, so
/// this page's record stands, its pending changes with it. A changed one is
/// another page's write: its numbering and mark are taken together, never one
/// record's mark with another's numbering, unless the two marks are of one
/// instance and this page's is later (or equal, over a change of this page's
/// that storage refused). Marks of different instances are not ordered, so a
/// changed stored record's state is taken.
/// </para>
/// <para>
/// <b>Owed jobs.</b> The stored ones, and those this page recorded and has
/// not yet seen stored, less those this page resolved. A job this page
/// learned from storage is resolved when a later readable record no longer
/// holds it — another page resolved it — while a refused or unreadable read
/// establishes nothing.
/// </para>
/// <para>
/// <b>A pass's authority over another instance's mark</b>
/// (<see cref="ApplyAsync"/>). A reconciliation pass reads the stored record
/// first (<see cref="BeginPassAsync"/>), then the server's ledger, so the
/// instance the server names is at least as new as any the record names. It
/// writes an entry of another instance over the stored mark only from the
/// state it started with, and only while both halves of it — the stored
/// record it read first and this page's record as it stood then — are
/// unchanged. Otherwise it writes nothing, takes the changed state, and the
/// page reconciles again. This is an optimistic check, not a compare-and-swap:
/// browser storage offers none across tabs, and §7.5 states the window left.
/// </para>
/// <para>
/// <b>A refusal never stops the gesture.</b> When storage refuses a
/// checkpoint, the change stands in this page's record, and export goes on
/// (the storage condition reports the refusal). A job durably recorded as
/// owed stays owed in storage until a later checkpoint succeeds.
/// </para>
/// <para>
/// <b>A record that cannot be read</b> is kept as it is (<see cref="IsUnreadable"/>):
/// neither migration nor a checkpoint replaces it — nor applies anything over
/// it, while this page knows it is stored — and XGP export waits on the user.
/// Their resolution (<see cref="ResolveUnreadableAsync"/>) authorizes
/// replacing that document with a fresh record, and nothing more. The
/// replacement is written only once a fresh read shows that document, or
/// nothing stored; the authorization ends once the fresh record is stored, or
/// once a read shows anything else.
/// </para>
/// </remarks>
internal sealed class XgpNumberingKeeper(XgpNumberingStore store)
{
    // The keeper's one operation in progress, held from its first read to its
    // last write (see the remarks).
    private readonly SemaphoreSlim _turn = new(1, 1);

    // The owed jobs this page has resolved: never owed again here, whatever a
    // stored record still says.
    private readonly HashSet<string> _resolved = new(StringComparer.Ordinal);

    // The owed jobs this page recorded and has not yet seen stored: kept
    // through a read that lacks them, since storage may never have taken them.
    private readonly HashSet<string> _unseen = new(StringComparer.Ordinal);

    // Whether this page's record holds a change storage refused.
    private bool _unsaved;

    // What this page last saw stored, read or written by it; null while it
    // knows nothing.
    private StoredRecord? _seen;

    // The unreadable document this page last read and the user has not
    // resolved; null while there is none.
    private string? _unreadable;

    // The unreadable document the user resolved: replacing it is authorized,
    // so a read that finds it still stored reads as nothing stored. Null until
    // a resolution, and again once the fresh record is stored or a read shows
    // anything else.
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
    public Task RestoreAsync() => AloneAsync(async () =>
    {
        var read = await ReadAsync();
        if (read is NumberingRead.Missing && await store.MigrateAsync() is { } migrated)
        {
            Record = migrated.Record;
            _seen = new StoredRecord(migrated.Stored ? migrated.Record : null);
            _unsaved = !migrated.Stored;
            return;
        }
        if (read is NumberingRead.Valid valid)
            Record = valid.Record;
        NoteRead(read);
    });

    /// <summary>
    /// Begins a reconciliation pass: the stored record read first, before the
    /// pass asks the server anything, and merged into this page's record.
    /// </summary>
    /// <returns>The state the pass starts from, which bounds its authority (<see cref="ApplyAsync"/>).</returns>
    public Task<PassStart> BeginPassAsync() => AloneAsync(async () =>
    {
        var read = await ReadAsync();
        if (read is NumberingRead.Valid valid)
            Record = Merge(valid.Record, Record);
        NoteRead(read);
        return new PassStart(Fresh(read), Record);
    });

    /// <summary>
    /// Applies one ended ledger entry of <paramref name="instance"/> for a
    /// pass begun at <paramref name="start"/>, in one checkpoint.
    /// </summary>
    /// <remarks>
    /// An entry of the mark's instance is ordered by number, an ordinary
    /// checkpoint. An entry of another instance — or any, while no mark is set
    /// — is written over the stored mark only from the state the pass started
    /// with, while both the stored record and this page's record are as they
    /// were then. If either has changed, nothing is written, the changed state
    /// is taken, and the caller reconciles again. A fresh read that is refused
    /// cannot show the stored record unchanged: nothing is written, and the
    /// entry is applied in this page's memory alone — an entry of another
    /// instance only while this page's record is unchanged. Over a record this
    /// page knows cannot be read — this read showed it, or a refused read
    /// cannot show it replaced — nothing is applied at all. An entry that
    /// changes nothing writes nothing.
    /// </remarks>
    public Task<EntryApplied> ApplyAsync(PassStart start, string instance, LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(entry);
        return AloneAsync(() => ApplyAloneAsync(start, instance, entry));
    }

    private async Task<EntryApplied> ApplyAloneAsync(PassStart start, string instance, LedgerEntry entry)
    {
        var read = await ReadAsync();
        if (IsUnreadable)
            return EntryApplied.Unreadable;

        // This page's record as it stands — no other operation of the keeper
        // can have changed it during the read — against the one the pass
        // started from.
        var page = Record;
        var current = read is NumberingRead.Valid valid ? Merge(valid.Record, page) : page;
        var fresh = Fresh(read);
        NoteRead(read);

        XgpNumberingRecord before, applied;
        if (current.HasMarkOf(instance))
        {
            before = current;
            applied = current.Applying(instance, entry, underPassAuthority: false);
        }
        else if (!page.Equals(start.Page) || (fresh is not null && fresh != start.Stored))
        {
            // The state the pass started from has changed: nothing is
            // written, and the changed state is taken, mark and numbering
            // together, with this page's own pending changes.
            Record = current;
            return EntryApplied.StateChanged;
        }
        else
        {
            before = start.Page;
            applied = start.Page.Applying(instance, entry, underPassAuthority: true);
        }

        var taken = applied.Mark != before.Mark;
        var changed = !applied.Equals(before);
        Take(before, applied);
        if (fresh is null)
        {
            // Refused: nothing is written; applied in this page alone.
            _unsaved |= changed;
        }
        else if (changed)
        {
            await WriteAsync(applied);
        }
        return taken ? EntryApplied.Taken : EntryApplied.NotNewer;
    }

    /// <summary>Records <paramref name="job"/> as owed, durably where storage allows, before its start is sent.</summary>
    /// <returns>Whether storage stored the record.</returns>
    public Task<bool> OweAsync(OwedXgpJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return AloneAsync(() =>
        {
            _unseen.Add(job.JobId);
            return CheckpointAloneAsync(record => record.Owing(job));
        });
    }

    /// <summary>Resolves the owed job <paramref name="jobId"/>: this page will never owe it again.</summary>
    /// <returns>Whether storage stored the record.</returns>
    public Task<bool> ResolveAsync(string jobId) => CheckpointAsync(record => record.Resolving(jobId));

    /// <summary>
    /// Records what a Web download committed, as Home sets it from an export
    /// (<see cref="XgpNumberingRecord.WithCommit"/>), in one checkpoint. An
    /// export that committed nothing changes no number: nothing is written,
    /// and the record takes nothing.
    /// </summary>
    /// <returns>
    /// Whether the record took the export — it committed something, so the
    /// record's numbering is now its — whether or not storage stored it. Each
    /// download the record takes is its own taking, though its numbering may
    /// equal what the record held.
    /// </returns>
    public async Task<bool> RecordExportAsync(XgpExportCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.CommittedNothing)
            return false;
        await CheckpointAsync(record => record.WithCommit(commit));
        return true;
    }

    /// <summary>
    /// The user's deliberate resolution of a record that cannot be read: it
    /// authorizes replacing that document with a fresh record, and nothing
    /// more. The fresh record is written as every record is, from a fresh
    /// read, and only once one shows that document, or nothing stored. A read
    /// the browser refuses shows neither: nothing is written, the replacement
    /// stands in this page, and the next checkpoint whose read shows the
    /// document writes it. A read that shows another page's record has
    /// replaced it already: that record is taken, and nothing is written.
    /// Whatever the page then may do is the ordinary admission's to decide.
    /// </summary>
    public Task ResolveUnreadableAsync() => AloneAsync(async () =>
    {
        if (_unreadable is not { } document)
            return;
        _replaceable = document;
        _unreadable = null;
        Record = XgpNumberingRecord.Empty;

        var read = await ReadAsync();
        if (read is NumberingRead.Valid valid)
            Record = Merge(valid.Record, Record);
        NoteRead(read);
        if (read is NumberingRead.Missing)
            await WriteAsync(Record);
        else if (read is NumberingRead.Refused)
            _unsaved = true;
    });

    /// <summary>
    /// One checkpoint: the stored record read afresh and merged with this
    /// page's, <paramref name="change"/> applied, and the result written whole
    /// in one storage write. The result is this page's record whether or not
    /// storage stored it. It is written only from a fresh read that showed what
    /// is stored — a record, or nothing: under a refused read, or over a stored
    /// record this page knows cannot be read, nothing is written, and the
    /// change stands in this page's record alone.
    /// </summary>
    /// <param name="change">The change, applied to the merged record.</param>
    /// <returns>Whether storage stored the record.</returns>
    public Task<bool> CheckpointAsync(Func<XgpNumberingRecord, XgpNumberingRecord> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return AloneAsync(() => CheckpointAloneAsync(change));
    }

    private async Task<bool> CheckpointAloneAsync(Func<XgpNumberingRecord, XgpNumberingRecord> change)
    {
        var read = await ReadAsync();
        var merged = read is NumberingRead.Valid valid ? Merge(valid.Record, Record) : Record;
        NoteRead(read);
        var changed = change(merged);
        Take(merged, changed);

        if (Fresh(read) is null)
        {
            _unsaved = true;
            return false;
        }
        return await WriteAsync(changed);
    }

    // Runs operation as the keeper's one operation in progress: none other
    // starts until it has ended, whatever is scheduled at its storage awaits.
    private async Task<T> AloneAsync<T>(Func<Task<T>> operation)
    {
        await _turn.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _turn.Release();
        }
    }

    private Task AloneAsync(Func<Task> operation) =>
        AloneAsync(async () =>
        {
            await operation();
            return true;
        });

    // Reads the stored record: one of its four outcomes, the document the user
    // authorized replacing reading as nothing stored. A read that shows
    // anything else — a record, nothing, or another unreadable document — ends
    // that authorization, and a readable one ends a standing unreadable one:
    // the document has been replaced since. A refused read establishes
    // nothing.
    private async Task<NumberingRead> ReadAsync()
    {
        var read = await store.ReadAsync();
        switch (read)
        {
            case NumberingRead.Unreadable unreadable when unreadable.Document == _replaceable:
                return NumberingRead.Missing.Value;
            case NumberingRead.Unreadable unreadable:
                _unreadable = unreadable.Document;
                _replaceable = null;
                break;
            case NumberingRead.Missing or NumberingRead.Valid:
                _unreadable = null;
                _replaceable = null;
                break;
        }
        return read;
    }

    // What a read showed stored: a record, or nothing; null for a read that
    // showed nothing — refused, or unreadable.
    private static StoredRecord? Fresh(NumberingRead read) => read switch
    {
        NumberingRead.Valid valid => new StoredRecord(valid.Record),
        NumberingRead.Missing => new StoredRecord(null),
        _ => null,
    };

    // A readable read is what this page has now seen stored: the owed jobs it
    // holds are seen stored, so a later read that lacks them means another
    // page resolved them. Called after the read's merge, which compares with
    // what was seen before it.
    private void NoteRead(NumberingRead read)
    {
        if (Fresh(read) is not { } fresh)
            return;
        _seen = fresh;
        if (fresh.Record is { } stored)
            _unseen.ExceptWith(stored.Owed.Select(job => job.JobId));
    }

    // Makes changed this page's record: the owed jobs the change dropped are
    // resolved by this page.
    private void Take(XgpNumberingRecord before, XgpNumberingRecord changed)
    {
        foreach (var job in before.Owed)
        {
            if (!changed.Owed.Any(kept => kept.JobId == job.JobId))
                _resolved.Add(job.JobId);
        }
        Record = changed;
    }

    // Writes record whole; stored, it is what this page has seen stored —
    // the document the user resolved, if any, is replaced, its authorization
    // spent — and every owed job it holds has been seen stored. The record is
    // the merged one, which holds every job this page recorded and has not
    // resolved, and no other operation runs meanwhile to record another.
    private async Task<bool> WriteAsync(XgpNumberingRecord record)
    {
        var stored = await store.WriteAsync(record);
        _unsaved = !stored;
        if (stored)
        {
            _seen = new StoredRecord(record);
            _replaceable = null;
            _unseen.Clear();
        }
        return stored;
    }

    private XgpNumberingRecord Merge(XgpNumberingRecord stored, XgpNumberingRecord page)
    {
        var pair = _seen is { } seen && Equals(seen.Record, stored) ? page : NewestOf(stored, page);
        var owed = stored.Owed
            .Concat(page.Owed.Where(job => _unseen.Contains(job.JobId) && !stored.Owed.Any(other => other.JobId == job.JobId)))
            .Where(job => !_resolved.Contains(job.JobId))
            .ToImmutableArray();
        return new XgpNumberingRecord { Numbering = pair.Numbering, Mark = pair.Mark, Owed = owed };
    }

    // Whose numbering and mark, always together, a stored record another page
    // changed and this page's yield: within one instance the later mark's —
    // with the marks equal, the stored one's, unless this page holds a change
    // storage refused; across instances, which no mark orders, the stored
    // record's.
    private XgpNumberingRecord NewestOf(XgpNumberingRecord stored, XgpNumberingRecord page)
    {
        if (stored.Mark?.Instance != page.Mark?.Instance)
            return stored;
        var storedNumber = stored.Mark?.Number ?? 0;
        var pageNumber = page.Mark?.Number ?? 0;
        return storedNumber > pageNumber ? stored
            : pageNumber > storedNumber ? page
            : _unsaved ? page : stored;
    }

    /// <summary>What this page knows storage to hold: a record, or — <see langword="null"/> — nothing stored.</summary>
    /// <param name="Record">The stored record; <see langword="null"/> when nothing is stored.</param>
    internal sealed record StoredRecord(XgpNumberingRecord? Record);

    /// <summary>
    /// The state a reconciliation pass starts from (§7.5): the stored record it
    /// read first — <see langword="null"/> when that read showed nothing — and
    /// this page's record as it stood then.
    /// </summary>
    internal sealed record PassStart(StoredRecord? Stored, XgpNumberingRecord Page);
}

/// <summary>What applying one ledger entry in a reconciliation pass did.</summary>
internal enum EntryApplied
{
    /// <summary>The record took the entry: its mark moved to it, in storage or, refused, in this page.</summary>
    Taken,

    /// <summary>The entry is at or before the mark: nothing changed but its job, resolved.</summary>
    NotNewer,

    /// <summary>
    /// The state the pass started from has changed: nothing was written, the
    /// changed state was taken, and the page reconciles again from there.
    /// </summary>
    StateChanged,

    /// <summary>The stored record cannot be read: nothing was applied.</summary>
    Unreadable,
}
