using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Services;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

public class OutputConsistencyTests
{
    private readonly XgProcessingService _svc = new();

    [Theory]
    [InlineData("MatchTest.xg")]
    [InlineData("MoneyTest.xg")]
    public void BothPathways_SameDecisionCount(string fileName)
    {
        var bytes = FixtureHelper.ReadFixture(fileName);

        var rows = _svc.ExtractDecisions(bytes, fileName);
        var diagrams = _svc.ExtractDiagramRequests(bytes, fileName);

        Assert.Equal(rows.Count, diagrams.Count);
    }

    [Theory]
    [InlineData("MatchTest.xg")]
    [InlineData("MoneyTest.xg")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Testing IDecisionFilterData contract explicitly")]
    public void BothPathways_FilterDataPropertiesAgree(string fileName)
    {
        var bytes = FixtureHelper.ReadFixture(fileName);

        var rows = _svc.ExtractDecisions(bytes, fileName);
        var diagrams = _svc.ExtractDiagramRequests(bytes, fileName);

        for (int i = 0; i < rows.Count; i++)
        {
            IDecisionFilterData r = rows[i];
            IDecisionFilterData d = diagrams[i];

            Assert.Equal(r.Player, d.Player);
            Assert.Equal(r.IsCube, d.IsCube);
            Assert.Equal(r.MatchLength, d.MatchLength);
            Assert.Equal(r.OnRollNeeds, d.OnRollNeeds);
            Assert.Equal(r.OpponentNeeds, d.OpponentNeeds);
            Assert.Equal(r.IsCrawford, d.IsCrawford);
        }
    }

    [Theory]
    [InlineData("MatchTest.xg", DecisionTypeOption.CheckerPlaysOnly)]
    [InlineData("MatchTest.xg", DecisionTypeOption.CubeOnly)]
    [InlineData("MoneyTest.xg", DecisionTypeOption.CheckerPlaysOnly)]
    [InlineData("MoneyTest.xg", DecisionTypeOption.CubeOnly)]
    public void BothPathways_SameFilteredCount(string fileName, DecisionTypeOption decisionType)
    {
        var bytes = FixtureHelper.ReadFixture(fileName);

        var rows = _svc.ExtractDecisions(bytes, fileName);
        var diagrams = _svc.ExtractDiagramRequests(bytes, fileName);

        var fs = new FilterConfig { DecisionType = decisionType }.Build();

        var filteredRows = rows.Where(r => fs.Matches(r)).Count();
        var filteredDiagrams = diagrams.Where(d => fs.Matches(d)).Count();

        Assert.Equal(filteredRows, filteredDiagrams);
    }
}