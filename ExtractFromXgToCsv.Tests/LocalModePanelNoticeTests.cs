using System.Net;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins <see cref="LocalModePanel"/>'s three boxes against the umbrella's
/// notices model (<c>SPEC-notices.md</c>, halheinrich/backgammon#248): each
/// renders through the shared <c>Notice</c>, and what the box <em>is</em>
/// decides whether it dismisses and how it is announced.
/// <list type="bullet">
/// <item>the zero-match notice and the skipped-files notice — gate reasons:
/// assertive, not dismissible;</item>
/// <item>the run error — error: assertive, dismissible per failed run. One
/// failure is one occurrence, so the next failure shows fresh although its
/// text may be identical: the text was never the occurrence.</item>
/// </list>
/// What the two gate reasons <em>say</em>, and when, is pinned by
/// <see cref="LocalModePanelZeroMatchTests"/> and
/// <see cref="LocalModePanelSkippedNoticeTests"/>; this class pins only what
/// kind of box each is. Every box is read through BgUiPrimitives'
/// <see cref="NoticeBox"/> (halheinrich/backgammon#385), whose
/// <see cref="NoticeBox.ShouldBe"/> states the call site's whole intent, the
/// splat trap included. Every gesture's completion is awaited before its
/// outcome is read (see <see cref="NoticeTesting"/>).
/// </summary>
public class LocalModePanelNoticeTests : BunitContext
{
    /// <summary>
    /// A server that refuses every <c>/api/process/start</c> identically — as
    /// an identity admitted for a different request — so two consecutive
    /// failed runs carry the same text, which is the case that proves the text
    /// is not the occurrence. A refusal ends the run with the run error; a
    /// start that cannot be made is retried instead (SPEC-notices.md §7.5).
    /// The book-status probe 404s, which the panel already reads as "unknown".
    /// </summary>
    private sealed class FailingStartHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/api/process/start"
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"reason\":\"ChangedRequest\"}", System.Text.Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
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

    public static TheoryData<NoticeDismissGesture> BothGestures =>
        new(Enum.GetValues<NoticeDismissGesture>());

    private async Task<IRenderedComponent<LocalModePanel>> RenderPanelAsync() =>
        (await LocalPanelHost.RenderAsync(this)).Panel;

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
        var host = await LocalPanelHost.RenderReadyAsync(ctx);
        await host.ApplyErrorMinAsync("0.05");
        await host.RunToCompletionAsync();
        var cut = host.Panel;

        // <Notice class="zero-match-notice mt-2" Kind="Warning" Announcement="Assertive">
        var box = NoticeBox.ByMarkerClass(cut, "zero-match-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class");
        Assert.Equal(["zero-match-notice", "mt-2"], box.CallerClasses);
    }

    [Fact]
    public async Task SkippedNotice_IsAnAssertiveNonDismissibleGateReason()
    {
        // A real run: the record is the run's, published when it ends.
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddClientServices();
        ctx.Services.AddSingleton(new HttpClient(new CapturingProcessHandler(new ProcessingProgress
        {
            Current = 1,
            Total = 1,
            Complete = true,
            TotalRows = 4,
            Skipped = [new SkippedItem("broken.xg", null, "Not a valid XG file: bad magic.")],
        }))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        var host = await LocalPanelHost.RenderReadyAsync(ctx);
        await host.RunToCompletionAsync();
        var cut = host.Panel;

        // Through the shared rendering: <Notice class="skipped-notice mt-2"
        // Kind="Warning" Announcement="Assertive">
        var box = NoticeBox.ByMarkerClass(cut, "skipped-notice").ShouldBe(
            NoticeKind.Warning, NoticeAnnouncement.Assertive, dismissible: false, "class");
        Assert.Equal(["skipped-notice", "mt-2"], box.CallerClasses);
        // The record is the notice's content, inside what is announced.
        Assert.NotNull(box.Content.QuerySelector(":scope > ul > li"));
    }

    // ── The run error — dismissible per failed run ──────────────────────────

    /// <summary>The run error boxes on screen: none, or the one.</summary>
    private static IReadOnlyList<NoticeBox> RunErrors(IRenderedComponent<LocalModePanel> cut) =>
        NoticeBox.AllIn(cut).Where(b => b.Kind == NoticeKind.Error).ToList();

    /// <summary>A panel one click from a run: folder committed, output path typed.</summary>
    private async Task<IRenderedComponent<LocalModePanel>> RenderReadyToRunAsync() =>
        (await LocalPanelHost.RenderReadyAsync(this)).Panel;

    /// <summary>
    /// Clicks Run and awaits the click's own completion — the whole failed
    /// run — then reads the one run error it left.
    /// </summary>
    private static async Task<NoticeBox> FailARunAsync(IRenderedComponent<LocalModePanel> cut)
    {
        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "Run")
                 .ClickAsync(new MouseEventArgs())
                 .WaitAsync(NoticeTesting.OperationBound);
        return Assert.Single(RunErrors(cut));
    }

    [Fact]
    public async Task RunError_IsAnAssertiveDismissibleNotice()
    {
        var cut = await RenderReadyToRunAsync();

        var box = await FailARunAsync(cut);

        // <Notice class="mt-2" Kind="Error" Announcement="Assertive" Dismissible="true" …>
        // It carried no role at all before the adoption: a failed gesture
        // nobody announced.
        box.ShouldBe(NoticeKind.Error, NoticeAnnouncement.Assertive, dismissible: true, "class");
        Assert.Equal(["mt-2"], box.CallerClasses);
        Assert.StartsWith("The server refused the run: its identity was already used", box.Content.TextContent.Trim());
    }

    [Theory]
    [MemberData(nameof(BothGestures))]
    public async Task RunError_DismissesByEitherGesture_AndTheNextFailureShowsFresh(NoticeDismissGesture gesture)
    {
        var cut = await RenderReadyToRunAsync();
        var first = await FailARunAsync(cut);
        var dismissedText = first.Content.TextContent;

        await first.DismissAndSettleAsync(cut, gesture);

        Assert.Empty(RunErrors(cut));

        // The next failed run is a new occurrence, with no reset wiring — and
        // it shows although its text is identical to the dismissed one's.
        var fresh = await FailARunAsync(cut);
        Assert.Equal(dismissedText, fresh.Content.TextContent);
    }

    [Fact]
    public async Task RunError_NoRunHasFailed_RendersNothing()
    {
        var cut = await RenderPanelAsync();

        Assert.Empty(RunErrors(cut));
    }
}
