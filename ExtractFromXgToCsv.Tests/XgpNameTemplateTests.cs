using BgDataTypes_Lib;
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
            Row = row ?? XgpNameTokens.SampleRow,
            Index = index,
        };

    private static DecisionRow Row(
        int roll = 31,
        int matchLength = 7,
        int onRollNeeds = 3,
        int opponentNeeds = 5,
        bool isCrawford = false) => new()
        {
            Id = new XgDecisionId("t.xg", 1, 1, roll == 0),
            Roll = roll,
            MatchLength = matchLength,
            OnRollNeeds = onRollNeeds,
            OpponentNeeds = opponentNeeds,
            IsCrawford = isCrawford,
        };

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
        Assert.Equal("001313a5a", template.Render(Context(row: Row(roll: 31))));
    }

    [Fact]
    public void LiteralOnlyPattern_RendersVerbatim()
    {
        Assert.Equal("quiz", Parse("quiz").Render(Context()));
    }

    [Theory]
    [InlineData(31, "31")]
    [InlineData(65, "65")]
    [InlineData(0, "00")] // cube decision
    public void DiceToken_RendersTwoDigits(int roll, string expected)
    {
        Assert.Equal(expected, Parse("{dice}").Render(Context(row: Row(roll: roll))));
    }

    [Fact]
    public void ScoreToken_RendersTheMatchScoreFormats()
    {
        var template = Parse("{score}");
        Assert.Equal("money", template.Render(Context(row: Row(matchLength: 0))));
        Assert.Equal("3a5aC", template.Render(Context(row: Row(isCrawford: true))));
        Assert.Equal("3a5a", template.Render(Context(row: Row())));
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
            row: Row(roll: 31),
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
