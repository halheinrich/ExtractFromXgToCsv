using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>
/// The Local run as <see cref="ExtractApp"/> holds it (the umbrella's
/// <c>SPEC-notices.md</c> §7.2 and §7.5): the job the run in hand follows, its
/// Stop, its progress and the run error, the result judged against the inputs
/// it was computed from, the last run's skip record, and the start, following,
/// stopping and acknowledging of the job. Constructed by the coordinator and
/// held privately, so nothing else can reach a run; every move is reported
/// through the owner's change callback.
/// </summary>
/// <remarks>
/// "Controller" is the word of the coordinator's settled scope
/// (halheinrich/backgammon#424); this is no MVC controller. The code was the
/// Local panel's, moved whole.
/// </remarks>
internal sealed class LocalRunController
{
    private readonly ILocalJobs _jobs;
    private readonly LocalJobTiming _timing;
    private readonly IJobIdentities _identities;
    private readonly Func<FilterConfig?> _filterInEffect;
    private readonly Action _changed;

    /// <summary>A run controller over the job protocol's three services.</summary>
    /// <param name="jobs">The job protocol's wire.</param>
    /// <param name="timing">How patiently a run starts and follows its job.</param>
    /// <param name="identities">Mints each start's identity.</param>
    /// <param name="filterInEffect">The filter in effect for the source on screen now, or null; a result is judged against it.</param>
    /// <param name="changed">The owner's change notification, raised on every move.</param>
    public LocalRunController(
        ILocalJobs jobs, LocalJobTiming timing, IJobIdentities identities, Func<FilterConfig?> filterInEffect, Action changed)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _filterInEffect = filterInEffect ?? throw new ArgumentNullException(nameof(filterInEffect));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    // ── What the owner reads ────────────────────────────────────────────────

    /// <summary>
    /// Whether a run is in hand: from the click until its outcome has been
    /// judged and published. It alone drives Stop, the busy cursor and
    /// "Starting…", and it alone exempts a result from the judgement.
    /// </summary>
    public bool IsRunInHand => _busy;

    /// <summary>The run's progress, or its terminal snapshot once published; null when none is on screen.</summary>
    public ProcessingProgress? Progress => _progress;

    /// <summary>The run error standing on screen; null when none is.</summary>
    public string? RunError => _runError;

    /// <summary>Whether a Stop pressed while the start was unanswered still waits for the answer.</summary>
    public bool IsStopPending => _stopPending;

    /// <summary>The last run's skip record, while it stands; null while there is none.</summary>
    public SkipRecord? LastRunSkips => _lastRun?.Skips;

    /// <summary>The last run's label — the run it is the history of, and how that run ended; null while there is no record.</summary>
    public string? LastRunLabel => _lastRun?.Label;

    /// <summary>Whether the filter of the run on screen restricted anything: the zero-match notice's premise.</summary>
    public bool RunFilterRestricted => _run is { FilterRestrictsNothing: false };

    private ProcessingProgress? _progress = null;
    private bool _busy = false;

    // The job the run in hand follows, once an answer has made it known; null
    // before then, and while no run is in hand. Stop cancels it, and each
    // cancel's answer is that job's run's alone (DeliverStopAsync).
    private JobInHand? _jobInHand;

    /// <summary>
    /// The job a run follows, once known: its identity, and whether a Stop for
    /// it has been confirmed delivered. One per run, compared by reference, so
    /// a Stop's answer that arrives after its run has ended — or after the next
    /// run has begun — is told apart from the run in hand's.
    /// </summary>
    private sealed class JobInHand(string jobId)
    {
        /// <summary>The job's identity, as minted for its start.</summary>
        public string JobId { get; } = jobId;

        /// <summary>
        /// Whether a cancel for the job has been confirmed delivered. Once it
        /// has, no cancel's failure undoes it — not even one for a request
        /// sent later.
        /// </summary>
        public bool StopDelivered { get; private set; }

