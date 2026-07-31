using BgDataTypes_Lib;
using ExtractFromXgToCsv.Client.Shared;
using ExtractFromXgToCsv.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Verifies that <see cref="LocalFolderProcessor"/> threads the opening book
/// through to <see cref="ConvertXgToJson_Lib.XgDecisionIterator"/> at every
/// call site, and that absence degrades cleanly. Enrichment correctness itself
/// is owned by ConvertXgToJson_Lib's DepthResolutionTests; these are wiring
/// tests over the four processing pathways.
///
/// <para>
/// The <c>ajhhBG0407.xg</c> fixture's game&#160;9 move&#160;1 (roll&#160;41) is a
/// V2 book stamp whose best play enriches, with the real book, to
/// <see cref="EnrichedDepth"/> — <see cref="AnalysisMode.BookRollout"/> +
/// <see cref="AnalysisLevel.Ply4"/>; without the book it degrades to a bare
/// "Book V2" / <see cref="AnalysisLevel.Unknown"/>. The CSV and Diagram-JSON
/// pathways surface that label directly. The Xgp and deck pathways carry no
/// depth label in their output, so they are probed with a book-specific filter
/// — a lone depth clause, <see cref="FilterConfig.IncludeBookRollouts"/>
/// qualified by <see cref="FilterConfig.BookRolloutLevels"/> =
/// <see cref="AnalysisLevel.Ply4"/>:
/// with the book the enriched decisions survive; without it they degrade to
/// Unknown and the filter admits nothing — an observable differential that can
/// only come from the options reaching those iterators.
/// </para>
/// </summary>
public class LocalFolderProcessorOpeningBookTests
{
    private const string BookOpeningFixture = "ajhhBG0407.xg";
    private const string EnrichedDepth = "Book V2: 12960 trials. 4-ply";

    private static string BookFixturePath =>
        Path.Combine(FixtureHelper.FixtureDir, "OpeningBookV2.ob");

