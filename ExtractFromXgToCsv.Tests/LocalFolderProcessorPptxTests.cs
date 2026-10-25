using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Text.RegularExpressions;
using XgFilter_Lib;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wiring test for the local-mode PPTX path: <see cref="LocalFolderProcessor.ProcessPptxAsync"/>
/// hands its requests to the library's real PPTX renderer and writes a
/// well-formed .pptx at the requested path, its one decision a problem slide
/// and a solution slide. One known input filtered to one decision, so the
/// render is small and no fixture added later changes it. Deck conformance is
/// BackgammonDiagram_Lib's PptxConformanceTests'; the deck walk that prepares
/// the requests is <see cref="LocalFolderProcessorDeckPreparationTests"/>'s;
/// corpus-scale deck output is the golden run's.
/// </summary>
public class LocalFolderProcessorPptxTests : IDisposable
{
    private const string Fixture = "MTCH4064.xg";

    private readonly string _root = Directory.CreateTempSubdirectory("xg-pptx-").FullName;
    private readonly string _folder;

    // The output is beside the input folder, not in it, so it is never read
    // as an input.
    public LocalFolderProcessorPptxTests()
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
    public async Task ProcessPptxAsync_OneDecision_WritesValidPptxOfTwoSlides()
    {
        var processor = new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance, NullLogger<FilteredDecisionIterator>.Instance, AppRanking.Ranking);
        var outputPath = Path.Combine(_root, "out.pptx");
        var progress = new RecordingProgress();

        await processor.ProcessPptxAsync(
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

        // PK\x03\x04 — zip local-file-header magic. PPTX is an OOXML zip.
        Assert.True(bytes.Length > 4);
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, bytes[..4]);

        using var archive = ZipFile.OpenRead(outputPath);
        Assert.Contains(archive.Entries, e => e.FullName == "[Content_Types].xml");

        // The one decision becomes a Problem + Solution pair: two slides.
        var slideCount = archive.Entries.Count(e =>
            Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"));
        Assert.Equal(2, slideCount);
    }
}
