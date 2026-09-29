using BgDataTypes_Lib;
using BgDataTypes_Lib.TestSupport;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the .xgp name-pattern grammar (<see cref="XgpNameTemplate.TryParse"/>)
/// and per-token rendering against <see cref="XgpNameTokens"/>. Render output
/// here is the <b>base name</b> — no extension, no uniquifier; those belong
/// to <see cref="XgpNameAllocator"/> and are pinned in
/// <see cref="XgpNameAllocatorTests"/>.
/// </summary>
public class XgpNameTemplateTests
{
    private static XgpNameTemplate Parse(string pattern)
    {
        Assert.True(XgpNameTemplate.TryParse(pattern, out var template, out var error), error);
        return template!;
    }

    private static XgpNameContext Context(
        DecisionRow? row = null,
        FilterConfig? filters = null,
        int startNumber = 1,
        int suffixLength = 3,
        int index = 0) => new()
        {
            StartNumber = startNumber,
            SuffixLength = suffixLength,
            Filters = filters ?? new FilterConfig(),
            Row = row ?? XgpNamePreview.SampleRow,
            Index = index,
        };

    /// <summary>
    /// A checker play's row, built by the producer: the opening 3-1 by
    /// default, 3-away/5-away in a 7-point match unless
    /// <paramref name="session"/> says otherwise.
    /// </summary>
    private static DecisionRow Row(IReadOnlyList<int>? dice = null, Session? session = null) =>
        TestRecords.Row(TestRecords.CheckerPlay(
            position: TestRecords.Position(
                session: session ?? TestRecords.MatchSession(onRollNeeds: 3, opponentNeeds: 5)),
            decision: TestRecords.CheckerPlayData(dice: dice)));

    // -------------------------------------------------------------------
    //  Rendering
    // -------------------------------------------------------------------

    [Fact]
    public void DefaultPattern_ReproducesTheOldPrefixCounterNaming()
    {
        // Counter unification: "pos{n}" must render byte-identically to the
        // old {Prefix}{number:D{SuffixLength}} rule, including natural growth
        // past the padded width.
        var template = Parse(XgpExportOptions.DefaultNamePattern);

        Assert.Equal("pos001", template.Render(Context(index: 0)));
        Assert.Equal("pos002", template.Render(Context(index: 1)));
        Assert.Equal("pos1000", template.Render(Context(startNumber: 999, index: 1)));
        Assert.Equal("pos0012", template.Render(Context(startNumber: 12, suffixLength: 4)));
    }

    [Fact]
    public void AdjacentTokens_RenderInOrderWithNoSeparator()
    {
        var template = Parse("{n}{dice}{score}");
        Assert.Equal("001313a5a", template.Render(Context(row: Row(dice: [3, 1]))));
    }

    [Fact]
    public void LiteralOnlyPattern_RendersVerbatim()
    {
        Assert.Equal("quiz", Parse("quiz").Render(Context()));
    }

    [Theory]
    [InlineData(3, 1, "31")]
    [InlineData(6, 5, "65")]
    public void DiceToken_RendersTwoDigits(int first, int second, string expected)
    {
        Assert.Equal(expected, Parse("{dice}").Render(Context(row: Row(dice: [first, second]))));
    }

    /// <summary>
    /// A cube decision has no roll, and its row states none; the token
    /// renders its established file-name spelling of that absence, "00"
    /// (Hal, 2026-09-29, halheinrich/backgammon#273: presentation only,
    /// never a roll handed to a domain API).
    /// </summary>
    [Fact]
    public void DiceToken_CubeDecision_RendersZeroZero_AsFileNamePresentation()
    {
        var cubeRow = TestRecords.Row(TestRecords.Cube());

        Assert.Equal(DecisionKind.Cube, cubeRow.Kind);
        Assert.Null(cubeRow.Roll);
        Assert.Equal("00", Parse("{dice}").Render(Context(row: cubeRow)));
    }

    /// <summary>
    /// The score token is the row's <see cref="DecisionRow.MatchScore"/>. A
    /// money session renders <c>moneyJ</c> or <c>moneyNJ</c> by its Jacoby
    /// rule — every money session states one, so the bare <c>money</c> of an
    /// unstated rule is gone (halheinrich/backgammon#273) — and a Crawford
    /// game's match score carries its <c>C</c>.
    /// </summary>
    [Fact]
    public void ScoreToken_RendersTheMatchScoreFormats()
    {
        var template = Parse("{score}");
        Assert.Equal("3a5a", template.Render(Context(row: Row())));
        Assert.Equal("1a5aC", template.Render(Context(row: Row(
            session: TestRecords.MatchSession(onRollNeeds: 1, opponentNeeds: 5, isCrawford: true)))));
        Assert.Equal("moneyJ", template.Render(Context(row: Row(
            session: TestRecords.MoneySession(isJacoby: true)))));
        Assert.Equal("moneyNJ", template.Render(Context(row: Row(
            session: TestRecords.MoneySession(isJacoby: false)))));
    }

