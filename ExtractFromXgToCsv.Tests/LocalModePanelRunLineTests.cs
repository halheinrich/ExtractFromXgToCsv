using System.Text.RegularExpressions;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The Local panel's terminal lines say what the run did
/// (halheinrich/backgammon#399): each line below is Hal's wording (chat,
/// 2026-10-23), held here as independent literals, and each is shown for the
/// terminal snapshot the run's record builds — a stopped run's file reached,
/// rows and time, and what it left at its output (<see cref="StoppedOutput"/>):
/// nothing, a CSV that closed holding exactly its rows, or a file whose
/// contents are uncertain, which shows no count. An XGP run says files, not
/// rows, in both its lines. Every case is a real run against a stub server
/// answering that snapshot; the snapshots themselves are pinned against the
/// record in <see cref="RunRecordTests"/> and <see cref="LocalFolderProcessorStopTests"/>.
/// </summary>
public class LocalModePanelRunLineTests : BunitContext
{
    private const string CreatedCsv = @"D:\xg\out (2).csv";
    private const string CloseFailure = "Closing the unfinished out (2).csv failed: The network name is no longer available.";

    public LocalModePanelRunLineTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    // The figures as the panel formats them, in the culture the test runs in.
    private static readonly string HalfSecond = 0.5.ToString("F1");

    private static readonly XgpExportCommit SevenFiles = new(new XgpExportOptions(), 7);

    /// <summary>A stopped snapshot at file 2 of 3, half a second in, two files finished.</summary>
    private static ProcessingProgress Stopped(int rows = 57) => new()
    {
        Current = 2,
        Total = 3,
        Complete = true,
        Cancelled = true,
        TotalRows = rows,
        ElapsedSec = 0.5,
        FilesPerSec = 4,
    };

    /// <summary>The line the panel shows after a <paramref name="format"/> run ending in <paramref name="terminal"/>, its whitespace as the browser renders it.</summary>
    private async Task<IRenderedComponent<Client.Components.LocalModePanel>> RunAsync(ProcessingProgress terminal, OutputFormat format = OutputFormat.Csv)
    {
        Services.AddSingleton(new HttpClient(new CapturingProcessHandler(terminal)) { BaseAddress = new Uri("http://localhost/") });
        var host = await LocalPanelHost.RenderReadyAsync(this, format);
        await host.RunToCompletionAsync();
        return host.Panel;
    }

    private static string Shown(IRenderedComponent<Client.Components.LocalModePanel> panel, string selector) =>
        Regex.Replace(panel.Find(selector).TextContent, @"\s+", " ").Trim();

    [Fact]
    public async Task ACsvStoppedWithItsFileClosed_SaysTheRowsItHoldsAndWhere()
    {
        var panel = await RunAsync(Stopped() with { StoppedOutput = new StoppedOutput(CreatedCsv, mayBeIncomplete: false) });

        Assert.Equal(
            $@"⏹ Stopped at file 2 of 3 — 57 rows written to {CreatedCsv} in {HalfSecond}s (4 files/sec)",
            Shown(panel, "#stoppedLine"));
        Assert.Empty(panel.FindAll("#closeFailure"));
    }

    /// <summary>A stop before the run created its file — any CSV, Diagram JSON, PPTX or PDF run: the snapshot is the same.</summary>
    [Fact]
    public async Task AStopBeforeTheOutputWasCreated_SaysNothingWritten()
    {
        var panel = await RunAsync(Stopped(rows: 0));

        Assert.Equal("⏹ Stopped at file 2 of 3 — nothing written", Shown(panel, "#stoppedLine"));
    }

    /// <summary>A CSV whose close failed after the stop: what it holds is uncertain, so no count, and the close failure beside the line.</summary>
    [Fact]
    public async Task ACsvWhoseCloseFailed_SaysItsOutputMayBeIncomplete_WithNoCount_AndTheCloseFailureBesideIt()
    {
        var panel = await RunAsync(Stopped() with
        {
            StoppedOutput = new StoppedOutput(CreatedCsv, mayBeIncomplete: true),
            CloseFailureMessage = CloseFailure,
        });

        Assert.Equal($@"⏹ Stopped at file 2 of 3 — output may be incomplete: {CreatedCsv}", Shown(panel, "#stoppedLine"));
        Assert.Equal(CloseFailure, Shown(panel, "#closeFailure"));
    }

    /// <summary>A Diagram JSON, PPTX or PDF run stopped during its write.</summary>
    [Fact]
    public async Task AStopDuringTheWrite_SaysTheOutputMayBeIncomplete()
    {
        const string deck = @"D:\xg\deck.pdf";
        var panel = await RunAsync(Stopped() with { StoppedOutput = new StoppedOutput(deck, mayBeIncomplete: true) });

        Assert.Equal($@"⏹ Stopped at file 2 of 3 — output may be incomplete: {deck}", Shown(panel, "#stoppedLine"));
        Assert.Empty(panel.FindAll("#closeFailure"));
    }

    [Fact]
    public async Task AStoppedXgpRun_SaysTheFilesItsCommitCounts()
    {
        var panel = await RunAsync(Stopped(rows: 7) with { XgpCommit = SevenFiles }, OutputFormat.Xgp);

        Assert.Equal(
            $"⏹ Stopped at file 2 of 3 — 7 files written in {HalfSecond}s (4 files/sec)",
            Shown(panel, "#stoppedLine"));
    }

    [Fact]
    public async Task ACompletedXgpRun_SaysFilesWritten_AndItsFolder()
    {
        var panel = await RunAsync(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 7,
            ElapsedSec = 0.5,
            FilesPerSec = 6,
            WrittenPath = LocalPanelHost.XgpOutputFolder,
            XgpCommit = SevenFiles,
        }, OutputFormat.Xgp);

        Assert.Equal(
            $@"✓ Done — 7 files written in {HalfSecond}s (6 files/sec) → {LocalPanelHost.XgpOutputFolder}",
            Shown(panel, "#doneLine"));
    }

    [Fact]
    public async Task ACompletedCsvRun_StillSaysRowsWritten()
    {
        var panel = await RunAsync(new ProcessingProgress
        {
            Current = 3,
            Total = 3,
            Complete = true,
            TotalRows = 57,
            ElapsedSec = 0.5,
            FilesPerSec = 6,
            WrittenPath = CreatedCsv,
        });

        Assert.Equal($@"✓ Done — 57 rows written in {HalfSecond}s (6 files/sec) → {CreatedCsv}", Shown(panel, "#doneLine"));
    }
}
