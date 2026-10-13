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
/// property would bind to null rather than fail, and a
/// <see cref="DecisionId"/> without its bundled converter would not
/// round-trip at all. The record's own contract is <see cref="SkipRecordTests"/>'.
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
                new SkippedItem("bad.xg", null, "Not a valid XG file."),
                new SkippedItem(
                    "match.xg", new XgDecisionId("match.xg", 2, 14, IsCube: false), "Disk full."),
            ],
        };

        var json = JsonSerializer.Serialize(sent, Web);
        var received = JsonSerializer.Deserialize<ProcessingProgress>(json, Web)!;

        Assert.Equal(sent.Skipped, received.Skipped);
        Assert.Equal(1, received.Skipped.FileCount);
        Assert.Equal(1, received.Skipped.DecisionCount);

        // The record is the array of its entries on the wire, and the decision
        // crosses in the library's canonical form, not as an object graph of
        // its own fields.
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(json).RootElement.GetProperty("skipped").ValueKind);
        Assert.Contains("\"match.xg:g2:m14:play\"", json, StringComparison.Ordinal);
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
    /// snapshot a run reported, serves it to every status read and hands it to
    /// the builder of the terminal result, so a settable property would let
    /// any holder change what another reads, outside the store's lock.
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
