using ExtractFromXgToCsv.Client.Shared;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// Home's clock for what counts as an edit (the umbrella's
/// <c>SPEC-notices.md</c> §7.5), and from it whether the controls follow an
/// export the numbering record took: a control the user has edited since that
/// export started keeps the user's choice.
/// </summary>
/// <remarks>
/// <para>
/// <b>The clock</b> counts this page's own events, never wall-clock time,
/// which can be set back: each user edit of the next number or the pattern —
/// a value the page restored or set is none, and changing a control away and
/// back is one — each export this page starts — a Local job as it is recorded
/// owed, a Web download as it begins — and each time the page sets the
/// controls itself.
/// </para>
/// <para>
/// <b>When an export started.</b> One this page started started at its own
/// moment. Of a Local export another page started, only a lower bound is
/// known: the start of the latest entry this page started that the same
/// instance's ledger admitted before it — admission needs the latest entry
/// applied, so the other page's start came after that entry ended — or the
/// moment the page last set the controls, if that is later, since an edit
/// made before then no longer shows. A request that was refused, or that
/// named another instance, is in no ledger of the export's instance, so it
/// orders nothing; where nothing orders the export, the user's edit stands.
/// </para>
/// <para>
/// <b>What it keeps.</b> The starts of this page's Local jobs a later taking
/// can still need (<see cref="Prune"/>): those still owed, which may yet be
/// taken or bound one, and of the rest only the latest the current ledger
/// admitted, which bounds every entry after it. A job never sent, or never
/// run, is forgotten at once (<see cref="ForgetLocalStart"/>).
/// </para>
/// <para>
/// Not thread-safe: Home calls it on the renderer's one thread.
/// </para>
/// </remarks>
internal sealed class XgpEditClock
{
    // The moment of the latest event; each event takes the next.
    private long _now;

    // The moment of the user's last edit of the next number or the pattern;
    // zero for none.
    private long _lastEdit;

    // The moment the page last set the controls itself; zero until it does.
    private long _controlsSet;

    // The moment the Web download in hand began.
    private long _webDownloadStart;

    // The moment each Local XGP job this page started was recorded as owed,
    // by its identity.
    private readonly Dictionary<string, long> _localStarts = new(StringComparer.Ordinal);

    /// <summary>The user edited the next number or the pattern.</summary>
    public void NoteEdit() => _lastEdit = ++_now;

    /// <summary>The page set the controls itself: an edit made before now no longer shows.</summary>
    public void NoteControlsSet() => _controlsSet = ++_now;

    /// <summary>A Web XGP download begins.</summary>
    public void NoteWebDownloadStart() => _webDownloadStart = ++_now;

    /// <summary>A Local XGP job this page is about to start, by the identity it minted, is recorded as owed.</summary>
    /// <exception cref="ArgumentException"><paramref name="jobId"/> is null or empty.</exception>
    public void NoteLocalStart(string jobId)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        _localStarts[jobId] = ++_now;
    }

    /// <summary>
    /// A Local XGP job this page started never ran — never sent, or refused on
    /// every attempt, or retired: it is in no ledger, so it orders nothing.
    /// </summary>
    public void ForgetLocalStart(string jobId) => _localStarts.Remove(jobId);

    /// <summary>How many of this page's Local starts the clock holds: what <see cref="Prune"/> bounds.</summary>
    public int HeldStarts => _localStarts.Count;

    /// <summary>
    /// Forgets the starts no later taking can need, given the ledger a pass
    /// read and the jobs the record still owes. A job still owed is kept: it
    /// may yet be taken, or bound one. Of the rest, only the latest that
    /// <paramref name="ledger"/> admitted is kept: every later entry is
    /// bounded by it, and no earlier one can be taken again. A job no ledger
    /// of this instance holds, and no longer owed, never ran here, or ran in
    /// an ended instance, and orders nothing.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="ledger"/> or <paramref name="owed"/> is null.</exception>
    public void Prune(LedgerSnapshot ledger, IEnumerable<string> owed)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(owed);
        var keep = new HashSet<string>(owed, StringComparer.Ordinal);
        if (ledger.Entries
                .Where(entry => _localStarts.ContainsKey(entry.JobId) && !keep.Contains(entry.JobId))
                .MaxBy(entry => entry.Number) is { } latest)
            keep.Add(latest.JobId);
        foreach (var jobId in _localStarts.Keys.Where(jobId => !keep.Contains(jobId)).ToList())
            _localStarts.Remove(jobId);
    }

    /// <summary>
    /// Whether the controls follow the Web download in hand, which the record
    /// took: unless the user has edited since it began.
    /// </summary>
    public bool FollowsWebDownload() => _lastEdit <= _webDownloadStart;

    /// <summary>
    /// Whether the controls follow <paramref name="taking"/>'s export: unless
    /// the user has edited since it started, or, where only a lower bound on
    /// its start is known, since that bound. A taking whose exports committed
    /// nothing changes no number, so there is nothing to follow.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="taking"/> is null.</exception>
    public bool Follows(LedgerTaking taking)
    {
        ArgumentNullException.ThrowIfNull(taking);
        return taking.Export is { } export && _lastEdit <= StartOf(export, taking.Ledger);
    }

    // When export started, on this page's clock: its own moment when this page
    // started it; otherwise the lower bound the remarks describe.
    private long StartOf(LedgerEntry export, LedgerSnapshot ledger)
    {
        if (_localStarts.TryGetValue(export.JobId, out var started))
            return started;
        var earlier = ledger.Entries
            .Where(entry => entry.Number < export.Number && _localStarts.ContainsKey(entry.JobId))
            .MaxBy(entry => entry.Number);
        return Math.Max(earlier is null ? 0 : _localStarts[earlier.JobId], _controlsSet);
    }
}
