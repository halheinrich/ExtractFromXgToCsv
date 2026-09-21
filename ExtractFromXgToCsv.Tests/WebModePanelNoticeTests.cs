using AngleSharp.Dom;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="WebModePanel"/>'s four boxes against the umbrella's notices
/// model (<c>SPEC-notices.md</c>, halheinrich/backgammon#248): each renders
/// through the shared <c>Notice</c>, what the box <em>is</em> decides whether
/// it dismisses, and its announcement is read off the content wrapper.
/// <list type="bullet">
/// <item>the busy notice — live indicator: polite, not dismissible;</item>
/// <item>the selected-size limit — gate reason: assertive, not dismissible,
/// and the <em>only</em> statement of that fact;</item>
/// <item>the zero-match notice — gate reason: assertive, not dismissible;</item>
/// <item>the run error — error: assertive, dismissible per failed gesture.</item>
/// </list>
/// Every gesture is a real DOM event through the real components. Why the
/// announcement and dismissibility are pinned per box rather than trusted to
/// the build: see <see cref="NoticeAssert"/>.
/// </summary>
public class WebModePanelNoticeTests : BunitContext
{
    private const string XgFixture = "MTCH4064.xg";
    private const string RunError = "div.alert-danger";

    public WebModePanelNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    private IRenderedComponent<WebModePanel> RenderPanel() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    // [0] is the .xg/.xgp picker; [1] is the optional opening-book input.
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
            var box = cut.Find(".busy-notice");

            NoticeAssert.IsNoticeBox(box, "alert-info", "style");
            NoticeAssert.Announces(box, "status");
            NoticeAssert.IsNotDismissible(box);
            Assert.Equal("max-width:800px", box.GetAttribute("style"));

            // The spinner-and-text row is this panel's layout, on an element
            // of its own inside the content wrapper. On the box the flex
            // classes would lay out the component's children, and reach
            // neither the spinner nor the text.
            Assert.DoesNotContain("d-flex", box.ClassList);
            var row = box.QuerySelector(":scope > div.bg-notice-content > div.d-flex.align-items-center.gap-2");
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

        var box = cut.Find("section div.alert-warning");
        Assert.Contains("limit is 50MB", box.TextContent);

        NoticeAssert.IsNoticeBox(box, "alert-warning");
        // It carried no role at all before the adoption; it is now the only
        // thing telling the user their selection was refused.
        NoticeAssert.Announces(box, "alert");
        NoticeAssert.IsNotDismissible(box);
        Assert.Contains("mt-1", box.ClassList);
        Assert.Contains("mb-1", box.ClassList);
        Assert.Contains("py-1", box.ClassList);

        // One fact, one box: the refusal is a standing gate reason, and is not
        // repeated as a (dismissible) run error.
        Assert.Empty(cut.FindAll(RunError));
    }

    // ── The zero-match notice — gate reason ─────────────────────────────────

    [Fact]
    public void ZeroMatchNotice_IsAnAssertiveNonDismissibleGateReason()
    {
        var cut = RenderPanel();
        SelectFiles(cut, Fixture());
        cut.Render(p => p
            .Add(c => c.FilterConfig,
                new FilterConfig { Players = new List<string> { "__no_such_player__" } })
            .Add(c => c.FilterApplied, true));

        var box = cut.Find(".zero-match-notice");

        NoticeAssert.IsNoticeBox(box, "alert-warning");
        NoticeAssert.Announces(box, "alert");
        NoticeAssert.IsNotDismissible(box);
    }

    // ── The run error — dismissible per failed gesture ──────────────────────

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
        cut.Render(p => p.Add(c => c.FilterApplied, true));
        return cut;
    }

    private static IElement FailADownload(IRenderedComponent<WebModePanel> cut)
    {
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(RunError)));
        return cut.Find(RunError);
    }

    [Fact]
    public void RunError_IsAnAssertiveDismissibleNotice()
    {
        var cut = RenderWithFailingDownload();

        var box = FailADownload(cut);

        Assert.Equal("Error: The download was blocked.", box.QuerySelector(".bg-notice-content")!.TextContent.Trim());
        NoticeAssert.IsNoticeBox(box, "alert-danger");
        // It carried no role at all before the adoption: a failed gesture
        // nobody announced.
        NoticeAssert.Announces(box, "alert");
        NoticeAssert.IsDismissible(box);
        Assert.Contains("mt-2", box.ClassList);
    }

    [Theory]
    [InlineData(RunError)]                          // the whole box
    [InlineData(RunError + " > button.btn-close")]  // the close button
    public void RunError_DismissesByTheBoxAndByTheCloseButton_AndTheNextFailureShowsFresh(string target)
    {
        var cut = RenderWithFailingDownload();
        FailADownload(cut);

        cut.Find(target).Click();

        Assert.Empty(cut.FindAll(RunError));

        // The next failure is a new occurrence, with no reset wiring — and it
        // shows although its text is identical to the dismissed one's.
        var fresh = FailADownload(cut);
        Assert.Contains("The download was blocked.", fresh.TextContent);
    }

    [Fact]
    public void RunError_NoRunHasFailed_RendersNothing()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(RunError));
    }
}
