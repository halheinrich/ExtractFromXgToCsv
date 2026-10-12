using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The CSV pathway (rows) and the Diagram JSON pathway (records) see the same
/// decisions through the same filter, under the application's one ranking:
/// each row is the producer's, built for <see cref="AppRanking.Ranking"/>, and
/// each record is judged through its view for that same ranking.
/// </summary>
public class OutputConsistencyTests
{
    private static readonly PlayRanking Ranking = AppRanking.Ranking;

    [Theory]
    [InlineData("MatchTest.xg")]
    [InlineData("MoneyTest.xg")]
    public void BothPathways_SameDecisionCount(string fileName)
    {
        var source = WebSources.Decode(fileName);

        var rows = WebSources.Rows(source, Ranking);
        var diagrams = WebSources.Records(source, Ranking);

        Assert.Equal(rows.Count, diagrams.Count);
    }

    [Theory]
    [InlineData("MatchTest.xg")]
    [InlineData("MoneyTest.xg")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Testing IDecisionFilterData contract explicitly")]
    public void BothPathways_FilterDataPropertiesAgree(string fileName)
    {
        var source = WebSources.Decode(fileName);

        var rows = WebSources.Rows(source, Ranking);
        var diagrams = WebSources.Records(source, Ranking);

        for (int i = 0; i < rows.Count; i++)
        {
            IDecisionFilterData r = rows[i];
            IDecisionFilterData d = diagrams[i].ViewFor(Ranking);

            Assert.Equal(Ranking, r.Ranking);
            Assert.Equal(r.Ranking, d.Ranking);
            Assert.Equal(r.Kind, d.Kind);
            Assert.Equal(r.Player, d.Player);
            Assert.Equal(r.Session, d.Session);
            Assert.Equal(r.MoveNumber, d.MoveNumber);
            Assert.Equal(r.IsStandardStart, d.IsStandardStart);
            Assert.Equal(r.PlayerResult, d.PlayerResult);
            Assert.Equal(r.Board, d.Board);
            Assert.Equal(r.Dice, d.Dice);
            Assert.Equal(r.AfterBestBoard, d.AfterBestBoard);
            Assert.Equal(r.AfterPlayerBoard, d.AfterPlayerBoard);
        }
    }

    [Theory]
    [InlineData("MatchTest.xg", DecisionTypeOption.CheckerPlaysOnly)]
    [InlineData("MatchTest.xg", DecisionTypeOption.CubeOnly)]
    [InlineData("MoneyTest.xg", DecisionTypeOption.CheckerPlaysOnly)]
    [InlineData("MoneyTest.xg", DecisionTypeOption.CubeOnly)]
    public void BothPathways_SameFilteredCount(string fileName, DecisionTypeOption decisionType)
    {
        var source = WebSources.Decode(fileName);

        var rows = WebSources.Rows(source, Ranking);
        var diagrams = WebSources.Records(source, Ranking);

        var fs = new FilterConfig { DecisionType = decisionType }.Build();

        var filteredRows = rows.Where(r => fs.Matches(r)).Count();
        var filteredDiagrams = diagrams.Where(d => fs.Matches(d.ViewFor(Ranking))).Count();

        Assert.Equal(filteredRows, filteredDiagrams);
    }
}
