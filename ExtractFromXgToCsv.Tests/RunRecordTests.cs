using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The terminal snapshots the job runner builds itself — after a cancellation
/// and after a failure — from a run's record (halheinrich/backgammon#374).
/// Neither may take what the run skipped or committed from the last progress
/// report: the pathways report every tenth file, and a run-ending exception
/// reports nothing, so the last report trails the record.
/// </summary>
public class RunRecordTests
{
    private static readonly XgpExportOptions Options = new() { NamePattern = "r{n}", StartNumber = 9 };

    /// <summary>A record that skipped one file and committed three positions.</summary>
    private static RunRecord RecordAfterThreeWrites()
    {
        var record = new RunRecord();
        record.Skip(new SkippedItem("bad.xg", null, "Not a valid XG file."));
        record.Commit(new XgpExportCommit(Options, 3));
        return record;
    }

    /// <summary>The run's last report, made before the skip and the writes.</summary>
    private static readonly ProcessingProgress LastReport = new()
    {
        Current = 1,
        Total = 12,
        FileName = "first.xg",
        TotalRows = 0,
        ElapsedSec = 0.4,
        FilesPerSec = 2,
        Skipped = SkipRecord.Empty,
        XgpCommit = new XgpExportCommit(Options, 0),
    };

    [Fact]
    public void CancelledSnapshot_TakesWhereTheRunStoodFromItsLastReport_AndWhatItDidFromTheRecord()
    {
        var record = RecordAfterThreeWrites();

        var terminal = record.CancelledSnapshot(LastReport);

        Assert.True(terminal.Complete);
        Assert.True(terminal.Cancelled);
        Assert.Null(terminal.ErrorMessage);
        Assert.Equal((1, 12, "first.xg"), (terminal.Current, terminal.Total, terminal.FileName));
        Assert.Equal(record.Skips, terminal.Skipped);
        Assert.Equal(new XgpExportCommit(Options, 3), terminal.XgpCommit);
        // The line the client shows agrees with the accounting.
        Assert.Equal(3, terminal.TotalRows);
        Assert.NotSame(LastReport, terminal);
    }

    [Fact]
    public void CancelledSnapshot_OfARunWithNoCommit_KeepsItsLastReportedRowCount()
    {
        var record = new RunRecord();
        var last = new ProcessingProgress { Current = 3, Total = 8, TotalRows = 41 };

        var terminal = record.CancelledSnapshot(last);

        Assert.Null(terminal.XgpCommit);
        Assert.Equal(41, terminal.TotalRows);
    }

    [Fact]
    public void FailedSnapshot_CarriesTheErrorWithWhatTheRunSkippedAndCommitted()
    {
        var record = RecordAfterThreeWrites();

        var terminal = record.FailedSnapshot("Disk full.");

        Assert.True(terminal.Complete);
        Assert.False(terminal.Cancelled);
        Assert.Equal("Disk full.", terminal.ErrorMessage);
        Assert.Equal(record.Skips, terminal.Skipped);
        Assert.Equal(new XgpExportCommit(Options, 3), terminal.XgpCommit);
        Assert.Equal(3, terminal.TotalRows);
    }
}
