using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The Local panel's completion line names what the run wrote, as the server
/// returned it (the umbrella's <c>SPEC-notices.md</c> §7.5): for a CSV run, the
/// file as it was created — numbered where a file of the requested name
/// existed — and for an XGP run, the output folder; and a deck run with
/// nothing to render says why. The page runs against a real server
/// (<see cref="LocalServer"/>), so what is on screen is what the run's
/// terminal result carried.
/// </summary>
public class LocalCompletionLineTests : IAsyncLifetime
{
    private readonly LocalServer _server = new();
    private readonly MemoryBrowserStorage _storage = new();
    private readonly SequentialJobIdentities _identities = new();
    private LocalPage? _page;

    public Task InitializeAsync()
    {
        _storage[HomeStoragePlan.FolderPathKey] = _server.InputFolder;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_page is not null)
            await _page.DisposeAsync();
        _server.Dispose();
    }

    /// <summary>Opens a page whose run writes <paramref name="format"/> to <paramref name="outputPath"/>, runs it, and returns its completion line.</summary>
    private async Task<string> RunToCompletionLineAsync(OutputFormat format, string outputPath)
    {
        _storage[HomeStoragePlan.OutputFormatKey] = format.ToString();
        _storage[HomeStoragePlan.OutputPathKey] = outputPath;
        _page = LocalPage.Open(_server, _storage, _identities);

        await _page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        string line = string.Empty;
        _page.WaitForScreen(() =>
            line = Assert.Single(_page.Home.FindAll(".text-success.fw-bold"), span => span.TextContent.Contains("Done")).TextContent);
        return line.Trim();
    }

    [Fact]
    public async Task ACsvRunOverAnExistingFile_ShowsTheNumberedFileItWrote()
    {
        File.WriteAllBytes(_server.CsvOutputPath, [1, 2, 3]);
        var written = Path.Combine(Path.GetDirectoryName(_server.CsvOutputPath)!, "out (2).csv");

        var line = await RunToCompletionLineAsync(OutputFormat.Csv, _server.CsvOutputPath);

        Assert.True(File.Exists(written));
        Assert.EndsWith("→ " + written, line);
    }

    [Fact]
    public async Task AnOrdinaryCsvRun_ShowsThePathItWasGiven()
    {
        var line = await RunToCompletionLineAsync(OutputFormat.Csv, _server.CsvOutputPath);

        Assert.True(File.Exists(_server.CsvOutputPath));
        Assert.EndsWith("→ " + _server.CsvOutputPath, line);
    }

    [Fact]
    public async Task AnXgpRun_ShowsItsOutputFolder()
    {
        var line = await RunToCompletionLineAsync(OutputFormat.Xgp, _server.OutputFolder);

        Assert.NotEmpty(_server.XgpFiles());
        Assert.EndsWith("→ " + _server.OutputFolder, line);
    }

    /// <summary>
    /// A deck run over a folder whose every file is unreadable has nothing to
    /// render: its line says the file was skipped, never that the filter
    /// matched nothing (halheinrich/backgammon#399, item 2), and the skip
    /// record it points to is below it.
    /// </summary>
    [Fact]
    public async Task ADeckRunWhoseEveryFileIsSkipped_SaysSo_AndShowsTheSkipRecordBelow()
    {
        foreach (var match in Directory.GetFiles(_server.InputFolder))
            File.Delete(match);
        MalformedXg.Write(Path.Combine(_server.InputFolder, "broken.xg"));
        _storage[HomeStoragePlan.OutputFormatKey] = nameof(OutputFormat.Pdf);
        _storage[HomeStoragePlan.OutputPathKey] = Path.Combine(_server.OutputFolder, "deck.pdf");
        _page = LocalPage.Open(_server, _storage, _identities);

        await _page.RunAsync().WaitAsync(NoticeTesting.OperationBound);

        _page.WaitForScreen(() =>
        {
            Assert.Equal(
                "✗ Error: No decisions to render — 1 file was skipped; see the list below.",
                _page.Home.Find("span.text-danger").TextContent.Trim());
            var record = _page.Home.Find("div.skipped-notice");
            Assert.Equal("The last run (failed)", record.QuerySelector(".skip-list-label")!.TextContent);
            Assert.Contains("broken.xg", Assert.Single(record.QuerySelectorAll("li")).TextContent);
        });
    }
}