    // A processor whose provider loaded the real fixture book.
    private static LocalFolderProcessor WithBook()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [new KeyValuePair<string, string?>("OpeningBookPath", BookFixturePath)])
            .Build();
        var provider = new OpeningBookProvider(config, NullLogger<OpeningBookProvider>.Instance);
        Assert.True(provider.Book is not null,
            $"Expected fixture not present: {BookFixturePath}. Copy OpeningBookV2.ob " +
            "from the eXtreme Gammon 2 install directory into TestData/FixtureFiles/.");
        return new LocalFolderProcessor(NullLogger<LocalFolderProcessor>.Instance, provider);
    }

    // A processor with no book — a null provider is the same unenriched path as
    // a provider that loaded nothing.
    private static LocalFolderProcessor WithoutBook() =>
        new(NullLogger<LocalFolderProcessor>.Instance);

    // An input folder holding only the book-opening fixture, so filter counts
    // are deterministic and independent of the rest of TestData.
    private static string MakeInputFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ob-proc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.Copy(
            Path.Combine(FixtureHelper.FixtureDir, BookOpeningFixture),
            Path.Combine(dir, BookOpeningFixture));
        return dir;
    }

    // Filter whose depth facet is a single clause — BookRollout qualified by
    // Ply4 — so it matches the enriched opening decisions only when a book
    // recovered their level. The toggle is what activates the facet; the level
    // list is inert without it (XgFilter_Lib cbca4b3).
    private static FilterConfig BookPly4Filter() => new()
    {
        IncludeBookRollouts = true,
        BookRolloutLevels = [AnalysisLevel.Ply4],
    };

    // -----------------------------------------------------------------------
    //  CSV pathway (ProcessAsync)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_WithBook_CsvCarriesEnrichedDepth()
    {
        var dir = MakeInputFolder();
        var outPath = Path.Combine(dir, "decisions.csv");
        try
        {
            await WithBook().ProcessAsync(
                dir, outPath, new DecisionFilterSet(), new Progress<ProcessingProgress>());

            var csv = await File.ReadAllTextAsync(outPath);
            Assert.Contains(EnrichedDepth, csv);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ProcessAsync_WithoutBook_CsvDegradesToBareBookLabel()
    {
        var dir = MakeInputFolder();
        var outPath = Path.Combine(dir, "decisions.csv");
        try
        {
            await WithoutBook().ProcessAsync(
                dir, outPath, new DecisionFilterSet(), new Progress<ProcessingProgress>());

            var csv = await File.ReadAllTextAsync(outPath);
            Assert.Contains("Book V2", csv);          // the book stamp is still recognised
            Assert.DoesNotContain(EnrichedDepth, csv); // but not enriched
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // -----------------------------------------------------------------------
    //  Diagram-JSON pathway (ProcessDiagramAsync)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDiagramAsync_WithBook_JsonCarriesEnrichedDepth()
    {
        var dir = MakeInputFolder();
        var outPath = Path.Combine(dir, "decisions.json");
        try
        {
            await WithBook().ProcessDiagramAsync(
                dir, outPath, new DecisionFilterSet(), new Progress<ProcessingProgress>());

            var json = await File.ReadAllTextAsync(outPath);
            Assert.Contains(EnrichedDepth, json);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ProcessDiagramAsync_WithoutBook_JsonDegradesToBareBookLabel()
    {
        var dir = MakeInputFolder();
        var outPath = Path.Combine(dir, "decisions.json");
        try
        {
            await WithoutBook().ProcessDiagramAsync(
                dir, outPath, new DecisionFilterSet(), new Progress<ProcessingProgress>());

            var json = await File.ReadAllTextAsync(outPath);
            Assert.Contains("Book V2", json);
            Assert.DoesNotContain(EnrichedDepth, json);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // -----------------------------------------------------------------------
    //  Xgp pathway (ProcessXgpAsync) — book-specific filter differential
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessXgpAsync_BookPly4Filter_ExportsOnlyWhenBookEnriches()
    {
        var dir = MakeInputFolder();
        var withDir = Path.Combine(Path.GetTempPath(), $"ob-xgp-with-{Guid.NewGuid():N}");
        var withoutDir = Path.Combine(Path.GetTempPath(), $"ob-xgp-without-{Guid.NewGuid():N}");
        var filters = BookPly4Filter();
        var filterSet = filters.Build();
        var options = new XgpExportOptions { NamePattern = "b{n}" };
        try
        {
            await WithBook().ProcessXgpAsync(
                dir, withDir, filterSet, options, filters, new Progress<ProcessingProgress>());
            await WithoutBook().ProcessXgpAsync(
                dir, withoutDir, filterSet, options, filters, new Progress<ProcessingProgress>());

            Assert.NotEmpty(Directory.GetFiles(withDir, "*.xgp"));
            Assert.Empty(Directory.GetFiles(withoutDir, "*.xgp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (Directory.Exists(withDir)) Directory.Delete(withDir, recursive: true);
            if (Directory.Exists(withoutDir)) Directory.Delete(withoutDir, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    //  Deck pathway (ProcessPptxAsync) — same book-specific differential
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessPptxAsync_BookPly4Filter_RendersWithBook_ThrowsWithout()
    {
        var dir = MakeInputFolder();
        var withPath = Path.Combine(Path.GetTempPath(), $"ob-deck-{Guid.NewGuid():N}.pptx");
        var withoutPath = Path.Combine(Path.GetTempPath(), $"ob-deck-{Guid.NewGuid():N}.pptx");
        var filterSet = BookPly4Filter().Build();
        try
        {
            // With the book, the enriched decisions survive the filter and a
            // deck is rendered.
            await WithBook().ProcessPptxAsync(
                dir, withPath, filterSet, new Progress<ProcessingProgress>());
            Assert.True(File.Exists(withPath));
            var head = await File.ReadAllBytesAsync(withPath);
            Assert.True(head.Length > 2 && head[0] == (byte)'P' && head[1] == (byte)'K',
                "PPTX is an OOXML zip (PK magic)");

            // Without the book, every candidate degrades to Unknown, the filter
            // admits nothing, and the deck helper refuses an empty render.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WithoutBook().ProcessPptxAsync(
                    dir, withoutPath, filterSet, new Progress<ProcessingProgress>()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(withPath)) File.Delete(withPath);
            if (File.Exists(withoutPath)) File.Delete(withoutPath);
        }
    }
}
