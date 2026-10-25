using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wiring test for the local-mode PDF path: <see cref="LocalFolderProcessor.ProcessPdfAsync"/>
/// hands its requests to the library's real PDF renderer and writes a
/// well-formed .pdf at the requested path. One known input filtered to one
/// decision, so the render is small and no fixture added later changes it.
/// Deck conformance is BackgammonDiagram_Lib's; the deck walk that prepares
/// the requests is <see cref="LocalFolderProcessorDeckPreparationTests"/>'s;
/// corpus-scale deck output is the golden run's.
/// </summary>
public class LocalFolderProcessorPdfTests : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    static LocalFolderProcessorPdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly string _root = Directory.CreateTempSubdirectory("xg-pdf-").FullName;
    private readonly string _folder;

    // The output is beside the input folder, not in it, so it is never read
    // as an input.
    public LocalFolderProcessorPdfTests()
    {
        _folder = Path.Combine(_root, "input");
        Directory.CreateDirectory(_folder);
        File.Copy(Path.Combine(FixtureHelper.FixtureDir, Fixture), Path.Combine(_folder, Fixture));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ProcessPdfAsync_OneDecision_WritesValidPdf()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance, AppRanking.Ranking);
        var outputPath = Path.Combine(_root, "out.pdf");
        var progress = new RecordingProgress();

        await processor.ProcessPdfAsync(
            _folder,
            outputPath,
            new DecisionFilterSet().Add(new FirstDecisionOnly()),
            progress);

        var lastProgress = progress.Last;
        Assert.NotNull(lastProgress);
        Assert.True(lastProgress!.Complete);
        Assert.Equal(1, lastProgress.TotalRows);
        Assert.Equal(outputPath, lastProgress.WrittenPath);

        var bytes = await File.ReadAllBytesAsync(outputPath);

        // %PDF- — PDF magic bytes at the start of the file.
        Assert.True(bytes.Length > 5);
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }
}
