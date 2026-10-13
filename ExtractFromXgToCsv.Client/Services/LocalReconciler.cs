using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.Logging;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The page's reconciliation with the Local server (the umbrella's
/// <c>SPEC-notices.md</c> §7.5), owned by Home with the numbering record it
/// keeps. It learns the server's instance and XGP ledger, applies the ended
/// entries this page has not, follows a running one, settles the jobs of this
/// instance the browser is owed, and says what a start may do now
/// (<see cref="State"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>When.</b> When the page loads (<see cref="StartAsync"/>), when the panel
/// asks — its job unknown to the server, its start refused or never
/// answered, its XGP job ended — and on its own: again after a failed attempt,
/// while an XGP job runs or an owed one is unresolved, and at once when the
/// state a pass started from changed under it. Until a reconciliation
/// succeeds the state is <i>checking</i>, never <i>nothing outstanding</i>.
/// </para>
/// <para>
/// <b>How.</b> A pass reads the stored record first, then the server's
/// ledger; that order is its authority. Each ended entry the record does not
/// hold is applied, in order, in one checkpoint each (numbering, mark and owed
/// together) — an entry of another instance than the record's mark only from
/// the state the pass started with, while it stands (<see cref="XgpNumberingKeeper.ApplyAsync"/>).
/// The controls are then offered the record's taking (<see cref="LedgerTaking"/>):
/// the event they follow is the record's mark moving, whether by an entry
/// applied here or by another page's accounting taken from storage — never a
/// change in the numbering's value, which a new export may leave unchanged,
/// and never a replay, which moves no mark. A result the
/// panel's follower received carries no authority of its own: it is applied
/// through a pass (<see cref="ApplyEndedAsync"/>). An owed job of this
/// instance with no entry is settled: retired resolves it; admitted means its
/// start arrived after the ledger read, so it is followed and applied. An owed
/// job of an earlier instance cannot be recovered: it blocks XGP until the
/// user resolves it (<see cref="ResolveLostAsync"/>). A read of the ledger
/// alone never resolves an owed job.
/// </para>
/// <para>
/// Not thread-safe: Blazor WebAssembly runs it on one thread, and its awaits
/// are where other work interleaves. Overlapping requests coalesce into one
/// more pass.
/// </para>
/// </remarks>
internal sealed class LocalReconciler
{
    private readonly LocalJobsClient _client;
    private readonly XgpNumberingKeeper _numbering;
    private readonly LocalJobTiming _timing;
    private readonly Func<LedgerTaking, Task> _followRecord;
    private readonly ILogger _logger;
    private readonly CancellationToken _lifetime;

    private Task? _pass;
    private bool _again;
    private CancellationTokenSource? _scheduled;

    // The record's mark when the controls were last offered a taking — the
    // restored record's until the first — so a mark that has not moved since
    // is no new taking: a replay offers nothing.
    private LedgerMark? _offered;

    /// <summary>
    /// A reconciliation over <paramref name="client"/> and the record
    /// <paramref name="numbering"/> keeps, which ends with
    /// <paramref name="lifetime"/>, the page's.
    /// </summary>
    /// <param name="client">The protocol's wire.</param>
    /// <param name="numbering">Home's numbering record.</param>
    /// <param name="timing">How often to follow and retry.</param>
    /// <param name="followRecord">
    /// Home's controls following the record: offered each taking — the
    /// entries of the pass's ledger the record took since the last offer, once
    /// its mark has moved to one of them.
    /// </param>
    /// <param name="logger">Logs a reconciliation that failed for any reason but the server's absence.</param>
    /// <param name="lifetime">The page's lifetime: nothing runs after it ends.</param>
    public LocalReconciler(
        LocalJobsClient client,
        XgpNumberingKeeper numbering,
        LocalJobTiming timing,
        Func<LedgerTaking, Task> followRecord,
        ILogger logger,
        CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(followRecord);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _numbering = numbering;
        _timing = timing;
        _followRecord = followRecord;
        _logger = logger;
        _lifetime = lifetime;
    }

    /// <summary>What a Local start may do now.</summary>
    public LocalStartState State { get; private set; } = LocalStartState.Checking;

    /// <summary>
    /// The owed jobs of an earlier instance: lost with it, each blocking XGP
    /// until the user resolves it. Empty until the page knows the instance.
    /// </summary>
    public IReadOnlyList<OwedXgpJob> LostJobs =>
        State.Instance is { } instance
            ? [.. _numbering.Record.Owed.Where(job => job.Instance != instance)]
            : [];

    /// <summary>Raised after <see cref="State"/> or <see cref="LostJobs"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Begins reconciling, for a page that has just restored the record: the
    /// controls were restored from it, so its mark is no taking to follow.
    /// </summary>
    public Task StartAsync()
    {
        _offered = _numbering.Record.Mark;
        return ReconcileAsync();
    }

