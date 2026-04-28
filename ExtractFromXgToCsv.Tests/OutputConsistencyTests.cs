using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using XgFilter_Razor.Shared;
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
    [InlineData("MatchTest.xg", "CheckerPlaysOnly")]
    [InlineData("MatchTest.xg", "CubeOnly")]
    [InlineData("MoneyTest.xg", "CheckerPlaysOnly")]
    [InlineData("MoneyTest.xg", "CubeOnly")]
    public void BothPathways_SameFilteredCount(string fileName, string decisionType)
    {
        var bytes = FixtureHelper.ReadFixture(fileName);

        var rows = _svc.ExtractDecisions(bytes, fileName);
        var diagrams = _svc.ExtractDiagramRequests(bytes, fileName);

        var cfg = new FilterConfig { DecisionType = decisionType };
        var fs = FilterSetBuilder.Build(cfg);

        var filteredRows = rows.Where(r => fs.Matches(r)).Count();
        var filteredDiagrams = diagrams.Where(d => fs.Matches(d)).Count();

        Assert.Equal(filteredRows, filteredDiagrams);
    }
}