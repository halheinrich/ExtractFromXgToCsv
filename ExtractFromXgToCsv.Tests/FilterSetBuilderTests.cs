using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using XgFilter_Razor.Shared;
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
        int opponentNeeds = 4,
        int moveNumber = 10,
        bool isStandardStart = true,
        int[]? board = null,
        int[]? afterBestBoard = null,
        int[]? afterPlayerBoard = null) => new()
        {
            Player = player,
            Roll = isCube ? 0 : 31,
            Error = error,
            MatchLength = matchLength,
            OnRollNeeds = onRollNeeds,
            OpponentNeeds = opponentNeeds,
            MoveNumber = moveNumber,
            IsStandardStart = isStandardStart,
            Board = board ?? new int[26],
            AfterBestBoard = afterBestBoard ?? new int[26],
            AfterPlayerBoard = afterPlayerBoard ?? new int[26],
        };

    // Make20Pt under the flipped after-POV: decision-maker's 20pt is index 5
    // in after-boards and their checkers are negative, so "made" is <= -2.
    private static int[] AfterBoardWith20PtMade()
    {
        var b = new int[26];
        b[5] = -2;
        return b;
    }

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
    public void MoveNumber_MinOnly()
    {
        var cfg = new FilterConfig { MoveNumberMin = 5 };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(moveNumber: 7)));
        Assert.False(fs.Matches(MakeRow(moveNumber: 3)));
    }

    [Fact]
    public void MoveNumber_MaxOnly()
    {
        var cfg = new FilterConfig { MoveNumberMax = 30 };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(moveNumber: 25)));
        Assert.False(fs.Matches(MakeRow(moveNumber: 40)));
    }

    [Fact]
    public void MoveNumber_MinAndMax()
    {
        var cfg = new FilterConfig { MoveNumberMin = 5, MoveNumberMax = 30 };
        var fs = FilterSetBuilder.Build(cfg);

        Assert.True(fs.Matches(MakeRow(moveNumber: 15)));
        Assert.False(fs.Matches(MakeRow(moveNumber: 3)));
        Assert.False(fs.Matches(MakeRow(moveNumber: 40)));
    }

    [Fact]
    public void MoveNumber_NeitherBound_NoFilterAdded()
    {
        // No MoveNumber bounds → MoveNumberFilter not constructed, so its
        // standard-start gate doesn't apply and a non-standard-start row
        // still passes (parallel to ErrorRange empty-bounds behaviour).
        var fs = FilterSetBuilder.Build(new FilterConfig());
        Assert.True(fs.Matches(MakeRow(moveNumber: 99, isStandardStart: false)));
    }

    [Fact]
    public void MoveNumber_CombinedWithPlayer()
    {
        var cfg = new FilterConfig
        {
            Players = ["Alice"],
            MoveNumberMin = 5,
            MoveNumberMax = 30,
        };
        var fs = FilterSetBuilder.Build(cfg);

        // Both pass.
        Assert.True(fs.Matches(MakeRow(player: "Alice", moveNumber: 15)));
        // Wrong player.
        Assert.False(fs.Matches(MakeRow(player: "Bob", moveNumber: 15)));
        // Move number out of range.
        Assert.False(fs.Matches(MakeRow(player: "Alice", moveNumber: 99)));
    }

    [Fact]
    public void PlayType_Empty_NoFilterAdded()
    {
        // A present PlayTypeFilter with an empty selection returns false for
        // every row (empty OR). If Build does not add the filter when the
        // selection is empty, a row with empty after-boards still passes.
        var fs = FilterSetBuilder.Build(new FilterConfig { PlayTypes = [] });
        Assert.True(fs.Matches(MakeRow()));
    }

    [Fact]
    public void PlayType_Make20Pt_Selected()
    {
        var cfg = new FilterConfig { PlayTypes = ["Make20Pt"] };
        var fs = FilterSetBuilder.Build(cfg);

        // Empty after-boards: neither play "makes" the 20pt → XOR false.
        Assert.False(fs.Matches(MakeRow()));

        // Best play makes it, player play does not → XOR true.
        Assert.True(fs.Matches(MakeRow(
            afterBestBoard:   AfterBoardWith20PtMade(),
            afterPlayerBoard: new int[26])));
    }

    [Fact]
    public void PlayType_CombinedWithErrorRange()
    {
        var cfg = new FilterConfig
        {
            PlayTypes = ["Make20Pt"],
            ErrorMin = 0.01,
        };
        var fs = FilterSetBuilder.Build(cfg);

        // Make20Pt passes, error passes.
        Assert.True(fs.Matches(MakeRow(
            error: 0.05,
            afterBestBoard:   AfterBoardWith20PtMade(),
            afterPlayerBoard: new int[26])));

        // Make20Pt passes, error below threshold → fails.
        Assert.False(fs.Matches(MakeRow(
            error: 0.001,
            afterBestBoard:   AfterBoardWith20PtMade(),
            afterPlayerBoard: new int[26])));

        // Error passes, Make20Pt fails (both after-boards empty).
        Assert.False(fs.Matches(MakeRow(error: 0.05)));
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