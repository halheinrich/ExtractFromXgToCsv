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
/// Pins <see cref="WebModePanel"/>'s busy contract. Every slow gesture on this
/// panel runs synchronously on the single WASM thread, so the failure mode is
/// <em>ordering</em>, not duration: a busy state raised immediately before the
/// work never reaches the screen, because <c>StateHasChanged</c> only queues a
/// render and the queue drains when the thread is handed back. Two halves are
/// pinned separately —
/// <list type="number">
/// <item>the busy state is <em>rendered</em> before the body runs, and</item>
/// <item>the wrapper <em>yields</em> before the body runs, which is what turns
/// that render into a paint.</item>
/// </list>
/// Dropping either line leaves code that reads correctly and, without (2)'s
/// pin, would still pass a naive markup assertion — bUnit renders eagerly
/// enough to hide it. A component test can only pin the wiring; the pixel was
/// pinned against a real browser when the affordance was measured (issue #53).
/// </summary>
public class WebModePanelBusyAffordanceTests : BunitContext
{
    public WebModePanelBusyAffordanceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    private IRenderedComponent<WebModePanel> RenderPanel(
        OutputFormat format = OutputFormat.Csv, bool filterApplied = false) =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, format)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, filterApplied)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    private static byte[] Fixture => FixtureHelper.ReadFixture("MTCH4064.xg");

    [Fact]
    public async Task RunBusy_RendersBusyStateBeforeTheBodyRuns()
    {
        var cut = RenderPanel();

        string? markupWhileBodyRan = null;
        await cut.InvokeAsync(() => cut.Instance.RunBusyForTest(
            "Working on it…",
            () => { markupWhileBodyRan = cut.Markup; return Task.CompletedTask; }));

        Assert.NotNull(markupWhileBodyRan);
        Assert.Contains("busy-notice", markupWhileBodyRan);
        Assert.Contains("Working on it…", markupWhileBodyRan);

        // ...and released once the body returns.
        Assert.DoesNotContain("busy-notice", cut.Markup);
    }

    /// <summary>
    /// The paint half. The check runs on the same synchronous stack that called
    /// the wrapper: if the body has already executed by the time
    /// <c>RunBusyForTest</c> has returned its task, the wrapper never handed the
    /// thread back, and on WASM the busy render it queued would never have
    /// painted. Deleting the <c>await Task.Yield()</c> fails exactly here.
    /// </summary>
    [Fact]
    public async Task RunBusy_YieldsBeforeTheBodyRuns()
    {
        var cut = RenderPanel();

        var bodyRan = false;
        var bodyRanBeforeFirstSuspension = false;
        Task? busy = null;

        await cut.InvokeAsync(() =>
        {
            busy = cut.Instance.RunBusyForTest(
                "Working on it…", () => { bodyRan = true; return Task.CompletedTask; });
            bodyRanBeforeFirstSuspension = bodyRan;
        });
        await busy!;

        Assert.True(bodyRan, "the body must still run");
        Assert.False(bodyRanBeforeFirstSuspension,
            "the wrapper must yield to the browser before running the body");
    }

    [Fact]
    public async Task RunBusy_ReleasesTheBusyStateWhenTheBodyThrows()
    {
        var cut = RenderPanel();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cut.InvokeAsync(() => cut.Instance.RunBusyForTest(
                "Working on it…",
                () => throw new InvalidOperationException("boom"))));

        Assert.DoesNotContain("busy-notice", cut.Markup);
    }

    [Fact]
    public void FileSelection_RunsThroughTheBusyWrapper()
    {
        var cut = RenderPanel();
        var renders = RecordRenders(cut);

        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(Fixture, "MTCH4064.xg"));

        Assert.Contains(renders, m => m.Contains("busy-notice"));
        Assert.Contains(renders, m => m.Contains("extracting decisions"));
        Assert.DoesNotContain("busy-notice", cut.Markup);
    }

    [Fact]
    public void OpeningBookSelection_RunsThroughTheBusyWrapper()
    {
        var cut = RenderPanel();
        var renders = RecordRenders(cut);

        // Garbage bytes: the load fails and degrades, which is the point — the
        // gesture must raise busy either way, and a real 13.6 MB book would buy
        // the pin nothing but runtime.
        cut.FindComponents<InputFile>()[1].UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3, 4], "not-a-book.ob"));

        Assert.Contains(renders, m => m.Contains("busy-notice"));
        Assert.Contains(renders, m => m.Contains("Loading opening book"));
        Assert.DoesNotContain("busy-notice", cut.Markup);
    }

    [Theory]
    [InlineData(OutputFormat.Csv, "Building CSV")]
    [InlineData(OutputFormat.DiagramJson, "Building JSON")]
    public void Download_RunsThroughTheBusyWrapper(OutputFormat format, string expectedMessage)
    {
        var cut = RenderPanel(format, filterApplied: true);
        cut.FindComponents<InputFile>()[0].UploadFiles(
            InputFileContent.CreateFromBinary(Fixture, "MTCH4064.xg"));

        var renders = RecordRenders(cut);
        cut.Find("button.btn-primary").Click();

        // Click returns at the wrapper's yield, not at the end of the handler —
        // itself a symptom of the discipline under test — so wait for the
        // release rather than reading the markup straight away.
        cut.WaitForAssertion(() => Assert.DoesNotContain("busy-notice", cut.Markup));

        Assert.Contains(renders, m => m.Contains("busy-notice") && m.Contains(expectedMessage));
        // The button's own label is the local half of the same affordance.
        Assert.Contains(renders, m => m.Contains("Building…"));
    }

    /// <summary>
    /// Captures the markup after every render, so a state raised and released
    /// inside one gesture is still observable once the gesture has returned.
    /// </summary>
    private static List<string> RecordRenders(IRenderedComponent<WebModePanel> cut)
    {
        var renders = new List<string>();
        cut.OnMarkupUpdated += (_, _) => renders.Add(cut.Markup);
        return renders;
    }
}
