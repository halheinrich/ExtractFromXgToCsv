using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ExtractFromXgToCsv.Client.Services;
using System.Text.Json;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

public class XgProcessingServiceTests
{
    private readonly XgProcessingService _svc = new();

    private static DecisionRow MakeRow(int index) => TestRecords.Row(
        TestRecords.CheckerPlay(
            id: new XgDecisionId("TestMatch.xg", Game: 1, MoveNumber: index, IsCube: false)));

    [Fact]
    public void BuildCsv_HeaderAndRowCount()
    {
        var rows = Enumerable.Range(1, 5).Select(MakeRow).ToList();
        var csv = _svc.BuildCsv(rows);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(6, lines.Length); // header + 5 rows
        Assert.StartsWith("Xgid,", lines[0]);
    }

    [Fact]
    public void BuildDiagramJson_ValidJsonArray()
    {
        var items = new List<BgDecisionData>
        {
            TestRecords.CheckerPlay(),
            TestRecords.Cube(),
        };

        var json = _svc.BuildDiagramJson(items);
        var deserialized = JsonSerializer.Deserialize<List<BgDecisionData>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(
            [DecisionKind.CheckerPlay, DecisionKind.Cube],
            deserialized.Select(d => d.Kind));
    }

    [Fact]
    public void BuildDiagramJson_RoundTripsProperties()
    {
        var item = TestRecords.Cube(
            position: TestRecords.Position(
                mop: TestRecords.Cube().Position.Mop,
                cubeSize: 2,
                cubeOwner: CubeOwner.OnRoll,
                session: TestRecords.MatchSession(length: 9, onRollNeeds: 3, opponentNeeds: 4)),
            decision: TestRecords.CubeData(noDoubleEquity: 0.45),
            descriptive: TestRecords.Descriptive(onRollName: "Alice", opponentName: "Bob"));

        var json = _svc.BuildDiagramJson([item]);
        var list = JsonSerializer.Deserialize<List<BgDecisionData>>(json);

        Assert.NotNull(list);
        var rt = Assert.IsType<CubeDecision>(Assert.Single(list));
        var match = Assert.IsType<MatchSession>(rt.Position.Session);
        Assert.Equal(9, match.Terms.Length);
        Assert.Equal(3, match.OnRollNeeds);
        Assert.Equal(4, match.OpponentNeeds);
        Assert.Equal(2, rt.Position.CubeSize);
        Assert.Equal(0.45, rt.Decision.NoDoubleEquity);
        Assert.Equal("Alice", rt.Descriptive.OnRollName);
    }
}
