using System.Globalization;
using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="CsvLayout"/>, the one statement of Extract's CSV: the
/// ruled header, each cell's formatting, quoting, the invariant culture and
/// empty cells (halheinrich/backgammon#424). Carried over from the shared
/// row's and the column selector's CSV tests, which leave with their legs.
/// The board, the after-boards, the id, the dice, the money rule and the
/// analysis mode and level never reach the CSV: the exact header and the
/// exact line pin that.
/// </summary>
public class CsvLayoutTests
{
    // -----------------------------------------------------------------------
    //  The header
    // -----------------------------------------------------------------------

    [Fact]
    public void Header_IsTheRuledElevenColumns()
    {
        Assert.Equal(
            "Xgid,Error,MatchScore,MatchLength,Player,SourceFile,Game,MoveNumber,Roll,AnalysisDepth,Equity",
            CsvLayout.Header);
    }

    // -----------------------------------------------------------------------
    //  Error: the decision's, under the app's ranking (Equity)
    // -----------------------------------------------------------------------

    [Fact]
    public void Error_AScoredDecision_WritesItsError()
    {
        // The user played the second candidate, 0.12345678 behind the best.
        var row = TestRecords.Row(TestRecords.CheckerPlay(
            decision: TestRecords.CheckerPlayData(
                plays:
                [
                    TestRecords.Candidate(equity: 0.5),
                    TestRecords.Candidate(play: [new(13, 10), new(6, 5)], equity: 0.5 - 0.12345678),
                ],
                userPlayIndex: 1)));

        Assert.Equal(PlayerResultKind.Scored, row.Result);
        Assert.Equal("0.123457", Cell(row, "Error"));
    }

    [Fact]
    public void Error_AnUnstatedDecision_WritesItsError()
    {
        var row = PlayRow(error: 0.12345678);

        Assert.Equal(PlayerResultKind.Unstated, row.Result);
        Assert.Equal("0.123457", Cell(row, "Error"));
    }

    [Fact]
    public void Error_ADecisionWithNoRecordedMove_IsAnEmptyCell()
    {
        // With the Result column ruled out, this empty cell is no longer told
        // apart from any other absent error; that is the ruling.
        var row = PlayRow(userPlayIndex: null);

        Assert.Equal(PlayerResultKind.NotRecorded, row.Result);
        Assert.Equal("", Cell(row, "Error"));
    }

    [Fact]
    public void Error_ACubeDecision_WritesItsError()
    {
        var row = CubeRow(error: 0.0456);

        Assert.Equal(0.0456, row.Error);
        Assert.Equal("0.0456", Cell(row, "Error"));
    }

    // -----------------------------------------------------------------------
    //  Roll: a cube decision is identified by a roll of 0
    // -----------------------------------------------------------------------

    [Fact]
    public void Roll_ACubeDecision_IsZero_ThoughTheRowStatesNoRoll()
    {
        // The one absent fact written as 0, by the ruling: the CSV has no
        // Kind column, and the 0 names the kind. The row keeps its null.
        var row = CubeRow();

        Assert.Null(row.Roll);
        Assert.Equal("0", Cell(row, "Roll"));
    }

    [Theory]
    [InlineData(6, 3, "63")]
    [InlineData(1, 1, "11")]
    public void Roll_ACheckerPlay_IsItsDigits(int first, int second, string expected)
    {
        Assert.Equal(expected, Cell(PlayRow(dice: [first, second]), "Roll"));
    }

    // -----------------------------------------------------------------------
    //  Quoting
    // -----------------------------------------------------------------------

    [Fact]
    public void Text_HoldingAComma_IsQuoted()
    {
        Assert.Contains(",\"Last, First\",", CsvLayout.Line(PlayRow(player: "Last, First")));
    }

    [Fact]
    public void Text_HoldingAQuote_IsQuoted_WithTheQuoteDoubled()
    {
        Assert.Contains(",\"say \"\"hello\"\"\",", CsvLayout.Line(PlayRow(player: "say \"hello\"")));
    }