    /// <summary>Reconciles now, or once more after the pass under way.</summary>
    public Task ReconcileAsync()
    {
        if (_pass is { IsCompleted: false })
        {
            _again = true;
            return _pass;
        }
        _pass = PassesAsync();
        return _pass;
    }

    /// <summary>
    /// Records <paramref name="job"/> as owed — durably where storage allows —
    /// before the page sends its start. Until the job is resolved, no other XGP
    /// start is sent.
    /// </summary>
    public async Task RecordOwedAsync(OwedXgpJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        await _numbering.OweAsync(job);
        Publish(State with { XgpBlock = XgpStartBlock.Checking });
    }

    /// <summary>
    /// Resolves an owed job whose start never ran — every attempt the page
    /// sent was refused, or Stop settled its identity as retired — then
    /// reconciles.
    /// </summary>
    public async Task ResolveNeverStartedAsync(string jobId)
    {
        await _numbering.ResolveAsync(jobId);
        await ReconcileAsync();
    }

    /// <summary>
    /// An XGP job the page followed to its terminal result: its result carries
    /// no authority of its own, so its entry is applied by a pass, from the
    /// ledger, like any other. The page acknowledges the job after this.
    /// </summary>
    public Task ApplyEndedAsync() => ReconcileAsync();

    /// <summary>
    /// The user's deliberate resolution of a job lost with an earlier
    /// instance: it is no longer owed, and export proceeds from the controls
    /// as they then stand. No number is inferred from anything.
    /// </summary>
    public async Task ResolveLostAsync(string jobId)
    {
        await _numbering.ResolveAsync(jobId);
        await ReconcileAsync();
    }

    /// <summary>
    /// The user's deliberate resolution of a numbering record that cannot be
    /// read: the document is replaced with a fresh record, and the page
    /// reconciles. That waives nothing: XGP stays blocked until the
    /// reconciliation has followed or applied what the instance holds, as
    /// anywhere else.
    /// </summary>
    public async Task ResolveUnreadableAsync()
    {
        Publish(State with { XgpBlock = XgpStartBlock.Checking });
        await _numbering.ResolveUnreadableAsync();
        await ReconcileAsync();
    }

    private async Task PassesAsync()
    {
        do
        {
            _again = false;
            CancelScheduled();
            try
            {
                await PassAsync();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Not the server's absence, which a pass handles: something
                // else failed. The state stays unknown, and the pass is
                // tried again.
                _logger.LogWarning(ex, "Reconciling with the Local server failed; trying again.");
                Publish(State with { XgpBlock = XgpStartBlock.Checking });
                ScheduleAfter(_timing.RetryDelay);
            }
        }
        while (_again && !_lifetime.IsCancellationRequested);
    }

    private async Task PassAsync()
    {
        // The stored record first, then the server: the instance the server
        // names was live after the record was read.
        var start = await _numbering.BeginPassAsync();

        if (await TryAsync(() => _client.ReadLedgerAsync(_lifetime), State.Instance) is not { } ledger)
            return;
        var instance = ledger.Instance;

        // A record that cannot be read may hold owed exports: nothing is
        // applied over it, and XGP waits on the user's resolution. A start of
        // any other format may name the instance learned.
        if (_numbering.IsUnreadable)
        {
            Publish(new LocalStartState(instance, _numbering.Record.Mark, XgpStartBlock.UnreadableRecord));
            return;
        }

        foreach (var entry in ledger.Entries.Where(entry => !entry.Running))
        {
            if (_numbering.Record.Holds(instance, entry))
                continue;
            switch (await _numbering.ApplyAsync(start, instance, entry))
            {
                case EntryApplied.Taken:
                    // The record took it: the controls are offered the taking.
                    await OfferTakingAsync(ledger);
                    break;
                case EntryApplied.StateChanged:
                    // Nothing was written, and the changed state was taken:
                    // reconcile again from there.
                    _again = true;
                    return;
                case EntryApplied.Unreadable:
                    Publish(new LocalStartState(instance, _numbering.Record.Mark, XgpStartBlock.UnreadableRecord));
                    return;
            }
        }

        var running = ledger.Entries.Any(entry => entry.Running);
        var unresolved = false;
        foreach (var job in _numbering.Record.Owed.Where(job => job.Instance == instance).ToArray())
        {
            if (ledger.Entries.FirstOrDefault(entry => entry.JobId == job.JobId) is { } entry)
            {
                // An ended entry was applied above, which resolved the job.
                unresolved |= entry.Running;
                continue;
            }

            // No entry: its start may be lost or delayed, so the ledger alone
            // resolves nothing. Settling decides, in one server step.
            var outcome = await TryAsync(() => SettleAsync(job.JobId, instance), instance);
            if (outcome is null)
                return;
            if (outcome == Settled.Retired)
                await _numbering.ResolveAsync(job.JobId);
            else
                unresolved = true;
        }

        // The record can take another page's accounting from storage too, at
        // any read: the controls are offered that taking as well.
        await OfferTakingAsync(ledger);

        var lost = _numbering.Record.Owed.Any(job => job.Instance != instance);
        Publish(new LocalStartState(
            instance,
            _numbering.Record.Mark,
            _numbering.IsUnreadable ? XgpStartBlock.UnreadableRecord
            : lost ? XgpStartBlock.LostExport
            : running ? XgpStartBlock.Running
            : unresolved ? XgpStartBlock.Checking
            : null));

        if (running || unresolved)
            ScheduleAfter(_timing.PollInterval);
    }

