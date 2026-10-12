using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Web mode handles a file per file (halheinrich/backgammon#364, the
/// umbrella's <c>SPEC-notices.md</c> §7.1 and §7.3): a pick's good files load
/// and a file that cannot be read is named in "this selection" with the
/// reader's message; a pick whose every file failed still shows its failures;
/// and the opening book's re-extract handles each file within its own
/// boundary, as the pick does. Before this one unreadable file failed the
/// whole selection — no file's rows loaded — and the book's re-extract sat
/// outside any catch.
/// </summary>
public class WebModePanelReadTests : BunitContext
{
    private const string Good = "MTCH4064.xg";
    private const string SelectionList = "div.skipped-notice.selection-skips";

    public WebModePanelReadTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    private IRenderedComponent<WebModePanel> RenderPanel() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
    // UploadFiles returns once the selection handler has finished.
    private static void Pick(IRenderedComponent<WebModePanel> cut, params InputFileContent[] files) =>
        cut.FindComponents<InputFile>()[0].UploadFiles(files);

    private static InputFileContent Fixture(string name, string? asName = null) =>
        InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(name), asName ?? name);

    private static InputFileContent Malformed(string name) =>
        InputFileContent.CreateFromBinary(
            System.Text.Encoding.ASCII.GetBytes("This is not an XG file; its header magic is wrong."), name);

    private static IReadOnlyList<NoticeBox> RunErrors(IRenderedComponent<WebModePanel> cut) =>
        NoticeBox.AllIn(cut).Where(b => b.Kind == NoticeKind.Error).ToList();

    [Fact]
    public void APickWithOneUnreadableFile_LoadsTheGoodFiles_AndNamesTheBadOneWithTheReadersMessage()
    {
        var cut = RenderPanel();

        Pick(cut, Malformed("broken.xg"), Fixture(Good));

        Assert.NotEmpty(cut.Instance.RowCache.Rows);
        Assert.All(cut.Instance.RowCache.Rows, row => Assert.Equal(Good, row.SourceFile));
        Assert.Empty(RunErrors(cut));

        var list = cut.Find(SelectionList);
        Assert.Equal("This selection", list.QuerySelector(".skip-list-label")!.TextContent);
        Assert.Contains("Skipped 1 file", list.TextContent);
        var entry = Assert.Single(list.QuerySelectorAll("li")).TextContent;
        Assert.Contains("broken.xg", entry);
        Assert.Contains(Assert.Single(cut.Instance.Session.Selection!.ReadSkips).Reason, entry);

        // And the good file is downloadable.
        Assert.False(cut.FindAll("button").Single(b => b.TextContent.Contains("Download")).HasAttribute("disabled"));
    }

    [Fact]
    public void APickWhoseEveryFileFailed_StillShowsItsFailures()
    {
        var cut = RenderPanel();

        Pick(cut, Malformed("a.xg"), Malformed("b.xg"));

        var entries = cut.Find(SelectionList).QuerySelectorAll("li").Select(li => li.TextContent).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains("a.xg", entries[0]);
        Assert.Contains("b.xg", entries[1]);
        Assert.Contains("Skipped 2 files", cut.Find(SelectionList).TextContent);
        Assert.Empty(RunErrors(cut));
        // Nothing decoded, so nothing to download.
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Download"));
    }

    /// <summary>
    /// A file that decodes but whose rows the producer refuses — here an XG
    /// file picked under a name with no extension the producer stamps — is a
    /// conversion skip, and stays decoded. Choosing a book reconverts the
    /// selection's rows under it, each file within its own boundary: the good
    /// file's rows reconvert and the refused one is named again.
    /// </summary>
    [Fact]
    public void TheBookReExtract_HandlesAFilePerFile()
    {
        var cut = RenderPanel();
        Pick(cut, Fixture(Good), Fixture("ajhhBG0407.xg", "renamed.bin"));

        Assert.Contains("renamed.bin", cut.Find(SelectionList).TextContent);
        var rowsBefore = cut.Instance.RowCache.Rows;
        Assert.NotEmpty(rowsBefore);

        cut.FindComponents<InputFile>()[1].UploadFiles(Fixture("OpeningBookV2.ob"));

        Assert.Contains("Book loaded", cut.Find("#bookStatus").TextContent);
        Assert.Empty(RunErrors(cut));
        var rowsAfter = cut.Instance.RowCache.Rows;
        Assert.NotSame(rowsBefore, rowsAfter);
        Assert.Equal(rowsBefore.Select(r => r.Id), rowsAfter.Select(r => r.Id));
        var entry = Assert.Single(cut.Find(SelectionList).QuerySelectorAll("li")).TextContent;
        Assert.Contains("renamed.bin", entry);
    }
}
