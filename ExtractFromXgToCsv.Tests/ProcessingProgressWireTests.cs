using System.Text.Json;
using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins that the skip record crosses the status endpoint intact
/// (halheinrich/backgammon#223). Neither end configures a serializer — the
/// server answers through a bare <c>AddControllers()</c> and the panel reads
/// with <c>ReadFromJsonAsync</c> — so both sides are
/// <see cref="JsonSerializerDefaults.Web"/>. The risk is silent: a
/// <see cref="SkippedItem"/> constructor parameter that stopped matching its
/// property would bind to null rather than fail, an entry whose unit went
/// unread would pass as a file, and a <see cref="DecisionId"/> without its
/// bundled converter would not round-trip at all. So an entry that states no
/// unit, or claims two, is refused here. The record's own contract is
/// <see cref="SkipRecordTests"/>'.
/// </summary>
public class ProcessingProgressWireTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SkipRecord_RoundTripsAsAnArrayOfItsEntries()
    {
        var sent = new ProcessingProgress
        {
            Complete = true,
            TotalRows = 7,
            Skipped =
            [
                SkippedItem.ForFile("bad.xg", "Not a valid XG file.\r\nThe header ends at byte 12."),
                SkippedItem.ForGame("match.xg", "Invalid standing in match.xg, game 1: A player in a match needs ... The standing is 19-away/19-away. The match length is 17."),
                SkippedItem.ForDecision(
                    "match.xg", new XgDecisionId("match.xg", 2, 14, IsCube: false), "Disk full."),
                SkippedItem.ForDecision("match.xg", decisionId: null, "Corrupt candidate in match.xg, game 3, move 1, roll 31: bad."),
            ],
        };

        var json = JsonSerializer.Serialize(sent, Web);
        var received = JsonSerializer.Deserialize<ProcessingProgress>(json, Web)!;

        Assert.Equal(sent.Skipped, received.Skipped);
        Assert.Equal(1, received.Skipped.FileCount);
        Assert.Equal(1, received.Skipped.GameCount);
        Assert.Equal(2, received.Skipped.DecisionCount);

        // The record is the array of its entries on the wire, and the decision
        // crosses in the library's canonical form, not as an object graph of
        // its own fields.
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(json).RootElement.GetProperty("skipped").ValueKind);
        Assert.Contains("\"match.xg:g2:m14:play\"", json, StringComparison.Ordinal);
    }

    /// <summary>Each unit crosses as a name, as a stopped snapshot's stage does, and reads back.</summary>
    [Theory]
    [InlineData(SkipUnit.File, "File")]
    [InlineData(SkipUnit.Game, "Game")]
    [InlineData(SkipUnit.Decision, "Decision")]
    public void AUnit_CrossesAsAName(SkipUnit unit, string name)
    {
        var sent = unit switch
        {
            SkipUnit.File => SkippedItem.ForFile("a.xg", "Why."),
            SkipUnit.Game => SkippedItem.ForGame("a.xg", "Why."),
            _ => SkippedItem.ForDecision("a.xg", decisionId: null, "Why."),
        };

        var json = JsonSerializer.Serialize(sent, Web);

        Assert.Contains($"\"unit\":\"{name}\"", json, StringComparison.Ordinal);
        Assert.Equal(sent, JsonSerializer.Deserialize<SkippedItem>(json, Web));
    }

    /// <summary>Only a decision carries an id, however the body says otherwise.</summary>
    [Theory]
    [InlineData("File")]
    [InlineData("Game")]
    public void AnEntryClaimingAnIdWithoutBeingADecision_IsRefused(string unit)
    {
        var json = $$"""{"unit":"{{unit}}","fileName":"a.xg","decisionId":"a.xg:g1:m1:play","reason":"Why."}""";

        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<SkippedItem>(json, Web));
    }

    /// <summary>
    /// An entry that states no unit is refused, never read as the first one:
    /// that would call a game or a decision a file, the very miscount the unit
    /// exists to end.
    /// </summary>
    [Fact]
    public void AnEntryThatNamesNoUnit_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => JsonSerializer.Deserialize<SkippedItem>("""{"fileName":"a.xg","reason":"Why."}""", Web));
    }

    /// <summary>So is a unit outside the three, by name or by number.</summary>
    [Theory]
    [InlineData("""{"unit":"Match","fileName":"a.xg","reason":"Why."}""")]
    [InlineData("""{"unit":1,"fileName":"a.xg","reason":"Why."}""")]
    public void AnEntryNamingNoneOfTheThreeUnits_IsRefused(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SkippedItem>(json, Web));
    }

    /// <summary>
    /// A stopped snapshot's stage crosses as a name and reads back; a
    /// snapshot that names none reads as none.
    /// </summary>
    [Fact]
    public void AStoppedSnapshotsStage_CrossesAsAName()
    {
        var sent = new ProcessingProgress { Complete = true, Cancelled = true, StoppedAt = StopStage.AfterInput };

        var json = JsonSerializer.Serialize(sent, Web);

        Assert.Contains("\"stoppedAt\":\"AfterInput\"", json, StringComparison.Ordinal);
        Assert.Equal(StopStage.AfterInput, JsonSerializer.Deserialize<ProcessingProgress>(json, Web)!.StoppedAt);
        Assert.Null(JsonSerializer.Deserialize<ProcessingProgress>("{\"complete\":true}", Web)!.StoppedAt);
    }

    [Fact]
    public void SnapshotWithoutSkips_DeserializesToAnEmptyRecord()
    {
        // A body from a sender that never wrote the field (the endpoint's shape
        // before the record existed) reads as "nothing skipped", never null.
        var received = JsonSerializer.Deserialize<ProcessingProgress>(
            "{\"complete\":true,\"totalRows\":3}", Web)!;

        Assert.Same(SkipRecord.Empty, received.Skipped);
    }

    [Fact]
    public void Skipped_RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessingProgress { Skipped = null! });
    }

    /// <summary>
    /// A snapshot is immutable: every property is set when it is made, or
    /// computed, and none can be set afterwards. The job store keeps the
    /// snapshot a run reported and serves it to every status read, so a
    /// settable property would let any holder change what another reads,
    /// outside the store's lock.
    /// </summary>
    [Fact]
    public void ASnapshot_HasNoPropertyThatCanBeSetAfterItIsMade()
    {
        var settable = typeof(ProcessingProgress).GetProperties()
            .Where(property => property.SetMethod is { } setter
                && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)))
            .Select(property => property.Name);

        Assert.Empty(settable);
    }
}
