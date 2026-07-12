using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wiring tests for the Local-mode Xgp pathway. Slice correctness is owned
/// by ConvertXgToJson_Lib's XgpExporter tests and the app-side batching
/// rules by <see cref="XgpExportServiceTests"/>; these tests verify that
/// <see cref="LocalFolderProcessor.ProcessXgpAsync"/> writes one correctly
/// named .xgp per filtered decision into the output <b>folder</b> and that
/// an exported file re-reads to its source decision.
///
/// Expected counts are computed from the iterator over the same fixture
/// folder, so adding/removing fixtures shifts both sides together
/// (Make20Pt pattern).
/// </summary>
public class LocalFolderProcessorXgpTests
{
    [Fact]
    public async Task ProcessXgpAsync_WritesOneNamedXgpPerDecision_ThatRoundTrips()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputDir = Path.Combine(Path.GetTempPath(), $"xgp-test-{Guid.NewGuid():N}");
        var options = new XgpExportOptions { Prefix = "loc", StartNumber = 5, SuffixLength = 4 };

        // Reference side: the same iterator walk the processor performs.
        var expected = XgDecisionIterator
            .IterateXgDirectory(FixtureHelper.FixtureDir)
            .ToList();
        Assert.True(expected.Count > 0, "fixture folder should yield decisions");

        try
        {
            ProcessingProgress? lastProgress = null;
            var progress = new Progress<ProcessingProgress>(p => lastProgress = p);

            await processor.ProcessXgpAsync(
                FixtureHelper.FixtureDir,
                outputDir,
                new DecisionFilterSet(),
                options,
                progress);

            Assert.NotNull(lastProgress);
            Assert.True(lastProgress!.Complete);
            Assert.Equal(expected.Count, lastProgress.TotalRows);

            var written = Directory.GetFiles(outputDir, "*.xgp")
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(expected.Count, written.Count);
            Assert.Equal(
                Enumerable.Range(0, expected.Count).Select(options.EntryName)
                    .OrderBy(n => n, StringComparer.Ordinal),
                written);

            // Semantic oracle on one sliced file: it re-reads to exactly one
            // decision whose XGID digests the same position/cube/dice/match
            // state as a decision in the reference walk.
            var first = XgFileReader.ReadFile(Path.Combine(outputDir, options.EntryName(0)));
            var reRead = XgDecisionIterator
                .IterateDiagramRequests(first, options.EntryName(0))
                .Single();
            Assert.Contains(expected, r => r.Xgid == reRead.Xgid);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessXgpAsync_NumbersDecisionsInProducerDiscoveryOrder()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputDir = Path.Combine(Path.GetTempPath(), $"xgp-order-{Guid.NewGuid():N}");
        var options = new XgpExportOptions { Prefix = "ord", StartNumber = 1, SuffixLength = 4 };

        // Reference sequence: the producer's *contractual* discovery order
        // (ascending full path, OrdinalIgnoreCase + Ordinal tiebreak) walked
        // per file. Deriving expected from the same enumeration the processor
        // now uses keeps this pin fixture-shift-proof rather than hardcoded —
        // add/remove a fixture and both sides move together.
        var expected = XgFileReader
            .EnumerateXgFormatFiles(FixtureHelper.FixtureDir, SearchOption.AllDirectories)
            .SelectMany(path => XgDecisionIterator.Iterate(
                XgFileReader.ReadFile(path), Path.GetFileName(path)))
            .Select(r => r.Xgid)
            .ToList();
        Assert.True(expected.Count > 0, "fixture folder should yield decisions");

        try
        {
            await processor.ProcessXgpAsync(
                FixtureHelper.FixtureDir,
                outputDir,
                new DecisionFilterSet(),
                options,
                new Progress<ProcessingProgress>());

            // The i-th written file (EntryName(i)) must re-read to the i-th
            // decision of the reference walk — pins numbering ORDER, not just
            // the set of names or the count.
            var actual = Enumerable.Range(0, expected.Count)
                .Select(i => Path.Combine(outputDir, options.EntryName(i)))
                .Select(p => XgDecisionIterator.IterateDiagramRequests(
                    XgFileReader.ReadFile(p), Path.GetFileName(p)).Single().Xgid)
                .ToList();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessXgpAsync_InvalidOptions_Throw()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessXgpAsync(
            FixtureHelper.FixtureDir,
            Path.Combine(Path.GetTempPath(), $"xgp-test-{Guid.NewGuid():N}"),
            new DecisionFilterSet(),
            new XgpExportOptions { Prefix = "a?b" },
            new Progress<ProcessingProgress>()));
    }
}
