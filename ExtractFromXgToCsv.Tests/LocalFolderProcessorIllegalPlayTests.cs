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
/// silence unit test (ConvertXgToJson_Lib's
/// <c>XgDecisionIteratorIllegalPlayTests</c>); this is the consumer half — it
/// drives <see cref="LocalFolderProcessor.ProcessDeckAsync"/>, the walk both
/// deck formats share, over that file with a <see cref="RecordingDeckRenderer"/>
/// in place of the library's render. An illegal play occurs and is legit
/// (Hal's ruling, 2026-11-02, halheinrich/backgammon#514), so its decision is
/// skipped and nothing says so: the file is read whole, its other decisions
/// emit, nothing lands on the run's skip record, and neither logger warns. The
/// bad-data half of a walk's reporting is
/// <see cref="LocalFolderProcessorOmissionTests"/>'.
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
    public async Task ProcessDeckAsync_IllegalPlayFile_WalksTheFileWhole_AndSaysNothing()
    {
        var fixture = Path.Combine(FixtureHelper.FixtureDir, IllegalPlayFixture);
        Assert.True(File.Exists(fixture),
            $"Required fixture missing: {fixture}. It lives in the gitignored " +
            "TestData/FixtureFiles set — restore it before running this smoke.");

        // A folder containing only the illegal-play file, so any "Skipping"
        // warning or any skip entry is unambiguously about it.
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

            // Neither seam fired — the file survived end to end.
            Assert.DoesNotContain(logger.Entries,
                e => e.Message.Contains("Skipping", StringComparison.Ordinal));

            // And the walk said nothing of the illegal play, at any level,
            // through the logger the filtering operation hands the producer.
            Assert.DoesNotContain(iteratorLogger.Entries,
                e => e.Message.Contains("llegal", StringComparison.Ordinal));
            Assert.DoesNotContain(iteratorLogger.Entries, e => e.Level >= LogLevel.Warning);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
