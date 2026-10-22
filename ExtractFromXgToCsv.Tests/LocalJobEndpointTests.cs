using System.Net;
using System.Net.Http.Json;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The Local job protocol on the server's side (the umbrella's
/// <c>SPEC-notices.md</c> §7.5), through the real wire: the real controller,
/// job store and processor in memory, reached over a <see cref="ProtocolWire"/>
/// that can lose a response, hold a request or fail one. A lost start
/// response is found again by its retry, and no second job runs; a duplicate
/// start after the acknowledgement runs nothing; two starts under different
/// identities both run; a collision's run error names the file; and a job
/// ends when it is cancelled, and when it fails even if logging the failure
/// throws.
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private Task<ProcessingProgress> TerminalAsync(string jobId) => TerminalAsync(_client, jobId);

    private static async Task<ProcessingProgress> TerminalAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow + NoticeTesting.OperationBound;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await client.GetFromJsonAsync<ProcessingProgress>($"/api/process/{jobId}/status");
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
        var start = new StartRequest("job-1", XgpRun());
        var lost = _server.Wire.Drop(ProtocolWire.Start);

        await Assert.ThrowsAsync<HttpRequestException>(() => StartAsync(start));
        await lost.Forwarded;
        var retried = await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);

        Assert.Equal(new StartResponse("job-1"), retried);
        var terminal = await TerminalAsync("job-1");
        // One run's files, once: a second run would have written incremented
        // names beside them.
        Assert.Equal(
            Enumerable.Range(1, terminal.XgpCommit!.Count).Select(n => $"pos{n:D3}.xgp"),
            _server.XgpFiles());
        Assert.Null(terminal.ErrorMessage);
    }

    // ── Duplicates and changes ──────────────────────────────────────────────

    /// <summary>
    /// A duplicate start after the job was acknowledged: the admission stands,
    /// so it answers as before and runs nothing — the released job is not
    /// registered again, which a new run would be.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADuplicateStartAfterTheAcknowledgement_IsAnsweredAsAdmitted_AndRunsNothing(bool xgp)
    {
        var start = new StartRequest("job-1", xgp ? XgpRun() : CsvRun());
        var first = await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);
        await TerminalAsync("job-1");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/process/job-1/acknowledge", null)).StatusCode);

        var repeat = await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);

        Assert.Equal(first, repeat);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/process/job-1/status")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSameIdentityWithAChangedRequest_IsRefused(bool xgp)
    {
        var start = new StartRequest("job-1", xgp ? XgpRun() : CsvRun());
        await ReadAsync<StartResponse>(await StartAsync(start), HttpStatusCode.OK);

        var changed = start with { Run = start.Run with { Anonymize = !start.Run.Anonymize } };
        var refused = await ReadAsync<ProtocolRefusal>(await StartAsync(changed), HttpStatusCode.Conflict);

        Assert.Equal(RefusalReason.ChangedRequest, refused.Reason);
    }

    [Fact]
    public async Task AStatusRead_NeverRemovesTheJob()
    {
        await ReadAsync<StartResponse>(
            await StartAsync(new StartRequest("job-1", CsvRun())), HttpStatusCode.OK);
        var terminal = await TerminalAsync("job-1");

        var again = await _client.GetFromJsonAsync<ProcessingProgress>("/api/process/job-1/status");

        Assert.True(again!.Complete);
        Assert.Equal(terminal.TotalRows, again.TotalRows);
    }

    // ── Two starts at once ──────────────────────────────────────────────────

    /// <summary>
    /// Two XGP starts under different identities, at once, into one folder:
    /// nothing admits one over the other any more, so both run — the ruled
    /// cost (§7.5) — and create-new keeps every file: the second run's take
    /// incremented names, and none is overwritten.
    /// </summary>
    [Fact]
    public async Task TwoSimultaneousStarts_BothRun_AndNoFileIsOverwritten()
    {
        _server.Gate.Close();

        var answers = await Task.WhenAll(
            StartAsync(new StartRequest("job-a", XgpRun())),
            StartAsync(new StartRequest("job-b", XgpRun())));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        _server.Gate.Open();
        var a = await TerminalAsync("job-a");
        var b = await TerminalAsync("job-b");
        Assert.Null(a.ErrorMessage);
        Assert.Null(b.ErrorMessage);
        Assert.Equal(a.XgpCommit!.Count + b.XgpCommit!.Count, _server.XgpFiles().Count);
    }

    // ── A collision ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnExistingFile_IsLeftAsItWas_AndTheRunCompletes_CountingEveryFileItWrote()
    {
        Directory.CreateDirectory(_server.OutputFolder);
        File.WriteAllBytes(Path.Combine(_server.OutputFolder, "pos002.xgp"), [7]);
        await ReadAsync<StartResponse>(
            await StartAsync(new StartRequest("job-1", XgpRun())), HttpStatusCode.OK);

        var terminal = await TerminalAsync("job-1");

        Assert.Null(terminal.ErrorMessage);
        Assert.Empty(terminal.Skipped);
        Assert.Contains("pos002 (2).xgp", _server.XgpFiles());
        Assert.Equal(new XgpExportCommit(new XgpExportOptions(), _server.XgpFiles().Count - 1), terminal.XgpCommit);
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(_server.OutputFolder, "pos002.xgp")));
    }

    // ── An identity's shape ─────────────────────────────────────────────────

    /// <summary>
    /// An identity the browser would never mint is refused as malformed, and
    /// starts nothing — one ending in a newline included, which a pattern
    /// anchored with <c>$</c> lets through.
    /// </summary>
    [Theory]
    [InlineData("job-1\n")]
    [InlineData("job 1")]
    public async Task AMalformedIdentity_IsABadRequest_AndStartsNothing(string jobId)
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await StartAsync(new StartRequest(jobId, XgpRun()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/process/{Uri.EscapeDataString(jobId)}/status")).StatusCode);
    }

    // ── How a job ends ──────────────────────────────────────────────────────

    /// <summary>
    /// A cancel through the controller ends the job: its terminal result is a
    /// stopped one, carrying what the job committed.
    /// </summary>
    [Fact]
    public async Task ACancel_EndsTheJob()
    {
        // A second match: a file is the cancellation's granularity, so the
        // cancel is seen before the second is read.
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, "MTCH4064.xg"), Path.Combine(_server.InputFolder, "second.xg"));
        _server.Gate.Close();
        await ReadAsync<StartResponse>(
            await StartAsync(new StartRequest("job-1", XgpRun())), HttpStatusCode.OK);
        await _server.Gate.Reached.WaitAsync(NoticeTesting.OperationBound);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/process/job-1/cancel", content: null)).StatusCode);
        _server.Gate.Open();

        var terminal = await TerminalAsync("job-1");
        Assert.True(terminal.Cancelled);
        Assert.Null(terminal.ErrorMessage);
        Assert.NotNull(terminal.XgpCommit);
    }

    /// <summary>
    /// A failed job records its terminal result before it logs the failure: a
    /// logger that throws cannot keep the job from ending.
    /// </summary>
    [Fact]
    public async Task AFailedJob_Ends_EvenWhenLoggingTheFailureThrows()
    {
        using var server = new LocalServer(builder => builder.ConfigureLogging(logging =>
            logging.AddProvider(new ThrowingLoggerProvider("ExtractFromXgToCsv.Controllers.ProcessController"))));
        using var client = server.Client();
        var run = new ProcessRequest
        {
            FolderPath = Path.Combine(server.InputFolder, "missing"),
            OutputPath = server.OutputFolder,
            OutputFormat = OutputFormat.Xgp,
        };
        await ReadAsync<StartResponse>(
            await client.PostAsJsonAsync("/api/process/start", new StartRequest("job-1", run)),
            HttpStatusCode.OK);

        var terminal = await TerminalAsync(client, "job-1");

        Assert.NotNull(terminal.ErrorMessage);
    }

    /// <summary>A logger that throws on every error logged under one category.</summary>
    private sealed class ThrowingLoggerProvider(string category) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName == category ? new Throwing() : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Throwing : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                    throw new InvalidOperationException("The log is unavailable.");
            }
        }
    }
}