    /// <summary>
    /// The live preview's sample decision is a producer-built row for the
    /// application's one ranking, and renders what the
    /// preview has always shown: a 3-1 at 3-away/5-away, game 2, move 5.
    /// </summary>
    [Fact]
    public void SampleRow_IsTheProducersRow_ForTheAppsRanking()
    {
        var sample = XgpNamePreview.SampleRow;

        Assert.Equal(AppRanking.Ranking, sample.Ranking);
        Assert.Equal(DecisionKind.CheckerPlay, sample.Kind);
        Assert.Equal(2, sample.Game);
        Assert.Equal(5, sample.MoveNumber);
        Assert.Equal("31_3a5a", Parse("{dice}_{score}").Render(Context(row: sample)));
    }

    [Fact]
    public void MinMoveToken_RendersTheFilterValue_EmptyWhenUnset()
    {
        var template = Parse("Move{min-move}");
        Assert.Equal("Move5",
            template.Render(Context(filters: new FilterConfig { MoveNumberMin = 5 })));
        // Unset filter → the token renders empty, not "0" or a placeholder.
        Assert.Equal("Move", template.Render(Context()));
    }

    [Fact]
    public void MixedPattern_DrawsBatchTokensFromFiltersAndItemTokensFromTheRow()
    {
        var template = Parse("Move{min-move}_{dice}_{score}");
        var rendered = template.Render(Context(
            row: Row(dice: [3, 1]),
            filters: new FilterConfig { MoveNumberMin = 5 }));
        Assert.Equal("Move5_31_3a5a", rendered);
    }

    // -------------------------------------------------------------------
    //  Parse failures
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_RejectsEmptyPatterns(string? pattern)
    {
        Assert.False(XgpNameTemplate.TryParse(pattern, out var template, out var error));
        Assert.Null(template);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_RejectsUnmatchedBraces()
    {
        Assert.False(XgpNameTemplate.TryParse("pos{n", out _, out var openError));
        Assert.Contains("'{'", openError);

        Assert.False(XgpNameTemplate.TryParse("pos}n", out _, out var closeError));
        Assert.Contains("'}'", closeError);
    }

    [Fact]
    public void TryParse_RejectsEmptyPlaceholder()
    {
        Assert.False(XgpNameTemplate.TryParse("pos{}", out _, out var error));
        Assert.Contains("empty {}", error);
    }

    [Fact]
    public void TryParse_RejectsUnknownToken_AndListsTheValidOnes()
    {
        Assert.False(XgpNameTemplate.TryParse("pos{bogus}", out _, out var error));
        Assert.Contains("{bogus}", error);
        foreach (var token in XgpNameTokens.All)
            Assert.Contains("{" + token.Name + "}", error);
    }

    [Fact]
    public void TryParse_RejectsIllegalFilenameCharactersInLiterals()
    {
        foreach (var c in XgpNameTemplate.InvalidFileNameChars)
        {
            Assert.False(
                XgpNameTemplate.TryParse($"a{c}b{{n}}", out _, out var error),
                $"'{c}' should be rejected");
            Assert.Contains("not allowed in filenames", error);
        }

        // Control characters are rejected the same way.
        Assert.False(XgpNameTemplate.TryParse("a\tb", out _, out var tabError));
        Assert.Contains("not allowed in filenames", tabError);
    }

    // -------------------------------------------------------------------
    //  Sanitize (token-output escape hatch; internal — InternalsVisibleTo)
    // -------------------------------------------------------------------

    [Fact]
    public void Sanitize_ReplacesIllegalAndControlCharsWithUnderscores()
    {
        foreach (var c in XgpNameTemplate.InvalidFileNameChars)
            Assert.Equal("x_y", XgpNameTemplate.Sanitize($"x{c}y"));

        Assert.Equal("a_b", XgpNameTemplate.Sanitize("a\tb"));
        Assert.Equal("___", XgpNameTemplate.Sanitize("/\\:"));
    }

    [Fact]
    public void Sanitize_LeavesCleanValuesUntouched()
    {
        Assert.Same("3a5aC", XgpNameTemplate.Sanitize("3a5aC"));
        Assert.Equal(string.Empty, XgpNameTemplate.Sanitize(string.Empty));
    }
}
