using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Services;
using System.Text.Json;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

public class XgProcessingServiceTests
{
    private readonly XgProcessingService _svc = new();

    private static DecisionRow MakeRow(int index) => new()
    {
        Xgid = $"XGID=test{index}",
        Error = 0.01 * index,
        MatchLength = 7,
        Player = "Alice",
        SourceFile = "TestMatch.xg",
        Game = 1,
        MoveNum = index,
        Roll = 31,
        AnalysisDepth = "3-ply",
        Equity = 0.5,
        Board = new int[26],
    };

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
            new()
            {
                Position = new PositionData { Mop = new int[26], OnRollNeeds = 3, OpponentNeeds = 4 },
                Decision = new DecisionData { IsCube = false, Dice = [3, 1] },
                Descriptive = new DescriptiveData { MatchLength = 7, OnRollName = "Alice" },
            },
            new()
            {
                Position = new PositionData { Mop = new int[26], OnRollNeeds = 5, OpponentNeeds = 2 },
                Decision = new DecisionData { IsCube = true, Dice = [0, 0] },
                Descriptive = new DescriptiveData { MatchLength = 7, OnRollName = "Bob" },
            }
        };

        var json = _svc.BuildDiagramJson(items);
        var deserialized = JsonSerializer.Deserialize<List<BgDecisionData>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);
    }

    [Fact]
    public void BuildDiagramJson_RoundTripsProperties()
    {
        var item = new BgDecisionData
        {
            Position = new PositionData { Mop = new int[26], OnRollNeeds = 3, OpponentNeeds = 4, CubeSize = 2 },
            Decision = new DecisionData { IsCube = true, Dice = [0, 0], NoDoubleEquity = 0.45 },
            Descriptive = new DescriptiveData { MatchLength = 9, OnRollName = "Alice", OpponentName = "Bob" },
        };

        var json = _svc.BuildDiagramJson([item]);
        var list = JsonSerializer.Deserialize<List<BgDecisionData>>(json);

        Assert.NotNull(list);
        var rt = list[0];
        Assert.Equal(3, rt.Position.OnRollNeeds);
        Assert.Equal(4, rt.Position.OpponentNeeds);
        Assert.Equal(2, rt.Position.CubeSize);
        Assert.True(rt.Decision.IsCube);
        Assert.Equal(0.45, rt.Decision.NoDoubleEquity);
        Assert.Equal(9, rt.Descriptive.MatchLength);
        Assert.Equal("Alice", rt.Descriptive.OnRollName);
    }
}