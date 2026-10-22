using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Truthful Stop, and following under failure (the umbrella's
/// <c>SPEC-notices.md</c> §7.5), driven through real pages — Home with the
/// real panel — over the real server: a lost start reply retried under its
/// identity and run once; a status read that fails, once or persistently; the
/// server no longer holding the job, which ends following; Stop before the
/// start is answered, which waits for the answer and is never shown as
/// delivered before it; and Stop's refusals and failures, which stand until a
/// Stop is delivered or the run ends, with the persistent-failure notice they
/// hold back.
/// </summary>
/// <remarks>
/// Each page is a <see cref="LocalPage"/>: its own app over one
/// <see cref="LocalServer"/> and one browser storage. The server's XGP runs
/// write the one fixture match's decisions as <c>pos001.xgp</c> onwards. The
/// Stop and persistent-failure cases came from the recovery suite this
/// replaced, each asserting what it asserted there, less the accounting the
/// recovery protocol kept.
/// </remarks>
public class LocalStopTests : IAsyncLifetime
{
    private readonly LocalServer _server = new();
    private readonly MemoryBrowserStorage _storage = new();
    private readonly SequentialJobIdentities _identities = new();
    private readonly List<LocalPage> _pages = [];

    // The fixture match's decisions: what one XGP run writes.
    private int _decisions;

    public Task InitializeAsync()
    {
        _storage[HomeStoragePlan.FolderPathKey] = _server.InputFolder;
        _storage[HomeStoragePlan.OutputPathKey] = _server.OutputFolder;
        _storage[HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Xgp);
        _decisions = ConvertXgToJson_Lib.XgDecisionIterator.Iterate(
            ConvertXgToJson_Lib.XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, "MTCH4064.xg")), "MTCH4064.xg").Count();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _server.Gate.Open();
        foreach (var page in _pages)
            await page.DisposeAsync();
        _server.Dispose();
    }

    private LocalPage Open(Func<ILocalJobs, ILocalJobs>? jobs = null)
    {
        var page = LocalPage.Open(_server, _storage, _identities, jobs);
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

    private static AngleSharp.Dom.IElement StopButton(LocalPage page) =>
        page.Home.FindAll("button").Single(b => b.TextContent.Trim() == "Stop");

    private static string RunErrorShown(LocalPage page) =>
        Assert.Single(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error).Content.TextContent.Trim();

    private int Starts => _server.Wire.CountRequests(r => r == "POST /api/process/start");

    private int Cancels => _server.Wire.CountRequests(r => r.EndsWith("/cancel", StringComparison.Ordinal));

    private int StatusReads => _server.Wire.CountRequests(r => r.EndsWith("/status", StringComparison.Ordinal));

    // Waits for a condition no render of the page signals.
    private static void Poll(Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(condition, NoticeTesting.OperationBound), "The condition never held.");

    private const string StillTryingMessage = "Can't reach the server. This run's result hasn't arrived yet — still trying…";
    private const string UnknownJobMessage = "The server no longer has this run — it may have restarted — so its result can't be shown.";
    private const string StopUnconfirmedMessage = "Couldn't confirm the Stop request. The run may still be going. Press Stop to try again.";

    // ── Following ───────────────────────────────────────────────────────────

    /// <summary>
    /// A start whose reply is lost after the server admitted it is sent again
    /// under the same identity, finds the same job, and the job runs once:
    /// one run's files, the control following its commit once.
    /// </summary>
    [Fact]
    public async Task ALostStartReply_IsResentUnderTheSameIdentity_AndTheJobRunsOnce()
    {
        var page = Open();
        _server.Wire.Drop(ProtocolWire.Start);

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(2, Starts);
        Assert.Equal(Enumerable.Range(1, _decisions).Select(n => $"pos{n:D3}.xgp"), _server.XgpFiles());
        page.WaitForScreen(() => Assert.Contains("Done", page.Home.Markup));
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
    }

    /// <summary>
    /// A status read that fails once is retried, below the persistent-failure
    /// threshold: nothing is shown, and the result is still delivered.
    /// </summary>
    [Fact]
    public async Task AStatusReadThatFailsOnce_StillDeliversTheResult()
    {
        var page = Open();
        _server.Wire.Fail(ProtocolWire.Status, times: 1);

        await page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Contains("Done", page.Home.Markup));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.True(StatusReads >= 2);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
    }

    [Fact]
    public async Task APersistentStatusFailure_IsTheRunError_AndFollowingContinues()
    {
        var page = Open();
        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var run = page.RunAsync();

        page.WaitFor(() => NoticeBox.AllIn(page.Home).Any(b => b.Kind == NoticeKind.Error));
        Assert.Equal("Can't reach the server. This run's result hasn't arrived yet — still trying…",
            NoticeBox.AllIn(page.Home).Single(b => b.Kind == NoticeKind.Error).Content.TextContent.Trim());
        Assert.False(run.IsCompleted);

        failing.Stop();
        await run.WaitAsync(NoticeTesting.OperationBound);

        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Contains("Done", page.Home.Markup);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
    }

    /// <summary>
    /// The server no longer holds the job — it restarted while the job ran:
    /// the status read answered 404 ends following, the run error says the
    /// result can't be shown, and the page leaves its busy state. The files
    /// written before stay on disk under their names, and the next number,
    /// with no commit to follow, stays where it was.
    /// </summary>
    [Fact]
    public async Task AStatusReadAnswered404_EndsFollowing_WithTheRunError_AndThePageLeavesBusy()
    {
        var page = Open();
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => StatusReads > 0);

        // The restart happens while the page's next status read is held on
        // the wire, so no read is inside the ending process — a harness
        // artefact, since a real restart resets the connection, which the page
        // retries — and the held read reaches the new one.
        var read = _server.Wire.Hold(ProtocolWire.Status);
        await read.Reached.WaitAsync(NoticeTesting.OperationBound);
        _server.Restart();
        read.Release();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() =>
        {
            Assert.Equal(UnknownJobMessage, RunErrorShown(page));
            Assert.DoesNotContain(page.Home.FindAll("button"), b => b.TextContent.Trim() == "Stop");
            Assert.Empty(page.Home.FindAll(".is-busy"));
            Assert.False(page.RunButton.HasAttribute("disabled"));
        });
        Assert.Equal(1, page.Options.StartNumber);

        // The ended process's job, let through, writes its files under their
        // names; nothing on the page changes.
        _server.Gate.Open();
        Poll(() => _server.XgpFiles().Count == _decisions);
        Assert.Equal(UnknownJobMessage, RunErrorShown(page));
    }

    // ── Stop before the start is answered ───────────────────────────────────

    /// <summary>
    /// Stop pressed while the start is unanswered — it reached the server,
    /// whose answer is held on the wire — is not delivered, and nothing says
    /// it was: no cancel is sent, the page stays busy, and the run goes on.
    /// Once the answer arrives, admitting the job, the Stop is delivered: the
    /// job, writing its first match's files, commits them and stops before
    /// the second, and the page follows it to its stopped result, the next
    /// number following what it committed. The cancel's response is held
    /// until the server has answered it — the server has cancelled — and only
    /// then is the job let past its first file.
    /// </summary>
    [Fact]
    public async Task StopBeforeTheAnswer_WaitsForIt_ThenCancelsTheAdmittedJob_AndFollowsItToItsStoppedResult()
    {
        _server.AddASecondMatch();
        var page = Open();
        _server.Gate.Close();
        var heldAnswer = _server.Wire.HoldResponse(ProtocolWire.Start);
        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await heldAnswer.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        Assert.Equal(0, Cancels);
        Assert.False(run.IsCompleted);
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Single(page.Home.FindAll("button"), b => b.TextContent.Trim() == "Stop");

        heldAnswer.Release();
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Equal(_decisions, _server.XgpFiles().Count);
        Assert.Equal(_decisions + 1, page.Options.StartNumber);
    }

    /// <summary>
    /// A lost start reply is never shown as a confirmed cancellation: the
    /// first start reaches the server — the job runs — and its reply is lost;
    /// Stop is pressed while the retry is held on the wire. Nothing is
    /// cancelled and nothing says so; once the retry is answered, admitted as
    /// the same job, the Stop is delivered and the run ends stopped.
    /// </summary>
    [Fact]
    public async Task AStopWhileALostStartReplyIsResent_IsDeliveredOnlyOnceTheJobIsAdmitted()
    {
        _server.AddASecondMatch();
        var page = Open();
        _server.Gate.Close();
        var lost = _server.Wire.Drop(ProtocolWire.Start);
        var heldRetry = _server.Wire.Hold(ProtocolWire.Start);
        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await lost.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        await heldRetry.Reached.WaitAsync(NoticeTesting.OperationBound);
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        Assert.Equal(0, Cancels);
        Assert.False(run.IsCompleted);
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);

        heldRetry.Release();
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(2, Starts);
        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        Assert.Equal(_decisions, _server.XgpFiles().Count);
    }

    /// <summary>
    /// Stop before the answer, in CSV: the start's admitted answer is held,
    /// and once it arrives Stop cancels the CSV job — held opening its file
    /// until the server has cancelled — and the page follows it to its
    /// stopped result.
    /// </summary>
    [Fact]
    public async Task ACsvStopBeforeTheAnswer_WaitsForIt_ThenCancelsTheAdmittedJob_AndFollowsItToItsStoppedResult()
    {
        var page = OpenCsv();
        _server.Gate.Close();
        var heldAnswer = _server.Wire.HoldResponse(ProtocolWire.Start);
        var cancel = _server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await heldAnswer.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        await StopButton(page).ClickAsync(new MouseEventArgs());
        Assert.Equal(0, Cancels);
        heldAnswer.Release();
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
    }

    /// <summary>
    /// Stop pressed after the start's admitted answer has arrived, but before
    /// the page has taken it and begun following the job — the job not yet
    /// known to Stop — is delivered before the job is followed, and the run
    /// ends stopped. The answer is held at the point the page takes it
    /// (<see cref="AnswerHeldJobs"/>), which the real wire cannot express.
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
    }

    // ── Stop once the job is known ──────────────────────────────────────────

    /// <summary>
    /// A cancel the server answers with an error, or refuses, was not
    /// delivered: the page says so, and keeps following. The run then ends
    /// as it would have — here completed — never shown as stopped.
    /// </summary>
    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError,
        "Couldn't confirm the Stop request. The run may still be going. Press Stop to try again.")]
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
    /// A stopped run's failure to close the file it left unfinished reaches
    /// the page, end to end: the processor reports it beside the
    /// cancellation, the controller hands it to the job's terminal result, and
    /// the panel shows it beside "Stopped". The CSV job is held opening its
    /// file until the server has cancelled it, over a destination whose close
    /// of that file fails.
    /// </summary>
    [Fact]
    public async Task AStoppedRunsCloseFailure_ReachesThePage_BesideTheStop()
    {
        using var server = new LocalServer(destination: new ScriptedDestination { FailCloseOf = "out.csv" });
        var storage = new MemoryBrowserStorage
        {
            [HomeStoragePlan.FolderPathKey] = server.InputFolder,
            [HomeStoragePlan.OutputPathKey] = server.CsvOutputPath,
            [HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Csv),
        };
        await using var page = LocalPage.Open(server, storage, new SequentialJobIdentities());
        server.Gate.Close();
        var cancel = server.Wire.HoldResponse(ProtocolWire.Cancel);
        var run = page.RunAsync();
        await server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => server.Wire.CountRequests(r => r.EndsWith("/status", StringComparison.Ordinal)) > 0);

        var stopping = StopButton(page).ClickAsync(new MouseEventArgs());
        await cancel.Forwarded.WaitAsync(NoticeTesting.OperationBound);
        cancel.Release();
        await stopping.WaitAsync(NoticeTesting.OperationBound);
        server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);

        page.WaitForScreen(() =>
        {
            Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent);
            Assert.Equal(
                $"Closing the unfinished out.csv failed: {ScriptedDestination.CloseFailure}",
                page.Home.Find("#closeFailure").TextContent.Trim());
        });
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
    /// Two Stops for one run, their cancels answered in either order — one
    /// delivered, the other not confirmed — and whichever of the two was sent
    /// first. Once a cancel for the job is confirmed delivered, no failure
    /// undoes it, whether it answers a request sent before or after: a failure
    /// answered first shows until the delivery takes it down, and one answered
    /// after the delivery shows nothing. The run ends stopped either way.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    public async Task TwoStopsAnsweredInEitherOrder_AConfirmedDeliveryStands(int delivered, bool deliveryAnsweredFirst)
    {
        _server.AddASecondMatch();
        CancelAnsweringJobs? jobs = null;
        var page = Open(jobs: inner => jobs = new CancelAnsweringJobs(inner));
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => StatusReads > 0);

        var stops = new[]
        {
            StopButton(page).ClickAsync(new MouseEventArgs()),
            StopButton(page).ClickAsync(new MouseEventArgs()),
        };
        Poll(() => jobs!.Sent == 2);
        var failed = 1 - delivered;
        if (deliveryAnsweredFirst)
        {
            await jobs!.AnswerAsync(delivered, deliver: true);
            await stops[delivered].WaitAsync(NoticeTesting.OperationBound);
            await jobs.AnswerAsync(failed, deliver: false);
            await stops[failed].WaitAsync(NoticeTesting.OperationBound);
        }
        else
        {
            await jobs!.AnswerAsync(failed, deliver: false);
            await stops[failed].WaitAsync(NoticeTesting.OperationBound);
            page.WaitForScreen(() => Assert.Equal(StopUnconfirmedMessage, RunErrorShown(page)));
            await jobs.AnswerAsync(delivered, deliver: true);
            await stops[delivered].WaitAsync(NoticeTesting.OperationBound);
        }

        page.WaitForScreen(() => Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error));
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
    }

    /// <summary>
    /// A Stop's answer belongs to its run: a cancel for one run's job stalls,
    /// the job ends, the next run starts, and only then does the cancel fail.
    /// Its failure is the ended run's, so nothing is shown on the run in hand,
    /// which goes on to its own result.
    /// </summary>
    [Fact]
    public async Task AStaleCancelsFailure_ArrivingAfterTheNextRunStarts_IsNotShown()
    {
        CancelAnsweringJobs? jobs = null;
        var page = Open(jobs: inner => jobs = new CancelAnsweringJobs(inner));
        _server.Gate.Close();
        var first = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => StatusReads > 0);
        var stopping = StopButton(page).ClickAsync(new MouseEventArgs());
        Poll(() => jobs!.Sent == 1);

        _server.Gate.Open();
        await first.WaitAsync(NoticeTesting.OperationBound);
        _server.Gate.Close();
        var next = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        await jobs!.AnswerAsync(0, deliver: false);
        await stopping.WaitAsync(NoticeTesting.OperationBound);
        await page.Home.InvokeAsync(() => { });

        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
        _server.Gate.Open();
        await next.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.Contains("Done", page.Home.Markup));
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);
    }

    // ── The persistent-failure notice: held back, shown, closed ─────────────

    /// <summary>
    /// The persistent-failure notice held back by a Stop failure: status reads
    /// fail past the threshold while a Stop not delivered stands, so the
    /// notice is due but held back. A Stop then delivered, the reads still
    /// failing, takes the Stop failure down and shows the notice in its place;
    /// contact returning takes it down, and the run ends stopped. Before the
    /// correction the notice fired only at the threshold, so once a delivered
    /// Stop had cleared the Stop failure nothing said the result was still owed.
    /// </summary>
    [Fact]
    public async Task APersistentFailureHeldBackByAStopFailure_ShowsOnceAStopIsDelivered_WhileFailuresContinue()
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

        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var reads = StatusReads;
        Poll(() => StatusReads > reads + LocalPage.FastTiming.FailuresBeforeError);
        page.WaitForScreen(() => Assert.Equal(StopUnconfirmedMessage, RunErrorShown(page)));

        await StopButton(page).ClickAsync(new MouseEventArgs());
        page.WaitForScreen(() => Assert.Equal(StillTryingMessage, RunErrorShown(page)));

        failing.Stop();
        page.WaitForScreen(() => Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error));
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.Contains("Stopped", page.Home.Find("span.text-warning").TextContent));
    }

    /// <summary>
    /// A persistent-failure notice the user closes stays closed for the rest
    /// of its failure streak, however many more reads fail. Once contact
    /// returns, a new streak that reaches the threshold shows it again.
    /// </summary>
    [Fact]
    public async Task APersistentFailureNoticeClosed_StaysClosedForItsStreak_AndANewStreakShowsItAgain()
    {
        var page = Open();
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => StatusReads > 0);

        var failing = _server.Wire.Fail(ProtocolWire.Status);
        page.WaitForScreen(() => Assert.Equal(StillTryingMessage, RunErrorShown(page)));
        await Assert.Single(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error)
            .DismissAndSettleAsync(page.Home, NoticeDismissGesture.CloseButton);
        var reads = StatusReads;
        Poll(() => StatusReads > reads + LocalPage.FastTiming.FailuresBeforeError);
        await page.Home.InvokeAsync(() => { });
        Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error);

        failing.Stop();
        reads = StatusReads;
        Poll(() => StatusReads >= reads + 2);
        failing = _server.Wire.Fail(ProtocolWire.Status);
        page.WaitForScreen(() => Assert.Equal(StillTryingMessage, RunErrorShown(page)));

        failing.Stop();
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.Contains("Done", page.Home.Markup));
    }

    /// <summary>
    /// A Stop failure the user closes while it holds the persistent-failure
    /// notice back: the notice was never shown, so it was not what the user
    /// closed, and the reads still fail, so it shows at the next failure.
    /// </summary>
    [Fact]
    public async Task AStopFailureClosed_WhileItHoldsThePersistentFailureBack_ShowsTheNoticeAtTheNextFailure()
    {
        var page = Open();
        _server.Gate.Close();
        var run = page.RunAsync();
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);
        Poll(() => StatusReads > 0);
        _server.Wire.Respond(ProtocolWire.Cancel, System.Net.HttpStatusCode.InternalServerError);
        await StopButton(page).ClickAsync(new MouseEventArgs());
        page.WaitForScreen(() => Assert.Equal(StopUnconfirmedMessage, RunErrorShown(page)));
        var failing = _server.Wire.Fail(ProtocolWire.Status);
        var reads = StatusReads;
        Poll(() => StatusReads > reads + LocalPage.FastTiming.FailuresBeforeError);

        await Assert.Single(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error)
            .DismissAndSettleAsync(page.Home, NoticeDismissGesture.CloseButton);
        page.WaitForScreen(() => Assert.Equal(StillTryingMessage, RunErrorShown(page)));

        failing.Stop();
        page.WaitForScreen(() => Assert.DoesNotContain(NoticeBox.AllIn(page.Home), b => b.Kind == NoticeKind.Error));
        _server.Gate.Open();
        await run.WaitAsync(NoticeTesting.OperationBound);
        page.WaitForScreen(() => Assert.Contains("Done", page.Home.Markup));
    }
}