        /// <summary>A cancel for the job was confirmed delivered.</summary>
        public void ConfirmStopDelivered() => StopDelivered = true;
    }

    // Stop pressed for the run in hand while its start was unanswered: the
    // start goes on being sent, and once its answer admits the job, the Stop
    // is delivered before the job is followed. Until then nothing says it was
    // delivered; the page says only that it waits (§7.5 allows the hint). It
    // ends the moment the start is answered (TakePendingStop), and is cleared
    // as each run starts and ends.
    private bool _stopPending;

    // This controller's lifetime, the coordinator's: a run it is following
    // stops when it goes.
    private readonly CancellationTokenSource _lifetime = new();

    // The failed run standing on screen, as its message; null when none is.
    // The run error notice's whole state — presence, text and dismissal are
    // this one field (see the notice's comment in the panel's markup). Distinct
    // from _progress.ErrorMessage, which is the server's report of a job that
    // started and then failed; this is the client's own failure to start or
    // follow one.
    private string? _runError;

    // What the run error shows of the run in hand while it goes on is made
    // from the two facts below (ShowInFlight): a Stop failure standing, which
    // the persistent-failure notice never replaces, or else that notice,
    // while it is shown.

    // A Stop for the run in hand that was refused, or could not be
    // confirmed, as its message; null while none stands. It stands until a
    // Stop for the job is delivered, the user closes it, or the run ends,
    // whatever contact does meanwhile.
    private string? _stopFailure;

    // The persistent-failure notice within the current failure streak.
    private StillTryingNotice _stillTrying;

    /// <summary>
    /// The persistent-failure notice within one failure streak — the requests
    /// that fail in a row until contact returns (SPEC-notices.md §7.5).
    /// </summary>
    private enum StillTryingNotice
    {
        /// <summary>
        /// Not on screen and not held back: no streak has reached the
        /// threshold, or one has and nothing has shown it yet. The next failure
        /// at or past the threshold shows it.
        /// </summary>
        None,

        /// <summary>
        /// Due, but a Stop failure stands in its place. It is shown once a
        /// Stop is confirmed delivered, while failures continue; if the user
        /// closes the Stop failure instead, it is due again at the next failure.
        /// </summary>
        Suppressed,

        /// <summary>On screen as the run error, until contact returns.</summary>
        Shown,

        /// <summary>
        /// Closed by the user: it stays closed until contact returns and a new
        /// streak begins, however many more requests fail.
        /// </summary>
        Dismissed,
    }

    /// <summary>
    /// One visit to a source: the stretch of the app's life over which it
    /// shows one source. Replacing or removing the source ends the visit, and
    /// a return to the same folder is a new one — a replacement is not undone
    /// by coming back (SPEC-notices.md §7.2). Compared by reference: two
    /// visits to one folder are different visits.
    /// </summary>
    private sealed class SourceVisit(FilterSourceToken? source)
    {
        /// <summary>The source this visit shows; <see langword="null"/> for none.</summary>
        public FilterSourceToken? Source { get; } = source;
    }

    // The current visit. It only ever moves forward, so a run that has been
    // superseded by a newer visit stays superseded.
    private SourceVisit _visit = new(null);

    /// <summary>
    /// What a run was computed from: the source visit it started in, the
    /// filter values it applied, and the output path it wrote to — plus
    /// whether that filter restricted nothing, the zero-match box's premise.
    /// Captured when the run starts, so the result on screen is read against
    /// its own run, never against whatever is on screen now.
    /// </summary>
    private sealed record RunInputs(
        SourceVisit Visit, FilterConfig Filter, bool FilterRestrictsNothing, string OutputPath);

    // The run whose progress, result or error is on screen; null when none is.
    // A result stands only while its run's source visit is the current one and
    // its filter values are still the current ones (see JudgeResult, and
    // RunAsync's judgement before it publishes a run's outcome).
    private RunInputs? _run;

