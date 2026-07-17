using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ConvertXgToJson_Lib.Models;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wiring tests for the Local-mode Xgp pathway. Slice correctness is owned
/// by ConvertXgToJson_Lib's XgpExporter tests, the app-side batching rules
/// by <see cref="XgpExportServiceTests"/>, and naming mechanics by
/// <see cref="XgpNameTemplateTests"/> / <see cref="XgpNameAllocatorTests"/>;
/// these tests verify that
/// <see cref="LocalFolderProcessor.ProcessXgpAsync"/> writes one correctly
/// named .xgp per filtered decision into the output <b>folder</b> and that
/// an exported file re-reads to its source decision.
///
/// Expected counts and names are computed from <see cref="WalkFixtureDecisions"/>
/// (and a locally built <see cref="XgpNameAllocator"/>) over the same fixture
/// folder, so adding/removing fixtures shifts both sides together (Make20Pt
/// pattern).
/// </summary>
public class LocalFolderProcessorXgpTests
{
    /// <summary>
    /// Reference walk over the fixture folder: the decisions
    /// <see cref="LocalFolderProcessor.ProcessXgpAsync"/> is expected to see,
    /// in the order it sees them. Mirrors the processor's own walk —
    /// <see cref="XgFileReader.EnumerateXgFormatFiles(string, SearchOption)"/>
    /// recursively (the producer's contractual discovery order: ascending full
    /// path, <c>OrdinalIgnoreCase</c> with an <c>Ordinal</c> tiebreak), each
    /// file read and iterated by its own name, unreadable files skipped — so
    /// the oracle tracks the code under test rather than a producer helper.
    /// Deriving expectations from the same enumeration the processor uses keeps
    /// these tests fixture-shift-proof: add or remove a fixture and both sides
    /// move together.
    /// </summary>
    private static List<DecisionRow> WalkFixtureDecisions()
    {
        var rows = new List<DecisionRow>();

        foreach (var path in XgFileReader.EnumerateXgFormatFiles(
                     FixtureHelper.FixtureDir, SearchOption.AllDirectories))
        {
            XgFile file;
            try { file = XgFileReader.ReadFile(path); }
            catch { continue; }

            rows.AddRange(XgDecisionIterator.Iterate(file, Path.GetFileName(path)));
        }

        return rows;
    }

    [Fact]
    public async Task ProcessXgpAsync_WritesOneNamedXgpPerDecision_ThatRoundTrips()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputDir = Path.Combine(Path.GetTempPath(), $"xgp-test-{Guid.NewGuid():N}");
        var options = new XgpExportOptions
        {
            NamePattern = "loc{n}",
            StartNumber = 5,
            SuffixLength = 4,
        };
        var filters = new FilterConfig();

        // Reference side: the same walk the processor performs, named by a
        // locally built allocator over the same options.
        var expected = WalkFixtureDecisions();
        Assert.True(expected.Count > 0, "fixture folder should yield decisions");

        var namer = XgpNameAllocator.Create(options, filters);
        var expectedNames = expected.Select(namer.Next).ToList();

        try
        {
            ProcessingProgress? lastProgress = null;
            var progress = new Progress<ProcessingProgress>(p => lastProgress = p);

            await processor.ProcessXgpAsync(
                FixtureHelper.FixtureDir,
                outputDir,
                new DecisionFilterSet(),
                options,
                filters,
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
                expectedNames.OrderBy(n => n, StringComparer.Ordinal),
                written);

            // Semantic oracle on one sliced file: it re-reads to exactly one
            // decision whose XGID digests the same position/cube/dice/match
            // state as a decision in the reference walk.
            var firstName = expectedNames[0];
            var first = XgFileReader.ReadFile(Path.Combine(outputDir, firstName));
            var reRead = XgDecisionIterator
                .IterateDiagramRequests(first, firstName)
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
        var options = new XgpExportOptions
        {
            NamePattern = "ord{n}",
            StartNumber = 1,
            SuffixLength = 4,
        };
        var filters = new FilterConfig();

        // Reference sequence: the processor's discovery order, walked per file.
        var expectedRows = WalkFixtureDecisions();
        Assert.True(expectedRows.Count > 0, "fixture folder should yield decisions");

        try
        {
            await processor.ProcessXgpAsync(
                FixtureHelper.FixtureDir,
                outputDir,
                new DecisionFilterSet(),
                options,
                filters,
                new Progress<ProcessingProgress>());

            // The i-th written file (the allocator's i-th name) must re-read
            // to the i-th decision of the reference walk — pins numbering
            // ORDER, not just the set of names or the count.
            var namer = XgpNameAllocator.Create(options, filters);
            var actual = expectedRows
                .Select(r => Path.Combine(outputDir, namer.Next(r)))
                .Select(p => XgDecisionIterator.IterateDiagramRequests(
                    XgFileReader.ReadFile(p), Path.GetFileName(p)).Single().Xgid)
                .ToList();

            Assert.Equal(expectedRows.Select(r => r.Xgid), actual);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessXgpAsync_CounterlessPattern_UniquifiesOnDisk()
    {
        // A pattern without {n} renders the same base name for every
        // decision — the per-run uniquifier is the only thing standing
        // between the batch and a folder of overwrites.
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputDir = Path.Combine(Path.GetTempPath(), $"xgp-uniq-{Guid.NewGuid():N}");
        var options = new XgpExportOptions { NamePattern = "same" };

        var expectedCount = WalkFixtureDecisions().Count;
        Assert.True(expectedCount > 1, "fixture folder should yield several decisions");

        try
        {
            await processor.ProcessXgpAsync(
                FixtureHelper.FixtureDir,
                outputDir,
                new DecisionFilterSet(),
                options,
                new FilterConfig(),
                new Progress<ProcessingProgress>());

            var written = Directory.GetFiles(outputDir, "*.xgp")
                .Select(p => Path.GetFileName(p)!)
                .ToHashSet(StringComparer.Ordinal);

            var expectedNames = Enumerable.Range(1, expectedCount)
                .Select(k => k == 1 ? "same.xgp" : $"same ({k}).xgp")
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(expectedNames, written);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessXgpAsync_Anonymize_RewritesEveryWrittenPositionToTheRoleNames()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputDir = Path.Combine(Path.GetTempPath(), $"xgp-anon-{Guid.NewGuid():N}");
        var options = new XgpExportOptions
        {
            NamePattern = "anon{n}",
            StartNumber = 1,
            SuffixLength = 4,
        };

        var expectedCount = WalkFixtureDecisions().Count;
        Assert.True(expectedCount > 0, "fixture folder should yield decisions");

        try
        {
            await processor.ProcessXgpAsync(
                FixtureHelper.FixtureDir,
                outputDir,
                new DecisionFilterSet(),
                options,
                new FilterConfig(),
                new Progress<ProcessingProgress>(),
                anonymize: true);

            var written = Directory.GetFiles(outputDir, "*.xgp");
            Assert.Equal(expectedCount, written.Length);

            // Every entry — .xg-sliced and .xgp-copied alike (the fixture folder
            // holds both) — must re-read named by decision role.
            foreach (var path in written)
                XgpAnonymizeAssert.IsRoleAnonymized(
                    File.ReadAllBytes(path), Path.GetFileName(path));
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
            new XgpExportOptions { NamePattern = "a?b{n}" },
            new FilterConfig(),
            new Progress<ProcessingProgress>()));
    }
}
