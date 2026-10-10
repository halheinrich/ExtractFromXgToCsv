using System.Net;
using AngleSharp.Dom;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="LocalModePanel"/>'s three boxes against the umbrella's
/// notices model (<c>SPEC-notices.md</c>, halheinrich/backgammon#248): each
/// renders through the shared <c>Notice</c>, what the box <em>is</em> decides
/// whether it dismisses, and its announcement is read off the content wrapper.
/// <list type="bullet">
/// <item>the zero-match notice and the skipped-files notice — gate reasons:
/// assertive, not dismissible;</item>
/// <item>the run error — error: assertive, dismissible per failed run.</item>
/// </list>
/// What the two gate reasons <em>say</em>, and when, is pinned by
/// <see cref="LocalModePanelZeroMatchTests"/> and
/// <see cref="LocalModePanelSkippedNoticeTests"/>; this class pins only what
/// kind of box each is. Why that is pinned per box rather than trusted to the
/// build: see <see cref="NoticeAssert"/>.
/// </summary>
public class LocalModePanelNoticeTests : BunitContext
{
    private const string RunError = "div.alert-danger";

    /// <summary>
    /// A server whose every <c>/api/process/start</c> fails identically — so
    /// two consecutive failed runs carry the same text, which is the case that
    /// proves the text is not the occurrence. The book-status probe 404s, which
    /// the panel already reads as "unknown".
    /// </summary>
    private sealed class FailingStartHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(
                request.RequestUri!.AbsolutePath == "/api/process/start"
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.NotFound));
    }

    public LocalModePanelNoticeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
        Services.AddSingleton(new HttpClient(new FailingStartHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        });
    }

    private IRenderedComponent<LocalModePanel> RenderPanel() =>
        Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.FolderPath, @"D:\xg")
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    private IRenderedComponent<LocalModePanel> RenderWith(ProcessingProgress snapshot)
    {
        var cut = RenderPanel();
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", snapshot);
        cut.Render();
        return cut;
    }

    // ── The two gate reasons ────────────────────────────────────────────────

    [Fact]
    public async Task ZeroMatchNotice_IsAnAssertiveNonDismissibleGateReason()
    {
        // A real run against a server that writes zero rows: the notice reads
        // the filter the run recorded, so a seeded snapshot cannot show it.
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddClientServices();
        ctx.Services.AddSingleton(new HttpClient(
            new CapturingProcessHandler("{\"complete\":true,\"totalRows\":0}"))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        var cut = ctx.RenderReadyLocalPanel(new FilterConfig { Players = ["Alice"] });
        await cut.RunToCompletionAsync();

        var box = cut.Find(".zero-match-notice");

        NoticeAssert.IsNoticeBox(box, "alert-warning");
        NoticeAssert.Announces(box, "alert");
        NoticeAssert.IsNotDismissible(box);
        Assert.Contains("mt-2", box.ClassList);
    }

    [Fact]
    public void SkippedNotice_IsAnAssertiveNonDismissibleGateReason()
    {
        var cut = RenderWith(new ProcessingProgress
        {
            Current = 1,
            Total = 1,
            Complete = true,
            TotalRows = 4,
            Skipped = [new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic.")],
        });

        var box = cut.Find(".skipped-notice");

        NoticeAssert.IsNoticeBox(box, "alert-warning");
        NoticeAssert.Announces(box, "alert");
        NoticeAssert.IsNotDismissible(box);
        Assert.Contains("mt-2", box.ClassList);
        // The record is the notice's content, inside the announced region.
        Assert.NotNull(box.QuerySelector(":scope > div.bg-notice-content > ul > li"));
    }

    // ── The run error — dismissible per failed run ──────────────────────────

    /// <summary>A panel one click from a run: folder bound, output path typed.</summary>
    private IRenderedComponent<LocalModePanel> RenderReadyToRun()
    {
        var cut = RenderPanel();
        cut.Find("#outputPath").Input(@"D:\xg\out.csv");
        return cut;
    }

    private static IElement FailARun(IRenderedComponent<LocalModePanel> cut)
    {
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(RunError)));
        return cut.Find(RunError);
    }

    [Fact]
    public void RunError_IsAnAssertiveDismissibleNotice()
    {
        var cut = RenderReadyToRun();

        var box = FailARun(cut);

        Assert.StartsWith("Error: ", box.QuerySelector(".bg-notice-content")!.TextContent.Trim());
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
        var cut = RenderReadyToRun();
        var dismissedText = FailARun(cut).TextContent;

        cut.Find(target).Click();

        Assert.Empty(cut.FindAll(RunError));

        // The next failed run is a new occurrence, with no reset wiring — and
        // it shows although its text is identical to the dismissed one's.
        var fresh = FailARun(cut);
        Assert.Equal(dismissedText, fresh.TextContent);
    }

    [Fact]
    public void RunError_NoRunHasFailed_RendersNothing()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll(RunError));
    }
}