    /// <summary>
    /// A finished run's skip record, identified with its run: the source visit
    /// it ran in — the record belongs to the run on that visit — and how it
    /// ended, which its label states.
    /// </summary>
    private sealed record LastRunRecord(SourceVisit Visit, SkipRecord Skips, RunEnding Ending)
    {
        /// <summary>The list's label: the run it is the history of, and how that run ended.</summary>
        public string Label => Ending switch
        {
            RunEnding.Cancelled => "The last run (stopped)",
            RunEnding.Failed => "The last run (failed)",
            _ => "The last run",
        };

        /// <summary>The record <paramref name="terminal"/> carries, for a run in <paramref name="visit"/>.</summary>
        public static LastRunRecord Of(SourceVisit visit, ProcessingProgress terminal) =>
            new(visit, terminal.Skipped,
                terminal.ErrorMessage is not null ? RunEnding.Failed
                : terminal.Cancelled ? RunEnding.Cancelled
                : RunEnding.Completed);
    }

    /// <summary>How a run that reached its terminal snapshot ended.</summary>
    private enum RunEnding { Completed, Cancelled, Failed }

    // The last run's skip record; null while there is none. Kept apart from
    // the result (_run, _progress, _runError) because it lives by other rules
    // (SPEC-notices.md §7.2): a filter change keeps it, replacing or removing
    // the source clears it, and the next run replaces it.
    private LastRunRecord? _lastRun;

    // ── The inputs moving ───────────────────────────────────────────────────

    /// <summary>
    /// The source on screen is <paramref name="source"/>: a different one —
    /// replaced or removed — ends the visit, and a return to a folder shown
    /// before starts a new one. The result is then judged against it.
    /// </summary>
    public void ShowSource(FilterSourceToken? source)
    {
        if (source != _visit.Source)
            _visit = new SourceVisit(source);
        JudgeResult();
    }

    /// <summary>
    /// Judges what is on screen against the current inputs — the source
    /// visit, and the filter in effect — when either may have moved.
    /// </summary>
    public void JudgeResult()
    {
        // Drop the run's result the moment it stops describing the current
        // inputs — otherwise "Done — N rows" keeps describing a folder or a
        // filter that is no longer the one on screen. Compared, not edged: an
        // empty filter stays in effect across a folder change, so "a filter is
        // in effect" never falls there, while the source the result was
        // computed from has gone. Not while a run is in hand: a running job's
        // progress and Stop stay with their own job, and its outcome is judged
        // before it is published (RunAsync).
        if (!_busy && _run is { } run && !DescribesCurrentInputs(run))
            DropResult();

        // The skip record belongs to the run on the source visit it ran in:
        // replacing or removing the source clears it, and a filter change
        // does not — it is that run's history, never a verdict on the new
        // filter.
        if (_lastRun is { } lastRun && !ReferenceEquals(lastRun.Visit, _visit))
            _lastRun = null;
    }

    // Whether the run started in the current source visit and the current
    // filter values are still the ones it was computed from. The filter by
    // value: the owner hands a new config on every ask.
    private bool DescribesCurrentInputs(RunInputs run) =>
        ReferenceEquals(run.Visit, _visit) && _filterInEffect() is { } current && current.Equals(run.Filter);

    private void DropResult()
    {
        _run = null;
        _progress = null;
        _runError = null;
    }

    // ── The user's gestures ─────────────────────────────────────────────────

    /// <summary>
    /// The user closed the run error: the occurrence it showed ends, whatever
    /// it was. A Stop failure closed ends, and a persistent-failure notice it
    /// held back is due again at the next failure; a persistent-failure notice
    /// closed stays closed for the rest of its streak.
    /// </summary>
    public void DismissRunError()
    {
        _runError = null;
        if (_stopFailure is not null)
        {
            _stopFailure = null;
            if (_stillTrying == StillTryingNotice.Suppressed)
                _stillTrying = StillTryingNotice.None;
        }
        else if (_stillTrying == StillTryingNotice.Shown)
        {
            _stillTrying = StillTryingNotice.Dismissed;
        }
    }

