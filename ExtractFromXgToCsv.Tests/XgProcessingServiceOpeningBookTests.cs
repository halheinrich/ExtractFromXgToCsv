using BgDataTypes_Lib;
using ConvertXgToJson_Lib;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Web-mode counterpart to <see cref="LocalFolderProcessorOpeningBookTests"/>:
/// pins that <see cref="XgProcessingService"/> loads an opening book from
/// uploaded <c>.ob</c> bytes, and that <see cref="XgSourceConverter"/> threads
/// it into both representations' conversions, enriching book-analysed
/// decisions exactly as the server pathway does — and
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
    //  Conversion enrichment (both representations, through the converter)
    // -----------------------------------------------------------------------

    [Fact]
    public void Rows_WithBook_EnrichBookDecision()
    {
        var source = WebSources.Decode(BookOpeningFixture);

        var row = WebSources.Rows(source, book: LoadBook())
            .Single(r => r.Kind == DecisionKind.CheckerPlay && r.Game == 9 && r.MoveNumber == 1);

        Assert.Equal(EnrichedDepth, row.AnalysisDepth);
    }

    [Fact]
    public void Rows_WithoutBook_DegradeToBareBookLabel()
    {
        var source = WebSources.Decode(BookOpeningFixture);

        var row = WebSources.Rows(source)
            .Single(r => r.Kind == DecisionKind.CheckerPlay && r.Game == 9 && r.MoveNumber == 1);

        Assert.Equal("Book V2", row.AnalysisDepth);
    }

    [Fact]
    public void Records_WithBook_EnrichBestCandidate()
    {
        var source = WebSources.Decode(BookOpeningFixture);

        var req = WebSources.Records(source, book: LoadBook())
            .OfType<CheckerPlayDecision>()
            .Single(r => r.Game == 9 && r.MoveNumber == 1);

        Assert.Equal(EnrichedDepth, req.Decision.RankedBy(AppRanking.Ranking).Best.Candidate.Depth);
    }

    [Fact]
    public void Records_WithoutBook_DegradeBestCandidate()
    {
        var source = WebSources.Decode(BookOpeningFixture);

        var req = WebSources.Records(source)
            .OfType<CheckerPlayDecision>()
            .Single(r => r.Game == 9 && r.MoveNumber == 1);

        Assert.Equal("Book V2", req.Decision.RankedBy(AppRanking.Ranking).Best.Candidate.Depth);
    }
}
