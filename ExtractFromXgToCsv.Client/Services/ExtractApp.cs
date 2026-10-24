using System.Globalization;
using System.Net.Http.Json;
using BgUiPrimitives_Razor;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;

namespace ExtractFromXgToCsv.Client.Services;

/// <summary>The one operation the app can have in flight.</summary>
internal enum ExtractPhase
{
    /// <summary>The settings are being read back from browser storage; every command waits for it.</summary>
    Restoring,

    Idle,

    /// <summary>
    /// A Local run, from the click until its job is acknowledged: the start,
    /// following, the result published, the commit followed and the
    /// acknowledgement answered or failed.
    /// </summary>
    Running,
}

/// <summary>
/// The app's one coordinator (halheinrich/backgammon#424, on BgQuiz_Blazor's
/// <c>QuizApp</c>): it owns the phase — the one operation in flight — the
/// settings and their browser storage, the source reported to the filter
/// setup, and the Local run, held privately in a
/// <see cref="LocalRunController"/>. Pages render it and send commands; a
/// command that does not fit the phase is refused before any await, so nothing
/// overlaps.
/// </summary>
/// <remarks>
/// <para>
/// <b>Settings while a run goes.</b> The settings commands stay open while
/// <see cref="ExtractPhase.Running"/>: the run captured its inputs at the
/// click, and its result is judged against the inputs when it ends
/// (<c>SPEC-notices.md</c> §7.2), so an edit is not a second operation. They
/// are refused only while <see cref="ExtractPhase.Restoring"/>.
/// </para>
/// <para>
/// <b>Two facts about a run.</b> The run in hand (<see cref="IsRunInHand"/>)
/// lasts from the click until the run's outcome is judged and published; it
/// drives Stop, the busy cursor and "Starting…", and exempts a result from the
/// judgement. The phase lasts the whole operation, through the commit followed
/// and the acknowledgement, and it alone refuses another Run — so no Run can
/// capture a next number the commit is about to move.
/// </para>
/// <para>
/// One per app scope: one per loaded app under WebAssembly. Not thread-safe:
/// every member is called on the renderer's synchronization context, as
/// <see cref="FilterSetup"/>'s are.
/// </para>
/// </remarks>
internal sealed class ExtractApp : IDisposable
{
    private const string OutputFormatKey = "xg_outputFormat";
    private const string FolderPathKey = "xg_folderPath";
    private const string OutputPathKey = "xg_outputPath";
    private const string XgpPatternKey = "xg_xgpPattern";
    private const string XgpNextNumberKey = "xg_xgpNextNumber";
    private const string XgpSuffixLengthKey = "xg_xgpSuffixLength";
    private const string XgpAnonymizeKey = "xg_xgpAnonymize";

    private readonly ReportingBrowserStorage _storage;
    private readonly FilterSetup _filterSetup;
    private readonly HttpClient _http;
    private readonly LocalRunController _run;
    private readonly HttpDocumentStorage _documentStorage;
    private readonly IDisposable _filterAttachment;

    // The one restore of this app, started by the first call to
    // EnsureRestoredAsync and shared by every later one.
    private Task? _restore;

    // The source is the input folder path. Two fields on purpose: FolderPath
    // follows every keystroke (the panel's input is controlled; persistence
    // stays per-keystroke), while _localSourcePath moves only at the commit
    // boundary — the source must never change per keystroke.
    private string? _localSourcePath;

    // The pattern and next number the control was last remembered under —
    // restored, or moved to by a run's commit (FollowCommitAsync). A pattern
    // edit continues that number while the pattern is the one it was
    // remembered under (ApplyPatternEditAsync). It decides nothing about
    // whether a commit is followed: that is the edit flag's, below.
    private (string Pattern, int NextNumber) _remembered;

    // Whether the user has edited the pattern or the next number since the
    // current XGP run captured its options (SPEC-notices.md §7.5: "has changed
    // the control"). An edit, not a value: a number changed away and back has
    // been changed, so the run's commit does not move it. Cleared as an XGP
    // run captures its options (RunAsync); set by the pattern and next-number
    // commands.
    private bool _editedSinceRunStarted;

