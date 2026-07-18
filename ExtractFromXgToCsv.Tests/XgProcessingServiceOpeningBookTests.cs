using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Web-mode counterpart to <see cref="LocalFolderProcessorOpeningBookTests"/>:
/// pins that <see cref="XgProcessingService"/> loads an opening book from
/// uploaded <c>.ob</c> bytes and threads it into both extraction entry points,
/// enriching book-analysed decisions exactly as the server pathway does — and
/// that a null book (or invalid <c>.ob</c> bytes) degrades cleanly. The
/// <c>ajhhBG0407.xg</c> game&#160;9 move&#160;1 decision is the same book-stamp
/// probe the server tests use.
/// </summary>
public class XgProcessingServiceOpeningBookTests
{
    private const string BookOpeningFixture = "ajhhBG0407.xg";
    private const string BookFixture = "OpeningBookV2.ob";
    private const string EnrichedDepth = "Book V2: 12960 trials. 4-ply";

    private readonly XgProcessingService _svc = new();

    private OpeningBook LoadBook()
    {
        Assert.True(File.Exists(Path.Combine(FixtureHelper.FixtureDir, BookFixture)),
            "OpeningBookV2.ob fixture must be present in TestData/FixtureFiles/.");

        Assert.True(
            _svc.TryLoadOpeningBook(FixtureHelper.ReadFixture(BookFixture), out var book),
            "the shipped opening book should load from its .ob bytes");
        return book!;
    }

    // -----------------------------------------------------------------------
    //  Bytes -> OpeningBook bridge
    // -----------------------------------------------------------------------

    [Fact]
    public void TryLoadOpeningBook_ValidBytes_LoadsBookWithEntries()
    {
        Assert.True(
            _svc.TryLoadOpeningBook(FixtureHelper.ReadFixture(BookFixture), out var book));
        Assert.NotNull(book);
        Assert.True(book!.EntryCount > 0);
    }

    [Fact]
    public void TryLoadOpeningBook_InvalidBytes_ReturnsFalseNoThrow()
    {
        Assert.False(_svc.TryLoadOpeningBook([1, 2, 3, 4], out var book));
        Assert.Null(book);
    }

    // -----------------------------------------------------------------------
    //  Extraction enrichment (both entry points)
    // -----------------------------------------------------------------------

    [Fact]
    public void ExtractDecisions_WithBook_EnrichesBookDecision()
    {
        var xgBytes = FixtureHelper.ReadFixture(BookOpeningFixture);

        var row = _svc.ExtractDecisions(xgBytes, BookOpeningFixture, LoadBook())
            .Single(r => !r.IsCube && r.Game == 9 && r.MoveNumber == 1);

        Assert.Equal(EnrichedDepth, row.AnalysisDepth);
    }

    [Fact]
    public void ExtractDecisions_WithoutBook_DegradesToBareBookLabel()
    {
        var xgBytes = FixtureHelper.ReadFixture(BookOpeningFixture);

        var row = _svc.ExtractDecisions(xgBytes, BookOpeningFixture)
            .Single(r => !r.IsCube && r.Game == 9 && r.MoveNumber == 1);

        Assert.Equal("Book V2", row.AnalysisDepth);
    }

    [Fact]
    public void ExtractDiagramRequests_WithBook_EnrichesBestCandidate()
    {
        var xgBytes = FixtureHelper.ReadFixture(BookOpeningFixture);

        var req = _svc.ExtractDiagramRequests(xgBytes, BookOpeningFixture, LoadBook())
            .Single(r => !r.Decision.IsCube
                      && r.Descriptive.Game == 9
                      && r.Descriptive.MoveNumber == 1);

        Assert.Equal(EnrichedDepth, req.Decision.Plays[0].Depth);
    }

    [Fact]
    public void ExtractDiagramRequests_WithoutBook_DegradesBestCandidate()
    {
        var xgBytes = FixtureHelper.ReadFixture(BookOpeningFixture);

        var req = _svc.ExtractDiagramRequests(xgBytes, BookOpeningFixture)
            .Single(r => !r.Decision.IsCube
                      && r.Descriptive.Game == 9
                      && r.Descriptive.MoveNumber == 1);

        Assert.Equal("Book V2", req.Decision.Plays[0].Depth);
    }
}
