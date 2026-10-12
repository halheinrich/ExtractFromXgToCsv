using System.Net;
using System.Net.Http.Json;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The recovery protocol on the server's side (the umbrella's
/// <c>SPEC-notices.md</c> §7.5), through the real wire: the real controller,
/// job store and processor in memory, reached over a <see cref="ProtocolWire"/>
/// that can lose a response, hold a request or fail one. A lost start
/// response is found again by its retry, and no second job runs; a delayed
/// start loses to settling — retired, refused — or wins it — admitted; a
/// duplicate start after the acknowledgement runs nothing; the ledger admits
/// one XGP job at a time; and a collision's run error names the file.
/// </summary>
public class LocalJobEndpointTests : IDisposable
{
    private readonly LocalServer _server = new();
    private readonly HttpClient _client;

    public LocalJobEndpointTests() => _client = _server.Client();

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> InstanceAsync() =>
        (await _client.GetFromJsonAsync<LedgerSnapshot>("/api/process/ledger"))!.Instance;

    private Task<LedgerSnapshot?> LedgerAsync() => _client.GetFromJsonAsync<LedgerSnapshot>("/api/process/ledger");

    private ProcessRequest XgpRun() => new()
    {
        FolderPath = _server.InputFolder,
        OutputPath = _server.OutputFolder,
        OutputFormat = OutputFormat.Xgp,
    };

    private ProcessRequest CsvRun() => new()
    {
        FolderPath = _server.InputFolder,
        OutputPath = Path.Combine(_server.OutputFolder, "decisions.csv"),
    };

    private Task<HttpResponseMessage> StartAsync(StartRequest start) =>
        _client.PostAsJsonAsync("/api/process/start", start);

    private Task<HttpResponseMessage> SettleAsync(string jobId, string instance) =>
        _client.PostAsJsonAsync($"/api/process/{jobId}/settle", new SettleRequest(instance));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<ProcessingProgress> TerminalAsync(string jobId)
    {
        var deadline = DateTime.UtcNow + NoticeTesting.OperationBound;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await _client.GetFromJsonAsync<ProcessingProgress>($"/api/process/{jobId}/status");
            if (snapshot is { Complete: true })
                return snapshot;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Job {jobId} never reached a terminal result.");
    }

    // ── A lost start response ───────────────────────────────────────────────

    [Fact]
    public async Task ALostStartResponse_ItsRetryFindsTheSameJob_AndNoSecondJobRuns()
    {
        var start = new StartRequest("job-1", await InstanceAsync(), Mark: null, XgpRun());
        var lost = _server.Wire.Drop(ProtocolWire.Start);

        await Assert.ThrowsAsync<HttpRequestException>(() => StartAsync(start));
        await lost.Forwarded;
        var retried = await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);

