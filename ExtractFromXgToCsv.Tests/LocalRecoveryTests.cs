using System.Net.Http.Json;
using System.Text.Json;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
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

    private LocalPage Open(bool reconciled = true)
    {
        var page = LocalPage.Open(_server, _storage, _identities, reconciled);
        _pages.Add(page);
        return page;
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
        Assert.Contains("given up as never started",
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

        var reloaded = Open();
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
        var reloaded = Open();
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

        await page.Home.Find("#xgpNextNumber").InputAsync(new ChangeEventArgs { Value = "77" });
        await page.Home.InvokeAsync(() => page.Panel.OnReconcileNeeded.InvokeAsync());
        Assert.Equal(77, page.Options.StartNumber);
        Assert.Equal(2 * _decisions, LastNumber(Record!.Value));
        Assert.Equal(2, MarkNumber(Record!.Value));

        var reloaded = Open();
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

    // ── A status read that keeps failing ────────────────────────────────────

    [Fact]
    public async Task APersistentStatusFailure_IsTheRunError_WithTheJobStillOwed_AndFollowingContinues()
    {
        var page = Open();
        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var run = page.RunAsync();

        page.WaitFor(() => NoticeBox.AllIn(page.Home).Any(b => b.Kind == NoticeKind.Error));
        Assert.Equal("Can't reach the server. This run's result is still owed — still trying…",
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
