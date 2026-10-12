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
/// and while an XGP job runs or an owed one is unresolved. Until a
/// reconciliation succeeds the state is <i>checking</i>, never <i>nothing
/// outstanding</i>.
/// </para>
/// <para>
/// <b>How.</b> The record is read afresh, then the server's ledger: each ended
/// entry newer than what the page applied is applied, in order — the controls
/// follow it control by control, and the record takes it in one checkpoint
/// (numbering, mark and owed together). An owed job of this instance with no
/// entry is settled: retired resolves it; admitted means its start arrived
/// after the ledger read, so it is followed and applied. An owed job of an
/// earlier instance cannot be recovered: it blocks XGP until the user resolves
/// it (<see cref="ResolveLostAsync"/>). A read of the ledger alone never
/// resolves an owed job.
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
    private readonly Func<XgpExportCommit, Task> _followControls;
    private readonly ILogger _logger;
    private readonly CancellationToken _lifetime;

    // The newest entry the page's controls have followed. Apart from the
    // record's mark: another page may have taken entries into the stored
    // record that this page's controls have not followed.
    private LedgerMark? _controlsMark;

    private Task? _pass;
    private bool _again;
    private CancellationTokenSource? _scheduled;

    /// <summary>
    /// A reconciliation over <paramref name="client"/> and the record
    /// <paramref name="numbering"/> keeps, which ends with
    /// <paramref name="lifetime"/>, the page's.
    /// </summary>
    /// <param name="client">The protocol's wire.</param>
    /// <param name="numbering">Home's numbering record.</param>
    /// <param name="timing">How often to follow and retry.</param>
    /// <param name="followControls">Home's per-control rule, given each applied entry's accounting.</param>
    /// <param name="logger">Logs a reconciliation that failed for any reason but the server's absence.</param>
    /// <param name="lifetime">The page's lifetime: nothing runs after it ends.</param>
    public LocalReconciler(
        LocalJobsClient client,
        XgpNumberingKeeper numbering,
        LocalJobTiming timing,
        Func<XgpExportCommit, Task> followControls,
        ILogger logger,
        CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(followControls);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _numbering = numbering;
        _timing = timing;
        _followControls = followControls;
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
    /// controls stand on the record's numbering, so they have followed its
    /// mark.
    /// </summary>
    public Task StartAsync()
    {
        _controlsMark = _numbering.Record.Mark;
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
        await _numbering.CheckpointAsync(record => record.Owing(job), State.Instance);
        Publish(State with { XgpBlock = XgpStartBlock.Checking });
    }

    /// <summary>
    /// Applies the ledger entry of an XGP job the page followed to its end,
    /// then reconciles; the page acknowledges the job after this.
    /// </summary>
    public async Task ApplyEndedAsync(EndedXgpJob ended)
    {
        ArgumentNullException.ThrowIfNull(ended);
        await ApplyAsync(ended.Instance, ended.Entry);
        await ReconcileAsync();
    }

    /// <summary>
    /// The user's deliberate resolution of a job lost with an earlier
    /// instance: it is no longer owed, and export proceeds from the controls
    /// as they then stand. No number is inferred from anything.
    /// </summary>
    public async Task ResolveLostAsync(string jobId)
    {
        await _numbering.CheckpointAsync(record => record.Resolving(jobId), State.Instance);
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
        await _numbering.RefreshAsync(State.Instance);

        if (await TryAsync(() => _client.ReadLedgerAsync(_lifetime), State.Instance) is not { } ledger)
            return;
        var instance = ledger.Instance;

        foreach (var entry in ledger.Entries.Where(entry => !entry.Running))
            await ApplyAsync(instance, entry);

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
                await _numbering.CheckpointAsync(record => record.Resolving(job.JobId), instance);
            else
                unresolved = true;
        }

        var lost = _numbering.Record.Owed.Any(job => job.Instance != instance);
        Publish(new LocalStartState(
            instance,
            _numbering.Record.Mark,
            lost ? XgpStartBlock.LostExport
            : running ? XgpStartBlock.Running
            : unresolved ? XgpStartBlock.Checking
            : null));

        if (running || unresolved)
            ScheduleAfter(_timing.PollInterval);
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
    /// One ledger entry, applied: the controls follow it if they have not, and
    /// the record takes it in one checkpoint if applying it changes the record
    /// — whether it does is the record's own rule
    /// (<see cref="XgpNumberingRecord.Applying"/>), so an entry at or before
    /// the mark writes nothing unless its job is still owed.
    /// </summary>
    private async Task ApplyAsync(string instance, LedgerEntry entry)
    {
        var mark = new LedgerMark(instance, entry.Number);
        if (mark.IsNewerThan(_controlsMark, instance))
        {
            if (entry.Commit is { } commit)
                await _followControls(commit);
            _controlsMark = mark;
        }

        if (!_numbering.Record.Applying(instance, entry).Equals(_numbering.Record))
            await _numbering.CheckpointAsync(current => current.Applying(instance, entry), instance);
    }

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
