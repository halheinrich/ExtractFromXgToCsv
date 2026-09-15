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
    // AppModeService is the entry-assembly marker; see FilterDocumentEndpointTests
    // for why `Program` cannot be named here.
    private sealed class LocalFactory : WebApplicationFactory<AppModeService>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("AppMode", "Local");
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

    private static void AssertTheMalformedFileIsTheOneSkip(ProcessingProgress terminal)
    {
        var skip = Assert.Single(terminal.Skipped);
        Assert.Equal(Malformed, skip.FileName);
        Assert.Null(skip.DecisionId);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));
    }

    /// <summary>
    /// Starts a job and polls its status, as the panel does, until the terminal
    /// snapshot arrives — which the store serves exactly once.
    /// </summary>
    private static async Task<ProcessingProgress> RunToTerminalAsync(ProcessRequest request)
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/process/start", request);
        start.EnsureSuccessStatusCode();
        var jobId = (await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("jobId").GetString();

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
