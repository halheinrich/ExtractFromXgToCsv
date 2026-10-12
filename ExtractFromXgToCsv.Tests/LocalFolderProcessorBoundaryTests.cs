using System.Text.Json;
using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins Local mode's boundaries (the umbrella's <c>SPEC-notices.md</c> §7.1,
/// halheinrich/backgammon#374): a file's contribution is staged whole and
/// committed only when its conversion succeeds; a destination failure fails
/// the run and is never a skip; a cancellation records no skip and keeps what
/// the run reached; and on the XGP pathway a decision whose payload
/// preparation fails is skipped alone, while a failed write — the commit —
/// fails the run.
/// </summary>
/// <remarks>
/// <para>
/// <b>A file that fails midway</b> is made with a filter that throws on a
/// chosen decision: the filtering operation lets a filter's failure propagate
/// to its enumerating caller (XgFilter_Lib's
/// <see cref="FilteredDecisionIterator.IterateFile"/> remarks), and no real
/// fixture fails partway through its conversion. The filter passes the
/// decisions before the throw, so a pathway that appended them as they were
/// produced — the code before this leg — leaves them in the output.
/// </para>
/// <para>
/// <b>A payload whose preparation fails</b> is made through the processor's
/// internal preparation seam: no real input is known to fail it.
/// </para>
/// </remarks>
public class LocalFolderProcessorBoundaryTests : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    // Discovery is ascending full path, so the good copy is read first and
    // the failing one after it.
    private const string GoodName = "a-good.xg";
    private const string FailingName = "b-fails.xg";
    private const string ThirdName = "c-third.xg";

    private readonly string _folder = Directory.CreateTempSubdirectory("xg-boundary-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static LocalFolderProcessor Processor() =>
        new(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance, AppRanking.Ranking);

    private void Copy(string name) =>
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(_folder, name));

    /// <summary>The fixture's rows, read straight off the producer as the run reads them.</summary>
    private static List<DecisionRow> FixtureRows(string asName)
    {
        var xgFile = XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, Fixture));
        var rows = XgDecisionIterator.Iterate(xgFile, asName, options: new(Ranking: AppRanking.Ranking)).ToList();
        Assert.True(rows.Count > 3, "the fixture must hold enough decisions to fail midway");
        return rows;
    }

    // ── A file that fails midway contributes nothing ────────────────────────

    [Fact]
    public async Task Csv_AFileThatFailsMidway_ContributesNoRows_AndIsAConversionSkip()
    {
        Copy(GoodName);
        Copy(FailingName);
        var good = FixtureRows(GoodName);
        var output = Path.Combine(_folder, "out.csv");
        var record = new RunRecord();

        await Processor().ProcessAsync(
            _folder, output, MidwayFailure.InTheSecondFile(good.Count), new RecordingProgress(), record);

        Assert.Equal(good.Select(r => r.ToCsvLine()), File.ReadAllLines(output).Skip(1));
        AssertTheFailingFileIsTheOneSkip(record);
    }

    [Fact]
    public async Task DiagramJson_AFileThatFailsMidway_ContributesNoRecords_AndIsAConversionSkip()
    {
        Copy(GoodName);
        Copy(FailingName);
        var good = FixtureRows(GoodName);
        var output = Path.Combine(_folder, "out.json");
        var record = new RunRecord();

        await Processor().ProcessDiagramAsync(
            _folder, output, MidwayFailure.InTheSecondFile(good.Count), new RecordingProgress(), record);

        var written = JsonSerializer.Deserialize<List<BgDecisionData>>(await File.ReadAllTextAsync(output))!;
        Assert.Equal(good.Select(r => r.Id), written.Select(r => r.Id));
        AssertTheFailingFileIsTheOneSkip(record);
    }

    [Fact]
    public async Task Xgp_AFileThatFailsMidway_WritesNoPositions_AndIsAConversionSkip()
    {
        Copy(GoodName);
        Copy(FailingName);
        var good = FixtureRows(GoodName);
        var outputDir = Path.Combine(_folder, "positions");
        var record = new RunRecord();
        var progress = new RecordingProgress();

        await Processor().ProcessXgpAsync(
            _folder, outputDir, MidwayFailure.InTheSecondFile(good.Count),
            new XgpExportOptions(), new FilterConfig(), progress, runRecord: record);

        Assert.Equal(good.Count, Directory.GetFiles(outputDir, "*.xgp").Length);
        Assert.Equal(good.Count, progress.Reports[^1].TotalRows);
        AssertTheFailingFileIsTheOneSkip(record);
    }

    private static void AssertTheFailingFileIsTheOneSkip(RunRecord record)
    {
        var skip = Assert.Single(record.Skips);
        Assert.Equal(FailingName, skip.FileName);
        Assert.Null(skip.DecisionId);
        Assert.Equal(MidwayFailure.Message, skip.Reason);
    }

    // ── A destination failure fails the run and is never a skip ─────────────

    [Fact]
    public async Task Csv_AnUnwritableOutputPath_FailsTheRun_WithNoSkip()
    {
        Copy(GoodName);
        var unwritable = Directory.CreateDirectory(Path.Combine(_folder, "out.csv")).FullName;
        var record = new RunRecord();

        await Assert.ThrowsAnyAsync<IOException>(() => ThrowsIoOrAccess(() => Processor().ProcessAsync(
            _folder, unwritable, new DecisionFilterSet(), new RecordingProgress(), record)));

        Assert.Empty(record.Skips);
    }

    [Fact]
    public async Task DiagramJson_AnUnwritableOutputPath_FailsTheRun_WithNoSkip()
    {
        Copy(GoodName);
        var unwritable = Directory.CreateDirectory(Path.Combine(_folder, "out.json")).FullName;
        var record = new RunRecord();

        await Assert.ThrowsAnyAsync<IOException>(() => ThrowsIoOrAccess(() => Processor().ProcessDiagramAsync(
            _folder, unwritable, new DecisionFilterSet(), new RecordingProgress(), record)));

        Assert.Empty(record.Skips);
    }

    /// <summary>
    /// The write of one position is the commit. Before this leg the write sat
    /// inside the per-decision catch, so a destination that refused one file
    /// was recorded as a skipped decision and the run went on.
    /// </summary>
    [Fact]
    public async Task Xgp_AFailedWrite_FailsTheRun_IsNoSkip_AndKeepsTheFilesWrittenBeforeIt()
    {
        Copy(GoodName);
        var outputDir = Path.Combine(_folder, "positions");
        // The second position's name is taken by a folder, so its write fails.
        Directory.CreateDirectory(Path.Combine(outputDir, "pos002.xgp"));
        var record = new RunRecord();

        await Assert.ThrowsAnyAsync<IOException>(() => ThrowsIoOrAccess(() => Processor().ProcessXgpAsync(
            _folder, outputDir, new DecisionFilterSet(), new XgpExportOptions(), new FilterConfig(),
            new RecordingProgress(), runRecord: record)));

        Assert.Empty(record.Skips);
        Assert.True(File.Exists(Path.Combine(outputDir, "pos001.xgp")));
        Assert.Single(Directory.GetFiles(outputDir, "*.xgp"));
    }

    // ── An XGP payload whose preparation fails is a decision skip ───────────

    [Fact]
    public async Task Xgp_APayloadWhosePreparationFails_IsADecisionSkip_AndConsumesNoNumber()
    {
        Copy(GoodName);
        var rows = FixtureRows(GoodName);
        var failing = rows[1].Id;
        var outputDir = Path.Combine(_folder, "positions");
        var record = new RunRecord();
        var progress = new RecordingProgress();
        var processor = new LocalFolderProcessor(
            NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance,
            AppRanking.Ranking, bookProvider: null,
            (id, source, bytes, anonymize) => id == failing
                ? throw new InvalidOperationException("Preparation failed.")
                : XgpPayload.Prepare(id, source, bytes, anonymize));

        await processor.ProcessXgpAsync(
            _folder, outputDir, new DecisionFilterSet(), new XgpExportOptions(), new FilterConfig(),
            progress, runRecord: record);

        var skip = Assert.Single(record.Skips);
        Assert.Equal(GoodName, skip.FileName);
        Assert.Equal(failing, skip.DecisionId);
        Assert.Equal("Preparation failed.", skip.Reason);

        // Every other decision is written, numbered without a gap: the skipped
        // one consumed no number.
        var expected = Enumerable.Range(1, rows.Count - 1).Select(n => $"pos{n:D3}.xgp");
        Assert.Equal(expected, Directory.GetFiles(outputDir, "*.xgp").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(rows.Count - 1, progress.Reports[^1].TotalRows);

        // And the decision after the skipped one is the second file: the
        // third decision, re-read, is what pos002 holds.
        var second = XgFileReader.ReadFile(Path.Combine(outputDir, "pos002.xgp"));
        Assert.Equal(rows[2].Xgid, XgDecisionIterator.Iterate(second, "pos002.xgp").Single().Xgid);
    }

    // ── A cancellation records no skip and keeps what the run reached ───────

    [Fact]
    public async Task Csv_ACancellation_RecordsNoSkip_AndKeepsTheFilesTheRunCommitted()
    {
        Copy(GoodName);
        Copy(FailingName);
        Copy(ThirdName);
        var good = FixtureRows(GoodName);
        var second = FixtureRows(FailingName);
        var output = Path.Combine(_folder, "out.csv");
        var record = new RunRecord();
        using var cts = new CancellationTokenSource();

        // Cancelled while the second file converts: it is committed — a file
        // is the cancellation's granularity — and the third is never read.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor().ProcessAsync(
            _folder, output, CancelAtCall.Set(good.Count + 1, cts), new RecordingProgress(), record,
            cts.Token));

        Assert.Empty(record.Skips);
        Assert.Equal(
            good.Concat(second).Select(r => r.ToCsvLine()),
            File.ReadAllLines(output).Skip(1));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Awaits <paramref name="run"/> and surfaces an access refusal as an
    /// <see cref="IOException"/>: Windows refuses a write to a path that is a
    /// folder with <see cref="UnauthorizedAccessException"/>, which is not one.
    /// </summary>
    private static async Task ThrowsIoOrAccess(Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    /// <summary>
    /// A filter that passes every decision until call <c>throwAt</c>, where it
    /// throws — the conversion of the file that decision belongs to fails
    /// midway, after the decisions before it passed.
    /// </summary>
    private sealed class MidwayFailure(int throwAt) : IDecisionFilter
    {
        public const string Message = "Conversion failed midway.";

        private int _calls;

        /// <summary>A set whose filter fails the second file at its third decision.</summary>
        public static DecisionFilterSet InTheSecondFile(int firstFileDecisions) =>
            new DecisionFilterSet().Add(new MidwayFailure(firstFileDecisions + 3));

        public bool Matches(IDecisionFilterData data) =>
            ++_calls == throwAt ? throw new InvalidOperationException(Message) : true;
    }

    /// <summary>A filter that passes everything and cancels the run at call <c>cancelAt</c>.</summary>
    private sealed class CancelAtCall(int cancelAt, CancellationTokenSource cts) : IDecisionFilter
    {
        private int _calls;

        public static DecisionFilterSet Set(int cancelAt, CancellationTokenSource cts) =>
            new DecisionFilterSet().Add(new CancelAtCall(cancelAt, cts));

        public bool Matches(IDecisionFilterData data)
        {
            if (++_calls == cancelAt)
                cts.Cancel();
            return true;
        }
    }

    /// <summary>A synchronous <see cref="IProgress{T}"/> that keeps every snapshot.</summary>
    private sealed class RecordingProgress : IProgress<ProcessingProgress>
    {
        public List<ProcessingProgress> Reports { get; } = [];
        public void Report(ProcessingProgress value) => Reports.Add(value);
    }
}