    /// <summary>
    /// Stop for the run in hand (SPEC-notices.md §7.5): once the job is known,
    /// it cancels it; before then, it waits for the start's answer, and the
    /// run delivers it once the answer admits the job — a refused start ran
    /// nothing (<see cref="RunAsync"/>). The owner sends it only while a run is
    /// in hand.
    /// </summary>
    public async Task StopAsync()
    {
        if (_jobInHand is { } job)
        {
            await DeliverStopAsync(job);
        }
        else
        {
            _stopPending = true;
            _changed();
        }
    }

    /// <summary>
    /// Asks the server to cancel <paramref name="job"/>, the job the run in
    /// hand follows. The answer is that run's alone: one that arrives once the
    /// run has ended, or the next has begun, changes nothing. Delivered, the
    /// page goes on following the job to its stopped result, which says so,
    /// and a Stop failure standing ends; the delivery stands for the job from
    /// then on, so no cancel's failure undoes it, whichever request it
    /// answers. Refused, or not confirmed — any other answer, or none — with
    /// no Stop for the job delivered, the page says so: Stop is never shown as
    /// delivered when it was not, and that stands until a Stop is delivered or
    /// the run ends. The page goes on following, and Stop can be pressed again.
    /// A Stop failure holds back the persistent-failure notice; once a Stop is
    /// delivered, a notice it held back is shown, since requests still fail.
    /// </summary>
    private async Task DeliverStopAsync(JobInHand job)
    {
        string? failure;
        try
        {
            failure = await _jobs.CancelAsync(job.JobId, _lifetime.Token) ? null : StopRefusedMessage;
        }
        catch (Exception ex) when (IsUnreachable(ex) && !_lifetime.IsCancellationRequested)
        {
            failure = StopUnconfirmedMessage;
        }

        if (!ReferenceEquals(job, _jobInHand) || job.StopDelivered)
            return;
        if (failure is not null)
        {
            _stopFailure = failure;
            if (_stillTrying == StillTryingNotice.Shown)
                _stillTrying = StillTryingNotice.Suppressed;
            ShowInFlight();
            return;
        }
        job.ConfirmStopDelivered();
        if (_stopFailure is null)
            return;
        _stopFailure = null;
        if (_stillTrying == StillTryingNotice.Suppressed)
            _stillTrying = StillTryingNotice.Shown;
        ShowInFlight();
    }

    /// <summary>
    /// The coordinator has gone: a run still being followed stops, and nothing
    /// it would show is published. Cancelled, not disposed: a run still in
    /// flight reads its token, and nothing registers on it but the linked
    /// sources each run disposes.
    /// </summary>
    public void End() => _lifetime.Cancel();

