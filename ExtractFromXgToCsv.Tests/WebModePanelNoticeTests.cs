using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="WebModePanel"/>'s four boxes against the umbrella's notices
/// model (<c>SPEC-notices.md</c>, halheinrich/backgammon#248): each renders
/// through the shared <c>Notice</c>, and what the box <em>is</em> decides
/// whether it dismisses and how it is announced.
/// <list type="bullet">
/// <item>the busy notice — live indicator: polite, not dismissible;</item>
/// <item>the selected-size limit — gate reason: assertive, not dismissible,
/// and the <em>only</em> statement of that fact;</item>
/// <item>the zero-match notice — gate reason: assertive, not dismissible;</item>
/// <item>the run error — error: assertive, dismissible per failed gesture.</item>
/// </list>
/// Every gesture is a real DOM event through the real components, and every
/// box is read through BgUiPrimitives' <see cref="NoticeBox"/>
/// (halheinrich/backgammon#385). Every gesture's completion is awaited before
/// its outcome is read (see <see cref="NoticeTesting"/>).
/// </summary>
public class WebModePanelNoticeTests : BunitContext
{
    private const string XgFixture = "MTCH4064.xg";

    public WebModePanelNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    public static TheoryData<NoticeDismissGesture> BothGestures =>
        new(Enum.GetValues<NoticeDismissGesture>());

