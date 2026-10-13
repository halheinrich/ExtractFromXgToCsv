using System.Net.Http.Json;
using System.Text.Json;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// A Local XGP run's accounting survives losing the job (the umbrella's
/// <c>SPEC-notices.md</c> §7.5, halheinrich/backgammon#397), driven through
/// real pages — Home with the real panel — over the real server, at each of
/// the protocol's failure points: a lost start response, a delayed start in
/// both orders, a reload while a job runs, refused or interrupted storage, a
/// lost acknowledgement, a stale tab, a job owed from an earlier instance,
/// reconciliation that cannot be reached, and a status read that keeps
/// failing.
/// </summary>
/// <remarks>
/// Each page is a <see cref="LocalPage"/>: its own app over one
/// <see cref="LocalServer"/> and one browser storage, so a reload — or a
/// second tab — is another page over the same two. The server's XGP runs
/// write the one fixture match's decisions as <c>pos001.xgp</c> onwards.
/// </remarks>
public class LocalRecoveryTests : IAsyncLifetime
{
    private const string NumberingKey = HomeStoragePlan.NumberingKey;

    private readonly LocalServer _server = new();
    private readonly MemoryBrowserStorage _storage = new();
    private readonly SequentialJobIdentities _identities = new();
    private readonly List<LocalPage> _pages = [];

    // The fixture match's decisions: what one XGP run writes.
    private int _decisions;
    private string _instance = default!;

