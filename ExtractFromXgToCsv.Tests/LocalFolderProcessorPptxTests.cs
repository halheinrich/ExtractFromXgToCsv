using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Text.RegularExpressions;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wiring test for the local-mode PPTX path. Deck-conformance is owned by
/// BackgammonDiagram_Lib's PptxConformanceTests; this test only verifies that
/// ExtractFromXgToCsv hands filtered decisions to the renderer and writes a
/// well-formed .pptx at the requested path.
/// </summary>
public class LocalFolderProcessorPptxTests
{
    [Fact]
    public async Task ProcessPptxAsync_WritesValidPptxFromFixtureFolder()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputPath = Path.Combine(Path.GetTempPath(),
            $"pptx-test-{Guid.NewGuid():N}.pptx");

        try
        {
            ProcessingProgress? lastProgress = null;
            var progress = new Progress<ProcessingProgress>(p => lastProgress = p);

            await processor.ProcessPptxAsync(
                FixtureHelper.FixtureDir,
                outputPath,
                new DecisionFilterSet(),
                progress);

            Assert.True(File.Exists(outputPath));
            var bytes = await File.ReadAllBytesAsync(outputPath);
            Assert.True(bytes.Length > 0);

            // PK\x03\x04 — zip local-file-header magic. PPTX is an OOXML zip.
            Assert.Equal(0x50, bytes[0]);
            Assert.Equal(0x4B, bytes[1]);
            Assert.Equal(0x03, bytes[2]);
            Assert.Equal(0x04, bytes[3]);

            using var archive = ZipFile.OpenRead(outputPath);
            Assert.Contains(archive.Entries, e => e.FullName == "[Content_Types].xml");

            Assert.NotNull(lastProgress);
            Assert.True(lastProgress!.Complete);
            Assert.True(lastProgress.TotalRows > 0);

            // Each decision becomes a Problem + Solution pair, so the deck
            // should have exactly twice as many slides as decisions.
            var slideCount = archive.Entries.Count(e =>
                Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"));
            Assert.Equal(2 * lastProgress.TotalRows, slideCount);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
