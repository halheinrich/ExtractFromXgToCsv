using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The terminal snapshots the job runner builds itself — after a stop and
/// after a failure — from a run's record (halheinrich/backgammon#374,
/// halheinrich/backgammon#399). Neither may take what the run did from the
/// last progress report: the pathways report every tenth file, and a
/// run-ending exception reports nothing, so the last report trails the
/// record. A stopped snapshot comes from the record alone: the file the run
/// reached, the elapsed time and throughput at the stop, and what it left at
/// its output.
/// </summary>
public class RunRecordTests
{
    private const string OutputPath = @"D:\xg\out (2).csv";

    private static readonly XgpExportOptions Options = new() { NamePattern = "r{n}", StartNumber = 9 };

    private readonly SteppedClock _clock = new();

    /// <summary>
    /// A record of a run over twelve files that has finished seven and taken
    /// up the eighth, producing <paramref name="rows"/>, two seconds after its
    /// walk began.
    /// </summary>
    private RunRecord RecordAtTheEighthFile(int rows = 41)
    {
        var record = new RunRecord(_clock);
        record.Begin(12);
        for (var done = 0; done < 7; done++)
        {
            record.BetweenFiles(done);
            record.TakeUpFile(done + 1);
        }
        record.Add(rows);
        record.BetweenFiles(7);
        record.TakeUpFile(8);
        _clock.Advance(TimeSpan.FromSeconds(2));
        return record;
    }

    [Fact]
    public void AStoppedSnapshot_TakesWhereTheRunStood_ItsTimeAndWhatItDid_FromTheRecord()
    {
        var record = RecordAtTheEighthFile();
        record.Skip(new SkippedItem("bad.xg", null, "Not a valid XG file."));

        var terminal = record.CancelledSnapshot();

        Assert.True(terminal.Complete);
        Assert.True(terminal.Cancelled);
        Assert.Null(terminal.ErrorMessage);
        Assert.Equal((8, 12), (terminal.Current, terminal.Total));
        Assert.Equal(41, terminal.TotalRows);
        Assert.Equal(2.0, terminal.ElapsedSec);
        // Seven files finished in two seconds.
        Assert.Equal(3, terminal.FilesPerSec);
        Assert.Equal(record.Skips, terminal.Skipped);
        Assert.Null(terminal.XgpCommit);
    }

    // Where a stop stood (Hal's wording, chat, 2026-10-23,
    // halheinrich/backgammon#424 6371257599): from the file reached and the
    // files finished, never from the file reached against the total.

    /// <summary>A one-file walk stopped before its file is taken up, and once it is: before the first input file, then during the input.</summary>
    [Fact]
    public void AStopBeforeTheFirstFile_IsBeforeIt_AndAStopInsideIt_IsDuringTheInput()
    {
        var record = new RunRecord(_clock);
        record.Begin(1);
        record.BetweenFiles(0);
        Assert.Equal(StopStage.BeforeFirstInputFile, record.CancelledSnapshot().StoppedAt);

        record.TakeUpFile(1);

        Assert.Equal(StopStage.DuringInput, record.CancelledSnapshot().StoppedAt);
    }

    /// <summary>
    /// A stop inside the last file has reached it without finishing it:
    /// during the input, though the file reached equals the total. Only the
    /// walk's end, every file finished, is after the input.
    /// </summary>
    [Fact]
    public void AStopInsideTheLastFile_IsDuringTheInput_AndOnlyTheWalksEnd_IsAfterIt()
    {
        var record = new RunRecord(_clock);
        record.Begin(1);
        record.BetweenFiles(0);
        record.TakeUpFile(1);
        var inside = record.CancelledSnapshot();
        Assert.Equal((1, 1), (inside.Current, inside.Total));
        Assert.Equal(StopStage.DuringInput, inside.StoppedAt);

        record.BetweenFiles(1);

        Assert.Equal(StopStage.AfterInput, record.CancelledSnapshot().StoppedAt);
    }

    /// <summary>A record whose walk never began has reached no file: before the first input file.</summary>
    [Fact]
    public void AStopBeforeTheWalkBegan_IsBeforeTheFirstInputFile()
    {
        Assert.Equal(StopStage.BeforeFirstInputFile, new RunRecord(_clock).CancelledSnapshot().StoppedAt);
    }