    /// <summary>The coordinator over the app's storage, its filter setup and its server.</summary>
    /// <param name="storage">The reporting access this app's own settings keys go through.</param>
    /// <param name="filterSetup">The filter surface's owner, to which the source is reported.</param>
    /// <param name="http">The client for the server's book status and saved-filters relay.</param>
    /// <param name="jobs">The job protocol's wire.</param>
    /// <param name="timing">How patiently a run starts and follows its job.</param>
    /// <param name="identities">Mints each start's identity.</param>
    public ExtractApp(
        ReportingBrowserStorage storage, FilterSetup filterSetup, HttpClient http,
        ILocalJobs jobs, LocalJobTiming timing, IJobIdentities identities)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _filterSetup = filterSetup ?? throw new ArgumentNullException(nameof(filterSetup));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _run = new LocalRunController(jobs, timing, identities, () => FilterInEffect, () => Changed?.Invoke());
        _remembered = (XgpOptions.NamePattern, XgpOptions.StartNumber);

        // The saved-filters storage seam, created once over a delegate reading
        // the latched folder: FilterSurface rebuilds its store when the bound
        // Storage *reference* changes, so the live-folder fact must ride the
        // delegate, not a fresh instance.
        _documentStorage = new HttpDocumentStorage(_http, () => _localSourcePath);

