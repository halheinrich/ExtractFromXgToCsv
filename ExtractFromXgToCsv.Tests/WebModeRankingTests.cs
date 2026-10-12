using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// One run, one ranking, in Web mode (halheinrich/backgammon#273): the
/// pipeline the panel runs — <see cref="XgSourceConverter"/>'s two
/// conversions for the cache's ranking, then <see cref="FilteredRowCache"/> —
/// under <see cref="PlayRanking.DepthFirst"/>, which is neither the
/// application's choice nor the producer's default
/// (<see cref="XgIteratorOptions"/> takes <see cref="PlayRanking.Equity"/>
/// when none is stated). Extraction that fell back to that default would build
/// Equity rows, which the cache refuses; filtering that did would admit the
/// Equity set. Either fails here.
/// </summary>
/// <remarks>
/// The discriminating filter and fixture are those of
/// <see cref="LocalFolderProcessorRankingTests"/>: "erred by at least 0.001"
/// over <c>MatchTest.xg</c> admits different decisions under the two rankings,
/// proven first from the producer's own read. That the panel builds its cache
/// for the application's ranking is <see cref="WebModePanelFilteringTests"/>'.
/// </remarks>
public class WebModeRankingTests
{
    private const string Fixture = "MatchTest.xg";
    private const PlayRanking Ranking = PlayRanking.DepthFirst;

    private static FilterConfig Filter() => new() { ErrorMin = 0.001 };

    /// <summary>The ids of the producer's records whose view for <paramref name="ranking"/> the filter admits.</summary>
    private static List<DecisionId> ReferenceIds(PlayRanking ranking)
    {
        var set = Filter().Build();
        var xg = XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, Fixture));
        return XgDecisionIterator.IterateDiagramRequests(xg, Fixture, options: new(Ranking: ranking))
            .Where(r => set.Matches(r.ViewFor(ranking)))
            .Select(r => r.Id)
            .ToList();
    }

    [Fact]
    public void ExtractionAndFiltering_RunUnderTheRunsRanking_NotADefault()
    {
        Assert.NotEqual(ReferenceIds(PlayRanking.Equity), ReferenceIds(Ranking));

        var source = WebSources.Decode(Fixture);
        var cache = new FilteredRowCache(Ranking);
        var rows = WebSources.Rows(source, cache.Ranking);
        var records = WebSources.Records(source, cache.Ranking);

        Assert.All(rows, r => Assert.Equal(Ranking, r.Ranking));
        cache.ReplaceRows(rows);
        cache.ReplaceRecords(records);
        cache.Refilter(Filter());

        var expected = ReferenceIds(Ranking);
        Assert.Equal(expected, cache.FilteredRows.Select(r => r.Id));
        Assert.Equal(expected, cache.FilteredDiagramRows.Select(r => r.Id));
    }
}