        Assert.Equal(new StartResponse("job-1", 1), retried);
        var terminal = await TerminalAsync("job-1");
        var entry = Assert.Single((await LedgerAsync())!.Entries);
        Assert.Equal(terminal.XgpCommit, entry.Commit);
        // One run's files, once: a second run would have stopped at pos001.
        Assert.Equal(
            Enumerable.Range(1, terminal.XgpCommit!.Count).Select(n => $"pos{n:D3}.xgp"),
            _server.XgpFiles());
        Assert.Null(terminal.ErrorMessage);
    }

    // ── A delayed start, in both orders ─────────────────────────────────────

    [Fact]
    public async Task ADelayedStart_SettledFirst_IsRetired_AndRefusedWhenItArrives()
    {
        var instance = await InstanceAsync();
        var held = _server.Wire.Hold(ProtocolWire.Start);
        var delayed = StartAsync(new StartRequest("job-1", instance, Mark: null, XgpRun()));
        await held.Reached.WaitAsync(NoticeTesting.OperationBound);

        var settled = await ReadAsync<SettleResponse>(await SettleAsync("job-1", instance), HttpStatusCode.OK);
        held.Release();

        Assert.Equal(SettleOutcome.Retired, settled.Outcome);
        Assert.Equal(RefusalReason.Retired, (await ReadAsync<ProtocolRefusal>(await delayed, HttpStatusCode.Conflict)).Reason);
        Assert.Empty((await LedgerAsync())!.Entries);
        Assert.Empty(_server.XgpFiles());
    }

    [Fact]
    public async Task ADelayedStart_ArrivingFirst_IsAdmitted_AndSettlingAnswersAdmitted()
    {
        var instance = await InstanceAsync();
        // The page's ledger read finds no entry; then the delayed start
        // arrives; then the page settles.
        Assert.Empty((await LedgerAsync())!.Entries);
        await ReadAsync<StartResponse>(await StartAsync(new StartRequest("job-1", instance, Mark: null, XgpRun())), HttpStatusCode.OK);

        var settled = await ReadAsync<SettleResponse>(await SettleAsync("job-1", instance), HttpStatusCode.OK);

        Assert.Equal(SettleOutcome.Admitted, settled.Outcome);
        await TerminalAsync("job-1");
        Assert.Equal("job-1", Assert.Single((await LedgerAsync())!.Entries).JobId);
    }

    [Fact]
    public async Task ASettleNamingAnotherInstance_IsRefused()
    {
        var refused = await ReadAsync<ProtocolRefusal>(await SettleAsync("job-1", "instance-before"), HttpStatusCode.Conflict);

        Assert.Equal(RefusalReason.OtherInstance, refused.Reason);
    }

    // ── Duplicates, changes and other instances ─────────────────────────────

    /// <summary>
    /// A duplicate start after the job was acknowledged: the disposition
    /// stands, so it answers as before and runs nothing — the released job is
    /// not registered again, which a new run would be.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADuplicateStartAfterTheAcknowledgement_ReturnsTheDisposition_AndRunsNothing(bool xgp)
    {
        var start = new StartRequest("job-1", await InstanceAsync(), Mark: null, xgp ? XgpRun() : CsvRun());
        var first = await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);
        await TerminalAsync("job-1");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/process/job-1/acknowledge", null)).StatusCode);

        var repeat = await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);

        Assert.Equal(first, repeat);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/process/job-1/status")).StatusCode);
        Assert.Equal(xgp ? 1 : 0, (await LedgerAsync())!.Entries.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSameIdentityWithAChangedRequest_IsRefused(bool xgp)
    {
        var start = new StartRequest("job-1", await InstanceAsync(), Mark: null, xgp ? XgpRun() : CsvRun());
        await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);

        var changed = start with { Run = start.Run with { Anonymize = !start.Run.Anonymize } };
        var refused = await ReadAsync<ProtocolRefusal>(await StartAsync(changed), HttpStatusCode.Conflict);

        Assert.Equal(RefusalReason.ChangedRequest, refused.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStartNamingAnotherInstance_IsRefused_AndStartsNothing(bool xgp)
    {
        var start = new StartRequest("job-1", "instance-before", Mark: null, xgp ? XgpRun() : CsvRun());

        var refused = await ReadAsync<ProtocolRefusal>(await StartAsync(start), HttpStatusCode.Conflict);

        Assert.Equal(RefusalReason.OtherInstance, refused.Reason);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/process/job-1/status")).StatusCode);
    }

    [Fact]
    public async Task AStatusRead_NeverRemovesTheJob()
    {
        await ReadAsync<StartResponse>(
            await StartAsync(new StartRequest("job-1", await InstanceAsync(), Mark: null, CsvRun())), HttpStatusCode.OK);
        var terminal = await TerminalAsync("job-1");

        var again = await _client.GetFromJsonAsync<ProcessingProgress>("/api/process/job-1/status");

        Assert.True(again!.Complete);
        Assert.Equal(terminal.TotalRows, again.TotalRows);
    }

    // ── XGP admission ───────────────────────────────────────────────────────

    [Fact]
    public async Task AStartNamingTheLatestEntry_IsRefused_WhileThatEntryRuns()
    {
        var instance = await InstanceAsync();
        _server.Gate.Close();
        await ReadAsync<StartResponse>(await StartAsync(new StartRequest("job-1", instance, Mark: null, XgpRun())), HttpStatusCode.OK);
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        var whileRunning = await StartAsync(new StartRequest("job-2", instance, new LedgerMark(instance, 1), XgpRun()));
        Assert.Equal(RefusalReason.XgpRunning, (await ReadAsync<ProtocolRefusal>(whileRunning, HttpStatusCode.Conflict)).Reason);

        _server.Gate.Open();
        var terminal = await TerminalAsync("job-1");
        var afterwards = await StartAsync(new StartRequest(
            "job-3", instance, new LedgerMark(instance, 1), XgpRun() with { XgpOptions = new XgpExportOptions { StartNumber = terminal.XgpCommit!.NextNumber } }));
        Assert.Equal(new StartResponse("job-3", 2), await ReadAsync<StartResponse>(afterwards, HttpStatusCode.OK));
    }

    [Fact]
    public async Task OfTwoSimultaneousStarts_ExactlyOneIsAdmitted()
    {
        var instance = await InstanceAsync();
        _server.Gate.Close();

        var answers = await Task.WhenAll(
            StartAsync(new StartRequest("job-a", instance, Mark: null, XgpRun())),
            StartAsync(new StartRequest("job-b", instance, Mark: null, XgpRun())));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await LedgerAsync())!.Entries);
        _server.Gate.Open();
    }

    // ── A collision ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnExistingFile_IsTheRunError_NamingIt_AndTheFilesBeforeItAreCounted()
    {
        Directory.CreateDirectory(_server.OutputFolder);
        File.WriteAllBytes(Path.Combine(_server.OutputFolder, "pos002.xgp"), [7]);
        await ReadAsync<StartResponse>(
            await StartAsync(new StartRequest("job-1", await InstanceAsync(), Mark: null, XgpRun())), HttpStatusCode.OK);

        var terminal = await TerminalAsync("job-1");

        Assert.Equal(
            "pos002.xgp already exists in the output folder, and XGP export never overwrites a file, "
            + "so the run stopped there. The 1 file(s) before it were written.",
            terminal.ErrorMessage);
        Assert.Empty(terminal.Skipped);
        Assert.Equal(new XgpExportCommit(new XgpExportOptions(), 1), terminal.XgpCommit);
        Assert.Equal(terminal.XgpCommit, Assert.Single((await LedgerAsync())!.Entries).Commit);
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(_server.OutputFolder, "pos002.xgp")));
    }
}