    [Theory]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("two\rlines", "\"two\rlines\"")]
    public void Text_HoldingALineBreak_IsQuoted(string player, string expected)
    {
        Assert.Contains($",{expected},", CsvLayout.Line(PlayRow(player: player)));
    }

    [Fact]
    public void SourceFile_HoldingCommas_IsQuoted()
    {
        var row = PlayRow(id: new XgDecisionId("file,with,commas.xg", 1, 1, IsCube: false));

        Assert.Contains(",\"file,with,commas.xg\",", CsvLayout.Line(row));
    }

    [Fact]
    public void SourceFile_HoldingSpaces_IsNotQuoted()
    {
        // RFC 4180 does not quote for spaces; the name passes through literally.
        var row = PlayRow(id: new XgpDecisionId("Mochy vs Falafel.xgp"));

        Assert.Equal("Mochy vs Falafel.xgp", Cell(row, "SourceFile"));
    }

    // -----------------------------------------------------------------------
    //  Numbers and culture
    // -----------------------------------------------------------------------

    [Fact]
    public void ErrorAndEquity_AreWrittenToSixSignificantDigits()
    {
        var row = PlayRow(error: 0.12345678, equity: -0.98765432);

        Assert.Equal("0.123457", Cell(row, "Error"));
        Assert.Equal("-0.987654", Cell(row, "Equity"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Line_IsCultureInvariant(string cultureName)
    {
        // Pinned under two comma-decimal cultures — sv-SE also writes its
        // minus sign as U+2212 under ICU — so a number formatted with the
        // ambient culture would split its cell in two or change its sign.
        var row = PlayRow(
            error: 0.12345678, equity: -0.98765432, dice: [6, 3],
            session: TestRecords.MatchSession(length: 9, onRollNeeds: 3, opponentNeeds: 5));
        const string expected = "XGID=-b----E-C---eE---c-e----B-:0:0:1:63:6:4:0:9:10,0.123457,3a5a,9,Alice,match.xg,1,1,63,3-ply,-0.987654";

        var original = CultureInfo.CurrentCulture;
        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            Assert.Equal(",", culture.NumberFormat.NumberDecimalSeparator);  // the pin is live

            Assert.Equal(expected, CsvLayout.Line(row));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // -----------------------------------------------------------------------
    //  Empty cells: an absent fact is never a 0 (the cube's Roll is the one
    //  ruled exception, above)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(true, "moneyJ")]
    [InlineData(false, "moneyNJ")]
    public void AMoneyRow_WritesAnEmptyMatchLength_AndItsRuleInMatchScore(bool isJacoby, string matchScore)
    {
        var row = PlayRow(session: TestRecords.MoneySession(isJacoby: isJacoby));

        Assert.Equal("", Cell(row, "MatchLength"));
        Assert.Equal(matchScore, Cell(row, "MatchScore"));
        Assert.Equal("9", Cell(PlayRow(session: TestRecords.MatchSession(length: 9, onRollNeeds: 3, opponentNeeds: 5)), "MatchLength"));
    }

    [Fact]
    public void AStandalonePosition_WritesEmptyGameAndMoveNumber_AndItsFileName()
    {
        var row = PlayRow(id: new XgpDecisionId("position.xgp"));

        Assert.Equal("", Cell(row, "Game"));
        Assert.Equal("", Cell(row, "MoveNumber"));
        Assert.Equal("position.xgp", Cell(row, "SourceFile"));
    }

    [Fact]
    public void ANullPlayer_IsAnEmptyCell()
    {
        Assert.Equal("", Cell(PlayRow(player: null), "Player"));
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// The cell under <paramref name="column"/> in <paramref name="row"/>'s
    /// line. Splits on commas, so only for a line with no quoted cell; the
    /// line must have one cell per column.
    /// </summary>
    private static string Cell(DecisionRow row, string column)
    {
        var names = CsvLayout.Header.Split(',');
        var cells = CsvLayout.Line(row).Split(',');
        Assert.Equal(names.Length, cells.Length);
        return cells[Array.IndexOf(names, column)];
    }

    /// <summary>
    /// A checker-play row under Equity, as the shared row's tests build one:
    /// one candidate, 8/5 6/5 at 3-ply with <paramref name="equity"/>; the
    /// user plays it unless <paramref name="error"/> states an unlisted play's
    /// error, or <paramref name="userPlayIndex"/> is <see langword="null"/>.
    /// </summary>
    private static DecisionRow PlayRow(
        DecisionId? id = null,
        double? error = null,
        string? player = "Alice",
        int[]? dice = null,
        double equity = 0.1604,
        Session? session = null,
        int? userPlayIndex = 0)
    {
        id ??= new XgDecisionId("match.xg", 1, 1, IsCube: false);
        return TestRecords.Row(TestRecords.CheckerPlay(
            id: id,
            position: TestRecords.Position(session: session),
            decision: TestRecords.CheckerPlayData(
                dice: dice ?? [3, 1],
                plays: [TestRecords.Candidate(equity: equity)],
                userPlayIndex: error is null ? userPlayIndex : null,
                unlistedPlayError: error),
            descriptive: TestRecords.Descriptive(
                onRollName: player,
                isStandardStart: id is XgpDecisionId ? null : true)));
    }

    /// <summary>
    /// A cube row under Equity: the builder's double/take, played; or, with
    /// <paramref name="error"/>, an unstated action with that doubling error.
    /// </summary>
    private static DecisionRow CubeRow(double? error = null) =>
        TestRecords.Row(TestRecords.Cube(
            decision: error is null
                ? TestRecords.CubeData()
                : TestRecords.CubeData(
                    userDoublerAction: null, userTakerAction: null,
                    unstatedDoublerActionError: error)));
}
