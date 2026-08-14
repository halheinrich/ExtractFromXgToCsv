using BgDataTypes_Lib;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wire tests for the Web-mode opening-book input: the status line reflects a
/// loaded / invalid book, and a book chosen <em>after</em> the .xg files
/// re-extracts the retained bytes so enrichment does not depend on pick order.
/// The second <see cref="InputFile"/> ([1]) is the .ob picker; [0] is the
/// .xg/.xgp picker.
/// </summary>
public class WebModePanelOpeningBookTests : BunitContext
{
    private const string XgFixture = "ajhhBG0407.xg";
    private const string BookFixture = "OpeningBookV2.ob";
    private const string EnrichedDepth = "Book V2: 12960 trials. 4-ply";

    public WebModePanelOpeningBookTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    private IRenderedComponent<WebModePanel> Panel() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false));

    private static IReadOnlyList<DecisionRow> Rows(IRenderedComponent<WebModePanel> cut) =>
        cut.Instance.RowCache.Rows;

    private static void UploadXg(IRenderedComponent<WebModePanel> cut) =>
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture));

    private static void UploadBook(IRenderedComponent<WebModePanel> cut, byte[] bytes, string name) =>
        cut.FindComponents<InputFile>()[1].UploadFiles(
            InputFileContent.CreateFromBinary(bytes, name));

    [Fact]
    public void UploadValidBook_ShowsLoadedStatus()
    {
        var cut = Panel();

        UploadBook(cut, FixtureHelper.ReadFixture(BookFixture), BookFixture);

        Assert.Contains("Book loaded", cut.Find("#bookStatus").TextContent);
    }

    [Fact]
    public void UploadInvalidBook_ShowsErrorStatus()
    {
        var cut = Panel();

        UploadBook(cut, [1, 2, 3, 4], "not-a-book.ob");

        Assert.Contains("isn't a valid opening book", cut.Find("#bookStatus").TextContent);
    }

    [Fact]
    public void FilesThenBook_ReExtractsWithEnrichment()
    {
        var cut = Panel();

        // .xg first: extracted without a book, so the opening decision is bare.
        UploadXg(cut);
        Assert.DoesNotContain(Rows(cut), r => r.AnalysisDepth == EnrichedDepth);

        // Book second: retained bytes are re-extracted, now enriched.
        UploadBook(cut, FixtureHelper.ReadFixture(BookFixture), BookFixture);
        Assert.Contains(Rows(cut), r => r.AnalysisDepth == EnrichedDepth);
    }
}
