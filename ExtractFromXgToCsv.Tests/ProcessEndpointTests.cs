using System.Net.Http.Json;
using System.Text.Json;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Hosted pins for the process endpoints through the real pipeline
/// (<see cref="WebApplicationFactory{TEntryPoint}"/> over the server's
/// <c>Program</c>), for what only the job runner in
/// <c>ProcessController</c> decides: the terminal snapshot it builds when a
/// run fails. A run that skipped files and then failed must still show the
/// skips beside the error (halheinrich/backgammon#223) — the error snapshot is
/// the runner's own, not one the processor reported, so nothing below the
/// controller can pin it.
/// </summary>
public class ProcessEndpointTests : IDisposable
{
    // JobStore is the entry-assembly marker; see FilterDocumentEndpointTests
    // for why.
    private sealed class LocalFactory : WebApplicationFactory<JobStore>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Empty disables the opening book: these runs need none, and an
            // auto-detected install would be loaded by every host.
            builder.UseSetting("OpeningBookPath", "");
        }
    }

    private static readonly string Malformed = Path.Combine("0-broken", "0-malformed.xg");

    private readonly string _folder =
        Directory.CreateTempSubdirectory("xg-process-wire-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RunThatSkipsAFileThenFails_TerminalSnapshotCarriesTheErrorAndTheSkip()
    {
        // The per-file loop records the malformed file and reads the good
        // one; then the Diagram JSON pathway's final write targets a path that
        // is a directory, and the run fails after the loop.
        File.Copy(
            Path.Combine(FixtureHelper.FixtureDir, "MTCH4064.xg"),
            Path.Combine(_folder, "MTCH4064.xg"));
        MalformedXg.Write(Path.Combine(_folder, Malformed));
        var unwritable = Directory.CreateDirectory(Path.Combine(_folder, "out.json")).FullName;

        var terminal = await RunToTerminalAsync(new ProcessRequest
        {
            FolderPath = _folder,
            OutputPath = unwritable,
            Filters = new FilterConfig(),
            OutputFormat = OutputFormat.DiagramJson,
        });

        Assert.False(string.IsNullOrEmpty(terminal.ErrorMessage));
        AssertTheMalformedFileIsTheOneSkip(terminal);
    }

    [Fact]
    public async Task DeckRunWhoseEveryFileIsSkipped_TerminalSnapshotCarriesTheErrorAndTheSkip()
    {
        // The case that read worst before: every file skipped leaves nothing to
        // render, and the run ended on "No decisions matched the filter" with
        // the reason invisible.
        MalformedXg.Write(Path.Combine(_folder, Malformed));

        var terminal = await RunToTerminalAsync(new ProcessRequest
        {
            FolderPath = _folder,
            OutputPath = Path.Combine(_folder, "deck.pptx"),
            Filters = new FilterConfig(),
            OutputFormat = OutputFormat.Pptx,
        });

        Assert.False(string.IsNullOrEmpty(terminal.ErrorMessage));
        AssertTheMalformedFileIsTheOneSkip(terminal);
    }

    /// <summary>
    /// The accounting case (halheinrich/backgammon#374): within one source
    /// file a decision is written, and the next write fails. The run fails,
    /// the failed write is not a skip, and the error snapshot the runner
    /// builds itself — no report followed the first write — carries the
    /// commit of the one file written, under the options the run was sent.
    /// </summary>
    [Fact]
    public async Task XgpRunWhoseSecondWriteFails_ErrorSnapshotCarriesTheFirstFilesCommit_AndNoSkip()
    {
        File.Copy(
            Path.Combine(FixtureHelper.FixtureDir, "MTCH4064.xg"),
            Path.Combine(_folder, "MTCH4064.xg"));
        var outputDir = Path.Combine(_folder, "positions");
        var options = new XgpExportOptions { NamePattern = "p{n}", StartNumber = 40, SuffixLength = 2 };
        // The second position's name is taken by a folder, so its write fails.
        Directory.CreateDirectory(Path.Combine(outputDir, "p41.xgp"));

        var terminal = await RunToTerminalAsync(new ProcessRequest
        {
            FolderPath = _folder,
            OutputPath = outputDir,
            Filters = new FilterConfig(),
            OutputFormat = OutputFormat.Xgp,
            XgpOptions = options,
        });

        Assert.False(string.IsNullOrEmpty(terminal.ErrorMessage));
        Assert.Empty(terminal.Skipped);
        Assert.Equal(new XgpExportCommit(options, 1), terminal.XgpCommit);
        Assert.True(File.Exists(Path.Combine(outputDir, "p40.xgp")));
    }

    private static void AssertTheMalformedFileIsTheOneSkip(ProcessingProgress terminal)
    {
        var skip = Assert.Single(terminal.Skipped);
        Assert.Equal(Malformed, skip.FileName);
        Assert.Null(skip.DecisionId);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));
    }

    /// <summary>
    /// Starts a job under an identity of its own and polls its status, as the
    /// panel does, until the terminal snapshot arrives.
    /// </summary>
    private static async Task<ProcessingProgress> RunToTerminalAsync(ProcessRequest request)
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();

        const string jobId = "run-1";
        var start = await client.PostAsJsonAsync(
            "/api/process/start", new StartRequest(jobId, request));
        start.EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await client.GetFromJsonAsync<ProcessingProgress>(
                $"/api/process/{jobId}/status");
            if (snapshot is { Complete: true })
                return snapshot;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Job {jobId} never reached a terminal snapshot.");
    }
}
