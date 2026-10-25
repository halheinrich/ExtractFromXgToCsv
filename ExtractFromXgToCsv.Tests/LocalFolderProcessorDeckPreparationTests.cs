using BackgammonDiagram_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The deck walk both deck formats share, <see cref="LocalFolderProcessor.ProcessDeckAsync"/>,
/// run as the product runs it — each file staged through the filtering
/// operation's <see cref="FilteredDecisionIterator.IterateFileDiagrams"/>,
/// <see cref="DiagramRequest.ForDecision"/> and
/// <see cref="DiagramRequestExtensions.ToProblemSolutionPair"/> — with a
/// <see cref="RecordingDeckRenderer"/> in place of the library's render. A CSV
/// run is no substitute: it stages with <c>IterateFile</c>, and a file can pass
/// one staging and fail the other. <see cref="LocalFolderProcessor.ProcessPdfAsync"/>
/// and <see cref="LocalFolderProcessor.ProcessPptxAsync"/> differ only in the
/// renderer and the label they hand this walk, so it covers the preparation
/// for both; their wiring to the real renderers is
/// <see cref="LocalFolderProcessorPdfTests"/>' and
/// <see cref="LocalFolderProcessorPptxTests"/>'.
/// </summary>
public class LocalFolderProcessorDeckPreparationTests : IDisposable
{
    // Fixture files that read cleanly, both source formats: the controlled
    // input, on which the preparation's assertions stand on a passing baseline.
    private static readonly string[] ReadableFixtures =
        ["MTCH4064.xg", "MoneyTest.xg", "PlayAnalysis.xgp", "TakeAnalysis.xgp"];

    private readonly string _root = Directory.CreateTempSubdirectory("xg-deck-prep-").FullName;
    private readonly string _folder;

    // The output is beside the input folder, not in it, so it is never read
    // as an input.
    public LocalFolderProcessorDeckPreparationTests()
    {
        _folder = Path.Combine(_root, "input");
        Directory.CreateDirectory(_folder);
        foreach (var name in ReadableFixtures)
            File.Copy(Path.Combine(FixtureHelper.FixtureDir, name), Path.Combine(_folder, name));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static LocalFolderProcessor Processor() =>
        new(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance, AppRanking.Ranking);

    /// <summary>
    /// Over a few files that read cleanly, with the empty filter: the run
    /// completes with rows and skips nothing, the renderer is handed each
    /// decision as a problem followed by its solution — twice the decision
    /// count — and what it returns is written at the path the run reports.
    /// </summary>
    [Fact]
    public async Task ProcessDeckAsync_ReadableFiles_HandsTheRendererAProblemThenASolutionPerDecision_AndWritesWhatItReturns()
    {
        var renderer = new RecordingDeckRenderer();
        var progress = new RecordingProgress();
        var outputPath = Path.Combine(_root, "deck.pdf");

        await Processor().ProcessDeckAsync(
            _folder, outputPath, new DecisionFilterSet(), progress, renderer.Render, "PDF",
            runRecord: null, CancellationToken.None);

        var lastProgress = progress.Last;
        Assert.NotNull(lastProgress);
        Assert.True(lastProgress!.Complete);
        Assert.True(lastProgress.TotalRows > 0);
        Assert.Empty(lastProgress.Skipped);

        // Each decision becomes a Problem + Solution pair, in that order: the
        // same decision drawn twice, the problem first.
        Assert.Equal(1, renderer.Renders);
        Assert.Equal(2 * lastProgress.TotalRows, renderer.Requests.Count);
        for (var i = 0; i < renderer.Requests.Count; i += 2)
        {
            var problem = renderer.Requests[i];
            var solution = renderer.Requests[i + 1];
            Assert.Equal(DiagramMode.Problem, problem.Mode);
            Assert.Equal(DiagramMode.Solution, solution.Mode);
            Assert.NotNull(problem.Decision);
            Assert.Same(problem.Decision, solution.Decision);
            Assert.Equal(problem.Xgid, solution.Xgid);
        }

        Assert.Equal(outputPath, lastProgress.WrittenPath);
        Assert.Equal(RecordingDeckRenderer.Bytes, await File.ReadAllBytesAsync(lastProgress.WrittenPath!));
    }

    /// <summary>
    /// Over the whole fixture folder, with the empty filter: the run completes
    /// with rows, and no fixture file is skipped on the deck path
    /// (halheinrich/backgammon#223) — every file, as the controlled input
    /// above cannot say. Known to fail while halheinrich/backgammon#419 is
    /// open: the fixture whose container manifest lists <c>temp.xgr2</c>
    /// (<c>Mikayel Vardanyan (2.40) - Chris Trencher (2.94) 9pt Monte Carlo
    /// Open 8-7-2018 Cons R2 013.xg</c>) is refused by the reader, so it is
    /// skipped, and the failure is left visible here, in one test for both
    /// deck formats.
    /// </summary>
    [Fact]
    public async Task ProcessDeckAsync_FixtureFolder_SkipsNoFile()
    {
        var progress = new RecordingProgress();

        await Processor().ProcessDeckAsync(
            FixtureHelper.FixtureDir, Path.Combine(_root, "deck.pdf"), new DecisionFilterSet(), progress,
            new RecordingDeckRenderer().Render, "PDF", runRecord: null, CancellationToken.None);

        var lastProgress = progress.Last;
        Assert.NotNull(lastProgress);
        Assert.True(lastProgress!.Complete);
        Assert.True(lastProgress.TotalRows > 0);

        // Rows > 0 alone would pass with files dropped; nothing in the
        // fixture folder may be skipped (halheinrich/backgammon#223).
        Assert.Empty(lastProgress.Skipped);
    }
}
