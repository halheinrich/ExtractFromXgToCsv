using System.Text.Json;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The one record type a run keeps (<c>SPEC-notices.md</c> §7.1): immutable,
/// ordered, its counts derived from the unit each entry states and worded in
/// one phrase, and on the wire the array of those entries. And the one entry
/// type's own rule: an entry claims one unit, and only a decision can carry an
/// id, while an omitted decision has none and is still no file.
/// </summary>
public class SkipRecordTests
{
    private static readonly SkippedItem File1 = SkippedItem.ForFile("a.xg", "Not a valid XG file.");
    private static readonly SkippedItem File2 = SkippedItem.ForFile(Path.Combine("sub", "b.xgp"), "Unexpected end of stream.");
    private static readonly SkippedItem Game1 = SkippedItem.ForGame("d.xg", "Invalid standing in d.xg, game 2: refused.");
    private static readonly SkippedItem Decision1 =
        SkippedItem.ForDecision("c.xg", new XgDecisionId("c.xg", 1, 4, IsCube: true), "No decision at those coordinates.");
    private static readonly SkippedItem OmittedDecision =
        SkippedItem.ForDecision("d.xg", decisionId: null, "Corrupt candidate in d.xg, game 3, move 1, roll 31: bad.");

    [Fact]
    public void Empty_HoldsNothing_AndCountsNothing()
    {
        Assert.True(SkipRecord.Empty.IsEmpty);
        Assert.Empty(SkipRecord.Empty);
        Assert.Equal(0, SkipRecord.Empty.FileCount);
        Assert.Equal(0, SkipRecord.Empty.GameCount);
        Assert.Equal(0, SkipRecord.Empty.DecisionCount);
        Assert.Equal(string.Empty, SkipRecord.Empty.CountPhrase);
    }

    [Fact]
    public void With_ReturnsANewRecord_AndLeavesTheOriginalUnchanged()
    {
        var one = SkipRecord.Empty.With(File1);
        var two = one.With(Decision1);

        Assert.Equal([File1], one);
        Assert.Equal([File1, Decision1], two);
        Assert.True(SkipRecord.Empty.IsEmpty);
    }

    [Fact]
    public void Counts_AreEachEntrysUnit_NotWhetherItNamesADecision()
    {
        SkipRecord record = [File1, Decision1, Game1, OmittedDecision, File2];

        Assert.Equal(5, record.Count);
        Assert.Equal(2, record.FileCount);
        Assert.Equal(1, record.GameCount);

        // An export skip and an omitted play alike; the omitted play has no id,
        // and is no file.
        Assert.Equal(2, record.DecisionCount);
    }

    [Fact]
    public void AnEntry_StatesTheOneUnitItWasBuiltFor_AndOnlyADecisionCanCarryAnId()
    {
        Assert.Equal(SkipUnit.File, File1.Unit);
        Assert.Equal(SkipUnit.Game, Game1.Unit);
        Assert.Equal(SkipUnit.Decision, Decision1.Unit);
        Assert.Equal(SkipUnit.Decision, OmittedDecision.Unit);
        Assert.Null(File1.DecisionId);
        Assert.Null(Game1.DecisionId);
        Assert.Null(OmittedDecision.DecisionId);
        Assert.NotNull(Decision1.DecisionId);
    }

    [Fact]
    public void AnEntry_RefusesANullName_OrANullReason()
    {
        Assert.Throws<ArgumentNullException>(() => SkippedItem.ForFile(null!, "Why."));
        Assert.Throws<ArgumentNullException>(() => SkippedItem.ForGame("a.xg", null!));
        Assert.Throws<ArgumentNullException>(() => SkippedItem.ForDecision(null!, decisionId: null, "Why."));
    }

    [Fact]
    public void Entries_AreEqualOnlyWhenTheirUnitsAre()
    {
        Assert.Equal(SkippedItem.ForGame("a.xg", "Why."), SkippedItem.ForGame("a.xg", "Why."));
        Assert.NotEqual(SkippedItem.ForGame("a.xg", "Why."), SkippedItem.ForFile("a.xg", "Why."));
        Assert.NotEqual(SkippedItem.ForGame("a.xg", "Why."), SkippedItem.ForDecision("a.xg", decisionId: null, "Why."));
    }

    /// <summary>The phrase names every non-zero count, in the unit order, joined as English joins them.</summary>
    [Theory]
    [InlineData(1, 0, 0, "1 file")]
    [InlineData(3, 0, 0, "3 files")]
    [InlineData(0, 1, 0, "1 game")]
    [InlineData(0, 0, 2, "2 decisions")]
    [InlineData(2, 1, 0, "2 files and 1 game")]
    [InlineData(1, 0, 1, "1 file and 1 decision")]
    [InlineData(0, 2, 3, "2 games and 3 decisions")]
    [InlineData(1, 2, 3, "1 file, 2 games and 3 decisions")]
    public void CountPhrase_NamesEveryNonZeroCount(int files, int games, int decisions, string phrase)
    {
        var record = SkipRecord.Of(
            Enumerable.Repeat(File1, files)
                .Concat(Enumerable.Repeat(OmittedDecision, decisions))
                .Concat(Enumerable.Repeat(Game1, games)));

        Assert.Equal(phrase, record.CountPhrase);
    }

    [Fact]
    public void Of_CopiesItsInput_SoALaterChangeToTheSourceReachesNoRecord()
    {
        // The collection rider (halheinrich/backgammon#273): what a record
        // hands out is never a list its creator kept.
        var kept = new List<SkippedItem> { File1 };
        var record = SkipRecord.Of(kept);
        kept.Add(File2);

        // And the record itself offers no mutating surface: it is sealed and
        // implements no collection interface that writes, which the compiler
        // already holds.
        Assert.Equal([File1], record);
    }

    [Fact]
    public void NullEntries_AreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => SkipRecord.Of([File1, null!]));
        Assert.Throws<ArgumentNullException>(() => SkipRecord.Empty.With(null!));
        Assert.Throws<ArgumentNullException>(() => SkipRecord.Of(null!));
    }

    [Fact]
    public void Equality_IsTheSameEntriesInTheSameOrder()
    {
        SkipRecord a = [File1, Decision1];
        SkipRecord b = [File1, Decision1];
        SkipRecord reordered = [Decision1, File1];

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, reordered);
        Assert.True(a != reordered);
    }

    [Fact]
    public void Json_IsTheArrayOfTheEntries_AndANullEntryIsRefused()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SkipRecord record = [File1, Decision1, Game1, OmittedDecision];

        var json = JsonSerializer.Serialize(record, web);

        Assert.StartsWith("[", json, StringComparison.Ordinal);
        Assert.Equal(record, JsonSerializer.Deserialize<SkipRecord>(json, web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SkipRecord>("[null]", web));
    }
}