    // ── The run ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One Local run (SPEC-notices.md §7.5), of <paramref name="request"/> as
    /// the owner captured it at the click: its identity minted before anything
    /// is sent, the start sent — and retried under the same identity — until
    /// the server answers, the job followed to its terminal result, the
    /// outcome judged and published, an XGP job's commit handed to
    /// <paramref name="followCommit"/>, and only then the job acknowledged.
    /// The run's inputs are captured before the first await.
    /// </summary>
    public async Task RunAsync(ProcessRequest request, Func<XgpExportCommit, Task> followCommit)
    {
        var run = new RunInputs(_visit, request.Filters, request.Filters.Build().IsEmpty, request.OutputPath);
        _run = run;
        _busy = true; _runError = null; _stopFailure = null; _stillTrying = StillTryingNotice.None;
        _progress = null; _jobInHand = null; _stopPending = false;
        // The next run replaces the last one's record: this run's is shown
        // when it ends, never while it is still growing.
        _lastRun = null;
        _changed();

        // What a retry repeats: the identity and the whole request, so the
        // server can tell a repeat from a different run.
        var jobId = _identities.Mint();
        var start = new StartRequest(jobId, request);

        // The run's outcome, held back until it is judged: the terminal
        // snapshot, or the client's own failure to start or follow the job.
        ProcessingProgress? terminal = null;
        string? failure = null;

        try
        {
            switch (await StartAsync(start))
            {
                case StartAnswer.Admitted:
                {
                    var job = _jobInHand = new JobInHand(jobId);
                    // A Stop pressed while the start was unanswered is
                    // delivered now that the job is known, before it is
                    // followed.
                    if (TakePendingStop())
                        await DeliverStopAsync(job);
                    terminal = await FollowAsync(jobId);
                    // The server no longer holds the job — it restarted, say:
                    // following ends, and the page says the result is lost.
                    if (terminal is null && !_lifetime.IsCancellationRequested)
                        failure = UnknownJobMessage;
                    break;
                }
                case StartAnswer.Refused refused:
                    // Nothing ran, a pending Stop included.
                    TakePendingStop();
                    failure = RefusedMessage(refused.Reason);
                    break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The coordinator has gone, and with it everything its run would
            // show.
            return;
        }
        catch (Exception ex)
        {
            failure = $"Error: {ex.Message}";
        }
        finally
        {
            _stopPending = false;
        }

        // The coordinator has gone, and with it everything its run would show;
        // the job's files are on disk under their names.
        if (_lifetime.IsCancellationRequested)
            return;

        // The run has ended: its outcome replaces whatever the run error
        // showed while it went on.
        _busy = false;
        _jobInHand = null;
        _stopFailure = null;
        _stillTrying = StillTryingNotice.None;

        // Judge before publishing: the outcome is shown only while the run's
        // source visit is the current one and the filter values on screen are
        // still the ones it was computed from. A run that completes after its
        // inputs were superseded — the folder changed, even and back again,
        // or the filter, while it ran — is never shown as the new inputs'
        // result, not even for the length of the hand-off below. Once
        // published, JudgeResult drops it the moment the inputs move on.
        if (DescribesCurrentInputs(run))
        {
            _progress = terminal ?? _progress;
            _runError = failure;
        }
        else
        {
            DropResult();
        }

        // The run's skip record is published by its own rule: only while the
        // source visit it ran in is still the current one, whatever the filter
        // now is. A run from a former visit publishes none, and cannot restore
        // one — a return to its folder included.
        _lastRun = terminal is not null && ReferenceEquals(run.Visit, _visit)
            ? LastRunRecord.Of(run.Visit, terminal)
            : null;
        _changed();

        // The job's own accounting, whatever is on screen (§7.2): an XGP
        // job's commit — completed, cancelled and failed alike, since a failed
        // run's files were written too — is followed, and the job is
        // acknowledged only after.
        if (terminal is not null)
        {
            if (terminal.XgpCommit is { } commit)
                await followCommit(commit);
            await AcknowledgeAsync(jobId);
        }
    }

    // The start has been answered: a Stop pressed while it was not is pending
    // no longer — the caller delivers it to an admitted job, and a refused
    // start ran nothing — so the hint that it waits goes with the answer.
    private bool TakePendingStop()
    {
        var pending = _stopPending;
        _stopPending = false;
        if (pending)
            _changed();
        return pending;
    }

    /// <summary>
    /// Sends <paramref name="start"/> until the server answers — admitted or
    /// refused — retrying a request that could not be made under the same
    /// identity, so a lost response finds the same job and a repeat never
    /// runs twice. A failure that persists shows as the run error, saying the
    /// result hasn't arrived yet, and the start goes on; a Stop pressed
    /// meanwhile waits for the answer.
    /// </summary>
    private async Task<StartAnswer> StartAsync(StartRequest start)
    {
        for (var failures = 0; ; )
        {
            try
            {
                var answer = await _jobs.StartAsync(start, _lifetime.Token);
                ContactReturned();
                return answer;
            }
            catch (Exception ex) when (IsUnreachable(ex) && !_lifetime.IsCancellationRequested)
            {
                StillTryingAfter(++failures);
            }
            await Task.Delay(_timing.RetryDelay, _lifetime.Token);
        }
    }

    /// <summary>
    /// Follows the job until its terminal result, retrying any status read
    /// that fails. A failure that persists shows as the run error, saying the
    /// result hasn't arrived yet, and following goes on; it never ends quietly.
    /// </summary>
    /// <returns>The terminal snapshot; <see langword="null"/> when the server does not hold the job, or the coordinator has gone.</returns>
    private async Task<ProcessingProgress?> FollowAsync(string jobId)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                await Task.Delay(failures == 0 ? _timing.PollInterval : _timing.RetryDelay, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            ProcessingProgress? snapshot;
            try
            {
                snapshot = await _jobs.ReadStatusAsync(jobId, _lifetime.Token);
            }
            catch (Exception ex) when (IsUnreachable(ex) && !_lifetime.IsCancellationRequested)
            {
                StillTryingAfter(++failures);
                continue;
            }
            failures = 0;
            ContactReturned();

            if (snapshot is null || snapshot.Complete)
                return snapshot;

            // The running job's own progress, shown with its Stop whatever has
            // changed on screen since it started.
            _progress = snapshot;
            _changed();
        }
    }

