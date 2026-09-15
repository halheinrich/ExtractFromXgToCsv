using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging;
using QuestPDF.Infrastructure;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Real-pipeline smoke for the local-mode deck path against a file that carries
/// XG's illegal-play marker. The producer owns the iterator's synthetic
/// log-contract unit test (ConvertXgToJson_Lib's
/// <c>XgDecisionIteratorIllegalPlayTests</c>); this is the consumer half — it
/// drives the actual user-facing <see cref="LocalFolderProcessor.ProcessPdfAsync"/>
/// over a real tournament file and asserts that:
/// <list type="bullet">
///   <item>the file is not dropped wholesale (decisions still emit, nothing
///   on the run's skip record, no <c>"Skipping"</c> warning from the per-file
///   catch), and</item>
///   <item>the illegal play surfaces as a contextual <c>Warning</c> — which
///   only happens because <c>ProcessDeckAsync</c> passes its <c>_logger</c>
///   into <c>IterateDiagramRequests</c>. Drop that argument and this fails.</item>
/// </list>
/// </summary>
public class LocalFolderProcessorIllegalPlayTests
{
    static LocalFolderProcessorIllegalPlayTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // The real tournament file whose game 6, move 25 (roll 3-3) is recorded as
    // an illegal play. Lives in the gitignored TestData/FixtureFiles set — the
    // "real" half of the keep-both convention, paired with the producer's
    // portable synthetic fixture.
    private const string IllegalPlayFixture =
        "Avi Cohen (6.86) - Max Stockslager (10.55) 2023-02-09_18122.xg";

    [Fact]
    public async Task ProcessPdfAsync_IllegalPlayFile_RendersDeckAndLogsTheIllegalPlay()
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
            var logger = new CapturingLogger<LocalFolderProcessor>();
            var processor = new LocalFolderProcessor(logger);

            ProcessingProgress? lastProgress = null;
            var progress = new Progress<ProcessingProgress>(p => lastProgress = p);

            await processor.ProcessPdfAsync(folder, outputPath, new DecisionFilterSet(), progress);

            // Deck rendered: a non-empty PDF at the requested path.
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);

            // Not dropped wholesale: decisions emitted and the run completed.
            Assert.NotNull(lastProgress);
            Assert.True(lastProgress!.Complete);
            Assert.True(lastProgress.TotalRows > 0);
            Assert.Empty(lastProgress.Skipped);

            // The per-file catch never fired — the file survived end to end.
            Assert.DoesNotContain(logger.Entries,
                e => e.Message.Contains("Skipping", StringComparison.Ordinal));

            // The illegal play surfaced as a contextual one-liner. This is the
            // assertion that fails if logger: _logger is dropped from the
            // ProcessDeckAsync call.
            Assert.Contains(logger.Entries, e =>
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
