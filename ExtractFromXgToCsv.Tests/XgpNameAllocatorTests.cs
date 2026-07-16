using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the per-run allocation rules of <see cref="XgpNameAllocator"/>:
/// the Windows-style duplicate uniquifier, the load-bearing Peek/Commit
/// split (failed decisions must not consume a number slot), and the
/// single validation throw point in <see cref="XgpNameAllocator.Create"/>.
/// </summary>
public class XgpNameAllocatorTests
{
    private static XgpNameAllocator Create(
        string pattern, int startNumber = 1, int suffixLength = 3, FilterConfig? filters = null) =>
        XgpNameAllocator.Create(
            new XgpExportOptions
            {
                NamePattern = pattern,
                StartNumber = startNumber,
                SuffixLength = suffixLength,
            },
            filters ?? new FilterConfig());

    private static DecisionRow Row(int roll = 31) => new()
    {
        Id = new XgDecisionId("t.xg", 1, 1, roll == 0),
        Roll = roll,
        MatchLength = 7,
        OnRollNeeds = 3,
        OpponentNeeds = 5,
    };

    [Fact]
    public void DuplicateBaseNames_GetWindowsStyleSuffixes()
    {
        var allocator = Create("quiz");
        var row = Row();

        Assert.Equal("quiz.xgp", allocator.Next(row));
        Assert.Equal("quiz (2).xgp", allocator.Next(row));
        Assert.Equal("quiz (3).xgp", allocator.Next(row));
    }

    [Fact]
    public void CounterPatterns_NeverUniquify()
    {
        var allocator = Create("pos{n}");
        var row = Row();

        Assert.Equal("pos001.xgp", allocator.Next(row));
        Assert.Equal("pos002.xgp", allocator.Next(row));
        Assert.Equal("pos003.xgp", allocator.Next(row));
    }

    [Fact]
    public void StartNumberAndSuffixLength_DriveTheCounterToken()
    {
        var allocator = Create("pos{n}", startNumber: 12, suffixLength: 4);
        var row = Row();

        Assert.Equal("pos0012.xgp", allocator.Next(row));
        Assert.Equal("pos0013.xgp", allocator.Next(row));
    }

    [Fact]
    public void Peek_IsIdempotent_AndConsumesNothing()
    {
        var allocator = Create("pos{n}");
        var row = Row();

        Assert.Equal("pos001.xgp", allocator.Peek(row));
        Assert.Equal("pos001.xgp", allocator.Peek(row));

        // Uniquifier state is untouched by Peek too: two peeks at the same
        // literal name both see the un-suffixed form.
        var literal = Create("same");
        Assert.Equal("same.xgp", literal.Peek(row));
        Assert.Equal("same.xgp", literal.Peek(row));
    }

    [Fact]
    public void PeekWithoutCommit_LeavesTheSlotForTheNextDecision()
    {
        // The failed-decision scenario: Local mode peeks a name, the write
        // fails, nothing is committed — the next decision must reuse the
        // same counter slot so the persisted counter matches what's on disk.
        var allocator = Create("pos{n}");

        Assert.Equal("pos001.xgp", allocator.Peek(Row(roll: 21))); // write fails
        Assert.Equal("pos001.xgp", allocator.Peek(Row(roll: 43))); // next decision
        allocator.Commit(Row(roll: 43));
        Assert.Equal("pos002.xgp", allocator.Peek(Row(roll: 65)));
    }

    [Fact]
    public void UniquifierBookkeeping_IsCaseInsensitive_LikeWindowsFilenames()
    {
        // No current token can render two base names differing only by case
        // (counter/dice are digits, score's shape is fixed), so the Windows
        // case-insensitivity contract can't be observed behaviorally yet —
        // it becomes live the day a free-text token (e.g. a player name)
        // joins the registry. Pin the comparer choice directly, per the
        // suite's private-invariant reflection idiom (see
        // WebModePanelFilteringTests).
        var allocator = Create("quiz");

        var counts = bUnitTestHelpers
            .GetPrivateField<Dictionary<string, int>>(allocator, "_baseNameCounts");
        Assert.Same(StringComparer.OrdinalIgnoreCase, counts.Comparer);

        var issued = bUnitTestHelpers
            .GetPrivateField<HashSet<string>>(allocator, "_issuedNames");
        Assert.Same(StringComparer.OrdinalIgnoreCase, issued.Comparer);
    }

    [Fact]
    public void Create_ThrowsArgumentException_PerInvalidOptionsShape()
    {
        // The allocator is the single validation throw point for both
        // export pathways — every TryValidate failure shape must surface.
        Assert.Throws<ArgumentException>(() => Create("pos{bogus}"));
        Assert.Throws<ArgumentException>(() => Create("a/b"));
        Assert.Throws<ArgumentException>(() => Create(""));
        Assert.Throws<ArgumentException>(() => Create("pos{n}", startNumber: 0));
        Assert.Throws<ArgumentException>(() => Create("pos{n}", suffixLength: 0));
        Assert.Throws<ArgumentException>(() => Create("pos{n}", suffixLength: 10));
    }

    [Fact]
    public void Create_ThrowsArgumentNullException_OnNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() =>
            XgpNameAllocator.Create(null!, new FilterConfig()));
        Assert.Throws<ArgumentNullException>(() =>
            XgpNameAllocator.Create(new XgpExportOptions(), null!));
    }
}
