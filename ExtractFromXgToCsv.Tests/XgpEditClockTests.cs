using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What counts as an edit (the umbrella's <c>SPEC-notices.md</c> §7.5), as
/// Home's clock decides it: the controls follow an export the record took
/// unless the user has edited since it started. An export this page started
/// started at its own moment; of one another page started only a lower bound
/// is known — the latest entry this page started that the same ledger
/// admitted before it, or the moment the page last set the controls, if
/// later — and where nothing orders it, the user's edit stands.
/// </summary>
public class XgpEditClockTests
{
    private const string Instance = "instance-now";

    private readonly XgpEditClock _clock = new();

    private static LedgerEntry Ended(int number, string jobId, int committed = 3) =>
        new(number, jobId, Running: false, new XgpExportCommit(new XgpExportOptions(), committed));

    private static LedgerTaking Taking(LedgerEntry[] ledger, params int[] taken) =>
        new(new LedgerSnapshot(Instance, ledger), [.. ledger.Where(entry => taken.Contains(entry.Number))]);

    // ── An export this page started ─────────────────────────────────────────

    [Fact]
    public void AnEditBeforeThisPagesExportStarted_IsFollowedPast()
    {
        _clock.NoteEdit();
        _clock.NoteLocalStart("mine");

        Assert.True(_clock.Follows(Taking([Ended(1, "mine")], 1)));
    }

    [Fact]
    public void AnEditSinceThisPagesExportStarted_Stands()
    {
        _clock.NoteLocalStart("mine");
        _clock.NoteEdit();

        Assert.False(_clock.Follows(Taking([Ended(1, "mine")], 1)));
    }

    // ── An export another page started ──────────────────────────────────────

    /// <summary>
    /// The review's sequence: this page edits, then starts J; another page
    /// applies J and runs K; this page takes K. K is not this page's, but the
    /// ledger admitted J before it, and J started after the edit, so no edit
    /// followed K's start: the controls follow.
    /// </summary>
    [Fact]
    public void AnotherPagesExport_AdmittedAfterOneThisPageStartedPastTheEdit_IsFollowed()
    {
        _clock.NoteEdit();
        _clock.NoteLocalStart("J");

        Assert.True(_clock.Follows(Taking([Ended(1, "J"), Ended(2, "K")], 1, 2)));
    }

    [Fact]
    public void AnotherPagesExport_AdmittedAfterOneThisPageStartedBeforeTheEdit_LeavesTheEdit()
    {
        _clock.NoteLocalStart("J");
        _clock.NoteEdit();

        Assert.False(_clock.Follows(Taking([Ended(1, "J"), Ended(2, "K")], 2)));
    }

    /// <summary>
    /// Only an entry admitted before the export orders it: one this page
    /// started that the ledger admitted after it says nothing of when it
    /// started.
    /// </summary>
    [Fact]
    public void AnEntryThisPageStartedAdmittedAfterTheExport_SuppliesNoBound()
    {
        _clock.NoteEdit();
        _clock.NoteLocalStart("J");

        Assert.False(_clock.Follows(Taking([Ended(1, "K"), Ended(2, "J", committed: 0)], 1, 2)));
    }

    /// <summary>
    /// A start this page made that no ledger of the export's instance holds —
    /// refused, or named another instance — orders nothing.
    /// </summary>
    [Fact]
    public void AStartTheLedgerDoesNotHold_SuppliesNoBound()
    {
        _clock.NoteEdit();
        _clock.NoteLocalStart("refused-or-elsewhere");

        Assert.False(_clock.Follows(Taking([Ended(1, "K")], 1)));
    }

    [Fact]
    public void AStartForgotten_AsNeverRun_SuppliesNoBound()
    {
        _clock.NoteEdit();
        _clock.NoteLocalStart("J");
        _clock.ForgetLocalStart("J");

        Assert.False(_clock.Follows(Taking([Ended(1, "J"), Ended(2, "K")], 2)));
    }

    /// <summary>
    /// With nothing in the ledger to order the export, the latest moment the
    /// page set the controls bounds it: an edit before then no longer shows,
    /// and one after it stands.
    /// </summary>
    [Fact]
    public void WithNoEntryToOrderIt_TheMomentThePageLastSetTheControlsBoundsIt()
    {
        _clock.NoteEdit();
        _clock.NoteControlsSet();
        Assert.True(_clock.Follows(Taking([Ended(1, "K")], 1)));

        _clock.NoteEdit();
        Assert.False(_clock.Follows(Taking([Ended(1, "K"), Ended(2, "K2")], 2)));
    }

    /// <summary>The later of the two bounds is the one used.</summary>
    [Fact]
    public void TheLaterOfTheEarlierEntrysStartAndTheControlsSetting_IsTheBound()
    {
        _clock.NoteLocalStart("J");
        _clock.NoteEdit();
        _clock.NoteControlsSet();

        Assert.True(_clock.Follows(Taking([Ended(1, "J"), Ended(2, "K")], 2)));
    }

    // ── What there is to follow ─────────────────────────────────────────────

    [Fact]
    public void ATakingWhoseExportsCommittedNothing_HasNothingToFollow()
    {
        _clock.NoteLocalStart("mine");

        Assert.False(_clock.Follows(Taking([Ended(1, "mine", committed: 0)], 1)));
    }

    /// <summary>
    /// The export a taking holds is its last entry that committed something,
    /// so an empty one after it decides nothing about when that export started.
    /// </summary>
    [Fact]
    public void ATakingsExport_IsItsLastEntryThatCommittedSomething()
    {
        _clock.NoteLocalStart("mine");
        _clock.NoteEdit();
        _clock.NoteLocalStart("empty");

        Assert.False(_clock.Follows(Taking([Ended(1, "mine"), Ended(2, "empty", committed: 0)], 1, 2)));
    }

    // ── A Web download ──────────────────────────────────────────────────────

    [Fact]
    public void AnEditBeforeAWebDownloadBegan_IsFollowedPast_AndOneSinceStands()
    {
        _clock.NoteEdit();
        _clock.NoteWebDownloadStart();
        Assert.True(_clock.FollowsWebDownload());

        _clock.NoteEdit();
        Assert.False(_clock.FollowsWebDownload());
    }
}
