using BgDataTypes_Lib.TestSupport;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The local-mode deck walk over a real tournament file that carries XG's
/// illegal-play marker. The producer owns the iterator's synthetic
/// log-contract unit test (ConvertXgToJson_Lib's
/// <c>XgDecisionIteratorIllegalPlayTests</c>); this is the consumer half — it
/// drives <see cref="LocalFolderProcessor.ProcessDeckAsync"/>, the walk both
/// deck formats share, over that file with a <see cref="RecordingDeckRenderer"/>
/// in place of the library's render, and asserts that:
/// <list type="bullet">
///   <item>the file is not dropped wholesale (decisions still emit, nothing
///   on the run's skip record, no <c>"Skipping"</c> warning from the per-file
///   catch), and</item>
///   <item>the illegal play surfaces as a contextual <c>Warning</c> on the
///   filtering operation's logger — which only happens because the processor
///   hands the run's <see cref="FilteredDecisionIterator"/> that logger, and
///   the operation forwards it to the producer. Hand it a null logger and
///   this fails.</item>
/// </list>
/// What it pins is the deck walk over that file. The PDF wrapper's wiring to
/// the real renderer is <see cref="LocalFolderProcessorPdfTests"/>' and
/// <c>LocalFolderProcessorStopTests.TheDeckPathways_HandTheLibraryTheRunsToken</c>'s;
/// rendering this file is the golden run's, whose frozen input includes it.
/// </summary>
public class LocalFolderProcessorIllegalPlayTests
{
    // The real tournament file whose game 6, move 25 (roll 3-3) is recorded as
    // an illegal play. Lives in the gitignored TestData/FixtureFiles set — the
    // "real" half of the keep-both convention, paired with the producer's
    // portable synthetic fixture.
    private const string IllegalPlayFixture =
        "Avi Cohen (6.86) - Max Stockslager (10.55) 2023-02-09_18122.xg";

    [Fact]
    public async Task ProcessDeckAsync_IllegalPlayFile_WalksTheFileAndLogsTheIllegalPlay()
    {
        var fixture = Path.Combine(FixtureHelper.FixtureDir, IllegalPlayFixture);
        Assert.True(File.Exists(fixture),
            $"Required fixture missing: {fixture}. It lives in the gitignored " +
            "TestData/FixtureFiles set — restore it before running this smoke.");

        // A folder containing only the illegal-play file, so any "Skipping"
        // warning or absent illegal-play warning is unambiguously about it.
        var folder = Path.Combine(Path.GetTempPath(), $"illegal-play-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.Copy(fixture, Path.Combine(folder, IllegalPlayFixture));
        var outputPath = Path.Combine(folder, "out.pdf");

        try
        {
            var logger = new RecordingLogger<LocalFolderProcessor>();
            var iteratorLogger = new RecordingLogger<FilteredDecisionIterator>();
            var processor = new LocalFolderProcessor(logger, iteratorLogger, AppRanking.Ranking);

            var progress = new RecordingProgress();

            await processor.ProcessDeckAsync(
                folder, outputPath, new DecisionFilterSet(), progress, new RecordingDeckRenderer().Render, "PDF",
                runRecord: null, CancellationToken.None);

            // The deck written: the stand-in's non-empty bytes at the requested path.
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);

            // Not dropped wholesale: decisions emitted and the run completed.
            var lastProgress = progress.Last;
            Assert.NotNull(lastProgress);
            Assert.True(lastProgress!.Complete);
            Assert.True(lastProgress.TotalRows > 0);
            Assert.Empty(lastProgress.Skipped);

            // The skip seam never fired — the file survived end to end.
            Assert.DoesNotContain(logger.Entries,
                e => e.Message.Contains("Skipping", StringComparison.Ordinal));

            // The illegal play surfaced as a contextual one-liner, through the
            // logger the filtering operation hands the producer.
            Assert.Contains(iteratorLogger.Entries, e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("Illegal play", StringComparison.Ordinal)
                && e.Message.Contains("game 6", StringComparison.Ordinal)
                && e.Message.Contains("move 25", StringComparison.Ordinal)
                && e.Message.Contains("roll 33", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