    /// <summary>
    /// Offers the controls the record's taking since the last offer: the
    /// entries of <paramref name="ledger"/> its mark has moved past. The event
    /// is the mark moving, so a replay — an entry the record already holds —
    /// offers nothing, and a new export offers itself though its numbering
    /// equals the record's. A mark this ledger does not hold is not offered
    /// yet: its entries cannot be named, nor ordered against this page's
    /// starts, until a pass reads that instance's ledger.
    /// </summary>
    private async Task OfferTakingAsync(LedgerSnapshot ledger)
    {
        if (_numbering.Record.Mark is not { } mark || mark == _offered || mark.Instance != ledger.Instance)
            return;
        var after = _offered?.Instance == mark.Instance ? _offered.Number : 0;
        _offered = mark;
        await _followRecord(new LedgerTaking(
            ledger, [.. ledger.Entries.Where(entry => entry.Number > after && entry.Number <= mark.Number)]));
    }

    /// <summary>What settling found, with the server's refusal — it is another instance now — as its own answer.</summary>
    private enum Settled { Retired, Admitted, OtherInstance }

    private async Task<Settled?> SettleAsync(string jobId, string instance) =>
        await _client.SettleAsync(jobId, instance, _lifetime) switch
        {
            SettleOutcome.Retired => Settled.Retired,
            SettleOutcome.Admitted => Settled.Admitted,
            _ => Settled.OtherInstance,
        };

    /// <summary>
    /// A request to the server; <see langword="null"/> when it could not be
    /// made — the XGP state is unknown, and the pass is tried again. A start
    /// of any other format may name <paramref name="instance"/>, the one the
    /// page knows by now: a ledger read that succeeded before a settle failed
    /// is a reconciliation with that instance.
    /// </summary>
    private async Task<T?> TryAsync<T>(Func<Task<T>> request, string? instance)
    {
        try
        {
            return await request();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is OperationCanceledException && !_lifetime.IsCancellationRequested))
        {
            Publish(new LocalStartState(instance, _numbering.Record.Mark, XgpStartBlock.Checking));
            ScheduleAfter(_timing.RetryDelay);
            return default;
        }
    }

    private void Publish(LocalStartState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private void ScheduleAfter(TimeSpan delay)
    {
        CancelScheduled();
        var scheduled = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _scheduled = scheduled;
        _ = ReconcileAfterAsync(delay, scheduled.Token);
    }

    private async Task ReconcileAfterAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await ReconcileAsync();
    }

    private void CancelScheduled()
    {
        _scheduled?.Cancel();
        _scheduled?.Dispose();
        _scheduled = null;
    }
}

/// <summary>
/// The numbering record's taking of Local XGP exports, as a reconciliation
/// pass offers it to the controls (the umbrella's <c>SPEC-notices.md</c>
/// §7.5): the event they follow. A taking is the record's mark moving to an
/// entry it did not hold — never a change in the numbering's value, which a
/// new export may leave as it was, and never a replay, which moves no mark.
/// </summary>
/// <param name="Ledger">
/// The ledger the pass read: its instance's entries are the ones taken, and
/// it orders them against the exports this page started.
/// </param>
/// <param name="Taken">The entries of <paramref name="Ledger"/> the record took, in admission order.</param>
internal sealed record LedgerTaking(LedgerSnapshot Ledger, IReadOnlyList<LedgerEntry> Taken)
{
    /// <summary>
    /// The export whose numbering the record now holds: the last entry taken
    /// that committed something. <see langword="null"/> when none did — an
    /// export that committed nothing changes no number, so there is nothing
    /// for the controls to follow.
    /// </summary>
    public LedgerEntry? Export => Taken.LastOrDefault(entry => entry.Commit is { CommittedNothing: false });
}
