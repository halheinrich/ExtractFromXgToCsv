using System.Text.Json;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The accounting an XGP export hands the numbering's owner
/// (<c>SPEC-notices.md</c> §7.1): its captured options and the files it
/// committed, valid by construction, crossing Local mode's status endpoint
/// intact.
/// </summary>
public class XgpExportCommitTests
{
    private static readonly XgpExportOptions Options = new() { NamePattern = "q{n}", StartNumber = 12, SuffixLength = 4 };

    [Fact]
    public void NextNumber_ContinuesAfterTheLastNumberUsed()
    {
        Assert.Equal(15, new XgpExportCommit(Options, 3).NextNumber);
        Assert.Equal(12, new XgpExportCommit(Options, 0).NextNumber);
    }

    [Fact]
    public void Construction_RefusesWhatNoExportCouldHaveCommitted()
    {
        Assert.Throws<ArgumentNullException>(() => new XgpExportCommit(null!, 1));
        Assert.Throws<ArgumentException>(() => new XgpExportCommit(Options with { NamePattern = "a/b{n}" }, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XgpExportCommit(Options, -1));
    }

    [Fact]
    public void Equality_IsTheOptionsAndTheCount()
    {
        Assert.Equal(new XgpExportCommit(Options, 3), new XgpExportCommit(Options with { }, 3));
        Assert.NotEqual(new XgpExportCommit(Options, 3), new XgpExportCommit(Options, 4));
        Assert.NotEqual(new XgpExportCommit(Options, 3), new XgpExportCommit(Options with { StartNumber = 13 }, 3));
    }

    [Fact]
    public void Json_RoundTripsOnASnapshot_UnderTheWebDefaultsBothEndsUse()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var sent = new ProcessingProgress { Complete = true, XgpCommit = new XgpExportCommit(Options, 3) };

        var json = JsonSerializer.Serialize(sent, web);
        var received = JsonSerializer.Deserialize<ProcessingProgress>(json, web)!;

        Assert.Equal(sent.XgpCommit, received.XgpCommit);
        // Derived, so never on the wire to disagree with the count.
        Assert.DoesNotContain("nextNumber", json, StringComparison.OrdinalIgnoreCase);
        // A snapshot of any other format carries none.
        Assert.Null(JsonSerializer.Deserialize<ProcessingProgress>("{\"complete\":true}", web)!.XgpCommit);
    }
}