    // The persistent-failure run error (StillTryingNotice): due at every
    // failure at or past the threshold, and shown then unless a Stop failure
    // stands in its place — which holds it back — or the user has closed it
    // in this streak. A Stop not delivered stands: the persistent failure
    // never replaces it, and contact returning never takes it down.
    private void StillTryingAfter(int failures)
    {
        if (failures < _timing.FailuresBeforeError || _stillTrying != StillTryingNotice.None)
            return;
        _stillTrying = _stopFailure is null ? StillTryingNotice.Shown : StillTryingNotice.Suppressed;
        ShowInFlight();
    }

    // Contact has returned: the failure streak is over. A notice shown is
    // taken down, and one held back or closed is forgotten, so the next
    // streak begins afresh. A Stop failure stands.
    private void ContactReturned()
    {
        if (_stillTrying == StillTryingNotice.None)
            return;
        _stillTrying = StillTryingNotice.None;
        ShowInFlight();
    }

    // Puts what the run error shows of the run in hand on screen: a Stop
    // failure standing, or else the persistent-failure notice while shown.
    private void ShowInFlight()
    {
        _runError = _stopFailure ?? (_stillTrying == StillTryingNotice.Shown ? StillTryingMessage : null);
        _changed();
    }

    // The page that started the job acknowledges it once it has taken the
    // terminal result. A lost acknowledgement is harmless: the server keeps
    // the result until its process ends, and a replay changes nothing.
    private async Task AcknowledgeAsync(string jobId)
    {
        try { await _jobs.AcknowledgeAsync(jobId, _lifetime.Token); }
        catch (Exception ex) when (IsUnreachable(ex) || ex is OperationCanceledException) { }
    }

    // A request that could not be made or answered: retried, never a result.
    private static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException;

    private const string StillTryingMessage =
        "Can't reach the server. This run's result hasn't arrived yet — still trying…";

    private const string UnknownJobMessage =
        "The server no longer has this run — it may have restarted — so its result can't be shown.";

    // Stop that the server refused: it holds no running job of that identity.
    private const string StopRefusedMessage =
        "The server didn't stop the run: it had already ended, or the server no longer has it.";

    // Stop the page cannot confirm: any answer but delivered or refused, or
    // none (Hal's wording, 2026-10-12).
    private const string StopUnconfirmedMessage =
        "Couldn't confirm the Stop request. The run may still be going. Press Stop to try again.";

    // The page says why a refused start started nothing.
    private static string RefusedMessage(RefusalReason reason) => reason switch
    {
        RefusalReason.ChangedRequest =>
            "The server refused the run: its identity was already used for a different run.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A refusal the page has no words for."),
    };
}