    private IRenderedComponent<WebModePanel> RenderPanel() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, (FilterConfig?)null)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
    // UploadFiles returns once the selection handler has finished.
    private static void SelectFiles(IRenderedComponent<WebModePanel> cut, params InputFileContent[] files) =>
        cut.FindComponents<InputFile>()[0].UploadFiles(files);

    private static InputFileContent Fixture() =>
        InputFileContent.CreateFromBinary(FixtureHelper.ReadFixture(XgFixture), XgFixture);

    // ── The busy notice — live indicator ────────────────────────────────────

    [Fact]
    public async Task BusyNotice_IsAPoliteNonDismissibleNotice_AndLaysOutItsOwnContent()
    {
        var cut = RenderPanel();

        // Read while the body runs — the only time the notice exists.
        var observed = false;
        await cut.InvokeAsync(() => cut.Instance.RunBusyForTest("Working on it…", () =>
        {
            // <Notice class="busy-notice" style="max-width:800px" Kind="Information"
            //         Announcement="Polite">
            var box = NoticeBox.ByMarkerClass(cut, "busy-notice").ShouldBe(
                NoticeKind.Information, NoticeAnnouncement.Polite, dismissible: false, "class", "style");
            Assert.Equal("max-width:800px", box.Element.GetAttribute("style"));

            // The spinner-and-text row is this panel's layout, on an element
            // of its own inside the content. On the box the flex classes would
            // lay out the component's children, and reach neither the spinner
            // nor the text.
            Assert.Equal(["busy-notice"], box.CallerClasses);
            var row = box.Content.QuerySelector(":scope > div.d-flex.align-items-center.gap-2");
            Assert.NotNull(row);
            Assert.NotNull(row.QuerySelector(":scope > span.spinner-border[aria-hidden=true]"));
            Assert.Equal("Working on it…", row.QuerySelector(":scope > span:not(.spinner-border)")!.TextContent);

            observed = true;
            return Task.CompletedTask;
        }));

        Assert.True(observed, "the body must have run with the notice on screen");
    }

    // ── The selected-size limit — gate reason ───────────────────────────────

    [Fact]
    public void OverLimitSelection_IsStatedOnce_ByAnAssertiveNonDismissibleGateReason()
    {
        var cut = RenderPanel();

        // One byte over the 50 MB cap. Never read: the handler refuses on the
        // declared size before opening a stream.
        SelectFiles(cut, InputFileContent.CreateFromBinary(new byte[50 * 1024 * 1024 + 1], "big.xg"));

        // One fact, one box: the refusal is a standing gate reason, and is not
        // repeated as a (dismissible) run error — it is the only notice.
        // <Notice class="mt-1 mb-1 py-1" Kind="Warning" Announcement="Assertive">
        // It carried no role at all before the adoption; it is now the only
        // thing telling the user their selection was refused.
        var box = Assert.Single(NoticeBox.AllIn(cut)).ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class");
        Assert.Equal(["mt-1", "mb-1", "py-1"], box.CallerClasses);
        Assert.Contains("limit is 50MB", box.Content.TextContent);
    }

    // ── The zero-match notice — gate reason ─────────────────────────────────

    [Fact]
    public void ZeroMatchNotice_IsAnAssertiveNonDismissibleGateReason()
    {
        var cut = RenderPanel();
        SelectFiles(cut, Fixture());
        cut.Render(p => p
            .Add(c => c.FilterInEffect,
                new FilterConfig { Players = new List<string> { "__no_such_player__" } }));

        // <Notice class="zero-match-notice" Kind="Warning" Announcement="Assertive">
        NoticeBox.ByMarkerClass(cut, "zero-match-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class");
    }

    // ── The run error — dismissible per failed gesture ──────────────────────

    /// <summary>The run error boxes on screen: none, or the one.</summary>
    private static IReadOnlyList<NoticeBox> RunErrors(IRenderedComponent<WebModePanel> cut) =>
        NoticeBox.AllIn(cut).Where(b => b.Kind == NoticeKind.Error).ToList();

    /// <summary>
    /// A panel with rows loaded and a filter in effect, whose every download
    /// fails identically at the browser hand-off — so two consecutive failures
    /// carry the same text, which is the case that proves the text is not the
    /// occurrence.
    /// </summary>
    private IRenderedComponent<WebModePanel> RenderWithFailingDownload()
    {
        JSInterop.SetupVoid("downloadFile", _ => true)
                 .SetException(new JSException("The download was blocked."));

        var cut = RenderPanel();
        SelectFiles(cut, Fixture());
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig()));
        return cut;
    }

    /// <summary>
    /// Clicks Download and awaits the click's own completion — the whole busy
    /// gesture, through its failure — then reads the one run error it left.
    /// </summary>
    private static async Task<NoticeBox> FailADownloadAsync(IRenderedComponent<WebModePanel> cut)
    {
        await cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Download", StringComparison.Ordinal))
                 .ClickAsync(new MouseEventArgs())
                 .WaitAsync(NoticeTesting.OperationBound);
        return Assert.Single(RunErrors(cut));
    }

    [Fact]
    public async Task RunError_IsAnAssertiveDismissibleNotice()
    {
        var cut = RenderWithFailingDownload();

        var box = await FailADownloadAsync(cut);

        // <Notice class="mt-2" Kind="Error" Announcement="Assertive" Dismissible="true" …>
        // It carried no role at all before the adoption: a failed gesture
        // nobody announced.
        box.ShouldBe(NoticeKind.Error, NoticeAnnouncement.Assertive, dismissible: true, "class");
        Assert.Equal(["mt-2"], box.CallerClasses);
        Assert.Equal("Error: The download was blocked.", box.Content.TextContent.Trim());
    }

    [Theory]
    [MemberData(nameof(BothGestures))]
    public async Task RunError_DismissesByEitherGesture_AndTheNextFailureShowsFresh(NoticeDismissGesture gesture)
    {
        var cut = RenderWithFailingDownload();
        var first = await FailADownloadAsync(cut);
        var dismissedText = first.Content.TextContent;

        await first.DismissAndSettleAsync(cut, gesture);

        Assert.Empty(RunErrors(cut));

        // The next failure is a new occurrence, with no reset wiring — and it
        // shows although its text is identical to the dismissed one's.
        var fresh = await FailADownloadAsync(cut);
        Assert.Equal(dismissedText, fresh.Content.TextContent);
    }

    [Fact]
    public void RunError_NoRunHasFailed_RendersNothing()
    {
        var cut = RenderPanel();

        Assert.Empty(RunErrors(cut));
    }
}
