using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

public class FilterSetBuilderTests
{
    private static DecisionRow MakeRow(
        string player = "Alice",
        bool isCube = false,
        double error = 0.05,
        int matchLength = 7,
        int onRollNeeds = 3,
        int opponentNeeds = 4) => new()
        {
            Player = player,
            Roll = isCube ? 0 : 31,
            Error = error,
            MatchLength = matchLength,
            OnRollNeeds = onRollNeeds,
            OpponentNeeds = opponentNeeds,
            Board = new int[26],
        };

    [Fact]
    public void EmptyConfig_MatchesEverything()
    {
        var fs = FilterSetBuilder.Build(new FilterConfig());
        Assert.True(fs.Matches(MakeRow()));
        Assert.True(fs.Matches(MakeRow(player: "Bob", isCube: true)));
    }

    [Fact]
    public void PlayerFilter_OnlyMatchingPlayersPass()
    {
        var cfg = new FilterConfig { Players = ["Alice"] };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(player: "Alice")));
        Assert.False(fs.Matches(MakeRow(player: "Bob")));
    }

    [Fact]
    public void DecisionType_CubeOnly()
    {
        var cfg = new FilterConfig { DecisionType = "CubeOnly" };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(isCube: true)));
        Assert.False(fs.Matches(MakeRow(isCube: false)));
    }

    [Fact]
    public void DecisionType_CheckerOnly()
    {
        var cfg = new FilterConfig { DecisionType = "CheckerPlaysOnly" };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.False(fs.Matches(MakeRow(isCube: true)));
        Assert.True(fs.Matches(MakeRow(isCube: false)));
    }

    [Fact]
    public void ErrorRange_MinOnly()
    {
        var cfg = new FilterConfig { ErrorMin = 0.03 };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(error: 0.05)));
        Assert.False(fs.Matches(MakeRow(error: 0.01)));
    }

    [Fact]
    public void ErrorRange_MinAndMax()
    {
        var cfg = new FilterConfig { ErrorMin = 0.02, ErrorMax = 0.06 };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(error: 0.04)));
        Assert.False(fs.Matches(MakeRow(error: 0.01)));
        Assert.False(fs.Matches(MakeRow(error: 0.10)));
    }

    [Fact]
    public void CombinedFilters_AndSemantics()
    {
        var cfg = new FilterConfig
        {
            Players = ["Alice"],
            DecisionType = "CheckerPlaysOnly",
            ErrorMin = 0.01
        };
        var fs = FilterSetBuilder.Build(cfg);

        // Passes all three
        Assert.True(fs.Matches(MakeRow(player: "Alice", isCube: false, error: 0.05)));
        // Wrong player
        Assert.False(fs.Matches(MakeRow(player: "Bob", isCube: false, error: 0.05)));
        // Wrong decision type
        Assert.False(fs.Matches(MakeRow(player: "Alice", isCube: true, error: 0.05)));
        // Below error threshold
        Assert.False(fs.Matches(MakeRow(player: "Alice", isCube: false, error: 0.001)));
    }
}