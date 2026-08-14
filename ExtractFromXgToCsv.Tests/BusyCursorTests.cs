using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the busy-cursor discipline, which both panels share and neither owns:
/// each puts the <c>is-busy</c> marker on its root for exactly as long as its
/// own busy flag is up, and app.css turns that one class into the cursor.
/// Because the marker rides the flag rather than any particular busy window,
/// a window added later inherits the cursor for free — and these tests are the
/// only place a panel that quietly stopped marking its root would be caught.
/// </summary>
/// <remarks>
/// The cursor itself is the OS's to draw and is not observable from a
/// component test; the binding that asks for it is, and it is the half that
/// can regress. The other half — that app.css still defines a rule for this
/// class — is deliberately unpinned: it is one rule in one file, and asserting
/// on stylesheet text would buy a brittle string match rather than a contract.
/// Both panels are exercised in one place because the contract is one
/// contract; a third panel's pin belongs here too (issue #77).
/// </remarks>
public class BusyCursorTests : BunitContext
{
    /// <summary>
    /// The marker app.css dresses, as a selector. One constant so a rename has
    /// one place to fail rather than eight.
    /// </summary>
    private const string BusyRoot = "div.is-busy";

    public BusyCursorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<XgProcessingService>();
    }

    // PPTX because the deck pathway is where the missing cursor was found: a
    // multi-minute atomic render with nothing at the pointer to say so.
    private IRenderedComponent<LocalModePanel> RenderLocal() =>
        Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Pptx)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    private IRenderedComponent<WebModePanel> RenderWeb() =>
        Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

    /// <summary>A snapshot mid-render: every file read, timings stopped early.</summary>
    private static ProcessingProgress RenderingSnapshot() => new()
    {
        Current = 40,
        Total = 40,
        Phase = JobPhase.Rendering,
        FileName = "Rendering PPTX (120 decisions, 240 slides)…",
        TotalRows = 120,
        ElapsedSec = 1.2,
        FilesPerSec = 33,
    };

    [Fact]
    public void LocalPanel_MarksItsRoot_BeforeTheFirstStatusPoll()
    {
        var cut = RenderLocal();
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_busy", true);
        cut.Render();

        // One marker, and it encloses the panel rather than decorating a
        // corner of it — the Run button the pointer is still resting on has to
        // be inside, which is where a per-spot class would have failed.
        Assert.Single(cut.FindAll(BusyRoot));
        Assert.NotEmpty(cut.FindAll($"{BusyRoot} #startingNotice"));
        Assert.NotEmpty(cut.FindAll($"{BusyRoot} button.btn-primary"));
    }

    [Fact]
    public void LocalPanel_KeepsItsRootMarked_ThroughTheAtomicRenderPhase()
    {
        var cut = RenderLocal();
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_busy", true);
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", RenderingSnapshot());
        cut.Render();

        Assert.Single(cut.FindAll(BusyRoot));
        Assert.NotEmpty(cut.FindAll($"{BusyRoot} #renderingNotice"));
    }

    [Fact]
    public void LocalPanel_IsUnmarked_WhenIdle()
    {
        var cut = RenderLocal();

        Assert.Empty(cut.FindAll(BusyRoot));
    }

    /// <summary>
    /// A finished run leaves its terminal snapshot on screen — the Done line and
    /// the progress block around it — with the busy flag back down. The marker
    /// rides the flag, not the presence of that block, so it must already be
    /// gone: the failure mode here is a busy cursor stranded over an idle app,
    /// which is worse than the one being fixed.
    /// </summary>
    [Fact]
    public void LocalPanel_DropsTheMarkerWithTheFlag_NotWithTheProgressBlock()
    {
        var cut = RenderLocal();
        var done = RenderingSnapshot();
        done.Complete = true;
        done.FileName = "Done";
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", done);
        cut.Render();

        Assert.NotEmpty(cut.FindAll("span.text-success"));
        Assert.Empty(cut.FindAll(BusyRoot));
    }

    /// <summary>
    /// Web mode's marker is raised by the same <c>StateHasChanged</c> that
    /// renders the busy notice, so the yield that gets the notice painted before
    /// the WASM thread is taken gets the cursor with it — see
    /// <see cref="WebModePanelBusyAffordanceTests"/> for the ordering half.
    /// Observing from inside the body is what makes the window the assertion is
    /// about the window the user is actually stuck in.
    /// </summary>
    [Fact]
    public async Task WebPanel_MarksItsRoot_ForTheWholeBusyBody()
    {
        var cut = RenderWeb();

        var markersDuringBody = 0;
        var noticesInsideMarker = 0;
        await cut.InvokeAsync(() => cut.Instance.RunBusyForTest(
            "Working on it…",
            () =>
            {
                markersDuringBody = cut.FindAll(BusyRoot).Count;
                noticesInsideMarker = cut.FindAll($"{BusyRoot} .busy-notice").Count;
                return Task.CompletedTask;
            }));

        Assert.Equal(1, markersDuringBody);
        Assert.Equal(1, noticesInsideMarker);
        // ...and released with the notice when the body returns.
        Assert.Empty(cut.FindAll(BusyRoot));
    }

    /// <summary>
    /// The wrapper's <c>finally</c> covers the marker too — a throwing body must
    /// not leave the cursor spinning over a panel that is done.
    /// </summary>
    [Fact]
    public async Task WebPanel_ReleasesTheMarker_WhenTheBodyThrows()
    {
        var cut = RenderWeb();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cut.InvokeAsync(() => cut.Instance.RunBusyForTest(
                "Working on it…",
                () => throw new InvalidOperationException("boom"))));

        Assert.Empty(cut.FindAll(BusyRoot));
    }
}
