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
/// rolls back; either way its job is no longer owed. An entry of the mark's
/// instance is newer by number; one of another instance only under a
/// reconciliation pass's authority, since marks of different instances are
/// not ordered.
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

    /// <summary>
    /// The next number is stated once, by the numbering: the one after the
    /// last used — 1 for a numbering that has used none — and it is derived,
    /// never stored.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 21)]
    public void TheNextNumber_IsTheOneAfterTheLastUsed_AndIsNotStored(int lastNumber, int next)
    {
        var numbering = new XgpNumbering("pos{n}", lastNumber, 3);

        Assert.Equal(next, numbering.NextNumber);
        Assert.DoesNotContain("next", System.Text.Json.JsonSerializer.Serialize(numbering), StringComparison.OrdinalIgnoreCase);
    }

    private static OwedXgpJob Owed(string jobId) => new(jobId, Current, From1, @"D:\xg\out");

    private static LedgerEntry Ended(int number, string jobId, int committed, XgpExportOptions? options = null) =>
        new(number, jobId, Running: false, new XgpExportCommit(options ?? From1, committed));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewerEntry_SetsTheNumberingFromItsAccounting_MovesTheMark_AndResolvesItsJob(bool underPassAuthority)
    {
        var applied = AfterTwoExports.Applying(Current, Ended(3, "job-9", 5, From1 with { StartNumber = 21 }), underPassAuthority);

        Assert.Equal(new XgpNumbering("pos{n}", 25, 3), applied.Numbering);
        Assert.Equal(new LedgerMark(Current, 3), applied.Mark);
        Assert.Equal(["job-1"], applied.Owed.Select(job => job.JobId));
    }

    /// <summary>
    /// A replay — the same entry again, or an older one of the mark's instance
    /// — changes neither the numbering nor the mark, even under a pass's
    /// authority, which orders nothing within an instance; it only resolves
    /// its job.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void AnEntryAtOrBeforeTheMark_ChangesNeitherTheNumberingNorTheMark_ButResolvesItsJob(int number, bool underPassAuthority)
    {
        var applied = AfterTwoExports.Applying(Current, Ended(number, "job-1", 7), underPassAuthority);

        Assert.Equal(AfterTwoExports.Numbering, applied.Numbering);
        Assert.Equal(AfterTwoExports.Mark, applied.Mark);
        Assert.Equal(["job-9"], applied.Owed.Select(job => job.JobId));
    }

    /// <summary>
    /// Marks of different instances are not ordered: an entry of another
    /// instance than the mark's changes neither the numbering nor the mark —
    /// whatever the page believes current — unless a reconciliation pass's
    /// authority stands behind it. Either way its job is resolved.
    /// </summary>
    [Fact]
    public void AnEntryOfAnotherInstance_IsNewerOnlyUnderAPassesAuthority()
    {
        var earlier = AfterTwoExports with { Mark = new LedgerMark(Earlier, 40) };

        var without = earlier.Applying(Current, Ended(1, "job-1", 3), underPassAuthority: false);
        var under = earlier.Applying(Current, Ended(1, "job-1", 3), underPassAuthority: true);

        Assert.Equal(earlier.Mark, without.Mark);
        Assert.Equal(earlier.Numbering, without.Numbering);
        Assert.Equal(["job-9"], without.Owed.Select(job => job.JobId));
        Assert.Equal(new LedgerMark(Current, 1), under.Mark);
        Assert.Equal(new XgpNumbering("pos{n}", 3, 3), under.Numbering);
    }

    [Fact]
    public void WhileNoMarkIsSet_AnEntryIsNewerOnlyUnderAPassesAuthority()
    {
        var unmarked = AfterTwoExports with { Mark = null };

        Assert.Null(unmarked.Applying(Current, Ended(1, "job-1", 3), underPassAuthority: false).Mark);
        Assert.Equal(new LedgerMark(Current, 1), unmarked.Applying(Current, Ended(1, "job-1", 3), underPassAuthority: true).Mark);
    }

    [Fact]
    public void AnEntryThatCommittedNothing_MovesTheMark_AndChangesNoNumber()
    {
        var applied = AfterTwoExports.Applying(Current, Ended(3, "job-9", 0), underPassAuthority: false);
        var unaccounted = AfterTwoExports.Applying(Current, new LedgerEntry(3, "job-9", Running: false, Commit: null), underPassAuthority: false);

        Assert.Equal(AfterTwoExports.Numbering, applied.Numbering);
        Assert.Equal(new LedgerMark(Current, 3), applied.Mark);
        Assert.Equal(applied, unaccounted);
    }

    [Fact]
    public void ARunningEntry_HasNothingToApply()
    {
        Assert.Throws<ArgumentException>(() =>
            AfterTwoExports.Applying(Current, new LedgerEntry(3, "job-9", Running: true, Commit: null), underPassAuthority: true));
    }

    /// <summary>
    /// The record already holds an entry at or before the mark of its own
    /// instance whose job is not owed — nothing to apply; it does not hold a
    /// newer one, one whose job is still owed, or any of another instance.
    /// </summary>
    [Fact]
    public void TheRecordHolds_OnlyEntriesAtOrBeforeItsMark_WhoseJobsAreNotOwed()
    {
        Assert.True(AfterTwoExports.Holds(Current, Ended(2, "job-2", 4)));
        Assert.True(AfterTwoExports.Holds(Current, Ended(1, "job-0", 4)));
        Assert.False(AfterTwoExports.Holds(Current, Ended(3, "job-3", 4)));
        Assert.False(AfterTwoExports.Holds(Current, Ended(1, "job-1", 4)));
        Assert.False(AfterTwoExports.Holds(Earlier, Ended(1, "job-0", 4)));
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

    /// <summary>
    /// Ledger marks are ordered by number within an instance, and not at all
    /// across instances: neither of two marks of different instances follows
    /// the other.
    /// </summary>
    [Fact]
    public void LedgerMarks_AreOrderedWithinAnInstance_AndNotAcrossInstances()
    {
        Assert.True(new LedgerMark(Current, 2).Follows(new LedgerMark(Current, 1)));
        Assert.False(new LedgerMark(Current, 1).Follows(new LedgerMark(Current, 1)));
        Assert.False(new LedgerMark(Current, 1).Follows(new LedgerMark(Current, 2)));
        Assert.False(new LedgerMark(Current, 9).Follows(new LedgerMark(Earlier, 1)));
        Assert.False(new LedgerMark(Earlier, 1).Follows(new LedgerMark(Current, 9)));
    }
}
