using System.Text.Json;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// One run, one ranking, in Local mode (halheinrich/backgammon#273): every
/// pathway runs under the ranking the processor was given, and under nothing
/// else. The processor is run under <see cref="PlayRanking.DepthFirst"/> —
/// not the application's choice, and not the producer's default either
/// (<see cref="XgIteratorOptions"/> takes <see cref="PlayRanking.Equity"/>
/// when none is stated) — so a pathway that fell back to a default would
/// produce the Equity result and fail here.
/// </summary>
/// <remarks>
/// <para>
/// The discriminating filter is "erred by at least 0.001" over
/// <c>MatchTest.xg</c>: which play is best, and so each played move's error,
/// is a ranking's, and on that file the two rankings admit different
/// decisions. Each test first proves that from the producer's own read — the
/// references are the producer's rows and record views for each ranking,
/// never restated here — so a fixture change that stopped discriminating
/// fails loudly rather than letting the test pass for the wrong reason.
/// </para>
/// <para>
/// What each pathway would lose to a default: every pathway filters through
/// the run's filtering operation, built for the processor's ranking — the CSV
/// and Xgp pathways its rows, which it builds for that ranking, and the
/// Diagram JSON and deck pathways its records, through their views for it.
/// </para>
/// </remarks>
public class LocalFolderProcessorRankingTests
{
    private const string Fixture = "MatchTest.xg";
    private const PlayRanking Ranking = PlayRanking.DepthFirst;

    private static FilterConfig Filter() => new() { ErrorMin = 0.001 };

    private static LocalFolderProcessor Processor() =>
        new(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance, Ranking);

    private static XgFile Read() =>
        XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, Fixture));

    /// <summary>The producer's rows for <paramref name="ranking"/> that the filter admits, as CSV lines.</summary>
    private static List<string> ReferenceCsvLines(PlayRanking ranking)
    {
        var set = Filter().Build();
        return XgDecisionIterator.Iterate(Read(), Fixture, options: new(Ranking: ranking))
            .Where(r => set.Matches(r))
            .Select(CsvLayout.Line)
            .ToList();
    }

    /// <summary>The ids of the producer's records whose view for <paramref name="ranking"/> the filter admits.</summary>
    private static List<DecisionId> ReferenceRecordIds(PlayRanking ranking)
    {
        var set = Filter().Build();
        return XgDecisionIterator.IterateDiagramRequests(Read(), Fixture, options: new(Ranking: ranking))
            .Where(r => set.Matches(r.ViewFor(ranking)))
            .Select(r => r.Id)
            .ToList();
    }

    /// <summary>The fixture discriminates: the two rankings admit different decisions.</summary>
    private static void AssertTheFixtureDiscriminates()
    {
        Assert.NotEqual(ReferenceRecordIds(PlayRanking.Equity), ReferenceRecordIds(Ranking));
        Assert.NotEqual(
            ReferenceRecordIds(PlayRanking.Equity).Count, ReferenceRecordIds(Ranking).Count);
    }

    [Fact]
    public async Task CsvPathway_BuildsAndFiltersRowsUnderTheProcessorsRanking()
    {
        AssertTheFixtureDiscriminates();
        var dir = InputFolder();
        var outPath = Path.Combine(dir, "out", "decisions.csv");
        try
        {
            await Processor().ProcessAsync(dir, outPath, Filter().Build(), new LastReport());

            var lines = (await File.ReadAllLinesAsync(outPath)).Skip(1).ToList();
            Assert.Equal(ReferenceCsvLines(Ranking), lines);
            Assert.NotEqual(ReferenceCsvLines(PlayRanking.Equity), lines);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task DiagramJsonPathway_FiltersRecordsUnderTheProcessorsRanking()
    {
        AssertTheFixtureDiscriminates();
        var dir = InputFolder();
        var outPath = Path.Combine(dir, "out", "decisions.json");
        try
        {
            await Processor().ProcessDiagramAsync(dir, outPath, Filter().Build(), new LastReport());

            var written = JsonSerializer.Deserialize<List<BgDecisionData>>(
                await File.ReadAllTextAsync(outPath))!;
            Assert.Equal(ReferenceRecordIds(Ranking), written.Select(r => r.Id));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task XgpPathway_FiltersRowsUnderTheProcessorsRanking()
    {
        AssertTheFixtureDiscriminates();
        var dir = InputFolder();
        var outDir = Path.Combine(dir, "out");
        var progress = new LastReport();
        try
        {
            await Processor().ProcessXgpAsync(
                dir, outDir, Filter().Build(), new XgpExportOptions(), Filter(), progress);

            Assert.Equal(ReferenceCsvLines(Ranking).Count, progress.Last!.TotalRows);
            Assert.Equal(ReferenceCsvLines(Ranking).Count, Directory.GetFiles(outDir, "*.xgp").Length);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task DeckPathway_FiltersRecordsUnderTheProcessorsRanking()
    {
        // RenderPdf throws without a license; the server sets it at startup.
        QuestPDF.Settings.License = LicenseType.Community;
        AssertTheFixtureDiscriminates();
        var dir = InputFolder();
        var outPath = Path.Combine(dir, "out", "decisions.pdf");
        var progress = new LastReport();
        try
        {
            await Processor().ProcessPdfAsync(dir, outPath, Filter().Build(), progress);

            Assert.True(progress.Last!.Complete);
            Assert.Equal(ReferenceRecordIds(Ranking).Count, progress.Last.TotalRows);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Constructor_RefusesAnUndefinedRanking()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance, (PlayRanking)99));
    }

    /// <summary>A temp input folder holding only the fixture, so the references cover the whole run.</summary>
    private static string InputFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xg-ranking-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(dir, Fixture));
        return dir;
    }

    /// <summary>Keeps the last report, applied inline (Progress&lt;T&gt; would post it).</summary>
    private sealed class LastReport : IProgress<ProcessingProgress>
    {
        public ProcessingProgress? Last { get; private set; }
        public void Report(ProcessingProgress value) => Last = value;
    }
}
