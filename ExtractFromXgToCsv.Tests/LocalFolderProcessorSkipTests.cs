using System.Text.Json;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the skip contract on all four processor pathways (CSV, Diagram JSON,
/// Xgp, deck): a file the run cannot process is skipped, not fatal — and is
/// <b>recorded</b>, counted once, named, with the reason, on the snapshot the
/// client reads. Before halheinrich/backgammon#223 the per-file catch only
/// logged, so a dropped file was invisible to the user and to every test that
/// asserted a non-zero row count.
/// <para>
/// Also pins that cancellation is never a skip. Each pathway checks the token
/// at the top of its per-file loop, but a cancellation raised <i>inside</i> a
/// file's body used to be caught as a skip and swallowed on three of the four
/// pathways; every skip catch now excludes
/// <see cref="OperationCanceledException"/>.
/// </para>
/// </summary>
/// <remarks>
/// The malformed file is synthesized per test (<see cref="MalformedXg"/>). It
/// sits in a subfolder, so the name the record carries is a real path relative
/// to the input folder rather than one that merely equals the bare name, and
/// its path sorts ahead of the good fixture's, so the good file is processed
/// <i>after</i> the skip: the tests prove the run goes on, not merely that it
/// survives a bad last file.
/// </remarks>
public class LocalFolderProcessorSkipTests
{
    private const string GoodFixture = "MTCH4064.xg";

    // '0' sorts before 'M' under the producer's OrdinalIgnoreCase discovery
    // order, so this is the first file every pathway meets.
    private static readonly string MalformedRelativePath = Path.Combine("0-broken", "0-malformed.xg");

    /// <summary>
    /// Loose enough that the good fixture yields decisions, tight enough to
    /// keep the deck pathway's render to a handful of slides (the same filter
    /// <see cref="LocalFolderProcessorPhaseTests"/> uses, for the same reason).
    /// </summary>
    private static DecisionFilterSet NarrowFilter() => new FilterConfig { ErrorMin = 0.05 }.Build();

    private static LocalFolderProcessor Processor() =>
        new(NullLogger<LocalFolderProcessor>.Instance);

    // ── Skips are recorded ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_MalformedFileBesideAGoodOne_RecordsTheSkipAndKeepsTheGoodRows()
    {
        var folder = TempFolder(withMalformed: true);
        try
        {
            var filter = NarrowFilter();
            var expected = GoodFixtureRows(filter);
            var outputPath = Path.Combine(folder, "out.csv");
            var progress = new RecordingProgress();

            await Processor().ProcessAsync(folder, outputPath, filter, progress);

            var terminal = progress.Reports[^1];
            AssertOnlyTheMalformedFileSkipped(terminal);

            // Exactly the good file's rows, header aside.
            Assert.Equal(expected.Count, terminal.TotalRows);
            Assert.Equal(expected, File.ReadAllLines(outputPath).Skip(1));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ProcessDiagramAsync_MalformedFileBesideAGoodOne_RecordsTheSkipAndKeepsTheGoodRows()
    {
        var folder = TempFolder(withMalformed: true);
        try
        {
            var filter = NarrowFilter();
            var expectedCount = GoodFixtureDiagramCount(filter);
            var outputPath = Path.Combine(folder, "out.json");
            var progress = new RecordingProgress();

            await Processor().ProcessDiagramAsync(folder, outputPath, filter, progress);

            var terminal = progress.Reports[^1];
            AssertOnlyTheMalformedFileSkipped(terminal);

            Assert.Equal(expectedCount, terminal.TotalRows);
            using var written = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(expectedCount, written.RootElement.GetArrayLength());
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ProcessXgpAsync_MalformedFileBesideAGoodOne_RecordsTheSkipAndKeepsTheGoodRows()
    {
        var folder = TempFolder(withMalformed: true);
        var outputDir = Path.Combine(folder, "positions");
        try
        {
            var filter = NarrowFilter();
            var expected = GoodFixtureRows(filter);
            var progress = new RecordingProgress();

            await Processor().ProcessXgpAsync(
                folder, outputDir, filter, new XgpExportOptions(), new FilterConfig(), progress);

            var terminal = progress.Reports[^1];
            AssertOnlyTheMalformedFileSkipped(terminal);

            Assert.Equal(expected.Count, terminal.TotalRows);
            Assert.Equal(expected.Count, Directory.GetFiles(outputDir, "*.xgp").Length);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ProcessPptxAsync_MalformedFileBesideAGoodOne_RecordsTheSkipAndKeepsTheGoodRows()
    {
        var folder = TempFolder(withMalformed: true);
        try
        {
            var filter = NarrowFilter();
            var expectedCount = GoodFixtureDiagramCount(filter);
            var outputPath = Path.Combine(folder, "deck.pptx");
            var progress = new RecordingProgress();

            await Processor().ProcessPptxAsync(folder, outputPath, filter, progress);

            var terminal = progress.Reports[^1];
            AssertOnlyTheMalformedFileSkipped(terminal);

            Assert.Equal(expectedCount, terminal.TotalRows);
            Assert.True(new FileInfo(outputPath).Length > 0);

            // The render snapshot — the last one before completion, and the one
            // the client shows for minutes at corpus scale — already carries it.
            var rendering = Assert.Single(progress.Reports, r => r.Phase == JobPhase.Rendering);
            Assert.Equal(terminal.Skipped, rendering.Skipped);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task SameNamedFilesInTwoSubfolders_AreRecordedByTheirPathsUnderTheInputFolder()
    {
        // Discovery is recursive, so a bare name cannot tell these apart; the
        // record names each by its path under the input folder. The log keeps
        // the bare name, like the rows and decision ids the run emits.
        var folder = TempFolder(withMalformed: false);
        try
        {
            var first = Path.Combine("a", "dup.xg");
            var second = Path.Combine("b", "dup.xg");
            MalformedXg.Write(Path.Combine(folder, first));
            MalformedXg.Write(Path.Combine(folder, second));
            var logger = new CapturingLogger<LocalFolderProcessor>();
            var progress = new RecordingProgress();

            await new LocalFolderProcessor(logger).ProcessAsync(
                folder, Path.Combine(folder, "out.csv"), NarrowFilter(), progress);

            var terminal = progress.Reports[^1];
            Assert.Equal(new[] { first, second }, terminal.Skipped.Select(s => s.FileName));
            Assert.Equal(2, logger.Entries.Count(e => e.Message == "Skipping dup.xg"));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task CallersSkipRecord_HoldsTheRunsRecord_AndBelongsToThatRunOnly()
    {
        var folder = TempFolder(withMalformed: true);
        try
        {
            var record = new SkipRecord();
            var progress = new RecordingProgress();

            await Processor().ProcessAsync(
                folder, Path.Combine(folder, "out.csv"), NarrowFilter(), progress, record);

            // The caller's record is the run's record: what a caller reads after
            // a run-ending failure is what every snapshot carried.
            Assert.Equal(progress.Reports[^1].Skipped, record.Snapshot());

            // A second run would report the first run's skips as its own.
            await Assert.ThrowsAsync<ArgumentException>(() => Processor().ProcessAsync(
                folder, Path.Combine(folder, "again.csv"), NarrowFilter(),
                new RecordingProgress(), record));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    // ── Cancellation is not a skip ──────────────────────────────────────────
    //
    // Each run holds one good file and cancels from the first progress report.
    // On the three reportEvery pathways that report lands after the loop-top
    // check has passed, so the token is observed inside the file's body — the
    // window the old bare catches swallowed. One file, because a second file's
    // loop-top check would throw anyway and hide the swallow. Even so, the
    // snapshot alone cannot catch it everywhere: after a swallowed skip the
    // Diagram JSON pathway's final write takes the token and throws the
    // cancellation itself, before any snapshot shows the skip. So each test
    // also asserts that nothing logged the cancellation as a skip. (Measured
    // against the old catches: CSV ran to a "complete" snapshot without
    // throwing, the deck pathway threw "nothing to render", and Diagram JSON
    // was caught by the log alone.) The Xgp pathway reports before its check
    // and was already correct; its test pins that it stays so.

    [Fact]
    public async Task ProcessAsync_CancelledInsideAFile_PropagatesAndRecordsNoSkip()
    {
        var folder = TempFolder(withMalformed: false);
        try
        {
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress(_ => cts.Cancel());
            var logger = new CapturingLogger<LocalFolderProcessor>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalFolderProcessor(logger).ProcessAsync(
                folder, Path.Combine(folder, "out.csv"), NarrowFilter(), progress,
                cancellationToken: cts.Token));

            AssertCancelledWithNoSkip(progress, logger);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ProcessDiagramAsync_CancelledInsideAFile_PropagatesAndRecordsNoSkip()
    {
        var folder = TempFolder(withMalformed: false);
        try
        {
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress(_ => cts.Cancel());
            var logger = new CapturingLogger<LocalFolderProcessor>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalFolderProcessor(logger).ProcessDiagramAsync(
                folder, Path.Combine(folder, "out.json"), NarrowFilter(), progress,
                cancellationToken: cts.Token));

            AssertCancelledWithNoSkip(progress, logger);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ProcessXgpAsync_CancelledAtTheFirstFile_PropagatesAndRecordsNoSkip()
    {
        var folder = TempFolder(withMalformed: false);
        try
        {
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress(_ => cts.Cancel());
            var logger = new CapturingLogger<LocalFolderProcessor>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalFolderProcessor(logger).ProcessXgpAsync(
                folder, Path.Combine(folder, "positions"), NarrowFilter(),
                new XgpExportOptions(), new FilterConfig(), progress,
                cancellationToken: cts.Token));

            AssertCancelledWithNoSkip(progress, logger);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ProcessPptxAsync_CancelledInsideAFile_PropagatesAndRecordsNoSkip()
    {
        var folder = TempFolder(withMalformed: false);
        try
        {
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress(_ => cts.Cancel());
            var logger = new CapturingLogger<LocalFolderProcessor>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalFolderProcessor(logger).ProcessPptxAsync(
                folder, Path.Combine(folder, "deck.pptx"), NarrowFilter(), progress,
                cancellationToken: cts.Token));

            AssertCancelledWithNoSkip(progress, logger);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AssertOnlyTheMalformedFileSkipped(ProcessingProgress terminal)
    {
        Assert.True(terminal.Complete);

        var skip = Assert.Single(terminal.Skipped);
        Assert.Equal(MalformedRelativePath, skip.FileName);
        Assert.Null(skip.DecisionId);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));

        Assert.Equal(1, terminal.SkippedFileCount);
        Assert.Equal(0, terminal.SkippedDecisionCount);
    }

    private static void AssertCancelledWithNoSkip(
        RecordingProgress progress, CapturingLogger<LocalFolderProcessor> logger)
    {
        var last = Assert.Single(progress.Reports);
        Assert.False(last.Complete);
        Assert.Empty(last.Skipped);

        // The seam logs every skip it records, so this is the record's other
        // face — and the one that still sees a swallow the snapshot cannot
        // (see the note above the cancellation tests).
        Assert.DoesNotContain(logger.Entries, e => e.Exception is OperationCanceledException);
    }

    /// <summary>
    /// The CSV lines the good fixture yields through <paramref name="filter"/>,
    /// read directly off the producer (no book, matching a processor built
    /// without one). Also the Xgp pathway's decision count.
    /// </summary>
    private static List<string> GoodFixtureRows(DecisionFilterSet filter)
    {
        var xgFile = XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, GoodFixture));
        var rows = XgDecisionIterator.Iterate(xgFile, GoodFixture)
            .Where(r => filter.Matches(r))
            .Select(r => r.ToCsvLine())
            .ToList();
        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>The good fixture's decision count through the diagram iterator.</summary>
    private static int GoodFixtureDiagramCount(DecisionFilterSet filter)
    {
        var xgFile = XgFileReader.ReadFile(Path.Combine(FixtureHelper.FixtureDir, GoodFixture));
        var count = XgDecisionIterator.IterateDiagramRequests(xgFile, GoodFixture)
            .Count(r => filter.Matches(r));
        Assert.True(count > 0, "the good fixture should yield decisions through the filter");
        return count;
    }

    private static string TempFolder(bool withMalformed)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xg-skip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.Copy(
            Path.Combine(FixtureHelper.FixtureDir, GoodFixture),
            Path.Combine(dir, GoodFixture));
        if (withMalformed)
            MalformedXg.Write(Path.Combine(dir, MalformedRelativePath));
        return dir;
    }

    private static void DeleteFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A synchronous <see cref="IProgress{T}"/>: records every snapshot and runs
    /// <c>onReport</c> inside <see cref="Report"/>. <see cref="Progress{T}"/>
    /// posts its callback instead, which would let the processor race past the
    /// point a test cancels at — and leave "the last snapshot" unsettled when
    /// the call returns.
    /// </summary>
    private sealed class RecordingProgress(Action<ProcessingProgress>? onReport = null)
        : IProgress<ProcessingProgress>
    {
        public List<ProcessingProgress> Reports { get; } = [];

        public void Report(ProcessingProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }
}