        // The filter in effect moves without any command here — a commit or
        // an edit on the filter surface — and the result is judged against it.
        // The judgement is cheap and starts nothing.
        _filterAttachment = _filterSetup.Attach(_ => OnFilterMoved());
    }

    // ── State pages render ──────────────────────────────────────────────────

    public ExtractPhase Phase { get; private set; } = ExtractPhase.Restoring;

    public OutputFormat OutputFormat { get; private set; } = OutputFormat.Csv;

    /// <summary>The folder path as typed; the source moves only when it is committed.</summary>
    public string FolderPath { get; private set; } = string.Empty;

    /// <summary>The output path — a file, or for XGP a folder.</summary>
    public string OutputPath { get; private set; } = string.Empty;

    /// <summary>The XGP naming options a run would capture now; their defaults are the record's own.</summary>
    public XgpExportOptions XgpOptions { get; private set; } = new();

    /// <summary>Whether an XGP run rewrites every exported position's player names.</summary>
    public bool Anonymize { get; private set; }

    /// <summary>
    /// The server's opening-book load state, so the status line can tell the
    /// user which analysis-depth stamps to expect; null until it has arrived,
    /// or if fetching it failed — the line shows only what is known.
    /// </summary>
    public OpeningBookStatus? BookStatus { get; private set; }

    /// <summary>
    /// The current source's token — the one property both the reports and the
    /// reads use: the latched folder path's. Null while no source exists (a
    /// blank path). The output path is not a source and never appears here.
    /// </summary>
    /// <remarks>
    /// The path goes to <see cref="FilterSourceToken.FromPath"/> in the
    /// spelling this app holds: what counts as the same folder is a property
    /// of paths, so the factory owns it (halheinrich/backgammon#94). The
    /// latched path itself keeps the user's spelling, for IO and display.
    /// </remarks>
    public FilterSourceToken? Source =>
        _localSourcePath is { } path ? FilterSourceToken.FromPath(path) : null;

    /// <summary>
    /// The filter in effect for the source on screen right now, or
    /// <see langword="null"/> when none is — read from the owner on each ask
    /// (<see cref="FilterSetupSnapshot.ConfigInEffectFor"/>, a new instance
    /// each call, so compare it by value). Source-relative by construction: a
    /// setup whose source is not this app's current one reads as nothing in
    /// effect. The ready empty selection is in effect without Apply
    /// (halheinrich/backgammon#266); this app adds no exception of its own.
    /// </summary>
    public FilterConfig? FilterInEffect =>
        Source is { } source ? _filterSetup.Current.ConfigInEffectFor(source) : null;

    /// <summary>
    /// The filter surface's saved-filters storage: the HTTP relay while a
    /// folder is latched to address, null otherwise — a blank path must render
    /// no saved-filters section, never a load failure.
    /// </summary>
    public IDocumentStorage? SavedFiltersStorage =>
        _localSourcePath is not null ? _documentStorage : null;

    // The run's facts, read through here so pages hold no run of their own.

    /// <summary>Whether a run is in hand: from the click until its outcome is judged and published.</summary>
    public bool IsRunInHand => _run.IsRunInHand;

    public ProcessingProgress? Progress => _run.Progress;

    public string? RunError => _run.RunError;

    /// <summary>Whether a Stop pressed before the start was answered still waits for the answer.</summary>
    public bool IsStopPending => _run.IsStopPending;

    /// <summary>Whether a delivered Stop is under way: from its confirmed delivery until the run's terminal result.</summary>
    public bool IsStopping => _run.IsStopping;

    public SkipRecord? LastRunSkips => _run.LastRunSkips;

    public string? LastRunLabel => _run.LastRunLabel;

    /// <summary>Whether the filter of the run on screen restricted anything: the zero-match notice's premise.</summary>
    public bool RunFilterRestricted => _run.RunFilterRestricted;

    /// <summary>
    /// Run's gate reason while a source is held: a selection on screen that
    /// needs Apply. Without a source nothing can be in effect, the empty filter
    /// included, and the blank folder box already says why Run is dark
    /// (halheinrich/backgammon#266).
    /// </summary>
    public bool AwaitsApply => Source is not null && FilterInEffect is null;

    /// <summary>Run's gate reason for XGP: the naming options do not validate.</summary>
    public bool XgpOptionsInvalid =>
        OutputFormat == OutputFormat.Xgp && !XgpOptions.TryValidate(out _);

    /// <summary>
    /// The Run gate — the button's and the command's one definition, so a
    /// click that reaches the run past a dark button starts nothing: no
    /// operation in flight, a folder and an output path, a filter in effect,
    /// and valid options for XGP. Nothing about the server: the start is the
    /// first contact.
    /// </summary>
    public bool CanRun =>
        Phase == ExtractPhase.Idle
        && !string.IsNullOrWhiteSpace(FolderPath)
        && !string.IsNullOrWhiteSpace(OutputPath)
        && FilterInEffect is not null
        && !XgpOptionsInvalid;

    /// <summary>Raised whenever anything here moves, on the renderer's context.</summary>
    public event Action? Changed;

    // ── The restore ─────────────────────────────────────────────────────────

    /// <summary>
    /// Read every setting back from browser storage, once per app: the first
    /// call starts the restore and every call returns that same task. It ends
    /// by reporting the source to the filter setup and entering
    /// <see cref="ExtractPhase.Idle"/>. A page's going changes nothing: a
    /// restore in flight completes for the app, and a refusal still reaches
    /// the storage condition.
    /// </summary>
    public Task EnsureRestoredAsync() => _restore ??= RestoreAsync();

    private async Task RestoreAsync()
    {
        // The book status is the server's, not a setting: its one fetch starts
        // with the restore, and nothing waits on it.
        _ = FetchBookStatusAsync();

        // Every read below answers the stored value, or null for "nothing
        // stored" and for a refusal alike — ReportingBrowserStorage has
        // reported a refusal to the condition before it returns — so each key
        // lands on its stored value or its own documented default.
        async Task<string?> ReadSettingAsync(string key, string consequence = "the setting takes its default for this visit")
        {
            var read = await _storage.ReadAsync(BrowserStorageArea.Local, key, consequence);
            return read.IsStored ? read.Value : null;
        }

        // The inverse of what persists it (format.ToString()): resolve the
        // stored token by searching the declared formats, never by parsing.
        // Enum.TryParse would also accept a numeric ordinal, tying a durable
        // localStorage entry to member numbering (halheinrich/backgammon#164);
        // an unrecognized token leaves the default standing, as before.
        var savedFormat = await ReadSettingAsync(OutputFormatKey);
        foreach (var candidate in Enum.GetValues<OutputFormat>())
        {
            if (string.Equals(savedFormat, candidate.ToString(), StringComparison.Ordinal))
            {
                OutputFormat = candidate;
                break;
            }
        }

        // Restore the folder path and latch it: a restored path is a committed
        // value — the saved-filters context loads for it, and the source is
        // minted from it — not a keystroke in flight.
        FolderPath = await ReadSettingAsync(FolderPathKey) ?? string.Empty;
        LatchSourceFolder(FolderPath);

        OutputPath = await ReadSettingAsync(OutputPathKey, "the output path starts blank for this visit") ?? string.Empty;

        // The XGP numbering is three remembered controls (SPEC-notices.md
        // §7.5): the pattern, the next number and the number length, each
        // under its own key, as written when it last changed. No older key is
        // read: there is no migration.
        if (await ReadSettingAsync(XgpPatternKey) is { } savedPattern)
            XgpOptions = XgpOptions with { NamePattern = savedPattern };
        if (TryParseStoredNumber(await ReadSettingAsync(XgpNextNumberKey), out var savedNext))
            XgpOptions = XgpOptions with { StartNumber = savedNext };
        if (TryParseStoredNumber(await ReadSettingAsync(XgpSuffixLengthKey), out var savedLength))
            XgpOptions = XgpOptions with { SuffixLength = savedLength };
        _remembered = (XgpOptions.NamePattern, XgpOptions.StartNumber);
        var savedAnonymize = await ReadSettingAsync(XgpAnonymizeKey);
        Anonymize = bool.TryParse(savedAnonymize, out var anon) && anon;

        ConformOutputPath();

        // Last, after the restore's every read: the source is known now, so
        // it is shown and reported — never before, since ReportSource(null)
        // is a real answer that ends an existing setup, and "not yet known" is
        // not "none".
        _run.ShowSource(Source);
        _filterSetup.ReportSource(Source);
        Phase = ExtractPhase.Idle;
        Changed?.Invoke();
    }

    // Server-side opening-book load state (Local mode only). A failure leaves
    // it unknown rather than asserting a state we don't know. No command
    // reads it, so nothing can race it.
    private async Task FetchBookStatusAsync()
    {
        try { BookStatus = await _http.GetFromJsonAsync<OpeningBookStatus>("/api/openingbook/status"); }
        catch { BookStatus = null; }
        Changed?.Invoke();
    }

    // ── Settings ────────────────────────────────────────────────────────────

    public async Task SetOutputFormatAsync(OutputFormat format)
    {
        if (Phase == ExtractPhase.Restoring) return;
        OutputFormat = format;
        ConformOutputPath();
        Changed?.Invoke();
        await _storage.WriteAsync(BrowserStorageArea.Local, OutputFormatKey, format.ToString(),
            "the output format holds for this visit only");
    }

    /// <summary>
    /// The per-keystroke half of the folder input: follow the text and
    /// persist it — never latch, never report. A half-typed path is not a
    /// source change.
    /// </summary>
    public async Task EditFolderPathAsync(string path)
    {
        if (Phase == ExtractPhase.Restoring) return;
        FolderPath = path;
        Changed?.Invoke();
        await _storage.WriteAsync(BrowserStorageArea.Local, FolderPathKey, path,
            "the folder path holds for this visit only");
    }

    /// <summary>
    /// The commit-boundary half: latch the committed path, show it and report
    /// it. A blur that commits the same folder reports the same token and
    /// ends nothing.
    /// </summary>
    public void CommitFolderPath(string path)
    {
        if (Phase == ExtractPhase.Restoring) return;
        LatchSourceFolder(path);
        _run.ShowSource(Source);
        _filterSetup.ReportSource(Source);
        Changed?.Invoke();
    }

    private void LatchSourceFolder(string path)
    {
        var trimmed = path.Trim();
        _localSourcePath = trimmed.Length == 0 ? null : trimmed;
    }

    public async Task SetOutputPathAsync(string path)
    {
        if (Phase == ExtractPhase.Restoring) return;
        OutputPath = path;
        Changed?.Invoke();
        await _storage.WriteAsync(BrowserStorageArea.Local, OutputPathKey, path,
            "the output path holds for this visit only");
    }

    // The output path's extension follows the format at three moments: the
    // format changing, the restore ending, and a run capturing its inputs — so
    // a run never writes a deck or a CSV under the wrong extension, and never
    // mid-typing (Hal ruled the timing 2026-10-22, halheinrich/backgammon#424).
    // Xgp is excluded: its output path is a folder, so no extension is forced
    // onto (or stripped from) whatever the user typed. Nothing waits on the
    // write.
    private void ConformOutputPath()
    {
        if (string.IsNullOrWhiteSpace(OutputPath) || OutputFormat == OutputFormat.Xgp)
            return;
        var expectedExt = OutputFormat switch
        {
            OutputFormat.DiagramJson => ".json",
            OutputFormat.Pptx        => ".pptx",
            OutputFormat.Pdf         => ".pdf",
            _                        => ".csv",
        };
        if (OutputPath.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
            return;
        OutputPath = Path.ChangeExtension(OutputPath, expectedExt);
        _ = _storage.WriteAsync(BrowserStorageArea.Local, OutputPathKey, OutputPath,
            "the output path's new extension holds for this visit only");
    }

    public Task EditPatternAsync(string pattern) =>
        Phase == ExtractPhase.Restoring ? Task.CompletedTask : ApplyPatternEditAsync(pattern);

    /// <summary>
    /// Append the token <paramref name="name"/> to the pattern. Caret-position
    /// insertion is deliberately out of scope.
    /// </summary>
    public Task InsertTokenAsync(string name) =>
        Phase == ExtractPhase.Restoring || string.IsNullOrEmpty(name)
            ? Task.CompletedTask
            : ApplyPatternEditAsync(XgpOptions.NamePattern + "{" + name + "}");

    private async Task ApplyPatternEditAsync(string pattern)
    {
        _editedSinceRunStarted = true;
        // Continue the remembered numbering only while the pattern is the one
        // it was remembered under; a different pattern starts a fresh series
        // at 1. The user can still override the number afterwards.
        var next = pattern == _remembered.Pattern ? _remembered.NextNumber : 1;
        var nextChanged = next != XgpOptions.StartNumber;
        // Every control change before any storage await: a next number the
        // user sets while a write is pending is never overwritten by this one.
        XgpOptions = XgpOptions with { NamePattern = pattern, StartNumber = next };
        Changed?.Invoke();
        await _storage.WriteAsync(BrowserStorageArea.Local, XgpPatternKey, pattern,
            "the name pattern holds for this visit only");
        if (nextChanged)
            await WriteNextNumberAsync();
    }

    /// <summary>
    /// The user's edit of the next number: <paramref name="number"/>, or null
    /// for text that is not one. Either way it is an edit — the control has
    /// been changed since a run captured it — but only a number moves it.
    /// </summary>
    public async Task EditNextNumberAsync(int? number)
    {
        if (Phase == ExtractPhase.Restoring) return;
        _editedSinceRunStarted = true;
        if (number is { } n)
            await SetNextNumberAsync(n);
    }

    public async Task SetSuffixLengthAsync(int length)
    {
        if (Phase == ExtractPhase.Restoring) return;
        XgpOptions = XgpOptions with { SuffixLength = length };
        Changed?.Invoke();
        await _storage.WriteAsync(BrowserStorageArea.Local, XgpSuffixLengthKey, FormatStoredNumber(length),
            "the number length holds for this visit only");
    }

    // Anonymize is independent of the naming options (XgpExportOptions), so it
    // isn't one of them: it rides in the request beside them and persists
    // under its own key.
    public async Task SetAnonymizeAsync(bool anonymize)
    {
        if (Phase == ExtractPhase.Restoring) return;
        Anonymize = anonymize;
        Changed?.Invoke();
        await _storage.WriteAsync(BrowserStorageArea.Local, XgpAnonymizeKey, Anonymize.ToString(),
            "the anonymize choice holds for this visit only");
    }

    // The one place the next-number control changes, whoever changes it —
    // the user, a pattern edit or a run's commit — and is remembered.
    private async Task SetNextNumberAsync(int next)
    {
        XgpOptions = XgpOptions with { StartNumber = next };
        Changed?.Invoke();
        await WriteNextNumberAsync();
    }

    // Remembers what the next-number control holds at the write — never a
    // value read before an await, so a newer choice is never written over.
    private async Task WriteNextNumberAsync() =>
        await _storage.WriteAsync(BrowserStorageArea.Local, XgpNextNumberKey, FormatStoredNumber(XgpOptions.StartNumber),
            "the next number holds for this visit only");

    // A number as it is stored, and read back: invariant digits, so a stored
    // value means the same whatever the browser's culture. A value that is
    // not one leaves the control at its default.
    private static string FormatStoredNumber(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseStoredNumber(string? stored, out int number) =>
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    // ── The run ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run: one Local run, as the one operation in flight from the click until
    /// its job is acknowledged (SPEC-notices.md §7.5). The run captures its
    /// inputs here, before any await — the output path's extension following
    /// the format first — and an XGP run's capture begins the watch for an
    /// edit of the pattern or the next number, which keeps the control from
    /// following its commit.
    /// </summary>
    public async Task RunAsync()
    {
        // The gate's own definition: a stale click past it has nothing to
        // run. It guarantees a filter in effect.
        if (!CanRun || FilterInEffect is not { } filter)
            return;

        Phase = ExtractPhase.Running;
        Changed?.Invoke();
        try
        {
            ConformOutputPath();
            var request = new ProcessRequest
            {
                FolderPath = FolderPath,
                OutputPath = OutputPath,
                Filters = filter,
                OutputFormat = OutputFormat,
                XgpOptions = XgpOptions,
                Anonymize = Anonymize,
            };

            // The options are captured: from here an edit of the pattern or the
            // next number is the user's choice over this run's commit.
            if (OutputFormat == OutputFormat.Xgp)
                _editedSinceRunStarted = false;

            await _run.RunAsync(request, FollowCommitAsync);
        }
        finally
        {
            Phase = ExtractPhase.Idle;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Stop the run in hand. With none in hand — none started, or one whose
    /// result is already published while its commit or acknowledgement is
    /// still pending — it does nothing: no cancel is sent and nothing says a
    /// Stop is pending.
    /// </summary>
    public Task StopAsync() => _run.IsRunInHand ? _run.StopAsync() : Task.CompletedTask;

    /// <summary>The user closed the run error.</summary>
    public void DismissRunError()
    {
        _run.DismissRunError();
        Changed?.Invoke();
    }

    /// <summary>
    /// The next-number control follows what an XGP run committed
    /// (SPEC-notices.md §7.5) — completed, cancelled or failed alike — to the
    /// number after the last the run used, and remembers it, unless the user
    /// has edited the pattern or the next number since the run captured its
    /// options. An edit, not a value: §7.5 says "has changed the control", so
    /// a number changed away and back to the run's start was the user's
    /// choice and stands (the umbrella's correction, ChatGPT's review
    /// 6331375637, accepted 6331396606). A run that committed nothing moves
    /// nothing. The number length decides nothing here: it says nothing about
    /// which number comes next. The run hands its commit here before its job
    /// is acknowledged.
    /// </summary>
    private async Task FollowCommitAsync(XgpExportCommit commit)
    {
        if (commit.CommittedNothing || _editedSinceRunStarted)
            return;
        _remembered = (XgpOptions.NamePattern, commit.NextNumber);
        await SetNextNumberAsync(commit.NextNumber);
    }

    // The filter in effect may have moved: the result is judged against it,
    // and pages re-render.
    private void OnFilterMoved()
    {
        _run.JudgeResult();
        Changed?.Invoke();
    }

    /// <summary>The app has gone: the filter attachment ends, and a run being followed stops.</summary>
    public void Dispose()
    {
        _filterAttachment.Dispose();
        _run.End();
    }
}
