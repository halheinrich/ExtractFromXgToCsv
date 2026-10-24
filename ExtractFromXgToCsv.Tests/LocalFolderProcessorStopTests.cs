using BackgammonDiagram_Lib;
using BackgammonDiagram_Lib.ExportRaster;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// What a stopped run reports and leaves (halheinrich/backgammon#399, item 1):
/// the stopped snapshot the job runner builds from the run's record says the
/// file the run reached, the elapsed time at the stop and, for its one output
/// file, whether it was created, where, and whether what it holds is known —
/// never the last progress report's figures, which trail the run by up to ten
/// files. No output is created once the run is stopped: a deck's render takes
/// the run's token and ends at the stop, and the token is checked again
/// immediately before the create, so a run stopped before its write writes
/// nothing; a stop that lands once the file exists leaves it, reported as
/// possibly incomplete.
/// </summary>
/// <remarks>
/// A run is stopped at a chosen decision by <see cref="CancelAtCall"/>, during
/// a deck's render by a renderer stand-in, and once its output exists by
/// <see cref="ScriptedDestination.AfterCreate"/>. The record's clock is a
/// <see cref="SteppedClock"/>, moved only at the stop, so the time at the stop
/// is exact.
/// </remarks>
public class LocalFolderProcessorStopTests : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    private readonly string _folder = Directory.CreateTempSubdirectory("xg-stop-").FullName;
    private readonly string _outputFolder;
    private readonly SteppedClock _clock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly RunRecord _record;

    public LocalFolderProcessorStopTests()
    {
        // Discovery reads only .xg and .xgp files, so the outputs can sit
        // under the input folder.
        _outputFolder = Directory.CreateDirectory(Path.Combine(_folder, "out")).FullName;
        _record = new RunRecord(_clock);
        foreach (var name in new[] { "a.xg", "b.xg", "c.xg" })
            File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(_folder, name));
    }

    public void Dispose()
    {
        _cts.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>The fixture's decisions, as each copy of it contributes.</summary>
    private static int DecisionsPerFile { get; } =
        XgDecisionIterator.Iterate(XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, Fixture)), Fixture).Count();

    private static LocalFolderProcessor Processor(IOutputDestination? destination = null) =>
        new(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance,
            AppRanking.Ranking, bookProvider: null, XgpPayload.Prepare, destination ?? FileSystemDestination.Instance);

    private string Output(string name) => Path.Combine(_outputFolder, name);

    /// <summary>A filter set that stops the run, half a second in, at the second file's first decision.</summary>
    private DecisionFilterSet StopInTheSecondFile() =>
        CancelAtCall.Set(DecisionsPerFile + 1, () =>
        {
            _clock.Advance(TimeSpan.FromSeconds(0.5));
            _cts.Cancel();
        });

    /// <summary>
    /// A deck renderer stand-in that ignores the token it is handed:
    /// <paramref name="whileRendering"/> runs inside it, and it returns a few
    /// bytes whatever has been stopped.
    /// </summary>
    private static Func<IEnumerable<DiagramRequest>, DiagramOptions, CancellationToken, byte[]> Renderer(Action? whileRendering = null) =>
        (requests, _, _) =>
        {
            Assert.NotEmpty(requests);
            whileRendering?.Invoke();
            return [1, 2, 3, 4];
        };

    private Task DeckAsync(
        string outputPath, DecisionFilterSet filters, RecordingProgress progress,
        Func<IEnumerable<DiagramRequest>, DiagramOptions, CancellationToken, byte[]> renderer, IOutputDestination? destination = null) =>
        Processor(destination).ProcessDeckAsync(
            _folder, outputPath, filters, progress, renderer, "PDF", _record, _cts.Token);

    // ── CSV ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stopped in the second of three files: the run reached the second file,
    /// its file — created under the next free name, as one existed — holds
    /// exactly the rows the stop reports, and the time is the stop's. The
    /// last progress report, made at the first file, says none of it.
    /// </summary>
    [Fact]
    public async Task ACsvRunStoppedAtTheSecondFile_ReportsTheFileReached_TheRowsItsFileHolds_ItsPath_AndTheTimeAtTheStop()
    {
        File.WriteAllText(Output("out.csv"), "not the run's");
        var progress = new RecordingProgress();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessAsync(
            _folder, Output("out.csv"), StopInTheSecondFile(), progress, _record, _cts.Token));

        var stopped = _record.CancelledSnapshot();
        var created = Output("out (2).csv");
        var rowsHeld = File.ReadAllLines(created).Length - 1;
        Assert.Equal(2 * DecisionsPerFile, rowsHeld);
        Assert.Equal((2, 3), (stopped.Current, stopped.Total));
        Assert.Equal(StopStage.DuringInput, stopped.StoppedAt);
        Assert.Equal(rowsHeld, stopped.TotalRows);
        Assert.Equal(new StoppedOutput(created, mayBeIncomplete: false), stopped.StoppedOutput);
        Assert.Equal(0.5, stopped.ElapsedSec);
        // Two files finished in half a second.
        Assert.Equal(4, stopped.FilesPerSec);
        Assert.Equal("not the run's", File.ReadAllText(Output("out.csv")));

        // What the stop line would have said from the last report.
        Assert.Equal((1, 0, 0.0), (progress.Last!.Current, progress.Last.TotalRows, progress.Last.ElapsedSec));
    }

    [Fact]
    public async Task ACsvRunStoppedBeforeItsCreate_WritesNoFile_AndReportsNothingWritten()
    {
        _cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessAsync(
            _folder, Output("out.csv"), new DecisionFilterSet(), new RecordingProgress(), _record, _cts.Token));

        Assert.Empty(Directory.GetFiles(_outputFolder));
        var stopped = _record.CancelledSnapshot();
        Assert.Null(stopped.StoppedOutput);
        Assert.Equal((0, 3), (stopped.Current, stopped.Total));
        Assert.Equal(StopStage.BeforeFirstInputFile, stopped.StoppedAt);
    }

    /// <summary>
    /// A CSV whose close fails after the stop: what it holds is uncertain, so
    /// the stop reports its path as possibly incomplete, with the close
    /// failure beside it; the run is still stopped.
    /// </summary>
    [Fact]
    public async Task ACsvWhoseCloseFailsAfterTheStop_ReportsItsPathAsPossiblyIncomplete_TheCloseFailureBesideIt()
    {
        var destination = new ScriptedDestination { FailCloseOf = "out.csv" };

        var ended = await Assert.ThrowsAsync<CancelledWithUnfinishedOutputException>(() => Processor(destination).ProcessAsync(
            _folder, Output("out.csv"), StopInTheSecondFile(), new RecordingProgress(), _record, _cts.Token));

        var stopped = _record.CancelledSnapshot(ended.CloseFailureMessage);
        Assert.True(stopped.Cancelled);
        Assert.Equal(StopStage.DuringInput, stopped.StoppedAt);
        Assert.Equal(new StoppedOutput(Output("out.csv"), mayBeIncomplete: true), stopped.StoppedOutput);
        Assert.Equal($"Closing the unfinished out.csv failed: {ScriptedDestination.CloseFailure}", stopped.CloseFailureMessage);
    }

    // ── Diagram JSON and the decks ──────────────────────────────────────────

    [Fact]
    public async Task ADiagramJsonRunStoppedDuringTheRead_CreatesNoFile_AndReportsNothingWritten()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessDiagramAsync(
            _folder, Output("out.json"), StopInTheSecondFile(), new RecordingProgress(), _record, _cts.Token));

        Assert.Empty(Directory.GetFiles(_outputFolder));
        var stopped = _record.CancelledSnapshot();
        Assert.Null(stopped.StoppedOutput);
        Assert.Equal((2, 3), (stopped.Current, stopped.Total));
        Assert.Equal(StopStage.DuringInput, stopped.StoppedAt);
    }

    [Fact]
    public async Task ADeckRunStoppedDuringTheRead_CreatesNoFile_AndReportsNothingWritten()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeckAsync(
            Output("deck.pdf"), StopInTheSecondFile(), new RecordingProgress(),
            (_, _, _) => throw new InvalidOperationException("A stopped walk renders nothing.")));

        Assert.Empty(Directory.GetFiles(_outputFolder));
        Assert.Null(_record.CancelledSnapshot().StoppedOutput);
        Assert.Equal(StopStage.DuringInput, _record.CancelledSnapshot().StoppedAt);
    }

    /// <summary>
    /// The safety net: a render that ignores its token and returns despite a
    /// Stop pressed during it is caught at the check before the output is
    /// created (CreateOutput) — nothing is written, where before that check
    /// the run created the file and wrote nothing into it.
    /// </summary>
    [Fact]
    public async Task ADeckRenderThatReturnsDespiteAStop_IsCaughtBeforeTheCreate_AndWritesNothing()
    {
        var progress = new RecordingProgress();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeckAsync(
            Output("deck.pdf"), new DecisionFilterSet(), progress, Renderer(whileRendering: _cts.Cancel)));

        Assert.Empty(Directory.GetFiles(_outputFolder));
        var stopped = _record.CancelledSnapshot();
        Assert.Null(stopped.StoppedOutput);
        Assert.Equal((3, 3), (stopped.Current, stopped.Total));
        Assert.Equal(StopStage.AfterInput, stopped.StoppedAt);
        Assert.Equal(JobPhase.Rendering, progress.Last!.Phase);
    }

    /// <summary>
    /// A stop that lands once the one output file exists — here the moment it
    /// is created, before its write — leaves the file, under the path it was
    /// created at, reported as possibly incomplete; a failure then closing it
    /// is reported beside the stop.
    /// </summary>
    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("pdf", false)]
    [InlineData("pdf", true)]
    public async Task AStopOnceTheOutputExists_ReportsTheCreatedPathAsPossiblyIncomplete(string format, bool closeFails)
    {
        var name = $"out.{format}";
        File.WriteAllText(Output(name), "not the run's");
        var created = Output($"out (2).{format}");
        var destination = new ScriptedDestination
        {
            AfterCreate = _cts.Cancel,
            FailCloseOf = closeFails ? Path.GetFileName(created) : null,
        };

        var ended = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => format == "json"
            ? Processor(destination).ProcessDiagramAsync(
                _folder, Output(name), new DecisionFilterSet(), new RecordingProgress(), _record, _cts.Token)
            : DeckAsync(Output(name), new DecisionFilterSet(), new RecordingProgress(), Renderer(), destination));

        var closeFailure = (ended as CancelledWithUnfinishedOutputException)?.CloseFailureMessage;
        Assert.Equal(closeFails, closeFailure is not null);
        var stopped = _record.CancelledSnapshot(closeFailure);
        Assert.Equal(new StoppedOutput(created, mayBeIncomplete: true), stopped.StoppedOutput);
        Assert.Equal(closeFailure, stopped.CloseFailureMessage);
        Assert.True(File.Exists(created));
        Assert.Equal(StopStage.AfterInput, stopped.StoppedAt);
    }

    // ── Where the run stopped ───────────────────────────────────────────────
    //
    // The stopped line opens with where the run stood among its input files
    // (Hal's wording, chat, 2026-10-23, halheinrich/backgammon#424
    // 6371257599), derived from the record's two facts. One input file is
    // enough to reach all three stages, which the file reached against the
    // total could not tell apart: 0 of 1 inside the walk's first step and 1
    // of 1 both inside the file and after it.

    private void LeaveOneInputFile()
    {
        File.Delete(Path.Combine(_folder, "b.xg"));
        File.Delete(Path.Combine(_folder, "c.xg"));
    }

    /// <summary>
    /// One input file, the run stopped once its CSV was created and before the
    /// file was taken up: before the first input file, the CSV holding its
    /// header alone, 0 rows.
    /// </summary>
    [Fact]
    public async Task AOneFileCsvStoppedBeforeItsFile_IsBeforeTheFirstInputFile_ItsCsvHoldingItsHeaderAlone()
    {
        LeaveOneInputFile();
        var destination = new ScriptedDestination { AfterCreate = _cts.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(destination).ProcessAsync(
            _folder, Output("out.csv"), new DecisionFilterSet(), new RecordingProgress(), _record, _cts.Token));

        var stopped = _record.CancelledSnapshot();
        Assert.Equal(StopStage.BeforeFirstInputFile, stopped.StoppedAt);
        Assert.Equal((0, 1), (stopped.Current, stopped.Total));
        Assert.Equal(new StoppedOutput(Output("out.csv"), mayBeIncomplete: false), stopped.StoppedOutput);
        Assert.Equal(0, stopped.TotalRows);
        Assert.Single(File.ReadAllLines(Output("out.csv")));
    }

    /// <summary>
    /// The same run stopped inside its one file — taken up, not finished:
    /// during the input, though the file reached is the last.
    /// </summary>
    [Fact]
    public async Task AOneFileCsvStoppedInsideItsFile_IsDuringTheInput_ThoughItReachedTheLastFile()
    {
        LeaveOneInputFile();
        var progress = new RecordingProgress(report =>
        {
            if (report.Current == 1)
                _cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessAsync(
            _folder, Output("out.csv"), new DecisionFilterSet(), progress, _record, _cts.Token));

        var stopped = _record.CancelledSnapshot();
        Assert.Equal(StopStage.DuringInput, stopped.StoppedAt);
        Assert.Equal((1, 1), (stopped.Current, stopped.Total));
    }

    /// <summary>
    /// The same one file processed whole, the run stopped at its write: after
    /// the input, with nothing written.
    /// </summary>
    [Fact]
    public async Task AOneFileDiagramJsonStoppedAtItsWrite_IsAfterTheInput()
    {
        LeaveOneInputFile();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessDiagramAsync(
            _folder, Output("out.json"), CancelAtCall.Set(1, _cts), new RecordingProgress(), _record, _cts.Token));

        var stopped = _record.CancelledSnapshot();
        Assert.Equal(StopStage.AfterInput, stopped.StoppedAt);
        Assert.Equal((1, 1), (stopped.Current, stopped.Total));
        Assert.Null(stopped.StoppedOutput);
    }

    /// <summary>
    /// Every pathway can be stopped before its first input file — here, Stop
    /// pressed before the run began — and each reports that stage with its
    /// own ending: nothing written, or for XGP, no files.
    /// </summary>
    [Theory]
    [InlineData(OutputFormat.Csv)]
    [InlineData(OutputFormat.DiagramJson)]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Xgp)]
    public async Task EveryPathway_StoppedBeforeItsFirstFile_IsBeforeTheFirstInputFile(OutputFormat format)
    {
        _cts.Cancel();
        var progress = new RecordingProgress();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => format switch
        {
            OutputFormat.Csv => Processor().ProcessAsync(
                _folder, Output("out.csv"), new DecisionFilterSet(), progress, _record, _cts.Token),
            OutputFormat.DiagramJson => Processor().ProcessDiagramAsync(
                _folder, Output("out.json"), new DecisionFilterSet(), progress, _record, _cts.Token),
            OutputFormat.Pdf => DeckAsync(
                Output("deck.pdf"), new DecisionFilterSet(), progress,
                (_, _, _) => throw new InvalidOperationException("A stopped walk renders nothing.")),
            _ => Processor().ProcessXgpAsync(
                _folder, _outputFolder, new DecisionFilterSet(), new XgpExportOptions(), new FilterConfig(),
                progress, runRecord: _record, cancellationToken: _cts.Token),
        });

        var stopped = _record.CancelledSnapshot();
        Assert.Equal(StopStage.BeforeFirstInputFile, stopped.StoppedAt);
        Assert.Equal(0, stopped.Current);
        Assert.Null(stopped.StoppedOutput);
        Assert.Equal(0, stopped.TotalRows);
        Assert.Empty(Directory.GetFiles(_outputFolder));
    }

    /// <summary>
    /// A deck run with a skipped file, stopped during its render: every input
    /// file was processed — the skipped one included, though it was never
    /// read — so the stage is after the input, and the skip is the run's.
    /// </summary>
    [Fact]
    public async Task ADeckRunWithASkip_StoppedDuringItsRender_IsAfterTheInput()
    {
        LeaveOneInputFile();
        MalformedXg.Write(Path.Combine(_folder, "broken.xg"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeckAsync(
            Output("deck.pdf"), new DecisionFilterSet(), new RecordingProgress(), Renderer(whileRendering: _cts.Cancel)));

        var stopped = _record.CancelledSnapshot();
        Assert.Equal(StopStage.AfterInput, stopped.StoppedAt);
        Assert.Equal((2, 2), (stopped.Current, stopped.Total));
        Assert.Equal("broken.xg", Assert.Single(stopped.Skipped).FileName);
        Assert.Null(stopped.StoppedOutput);
    }

    // ── A Stop during the render ────────────────────────────────────────────
    //
    // The render takes the run's token (halheinrich/backgammon#424, Hal's
    // word 2026-10-23): BackgammonDiagram_Lib honours it between pages, so a
    // Stop pressed during a long render ends it there, rather than waiting
    // for the whole document.

    // The real PDF renders below go through QuestPDF, whose license must be
    // set before any call; as the suite's other PDF tests do, this class sets
    // its own.
    static LocalFolderProcessorStopTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    /// <summary>
    /// A render stand-in that honours the token it is handed, as the library
    /// does: page by page, the token checked before each, the Stop pressed
    /// once <see cref="StopAt"/> pages are built. It records how far it got.
    /// </summary>
    private sealed class TokenHonouringRender(Action stop)
    {
        /// <summary>The pages a render runs to when nothing stops it.</summary>
        public const int Pages = 1_000;

        /// <summary>The page count at which the stand-in presses Stop.</summary>
        public const int StopAt = 3;

        /// <summary>The pages built before the render ended.</summary>
        public int Built { get; private set; }

        public byte[] Render(IEnumerable<DiagramRequest> requests, DiagramOptions options, CancellationToken token)
        {
            Assert.NotEmpty(requests);
            for (Built = 0; Built < Pages; Built++)
            {
                token.ThrowIfCancellationRequested();
                if (Built == StopAt)
                    stop();
            }
            return [1, 2, 3, 4];
        }
    }

    /// <summary>
    /// A Stop pressed during the render ends it at its next page: the run ends
    /// cancelled while the render is still running — cut short, not run out —
    /// with nothing written, after the input.
    /// </summary>
    [Fact]
    public async Task ADeckRunStoppedDuringTheRender_EndsTheRender_WritesNothing_AfterTheInput()
    {
        var render = new TokenHonouringRender(_cts.Cancel);

        var stoppedBy = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeckAsync(
            Output("deck.pdf"), new DecisionFilterSet(), new RecordingProgress(), render.Render));

        Assert.Equal(TokenHonouringRender.StopAt + 1, render.Built);
        Assert.Equal(_cts.Token, stoppedBy.CancellationToken);
        Assert.Empty(Directory.GetFiles(_outputFolder));
        var stopped = _record.CancelledSnapshot();
        Assert.Null(stopped.StoppedOutput);
        Assert.Equal(StopStage.AfterInput, stopped.StoppedAt);
    }

    /// <summary>
    /// The token <see cref="LocalFolderProcessor.ProcessPdfAsync"/> and
    /// <see cref="LocalFolderProcessor.ProcessPptxAsync"/> hand the library is
    /// the run's: Stop pressed as the render begins, the run ends with the
    /// library's own cancellation for that token — thrown inside
    /// BackgammonDiagram_Lib, not by the check before the create — and
    /// nothing is written.
    /// </summary>
    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Pptx)]
    public async Task TheDeckPathways_HandTheLibraryTheRunsToken(OutputFormat format)
    {
        LeaveOneInputFile();
        var progress = new RecordingProgress(report =>
        {
            if (report.Phase == JobPhase.Rendering)
                _cts.Cancel();
        });
        var output = Output(format == OutputFormat.Pdf ? "deck.pdf" : "deck.pptx");

        var stoppedBy = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => format == OutputFormat.Pdf
            ? Processor().ProcessPdfAsync(_folder, output, new DecisionFilterSet(), progress, _record, _cts.Token)
            : Processor().ProcessPptxAsync(_folder, output, new DecisionFilterSet(), progress, _record, _cts.Token));

        Assert.Equal(_cts.Token, stoppedBy.CancellationToken);
        Assert.Contains("BackgammonDiagram_Lib", stoppedBy.StackTrace);
        Assert.Empty(Directory.GetFiles(_outputFolder));
        Assert.Equal(StopStage.AfterInput, _record.CancelledSnapshot().StoppedAt);
    }
}
