using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wiring test for the local-mode PDF path. Deck-conformance is owned by
/// BackgammonDiagram_Lib's own PDF tests; this test only verifies that
/// ExtractFromXgToCsv hands filtered decisions to the renderer and writes a
/// well-formed .pdf at the requested path.
/// </summary>
public class LocalFolderProcessorPdfTests
{
    static LocalFolderProcessorPdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task ProcessPdfAsync_WritesValidPdfFromFixtureFolder()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance);
        var outputPath = Path.Combine(Path.GetTempPath(),
            $"pdf-test-{Guid.NewGuid():N}.pdf");

        try
        {
            ProcessingProgress? lastProgress = null;
            var progress = new Progress<ProcessingProgress>(p => lastProgress = p);

            await processor.ProcessPdfAsync(
                FixtureHelper.FixtureDir,
                outputPath,
                new DecisionFilterSet(),
                progress);

            Assert.True(File.Exists(outputPath));
            var bytes = await File.ReadAllBytesAsync(outputPath);
            Assert.True(bytes.Length > 0);

            // %PDF- — PDF magic bytes at the start of the file.
            Assert.Equal((byte)'%', bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'D', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
            Assert.Equal((byte)'-', bytes[4]);

            Assert.NotNull(lastProgress);
            Assert.True(lastProgress!.Complete);
            Assert.True(lastProgress.TotalRows > 0);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