    public async Task InitializeAsync()
    {
        _storage[HomeStoragePlan.FolderPathKey] = _server.InputFolder;
        _storage[HomeStoragePlan.OutputPathKey] = _server.OutputFolder;
        _storage[HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Xgp);

        using var client = _server.Client();
        _instance = (await client.GetFromJsonAsync<LedgerSnapshot>("/api/process/ledger"))!.Instance;
        _decisions = ConvertXgToJson_Lib.XgDecisionIterator.Iterate(
            ConvertXgToJson_Lib.XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, "MTCH4064.xg")), "MTCH4064.xg").Count();
    }

    public async Task DisposeAsync()
    {
        _server.Gate.Open();
        foreach (var page in _pages)
            await page.DisposeAsync();
        _server.Dispose();
    }

    private LocalPage Open(bool reconciled = true, Func<ILocalJobs, ILocalJobs>? jobs = null)
    {
        var page = LocalPage.Open(_server, _storage, _identities, reconciled, jobs);
        _pages.Add(page);
        return page;
    }

    /// <summary>A page whose output format is CSV, written beside the XGP folder.</summary>
    private LocalPage OpenCsv()
    {
        _storage[HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Csv);
        _storage[HomeStoragePlan.OutputPathKey] = _server.CsvOutputPath;
        return Open();
    }

    /// <summary>A reload: the page goes, and its app with it, before a new one opens over the same server and storage.</summary>
    private async Task<LocalPage> ReloadAsync(LocalPage page, bool reconciled = true)
    {
        _pages.Remove(page);
        await page.DisposeAsync();
        return Open(reconciled);
    }

    /// <summary>Restarts the server: a new instance, which every page reaches next.</summary>
    private async Task RestartAsync()
    {
        _server.Restart();
        _instance = (await LedgerAsync()).Instance;
    }

    /// <summary>The stored numbering record, parsed; null when none is stored.</summary>
    private JsonElement? Record =>
        _storage[NumberingKey] is { } text ? JsonDocument.Parse(text).RootElement : null;

    private static int? LastNumber(JsonElement record) =>
        record.TryGetProperty("numbering", out var numbering) ? numbering.GetProperty("lastNumber").GetInt32() : null;

    private static int? MarkNumber(JsonElement record) =>
        record.TryGetProperty("mark", out var mark) ? mark.GetProperty("number").GetInt32() : null;

    private static string[] OwedIds(JsonElement record) =>
        [.. record.GetProperty("owed").EnumerateArray().Select(job => job.GetProperty("jobId").GetString()!)];

    private async Task<LedgerSnapshot> LedgerAsync()
    {
        using var client = _server.Client();
        return (await client.GetFromJsonAsync<LedgerSnapshot>("/api/process/ledger"))!;
    }

    private static string Hint(LocalPage page) =>
        page.Home.FindAll("#startHint") is [var hint] ? hint.TextContent.Trim() : string.Empty;

    private int Starts => _server.Wire.Requests.Count(r => r == "POST /api/process/start");

    /// <summary>
    /// One XGP job run straight against the server, as a page elsewhere would,
    /// touching none of this browser's storage — into its own folder, so its
    /// files never meet this test's pages' — and awaited to its end.
    /// </summary>
    private Task ExportElsewhereAsync(string jobId, LedgerMark? mark, XgpExportOptions? options = null) =>
        _server.ExportAsync(jobId, _instance, mark, options);

    // ── A lost start response ───────────────────────────────────────────────

    [Fact]
    public async Task ALostStartResponse_IsRetried_FindsTheSameJob_AndItsAccountingIsAppliedOnce()
    {
        var page = Open();
        _server.Wire.Drop(ProtocolWire.Start);

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(2, _server.Wire.Requests.Count(r => r == "POST /api/process/start"));
        Assert.Single((await LedgerAsync()).Entries);
        Assert.Equal(_decisions, _server.XgpFiles().Count);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
        var record = Record!.Value;
        Assert.Equal(_decisions, LastNumber(record));
        Assert.Equal(1, MarkNumber(record));
        Assert.Empty(OwedIds(record));
        Assert.Contains("Done", page.Home.Markup);
    }

    // ── A delayed start, in both orders ─────────────────────────────────────

    /// <summary>
    /// Settling wins: the first page's start is held on the wire; another page
    /// reconciles, finds the owed job with no entry, and settles it — retired,
    /// resolved. The held start, delivered after, is refused, so no export
    /// runs; the first page says why.
    /// </summary>
    [Fact]
    public async Task ADelayedStart_SettledFirst_IsRetired_AndNoExportStarts()
    {
        var first = Open();
        var held = _server.Wire.Hold(ProtocolWire.Start);
        var run = first.RunAsync();
        await held.Reached.WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(["job-1"], OwedIds(Record!.Value));

        var second = Open();
        second.WaitFor(() => second.StartState.XgpBlock is null);
        Assert.Empty(OwedIds(Record!.Value));

        held.Release();
        await run.WaitAsync(NoticeTesting.OperationBound);

        Assert.Empty((await LedgerAsync()).Entries);
        Assert.Empty(_server.XgpFiles());
        Assert.Contains("This run request can no longer start. Run again to start a new one.",
            Assert.Single(NoticeBox.AllIn(first.Home), b => b.Kind == NoticeKind.Error).Content.TextContent);
        Assert.Empty(OwedIds(Record!.Value));
    }

    /// <summary>
    /// The start wins: the other page's ledger read finds no entry, then the
    /// held start is delivered, then the settle — admitted. That page keeps the
    /// job owed and XGP blocked while it runs, follows it, and applies its
    /// accounting once.
    /// </summary>
    [Fact]
    public async Task ADelayedStart_AdmittedFirst_StaysOwed_IsFollowed_AndAppliedOnce()
    {
        var first = Open();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var run = first.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);

        _server.Gate.Close();
        var heldSettle = _server.Wire.Hold(ProtocolWire.Settle);
        var second = Open(reconciled: false);
        await heldSettle.Reached.WaitAsync(NoticeTesting.OperationBound);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        heldSettle.Release();
        await heldSettle.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        // Admitted: still owed, and no XGP start may be sent while it runs.
        second.WaitFor(() => second.StartState.Instance is not null);
        Assert.NotNull(second.StartState.XgpBlock);
        Assert.Equal(["job-1"], OwedIds(Record!.Value));

        _server.Gate.Open();
        second.WaitFor(() => second.StartState.XgpBlock is null);
        await run.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(_decisions + 1, second.Options.StartNumber);
        Assert.Equal(_decisions + 1, first.Options.StartNumber);
        var record = Record!.Value;
        Assert.Equal(_decisions, LastNumber(record));
        Assert.Equal(1, MarkNumber(record));
        Assert.Empty(OwedIds(record));
        Assert.Equal(_decisions, _server.XgpFiles().Count);
    }

    // ── A reload while an XGP job runs ──────────────────────────────────────

    [Fact]
    public async Task AReloadWhileAnXgpJobRuns_BlocksXgp_AndAppliesItsAccountingWhenItEnds_ShowingNothingElse()
    {
        var before = Open();
        _server.Gate.Close();
        var run = before.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        var reloaded = await ReloadAsync(before);
        Assert.Equal(XgpStartBlock.Running, reloaded.StartState.XgpBlock);
        reloaded.WaitForScreen(() =>
        {
            Assert.Equal("An XGP export is running; wait for it to finish.", Hint(reloaded));
            Assert.True(reloaded.RunButton.HasAttribute("disabled"));
        });

        _server.Gate.Open();
        reloaded.WaitFor(() => reloaded.StartState.XgpBlock is null);

        Assert.Equal(_decisions + 1, reloaded.Options.StartNumber);
        Assert.Equal(1, MarkNumber(Record!.Value));
        // Its accounting, and nothing else: the job was the earlier page's.
        Assert.DoesNotContain("Done", reloaded.Home.Markup);
        Assert.Empty(reloaded.Home.FindAll("div.skipped-notice"));
        await run.WaitAsync(NoticeTesting.OperationBound);
    }

    // ── Refused or interrupted accounting storage ───────────────────────────

    [Fact]
    public async Task ARefusedCheckpoint_IsAppliedInMemory_ExportStaysUsable_AndAReloadAppliesTheEntryAgain()
    {
        var page = Open();
        _storage.RefuseWrites = true;

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        // Applied in this page: the numbering and the mark moved, and export
        // goes on — the next run is admitted.
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
        Assert.Equal(new LedgerMark(_instance, 1), page.StartState.Mark);
        Assert.Null(page.StartState.XgpBlock);
        Assert.Single(page.Home.FindAll("#storageUnavailableNotice"));
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(2 * _decisions, _server.XgpFiles().Count);
        Assert.Null(_storage[NumberingKey]);

        // A reload in the same instance applies the entries again, from the
        // ledger.
        _storage.RefuseWrites = false;
        var reloaded = await ReloadAsync(page);
        Assert.Equal(2 * _decisions + 1, reloaded.Options.StartNumber);
        Assert.Equal(2, MarkNumber(Record!.Value));
        Assert.Equal(2 * _decisions, LastNumber(Record!.Value));
    }

    [Fact]
    public async Task AFailedWrite_LeavesTheOldDocumentWhole_NumbersAndMarkTogether()
    {
        var page = Open();
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        var afterTheFirst = _storage[NumberingKey];
        Assert.Equal(1, MarkNumber(Record!.Value));

        _storage.RefuseWrites = true;
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(afterTheFirst, _storage[NumberingKey]);
        Assert.Equal(2 * _decisions + 1, page.Options.StartNumber);
    }

    // ── A lost acknowledgement, then recovery after newer accounting ────────

    [Fact]
    public async Task ALostAcknowledgement_ThenRecoveryAfterNewerAccounting_NeverRollsBack_AndNewerChoicesStand()
    {
        var page = Open();
        _server.Wire.Fail(ProtocolWire.Acknowledge, times: 1);
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        // The first job's acknowledgement was lost: the server still holds it.
        using (var client = _server.Client())
            Assert.True((await client.GetFromJsonAsync<ProcessingProgress>("/api/process/job-1/status"))!.Complete);

        // A reconciliation that finds both entries again, the first behind the
        // mark: the replay changes nothing — not even for a moment, so the
        // record is not written at all — and the newer choice stands.
        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "77" });
        var writes = _storage.Writes.Count(write => write.Key == NumberingKey);
        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());
        Assert.Equal(writes, _storage.Writes.Count(write => write.Key == NumberingKey));
        Assert.Equal(77, page.Options.StartNumber);
        Assert.Equal(2 * _decisions, LastNumber(Record!.Value));
        Assert.Equal(2, MarkNumber(Record!.Value));

        var reloaded = await ReloadAsync(page);
        Assert.Equal(2 * _decisions + 1, reloaded.Options.StartNumber);
        Assert.Equal(2, MarkNumber(Record!.Value));
    }

    // ── Admission ───────────────────────────────────────────────────────────

    /// <summary>
    /// A stale tab — open before another tab's export — is refused: its mark
    /// does not name the latest entry. It says why, reconciles, takes the
    /// other export's accounting, and its next run is admitted, numbered on
    /// from it.
    /// </summary>
    [Fact]
    public async Task AStaleTab_IsRefused_ThenReconciles_AndItsNextRunIsAdmitted()
    {
        var fresh = Open();
        var stale = Open();
        await fresh.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        await stale.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Contains("Another XGP export finished since this page last counted",
            Assert.Single(NoticeBox.AllIn(stale.Home), b => b.Kind == NoticeKind.Error).Content.TextContent);
        stale.WaitFor(() => stale.StartState.Mark == new LedgerMark(_instance, 1));
        Assert.Equal(_decisions + 1, stale.Options.StartNumber);
        Assert.Equal(_decisions, _server.XgpFiles().Count);

        await stale.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(2, (await LedgerAsync()).Entries.Count);
        Assert.Equal(2 * _decisions, _server.XgpFiles().Count);
    }

    // ── Owed jobs ───────────────────────────────────────────────────────────

    [Fact]
    public void AJobOwedFromAnEarlierInstance_ShowsTheGateReason_AndItsActionResolvesIt()
    {
        _storage[NumberingKey] = HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, null,
            HomeStoragePlan.OwedJob("lost-1", "instance-before", "pos{n}", 5, 3, @"D:\xg\lost"));
        var page = Open();

        Assert.Equal(XgpStartBlock.LostExport, page.StartState.XgpBlock);
        page.WaitForScreen(() =>
        {
            Assert.Equal("Resolve the lost XGP export above first.", Hint(page));
            Assert.True(page.RunButton.HasAttribute("disabled"));
        });
        var box = NoticeBox.ByMarkerClass(page.Home, "lost-export-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class", "style");
        var text = System.Text.RegularExpressions.Regex.Replace(box.Content.TextContent, @"\s+", " ");
        Assert.Contains("pos{n}", text);
        Assert.Contains("numbered from 5", text);
        Assert.Contains(@"D:\xg\lost", text);
        Assert.Contains("last counted number 4", text);
        Assert.Contains("existing files are never overwritten", text);

        page.Home.FindAll(".lost-export-notice button").Single().Click();
        page.WaitFor(() => page.StartState.XgpBlock is null);

        page.WaitForScreen(() =>
        {
            Assert.Empty(page.Home.FindAll(".lost-export-notice"));
            Assert.False(page.RunButton.HasAttribute("disabled"));
        });
        Assert.Empty(OwedIds(Record!.Value));
        // No number is inferred: the numbering stands as the browser counted it.
        Assert.Equal(4, LastNumber(Record!.Value));
        Assert.Equal(5, page.Options.StartNumber);
    }

    // ── The owe step's answer ───────────────────────────────────────────────

    /// <summary>
    /// The owe step's read finds the record unreadable — a newer version of
    /// the app wrote it after this page reconciled — so no start is sent: the
    /// job is resolved as never sent, the document is kept, and the gate
    /// reason shows.
    /// </summary>
    [Fact]
    public async Task TheOweStepFindingTheRecordUnreadable_SendsNoStart()
    {
        var page = Open();
        var starts = Starts;
        _storage[NumberingKey] = "not json";

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(starts, Starts);
        Assert.Equal("not json", _storage[NumberingKey]);
        page.WaitFor(() => page.StartState.XgpBlock == XgpStartBlock.UnreadableRecord);
        page.WaitForScreen(() => Assert.Single(page.Home.FindAll(".unreadable-numbering-notice")));
        Assert.Empty((await LedgerAsync()).Entries);
    }

    /// <summary>
    /// The owe step learns of a job lost with an earlier instance — another
    /// page recorded it after this one reconciled — so no start is sent: the
    /// job is resolved as never sent, only the lost one stays owed, and its
    /// gate reason shows once the page has reconciled.
    /// </summary>
    [Fact]
    public async Task TheOweStepLearningOfALostJob_SendsNoStart()
    {
        var page = Open();
        var starts = Starts;
        _storage[NumberingKey] = HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, null,
            HomeStoragePlan.OwedJob("lost-1", "instance-before", "pos{n}", 5, 3, @"D:\xg\lost"));

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(starts, Starts);
        Assert.Equal(["lost-1"], OwedIds(Record!.Value));
        page.WaitFor(() => page.StartState.XgpBlock == XgpStartBlock.LostExport);
        page.WaitForScreen(() => Assert.Single(page.Home.FindAll(".lost-export-notice")));
        Assert.Empty((await LedgerAsync()).Entries);
    }

    [Fact]
    public void WhileTheServerCannotBeReached_TheStateIsUnknown_AndRunWaits()
    {
        var failing = _server.Wire.Fail(ProtocolWire.Ledger);
        var page = Open(reconciled: false);

        page.WaitFor(() => _server.Wire.Requests.Count(r => r == "GET /api/process/ledger") >= 2);
        Assert.Equal(LocalStartState.Checking, page.StartState);
        page.WaitForScreen(() =>
        {
            Assert.Equal("Checking the server…", Hint(page));
            Assert.True(page.RunButton.HasAttribute("disabled"));
        });

        failing.Stop();
        page.WaitFor(() => page.StartState.XgpBlock is null);
        page.WaitForScreen(() => Assert.False(page.RunButton.HasAttribute("disabled")));
    }

    [Fact]
    public void WhileAnOwedJobCannotBeSettled_TheStateIsUnknown_AndXgpWaits()
    {
        _storage[NumberingKey] = "{\"version\":1,\"owed\":["
            + HomeStoragePlan.OwedJob("job-0", _instance, "pos{n}", 1, 3, _server.OutputFolder) + "]}";
        var failing = _server.Wire.Fail(ProtocolWire.Settle);
        var page = Open(reconciled: false);

        page.WaitFor(() => page.StartState.Instance is not null && _server.Wire.Requests.Count(r => r.EndsWith("/settle")) >= 2);
        Assert.Equal(XgpStartBlock.Checking, page.StartState.XgpBlock);
        page.WaitForScreen(() =>
        {
            Assert.Equal("Checking for earlier XGP exports…", Hint(page));
            Assert.True(page.RunButton.HasAttribute("disabled"));
        });

        failing.Stop();
        page.WaitFor(() => page.StartState.XgpBlock is null);
        Assert.Empty(OwedIds(Record!.Value));
    }

    // ── Stop ────────────────────────────────────────────────────────────────

    private static AngleSharp.Dom.IElement StopButton(LocalPage page) =>
        page.Home.FindAll("button").Single(b => b.TextContent.Trim() == "Stop");

    private static string RunErrorShown(LocalPage page) =>
        Assert.Single(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error).Content.TextContent.Trim();

    private int StatusReads => _server.Wire.CountRequests(r => r.EndsWith("/status", StringComparison.Ordinal));

    // Waits for a condition no render of the page signals.
    private static void Poll(Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(condition, NoticeTesting.OperationBound), "The condition never held.");

    private const string StillTryingMessage = "Can't reach the server. This run's result hasn't arrived yet — still trying…";
    private const string UnknownJobMessage = "The server no longer has this run — it may have restarted — so its result can't be shown.";
    private const string StopUnconfirmedMessage = "Stop didn't reach the server, so the run may still be going. Press Stop to try again.";

    /// <summary>
    /// Stop before the start is answered settles the job's identity. Here the
    /// start reaches the server first: settling answers admitted, so Stop
    /// cancels the job, and the page follows it to its stopped result. The
    /// cancel's response is held until the server has answered it — the
    /// server has cancelled — and only then is the job let past its first
    /// file.
    /// </summary>
    [Fact]
    public async Task StopBeforeTheAnswer_WithTheStartWinning_CancelsTheJob_AndFollowsItToItsStoppedResult()
    {
        _server.AddASecondMatch();
        var page = Open();
        _server.Gate.Close();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var heldSettle = _server.Wire.Hold(ProtocolWire.Settle);
        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        await heldSettle.Reached.WaitAsync(NoticeTesting.OperationBound);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        // The job is writing its first match's files when Stop's cancel
        // arrives, so it commits them and stops before the second.
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        heldSettle.Release();
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        var entry = Assert.Single((await LedgerAsync()).Entries);
        Assert.False(entry.Running);
        Assert.Equal(1, MarkNumber(Record!.Value));
        Assert.Equal(entry.Commit!.Count, LastNumber(Record!.Value));
        Assert.Empty(OwedIds(Record!.Value));
    }

    /// <summary>
    /// The converse: settling reaches the server before the held start, so
    /// the identity is retired — nothing ran — and the start, arriving after,
    /// is refused. No job runs, no file is written, and the job is not owed.
    /// </summary>
    [Fact]
    public async Task StopBeforeTheAnswer_WithSettlingWinning_RetiresTheIdentity_AndNothingRuns()
    {
        var page = Open();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var run = page.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        await run.WaitAsync(NoticeTesting.OperationBound);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        Assert.Empty((await LedgerAsync()).Entries);
        Assert.Empty(_server.XgpFiles());
        Assert.Empty(OwedIds(Record!.Value));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        page.WaitFor(() => page.StartState.XgpBlock is null);
    }

    /// <summary>
    /// A cancel the server answers with an error, or refuses, was not
    /// delivered: the page says so, and keeps following. The run then ends
    /// as it would have — here completed — never shown as stopped.
    /// </summary>
    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError,
        "Stop didn't reach the server, so the run may still be going. Press Stop to try again.")]
    [InlineData(System.Net.HttpStatusCode.NotFound,
        "The server didn't stop the run: it had already ended, or the server no longer has it.")]
    public async Task ACancelNotDelivered_IsShown_AndFollowingContinues(System.Net.HttpStatusCode answer, string shown)
    {
        var page = Open();
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        page.WaitFor(() => _server.Wire.Requests.Any(r => r.EndsWith("/status", StringComparison.Ordinal)));
        _server.Wire.Respond(ProtocolWire.Cancel, answer);

        await StopButton(page).ClickAsync(new MouseEventArgs());

        page.WaitForScreen(() => Assert.Equal(shown,
            Assert.Single(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error).Content.TextContent.Trim()));
        Assert.False(run.IsCompleted);
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);
        Assert.Contains("Done", page.Home.Markup);
        Assert.DoesNotContain("Stopped", page.Home.Markup);
    }

    /// <summary>
    /// Stop pressed after the start's admitted answer has arrived, but before
    /// the page has taken it and begun following the job — the job not yet
    /// known to Stop — is delivered before the job is followed, and the run
    /// ends stopped. The answer is held at the point the page takes it
    /// (<see cref="AnswerHeldJobs"/>): the real wire's own cancellation would
    /// otherwise turn a late Stop into the settle path.
    /// </summary>
    [Fact]
    public async Task AStopPressedAfterTheAdmittedAnswerArrived_BeforeTheJobIsFollowed_IsDelivered()
    {
        _server.AddASecondMatch();
        AnswerHeldJobs? jobs = null;
        var page = Open(jobs: inner => jobs = new AnswerHeldJobs(inner));
        _server.Gate.Close();
        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await jobs!.Arrived.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        jobs.Release();
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Equal(0, _server.Wire.CountRequests(r => r.EndsWith("/settle", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A Stop not delivered stands until a Stop is delivered or the run ends:
    /// status reads then fail past the persistent-failure threshold, and
    /// contact returns, and the Stop failure is still what the run error says.
    /// Stop pressed again, and delivered, takes it down; the run ends stopped.
    /// </summary>
    [Fact]
    public async Task AStopNotDelivered_StandsThroughAPersistentFailureAndContactReturning_UntilAStopIsDelivered()
    {
        _server.AddASecondMatch();
        var page = Open();
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => StatusReads > 0);
        _server.Wire.Respond(ProtocolWire.Cancel, System.Net.HttpStatusCode.InternalServerError);
        await StopButton(page).ClickAsync(new MouseEventArgs());
        page.WaitForScreen(() => Assert.Equal(StopUnconfirmedMessage, RunErrorShown(page)));

        // Failed reads render nothing while the Stop failure stands, so the
        // wire is polled, not the page.
        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var reads = StatusReads;
        Poll(() => StatusReads > reads + LocalPage.FastTiming.FailuresBeforeError);
        failing.Stop();
        reads = StatusReads;
        Poll(() => StatusReads >= reads + 2);
        page.WaitForScreen(() => Assert.Equal(StopUnconfirmedMessage, RunErrorShown(page)));

        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var stopping = StopButton(page).ClickAsync(new MouseEventArgs());
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        await stopping.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error));
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
    }

    /// <summary>
    /// Stop before the answer, in a format the ledger does not admit: the
    /// start wins, settling answers admitted, Stop cancels the CSV job — held
    /// opening its file until the server has cancelled — and the page follows
    /// it to its stopped result.
    /// </summary>
    [Fact]
    public async Task ACsvStopBeforeTheAnswer_WithTheStartWinning_CancelsTheJob_AndFollowsItToItsStoppedResult()
    {
        var page = OpenCsv();
        _server.Gate.Close();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var heldSettle = _server.Wire.Hold(ProtocolWire.Settle);
        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        await heldSettle.Reached.WaitAsync(NoticeTesting.OperationBound);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        heldSettle.Release();
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Empty((await LedgerAsync()).Entries);
    }

    /// <summary>
    /// Stop while the settle goes unanswered: the page keeps settling, says
    /// meanwhile that the result hasn't arrived, and Stop pressed again
    /// changes nothing. Once the settle is answered — retired, since the start
    /// is still on the wire — nothing ran, the start that arrives after is
    /// refused, and nothing is shown.
    /// </summary>
    [Fact]
    public async Task StopWhileTheSettleGoesUnanswered_KeepsSettling_AndNothingRuns()
    {
        var page = OpenCsv();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var failing = _server.Wire.Fail(ProtocolWire.Settle);
        var run = page.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        page.WaitForScreen(() => Assert.Equal(StillTryingMessage, RunErrorShown(page)));
        await StopButton(page).ClickAsync(new MouseEventArgs());
        Assert.False(run.IsCompleted);
        failing.Stop();
        await run.WaitAsync(NoticeTesting.OperationBound);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.False(File.Exists(_server.CsvOutputPath));
    }

    /// <summary>
    /// A settle the server refuses as another instance's — it restarted while
    /// Stop was settling — is shown, and the page reconciles, whatever the
    /// format: the server does not hold the run, whether or not it ran.
    /// </summary>
    [Theory]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.Xgp)]
    public async Task ASettleAnsweredByAnotherInstance_IsShown_AndThePageReconciles_InEveryFormat(OutputFormat format)
    {
        var page = format == OutputFormat.Csv ? OpenCsv() : Open();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var heldSettle = _server.Wire.Hold(ProtocolWire.Settle);
        var run = page.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);
        await StopButton(page).ClickAsync(new MouseEventArgs());
        await heldSettle.Reached.WaitAsync(NoticeTesting.OperationBound);

        await RestartAsync();
        heldSettle.Release();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Equal(UnknownJobMessage, RunErrorShown(page)));
        page.WaitFor(() => page.StartState.Instance == _instance);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);
    }

    /// <summary>
    /// Stop pressed twice before the start is answered settles the identity
    /// once: the second press, while the first's settle is on its way,
    /// changes nothing.
    /// </summary>
    [Fact]
    public async Task StopPressedTwiceBeforeTheAnswer_SettlesTheIdentityOnce()
    {
        var page = Open();
        var heldStart = _server.Wire.Hold(ProtocolWire.Start);
        var heldSettle = _server.Wire.Hold(ProtocolWire.Settle);
        var run = page.RunAsync();
        await heldStart.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        await heldSettle.Reached.WaitAsync(NoticeTesting.OperationBound);
        await StopButton(page).ClickAsync(new MouseEventArgs());
        heldSettle.Release();
        await run.WaitAsync(NoticeTesting.OperationBound);
        heldStart.Release();
        await heldStart.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(1, _server.Wire.CountRequests(r => r.EndsWith("/settle", StringComparison.Ordinal)));
        Assert.Empty((await LedgerAsync()).Entries);
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Empty(OwedIds(Record!.Value));
    }

    // ── What counts as an edit ──────────────────────────────────────────────

    /// <summary>
    /// A reload while an export runs: the reloaded page's next number is a
    /// value it restored, not the user's edit, so when the export applies the
    /// control follows it — though the export started from a number the user
    /// typed before the reload, which the restored control does not show.
    /// </summary>
    [Fact]
    public async Task AReloadMidRun_TheRestoredControlFollowsTheExport_WhenItApplies()
    {
        var before = Open();
        await before.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "50" });
        _server.Gate.Close();
        var run = before.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        var reloaded = await ReloadAsync(before);
        Assert.Equal(1, reloaded.Options.StartNumber);
        _server.Gate.Open();
        reloaded.WaitFor(() => reloaded.StartState.XgpBlock is null);

        Assert.Equal(50 + _decisions, reloaded.Options.StartNumber);
        await run.WaitAsync(NoticeTesting.OperationBound);
    }

    /// <summary>
    /// The next number changed away and back while the export ran: the value
    /// is the one the export started from, but changing it is the user's own
    /// input — their choice — so the control keeps it, while the record takes
    /// the export.
    /// </summary>
    [Fact]
    public async Task TheNextNumberChangedAwayAndBack_IsTheUsersChoice_AndDoesNotFollow()
    {
        var page = Open();
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "77" });
        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "1" });
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(1, page.Options.StartNumber);
        Assert.Equal(_decisions, LastNumber(Record!.Value));
        Assert.Equal(1, MarkNumber(Record!.Value));
    }

    /// <summary>
    /// The user sets the next number back to an earlier export's start, then
    /// a reconciliation finds both exports in the ledger again: the replay
    /// takes nothing into the record, so the controls follow nothing, and the
    /// user's number stays.
    /// </summary>
    [Fact]
    public async Task ANextNumberSetBackToAnEarlierExportsStart_StaysThroughAReplay()
    {
        var page = Open();
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "1" });
        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());

        Assert.Equal(1, page.Options.StartNumber);
        Assert.Equal(2, MarkNumber(Record!.Value));
        Assert.Equal(2 * _decisions, LastNumber(Record!.Value));
    }

    // ── When an export another page started began ───────────────────────────

    // Status reads of this page's first job fail until stopped, so the page
    // does not see that job end, nor reconcile, while another page acts.
    private ProtocolWire.Rule BlindToTheFirstJob() =>
        _server.Wire.Fail(r => ProtocolWire.Status(r) && r.RequestUri!.AbsolutePath.Contains("/job-1/", StringComparison.Ordinal));

    /// <summary>
    /// The review's sequence: page P edits the next number, then starts job J;
    /// tab Q applies J — P has not seen it end — and runs K; then P's pass
    /// takes the record with K's mark from storage. K is not P's, but the
    /// ledger admitted J before it, and P started J after its edit: no edit
    /// followed either start, so P's control follows K.
    /// </summary>
    [Fact]
    public async Task AnExportAnotherPageStarted_AdmittedAfterOneThisPageStartedPastItsEdit_IsFollowed()
    {
        var p = Open();
        await p.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "50" });
        var blind = BlindToTheFirstJob();
        var j = p.RunAsync();
        await WaitUntilAsync(async () => (await LedgerAsync()).Entries is [{ Running: false }]);

        var q = Open();
        Assert.Equal(new LedgerMark(_instance, 1), q.StartState.Mark);
        Assert.Equal(50 + _decisions, q.Options.StartNumber);
        await q.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(2, MarkNumber(Record!.Value));

        blind.Stop();
        await j.WaitAsync(NoticeTesting.OperationBound);
        p.WaitFor(() => p.StartState.Mark == new LedgerMark(_instance, 2));

        Assert.Equal(50 + 2 * _decisions, p.Options.StartNumber);
    }

    /// <summary>
    /// An earlier entry of another instance supplies no bound: P edits the
    /// next number, then starts job E, which ends in an instance a restart
    /// then ends. Tab Q, in the new instance, resolves E as a lost export and
    /// runs K; P's pass takes the record with K's mark from storage. P started
    /// E after its edit, but no ledger of K's instance holds E, so nothing
    /// orders K after the edit: P's edit stands.
    /// </summary>
    [Fact]
    public async Task AnEntryOfAnotherInstance_SuppliesNoBound_AndTheEditStands()
    {
        var p = Open();
        await p.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "500" });
        var blind = BlindToTheFirstJob();
        var e = p.RunAsync();
        await WaitUntilAsync(async () => (await LedgerAsync()).Entries is [{ Running: false }]);
        await RestartAsync();

        var q = Open();
        Assert.Equal(XgpStartBlock.LostExport, q.StartState.XgpBlock);
        await q.Home.FindAll(".lost-export-notice button").Single().ClickAsync(new MouseEventArgs());
        q.WaitFor(() => q.StartState.XgpBlock is null);
        await q.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(1, MarkNumber(Record!.Value));

        blind.Stop();
        await e.WaitAsync(NoticeTesting.OperationBound);
        p.WaitFor(() => p.StartState.Mark == new LedgerMark(_instance, 1));

        Assert.Equal(500, p.Options.StartNumber);
    }

    // ── The controls follow the record's taking ─────────────────────────────

    /// <summary>
    /// The controls follow the record's taking of an export — the event — not
    /// a change in its value: the user sets the next number back to 1 and
    /// exports again, into another folder, so the new entry's numbering equals
    /// what the record held. The record takes it, its mark moving, and the
    /// control advances past the numbers the export used.
    /// </summary>
    [Fact]
    public async Task ANewEntryWhoseNumberingEqualsTheRecords_AdvancesTheControl()
    {
        var page = Open();
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);

        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "1" });
        await page.Home.SetOutputPathAsync(Path.Combine(_server.OutputFolder, "again"));
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(2, MarkNumber(Record!.Value));
        Assert.Equal(_decisions, LastNumber(Record!.Value));
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
    }

    /// <summary>
    /// A replay of another page's export the record already took is no new
    /// taking: the user's edit made after this page followed it stands
    /// through every reconciliation that finds the entry again.
    /// </summary>
    [Fact]
    public async Task AReplayOfAnotherPagesExport_DoesNotOverrideALaterEdit()
    {
        var page = Open();
        await ExportElsewhereAsync("elsewhere-1", mark: null);
        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());
        page.WaitFor(() => page.StartState.Mark == new LedgerMark(_instance, 1));
        Assert.Equal(_decisions + 1, page.Options.StartNumber);

        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "7" });
        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());
        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());

        Assert.Equal(7, page.Options.StartNumber);
        Assert.Equal(1, MarkNumber(Record!.Value));
    }

    // ── Ordering across instances ───────────────────────────────────────────

    /// <summary>
    /// A pass paused across a restart: page A has the old instance's ledger in
    /// hand — an entry newer than its mark among it — when the server
    /// restarts and page B applies the new instance's first export. Released,
    /// A writes nothing over B's numbers or mark: the stored record changed
    /// since its pass read it, so it takes the changed state and reconciles
    /// again, and its controls are never set from the old instance's entry.
    /// </summary>
    [Fact]
    public async Task APassPausedAcrossARestart_NeitherOverwritesTheNewerInstancesRecord_NorRollsItsControlsBack()
    {
        var a = Open();
        await a.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        await ExportElsewhereAsync("elsewhere-2", new LedgerMark(_instance, 1),
            new XgpExportOptions { StartNumber = _decisions + 1 });
        var paused = _server.Wire.HoldResponse(ProtocolWire.Ledger);
        var reconciling = a.Home.InvokeAsync(() => a.Panel.OnReconcileNeeded.InvokeAsync());
        await paused.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        await RestartAsync();
        var b = Open();
        await b.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "1000" });
        await b.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        var bsRecord = _storage[NumberingKey];
        Assert.Equal(999 + _decisions, LastNumber(Record!.Value));

        paused.Release();
        await reconciling.WaitAsync(NoticeTesting.OperationBound);
        a.WaitFor(() => a.StartState.Instance == _instance);

        Assert.Equal(bsRecord, _storage[NumberingKey]);
        Assert.Equal(new LedgerMark(_instance, 1), a.StartState.Mark);
        // A's controls follow the newer accounting the record took, never the
        // ended instance's entry.
        Assert.Equal(1000 + _decisions, a.Options.StartNumber);
    }

    /// <summary>
    /// A pass held after reading the stored record, then a change to this
    /// page's own record in memory alone — the lost export resolved while
    /// storage refused the write — then the pass released: the pass does not
    /// write over that change. It writes nothing, reconciles again, and the
    /// entry it applies then is written with the resolution standing.
    /// </summary>
    [Fact]
    public async Task APassHeldAcrossAChangeToThisPagesRecordInMemory_DoesNotWriteOverTheChange()
    {
        _storage[NumberingKey] = HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, ("instance-before", 2),
            HomeStoragePlan.OwedJob("lost-1", "instance-before", "pos{n}", 5, 3, @"D:\xg\lost"));
        var page = Open();
        Assert.Equal(XgpStartBlock.LostExport, page.StartState.XgpBlock);
        await ExportElsewhereAsync("elsewhere-1", mark: null);
        var held = _server.Wire.Hold(ProtocolWire.Ledger);
        var reconciling = page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());
        await held.Reached.WaitAsync(NoticeTesting.OperationBound);

        _storage.RefuseWrites = true;
        var resolving = page.Home.FindAll(".lost-export-notice button").Single().ClickAsync(new MouseEventArgs());
        page.WaitFor(() => _storage.RefusedWrites.Contains(NumberingKey));
        _storage.RefuseWrites = false;
        held.Release();
        await reconciling.WaitAsync(NoticeTesting.OperationBound);
        await resolving.WaitAsync(NoticeTesting.OperationBound);
        page.WaitFor(() => page.StartState.XgpBlock is null);

        var record = Record!.Value;
        Assert.Empty(OwedIds(record));
        Assert.Equal(1, MarkNumber(record));
        Assert.Equal(_decisions, LastNumber(record));
        Assert.Empty(page.Home.FindAll(".lost-export-notice"));
    }

    /// <summary>
    /// A stale tab — it last reconciled with the instance before a restart —
    /// records its owed job before its start: that checkpoint keeps the newer
    /// instance's stored mark and numbering, rather than writing its own older
    /// one over them. Its start is then refused, as naming the ended instance.
    /// </summary>
    [Fact]
    public async Task AStaleTabsPreStartWrite_KeepsTheNewerInstancesStoredMark()
    {
        var stale = Open();
        await stale.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        await RestartAsync();
        var fresh = Open();
        await fresh.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(new LedgerMark(_instance, 1), fresh.StartState.Mark);

        await stale.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        var record = Record!.Value;
        Assert.Equal(1, MarkNumber(record));
        Assert.Equal(_instance, record.GetProperty("mark").GetProperty("instance").GetString());
        Assert.Equal(2 * _decisions, LastNumber(record));
        Assert.Contains(_storage.Writes, write => write.Key == NumberingKey && write.Value.Contains("job-3"));
    }

    /// <summary>
    /// A result the follower received from an instance that has since ended,
    /// delivered after newer accounting: it carries no authority of its own,
    /// so it is not applied over the newer record — it goes through a pass,
    /// whose ledger is the new instance's — and its job stays owed, a lost
    /// export, until the user resolves it.
    /// </summary>
    [Fact]
    public async Task AFollowersResultFromAnEndedInstance_DeliveredAfterNewerAccounting_IsNotAppliedOverIt()
    {
        var page = Open();
        _server.Gate.Close();
        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var delayed = _server.Wire.HoldResponse(ProtocolWire.Status);
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        _server.Gate.Open();
        await WaitUntilAsync(async () => (await LedgerAsync()).Entries is [{ Running: false }]);
        failing.Stop();
        await delayed.Forwarded.WaitAsync(NoticeTesting.OperationBound);

        await RestartAsync();
        await ExportElsewhereAsync("elsewhere-1", mark: null, new XgpExportOptions { StartNumber = 500 });
        var other = Open();
        var newer = _storage[NumberingKey];
        Assert.Equal(1, MarkNumber(Record!.Value));
        Assert.Equal(499 + _decisions, LastNumber(Record!.Value));

        delayed.Release();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitFor(() => page.StartState.Instance == _instance);

        Assert.Equal(newer, _storage[NumberingKey]);
        Assert.Equal(["job-1"], OwedIds(Record!.Value));
        Assert.Equal(XgpStartBlock.LostExport, page.StartState.XgpBlock);
        page.WaitForScreen(() => Assert.Single(page.Home.FindAll(".lost-export-notice")));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + NoticeTesting.OperationBound;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never held.");
            await Task.Delay(20);
        }
    }

    // ── Owed jobs learned from storage ──────────────────────────────────────

    /// <summary>
    /// Page P2 learns a lost job from storage; page P1 resolves it; P2's next
    /// readable read no longer holds it, so P2 drops it — another page
    /// resolved it — and after a restart no lost-export gate appears.
    /// </summary>
    [Fact]
    public async Task AnOwedJobLearnedFromStorage_IsDroppedOnceAnotherPageResolvesIt()
    {
        _storage[NumberingKey] = HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, null,
            HomeStoragePlan.OwedJob("lost-1", "instance-before", "pos{n}", 5, 3, @"D:\xg\lost"));
        var p2 = Open();
        Assert.Equal(XgpStartBlock.LostExport, p2.StartState.XgpBlock);
        var p1 = Open();
        await p1.Home.FindAll(".lost-export-notice button").Single().ClickAsync(new MouseEventArgs());
        Assert.Empty(OwedIds(Record!.Value));

        await RestartAsync();
        await p2.Home.InvokeAsync(() => p2.Panel.OnReconcileNeeded.InvokeAsync());
        p2.WaitFor(() => p2.StartState.Instance == _instance);

        Assert.Null(p2.StartState.XgpBlock);
        p2.WaitForScreen(() => Assert.Empty(p2.Home.FindAll(".lost-export-notice")));
    }

    /// <summary>A refused read establishes nothing: the learned job stays owed, and its gate stays.</summary>
    [Fact]
    public async Task ARefusedRead_KeepsAnOwedJobLearnedFromStorage()
    {
        _storage[NumberingKey] = HomeStoragePlan.NumberingDocument("pos{n}", 4, 3, null,
            HomeStoragePlan.OwedJob("lost-1", "instance-before", "pos{n}", 5, 3, @"D:\xg\lost"));
        var p2 = Open();
        var p1 = Open();
        await p1.Home.FindAll(".lost-export-notice button").Single().ClickAsync(new MouseEventArgs());

        _storage.RefuseReads = true;
        await p2.Home.InvokeAsync(() => p2.Panel.OnReconcileNeeded.InvokeAsync());

        Assert.Equal(XgpStartBlock.LostExport, p2.StartState.XgpBlock);
        Assert.Single(p2.Home.FindAll(".lost-export-notice"));
    }

    // ── A start refused on every attempt ────────────────────────────────────

    /// <summary>
    /// A start refused on its only attempt never ran — here as naming the
    /// instance a restart ended — so its job is resolved, and no lost-export
    /// gate appears for it.
    /// </summary>
    [Fact]
    public async Task AStartRefusedOnItsOnlyAttempt_LeavesNoLostExport()
    {
        var page = Open();
        await RestartAsync();

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Contains("The server has restarted since this page last checked it",
            Assert.Single(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error).Content.TextContent);
        page.WaitFor(() => page.StartState.Instance == _instance);
        Assert.Null(page.StartState.XgpBlock);
        Assert.Empty(OwedIds(Record!.Value));
        page.WaitForScreen(() => Assert.Empty(page.Home.FindAll(".lost-export-notice")));
    }

    /// <summary>
    /// An attempt that went unanswered, then a refused one: the first may have
    /// arrived — a later refusal does not settle it — so the job stays owed,
    /// and once the page has reconciled with the new instance it is a lost
    /// export for the user to resolve.
    /// </summary>
    [Fact]
    public async Task AnUnansweredAttemptThenARefusedOne_LeavesTheJobOwed()
    {
        var page = Open();
        _server.Wire.Fail(ProtocolWire.Start, times: 1);
        var retry = _server.Wire.Hold(ProtocolWire.Start);
        var run = page.RunAsync();
        await retry.Reached.WaitAsync(NoticeTesting.OperationBound);

        await RestartAsync();
        retry.Release();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitFor(() => page.StartState.Instance == _instance);

        Assert.Equal(["job-1"], OwedIds(Record!.Value));
        Assert.Equal(XgpStartBlock.LostExport, page.StartState.XgpBlock);
    }

    // ── A record that cannot be read ────────────────────────────────────────

    /// <summary>
    /// A malformed record, and one a newer version wrote, each gate XGP: the
    /// gate reason shows, suggesting no number; the document is kept — not
    /// migrated over, though a key it replaced still holds a number, and not
    /// written by a reconciliation that has an ended entry to apply — and only
    /// the action replaces it, after which the entry is applied.
    /// </summary>
    [Theory]
    [InlineData("{\"version\":1,\"owed\":")]
    [InlineData("{\"version\":2,\"numbering\":{\"pattern\":\"pos{n}\",\"lastNumber\":40,\"suffixLength\":3},\"owed\":[]}")]
    public async Task AnUnreadableRecord_GatesXgp_IsKept_AndOnlyTheActionReplacesIt(string unreadable)
    {
        await ExportElsewhereAsync("elsewhere-1", mark: null);
        _storage[NumberingKey] = unreadable;
        _storage[HomeStoragePlan.XgpLastNumberKey] = "40";
        var page = Open();

        Assert.Equal(XgpStartBlock.UnreadableRecord, page.StartState.XgpBlock);
        page.WaitForScreen(() =>
        {
            Assert.Equal("Resolve the unreadable XGP numbering above first.", Hint(page));
            Assert.True(page.RunButton.HasAttribute("disabled"));
        });
        var box = NoticeBox.ByMarkerClass(page.Home, "unreadable-numbering-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class", "style");
        var text = System.Text.RegularExpressions.Regex.Replace(box.Content.TextContent, @"\s+", " ");
        Assert.Contains("saved XGP numbering couldn't be read", text);
        Assert.DoesNotMatch(@"\d", text);

        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());
        Assert.Equal(unreadable, _storage[NumberingKey]);
        Assert.Equal("40", _storage[HomeStoragePlan.XgpLastNumberKey]);
        Assert.Equal(1, page.Options.StartNumber);

        await page.Home.FindAll(".unreadable-numbering-notice button").Single().ClickAsync(new MouseEventArgs());
        page.WaitFor(() => page.StartState.XgpBlock is null);

        Assert.Equal(1, MarkNumber(Record!.Value));
        Assert.Equal(_decisions, LastNumber(Record!.Value));
        page.WaitForScreen(() => Assert.Empty(page.Home.FindAll(".unreadable-numbering-notice")));
    }

    /// <summary>
    /// Resolving waives nothing: with an ended entry the page has not applied,
    /// no XGP start can be sent while the reconciliation's answer is held, and
    /// once it arrives the entry is applied — the controls following it —
    /// before admission is enabled.
    /// </summary>
    [Fact]
    public async Task TheResolution_WithAnEntryOutstanding_AdmitsNoStart_UntilTheEntryIsApplied()
    {
        await ExportElsewhereAsync("elsewhere-1", mark: null);
        _storage[NumberingKey] = "not json";
        var page = Open();
        var starts = Starts;
        var held = _server.Wire.Hold(ProtocolWire.Ledger);

        var resolving = page.Home.FindAll(".unreadable-numbering-notice button").Single().ClickAsync(new MouseEventArgs());
        await held.Reached.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(XgpStartBlock.Checking, page.StartState.XgpBlock);
        Assert.Equal("{\"version\":1,\"owed\":[]}", _storage[NumberingKey]);
        page.WaitForScreen(() => Assert.True(page.RunButton.HasAttribute("disabled")));
        // A click that reaches the run past the dark button sends nothing.
        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);
        Assert.Equal(starts, Starts);

        held.Release();
        await resolving.WaitAsync(NoticeTesting.OperationBound);
        page.WaitFor(() => page.StartState.XgpBlock is null);

        Assert.Equal(new LedgerMark(_instance, 1), page.StartState.Mark);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
        Assert.Equal(starts, Starts);
    }

    [Fact]
    public async Task ARefusedRead_DoesNotGate()
    {
        _storage[NumberingKey] = "not json";
        _storage.RefuseReads = true;
        var page = Open();

        await page.Home.Find("#fmtXgp").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.NotEqual(XgpStartBlock.UnreadableRecord, page.StartState.XgpBlock);
        page.WaitForScreen(() => Assert.Empty(page.Home.FindAll(".unreadable-numbering-notice")));
        Assert.Equal("not json", _storage[NumberingKey]);
    }

    // ── A status read that keeps failing ────────────────────────────────────

    [Fact]
    public async Task APersistentStatusFailure_IsTheRunError_WithTheJobStillOwed_AndFollowingContinues()
    {
        var page = Open();
        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var run = page.RunAsync();

        page.WaitFor(() => NoticeBox.AllIn(page.Home).Any(b => b.Kind == NoticeKind.Error));
        Assert.Equal("Can't reach the server. This run's result hasn't arrived yet — still trying…",
            NoticeBox.AllIn(page.Home).Single(b => b.Kind == NoticeKind.Error).Content.TextContent.Trim());
        Assert.Equal(["job-1"], OwedIds(Record!.Value));
        Assert.False(run.IsCompleted);

        failing.Stop();
        await run.WaitAsync(NoticeTesting.OperationBound);

        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Contains("Done", page.Home.Markup);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
        Assert.Empty(OwedIds(Record!.Value));
    }
}
