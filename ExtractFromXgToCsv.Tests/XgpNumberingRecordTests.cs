using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The numbering record's changes (the umbrella's <c>SPEC-notices.md</c> §7.5),
/// directly. Applying a ledger entry is one change of numbering, mark and owed
/// jobs together: an entry newer than the mark sets the numbering from its
/// accounting — none for an export that committed nothing — and moves the
/// mark; an entry at or before the mark changes neither, so a replay never
/// rolls back; either way its job is no longer owed.
/// </summary>
public class XgpNumberingRecordTests
{
    private const string Current = "instance-now";
    private const string Earlier = "instance-before";

    private static readonly XgpExportOptions From1 = new();

    private static readonly XgpNumberingRecord AfterTwoExports = new()
    {
        Numbering = new XgpNumbering("pos{n}", 20, 3),
        Mark = new LedgerMark(Current, 2),
        Owed = [Owed("job-1"), Owed("job-9")],
    };

    private static OwedXgpJob Owed(string jobId) => new(jobId, Current, From1, @"D:\xg\out");

    private static LedgerEntry Ended(int number, string jobId, int committed, XgpExportOptions? options = null) =>
        new(number, jobId, Running: false, new XgpExportCommit(options ?? From1, committed));

    [Fact]
    public void ANewerEntry_SetsTheNumberingFromItsAccounting_MovesTheMark_AndResolvesItsJob()
    {
        var applied = AfterTwoExports.Applying(Current, Ended(3, "job-9", 5, From1 with { StartNumber = 21 }));

        Assert.Equal(new XgpNumbering("pos{n}", 25, 3), applied.Numbering);
        Assert.Equal(new LedgerMark(Current, 3), applied.Mark);
        Assert.Equal(["job-1"], applied.Owed.Select(job => job.JobId));
    }

    /// <summary>
    /// A replay — the same entry again, an older one of this instance, or any
    /// entry of an earlier instance than the mark's — changes neither the
    /// numbering nor the mark; it only resolves its job.
    /// </summary>
    [Theory]
    [InlineData(Current, 2)]
    [InlineData(Current, 1)]
    public void AnEntryAtOrBeforeTheMark_ChangesNeitherTheNumberingNorTheMark_ButResolvesItsJob(string instance, int number)
    {
        var applied = AfterTwoExports.Applying(instance, Ended(number, "job-1", 7));

        Assert.Equal(AfterTwoExports.Numbering, applied.Numbering);
        Assert.Equal(AfterTwoExports.Mark, applied.Mark);
        Assert.Equal(["job-9"], applied.Owed.Select(job => job.JobId));
    }

    [Fact]
    public void AnEntryOfTheCurrentInstance_IsNewerThanAMarkOfAnEarlierOne()
    {
        var earlier = AfterTwoExports with { Mark = new LedgerMark(Earlier, 40) };

        var applied = earlier.Applying(Current, Ended(1, "job-1", 3));

        Assert.Equal(new LedgerMark(Current, 1), applied.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 3, 3), applied.Numbering);
    }

    [Fact]
    public void AnEntryThatCommittedNothing_MovesTheMark_AndChangesNoNumber()
    {
        var applied = AfterTwoExports.Applying(Current, Ended(3, "job-9", 0));
        var unaccounted = AfterTwoExports.Applying(Current, new LedgerEntry(3, "job-9", Running: false, Commit: null));

        Assert.Equal(AfterTwoExports.Numbering, applied.Numbering);
        Assert.Equal(new LedgerMark(Current, 3), applied.Mark);
        Assert.Equal(applied, unaccounted);
    }

    [Fact]
    public void ARunningEntry_HasNothingToApply()
    {
        Assert.Throws<ArgumentException>(() =>
            AfterTwoExports.Applying(Current, new LedgerEntry(3, "job-9", Running: true, Commit: null)));
    }

    [Fact]
    public void OwingAndResolving_ChangeOnlyTheOwedJobs_AndOweAJobOnce()
    {
        var owing = AfterTwoExports.Owing(Owed("job-5")).Owing(Owed("job-5"));

        Assert.Equal(["job-1", "job-9", "job-5"], owing.Owed.Select(job => job.JobId));
        Assert.Equal(AfterTwoExports, owing.Resolving("job-5"));
        Assert.Equal(AfterTwoExports.Mark, owing.Mark);
        Assert.Equal(AfterTwoExports.Numbering, owing.Numbering);
    }

    [Fact]
    public void LedgerMarks_AreOrderedWithinAnInstance_AndTheCurrentInstanceIsNewest()
    {
        Assert.True(new LedgerMark(Current, 2).IsNewerThan(new LedgerMark(Current, 1), Current));
        Assert.False(new LedgerMark(Current, 1).IsNewerThan(new LedgerMark(Current, 1), Current));
        Assert.True(new LedgerMark(Current, 1).IsNewerThan(new LedgerMark(Earlier, 9), Current));
        Assert.False(new LedgerMark(Earlier, 9).IsNewerThan(new LedgerMark(Current, 1), Current));
        Assert.True(new LedgerMark(Earlier, 1).IsNewerThan(null, Current));
        // Two earlier instances are not ordered: neither is newer.
        Assert.False(new LedgerMark(Earlier, 9).IsNewerThan(new LedgerMark("instance-older", 1), Current));
    }
}