    /// <summary>Only a stopped snapshot names a stage.</summary>
    [Fact]
    public void AFailedSnapshot_NamesNoStage()
    {
        Assert.Null(RecordAtTheEighthFile().FailedSnapshot("Disk full.").StoppedAt);
    }

    [Fact]
    public void AStoppedSnapshot_ReadsTheClockAtTheStop_NotAtTheLastFile()
    {
        var record = RecordAtTheEighthFile();
        _clock.Advance(TimeSpan.FromSeconds(1.5));

        Assert.Equal(3.5, record.CancelledSnapshot().ElapsedSec);
    }

    [Fact]
    public void AStoppedSnapshot_BetweenFiles_HasReachedTheLastFileFinished()
    {
        var record = RecordAtTheEighthFile();
        record.BetweenFiles(8);

        var terminal = record.CancelledSnapshot();

        Assert.Equal(8, terminal.Current);
        Assert.Equal(4, terminal.FilesPerSec);
    }

    [Fact]
    public void AStoppedXgpSnapshot_CountsItsCommit()
    {
        var record = RecordAtTheEighthFile(rows: 3);
        record.Commit(new XgpExportCommit(Options, 3));

        var terminal = record.CancelledSnapshot();

        Assert.Equal(new XgpExportCommit(Options, 3), terminal.XgpCommit);
        Assert.Equal(3, terminal.TotalRows);
        Assert.Null(terminal.StoppedOutput);
    }

    [Fact]
    public void AStopBeforeTheOutputWasCreated_LeftNothing()
    {
        Assert.Null(RecordAtTheEighthFile().CancelledSnapshot().StoppedOutput);
    }

    [Fact]
    public void AStoppedCsvThatClosed_HoldsExactlyItsRows_AtThePathItWasCreatedAt()
    {
        var record = RecordAtTheEighthFile();
        record.OutputCreated(OutputPath, OutputFill.Appended);

        var terminal = record.CancelledSnapshot();

        Assert.Equal(new StoppedOutput(OutputPath, mayBeIncomplete: false), terminal.StoppedOutput);
        Assert.Equal(41, terminal.TotalRows);
        Assert.Null(terminal.CloseFailureMessage);
    }

    [Fact]
    public void AStoppedCsvWhoseCloseFailed_MayBeIncomplete_WithTheCloseFailureBesideIt()
    {
        var record = RecordAtTheEighthFile();
        record.OutputCreated(OutputPath, OutputFill.Appended);

        var terminal = record.CancelledSnapshot("Closing the unfinished out (2).csv failed: gone.");

        Assert.Equal(new StoppedOutput(OutputPath, mayBeIncomplete: true), terminal.StoppedOutput);
        Assert.Equal("Closing the unfinished out (2).csv failed: gone.", terminal.CloseFailureMessage);
    }

    [Fact]
    public void AStopOnceAWholeOutputWasCreated_MayBeIncomplete()
    {
        const string deck = @"D:\xg\deck.pdf";
        var record = RecordAtTheEighthFile();
        record.OutputCreated(deck, OutputFill.Whole);

        Assert.Equal(new StoppedOutput(deck, mayBeIncomplete: true), record.CancelledSnapshot().StoppedOutput);
    }

    [Fact]
    public void FailedSnapshot_CarriesTheErrorWithWhatTheRunSkippedAndCommitted()
    {
        var record = new RunRecord();
        record.Skip(new SkippedItem("bad.xg", null, "Not a valid XG file."));
        record.Commit(new XgpExportCommit(Options, 3));

        var terminal = record.FailedSnapshot("Disk full.");

        Assert.True(terminal.Complete);
        Assert.False(terminal.Cancelled);
        Assert.Equal("Disk full.", terminal.ErrorMessage);
        Assert.Equal(record.Skips, terminal.Skipped);
        Assert.Equal(new XgpExportCommit(Options, 3), terminal.XgpCommit);
        Assert.Equal(3, terminal.TotalRows);
    }

    /// <summary>A record belongs to one run: once its walk has begun it is not fresh, and it cannot begin again.</summary>
    [Fact]
    public void ARecordWhoseWalkHasBegun_IsNotFresh_AndCannotBeginAgain()
    {
        var record = new RunRecord(_clock);
        Assert.True(record.IsFresh);

        record.Begin(3);

        Assert.False(record.IsFresh);
        Assert.Throws<InvalidOperationException>(() => record.Begin(3));
    }
}
