using System.Text.Json;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The one record type both modes keep (<c>SPEC-notices.md</c> §7.1):
/// immutable, ordered, its counts derived from its entries, and on the wire
/// the array of those entries.
/// </summary>
public class SkipRecordTests
{
    private static readonly SkippedItem File1 = new("a.xg", null, "Not a valid XG file.");
    private static readonly SkippedItem File2 = new(Path.Combine("sub", "b.xgp"), null, "Unexpected end of stream.");
    private static readonly SkippedItem Decision1 =
        new("c.xg", new XgDecisionId("c.xg", 1, 4, IsCube: true), "No decision at those coordinates.");

    [Fact]
    public void Empty_HoldsNothing_AndCountsNothing()
    {
        Assert.True(SkipRecord.Empty.IsEmpty);
        Assert.Empty(SkipRecord.Empty);
        Assert.Equal(0, SkipRecord.Empty.FileCount);
        Assert.Equal(0, SkipRecord.Empty.DecisionCount);
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
    public void Counts_TellFilesFromDecisions_ByWhetherAnEntryNamesADecision()
    {
        SkipRecord record = [File1, Decision1, File2];

        Assert.Equal(3, record.Count);
        Assert.Equal(2, record.FileCount);
        Assert.Equal(1, record.DecisionCount);
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
        SkipRecord record = [File1, Decision1];

        var json = JsonSerializer.Serialize(record, web);

        Assert.StartsWith("[", json, StringComparison.Ordinal);
        Assert.Equal(record, JsonSerializer.Deserialize<SkipRecord>(json, web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SkipRecord>("[null]", web));
    }
}
