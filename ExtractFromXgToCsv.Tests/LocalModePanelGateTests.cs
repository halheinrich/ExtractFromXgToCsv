using AngleSharp.Html.Dom;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the Run button's filter gate, and what a run's result describes.
/// <para>
/// <b>The gate.</b> The bug pattern that produced the original regression was
/// a permanently-disabled Run button because the filter fact never became
/// true; the theory fails closed if <c>FilterInEffect</c> stops deciding the
/// button's <c>disabled</c> attribute. One parameter, two rows: a filter is in
/// effect or it is not, and an edit that moves the selection off what is in
/// effect reaches this panel as null.
/// </para>
/// <para>
/// <b>The result.</b> A result describes the inputs it was computed from — the
/// source and the filter <em>values</em> — not merely whether some filter is
/// in effect. Watching "a filter is in effect" fall is not enough now that the
/// empty selection stays in effect across a source replacement
/// (halheinrich/backgammon#266): a folder change never makes it fall, while the
/// folder the result was computed from has gone. And a run that completes
/// after its inputs were superseded must not land its result as the new
/// inputs' (halheinrich/backgammon#374, Arc 2). Each case runs a real run
/// through <see cref="CapturingProcessHandler"/>, because the run is where the
/// panel records what it was computed from.
/// </para>
/// </summary>
public class LocalModePanelGateTests : BunitContext
{
    private const string FolderA = @"D:\xg\a";
    private const string FolderB = @"D:\xg\b";

    private readonly CapturingProcessHandler _server = new();

    public LocalModePanelGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
        Services.AddSingleton(new HttpClient(_server) { BaseAddress = new Uri("http://localhost/") });
    }

    [Theory]
    [InlineData(false, true)]   // no filter in effect → disabled
    [InlineData(true,  false)]  // filter in effect, paths set → enabled
    public void RunButton_DisabledGatesOnFilterInEffect(bool inEffect, bool expectDisabled)
    {
        // The folder path is a Home-owned parameter since the source hoist;
        // only the output path is still panel state (normally restored from
        // browser storage in OnAfterRenderAsync), so only it needs the
        // reflection seed. Both are set so the filter is the deciding factor.
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.Source, FilterSourceToken.FromPath(FolderA))
            .Add(c => c.FilterInEffect, inEffect ? new FilterConfig() : null)
            .Add(c => c.FolderPath, FolderA));

        bUnitTestHelpers.SetPrivateField(cut.Instance, "_outputPath", @"D:\xg\out.csv");
        cut.Render();

        Assert.Equal(expectDisabled, RunButton(cut).IsDisabled);
    }

    /// <summary>
    /// Pins the error-render branch added when <c>ProcessingProgress.ErrorMessage</c>
    /// was decoupled from <c>FileName</c>. Before the split, the server's catch path
    /// stuffed <c>"Error: ..."</c> into <c>FileName</c> and the UI rendered it through
    /// the in-progress "File X of Y: @FileName" slot. The dedicated branch shows the
    /// message in a <c>.text-danger</c> span; this test fails closed if either the
    /// property or the branch is removed.
    /// </summary>
    [Fact]
    public void Progress_WithErrorMessage_RendersErrorBranch()
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterInEffect, new FilterConfig()));

        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "boom",
        });
        cut.Render();

        var errorSpan = cut.Find("span.text-danger");
        Assert.Contains("boom", errorSpan.TextContent);
    }

    // ── What a result describes ─────────────────────────────────────────────

    [Fact]
    public async Task UnrelatedRerender_KeepsTheResult()
    {
        var cut = await RenderWithCompletedRunAsync(new FilterConfig { ErrorMin = 0.05 });

        // A re-render that changes neither input — what every host render is:
        // a new config instance of the same value, and an unrelated parameter.
        // A result compared by reference, or dropped on any re-render, would
        // vanish here on an unrelated keystroke elsewhere on the page.
        cut.Render(p => p
            .Add(c => c.FilterInEffect, new FilterConfig { ErrorMin = 0.05 })
            .Add(c => c.OutputFormat, OutputFormat.DiagramJson));

        Assert.Contains("Done", cut.Markup);
    }

    [Fact]
    public async Task FilterLeavingEffect_DropsTheResult()
    {
        var cut = await RenderWithCompletedRunAsync(new FilterConfig { ErrorMin = 0.05 });

        // The shape an edit arrives in: the selection on screen no longer
        // equals what is in effect, so the host hands down null.
        cut.Render(p => p.Add(c => c.FilterInEffect, (FilterConfig?)null));

        Assert.DoesNotContain("Done", cut.Markup);
    }

    [Fact]
    public async Task FilterValuesChanging_DropsTheResult()
    {
        var cut = await RenderWithCompletedRunAsync(new FilterConfig { ErrorMin = 0.05 });

        // A different filter in effect for the same folder — an edit applied —
        // is not what the result was computed from.
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { ErrorMin = 0.10 }));

        Assert.DoesNotContain("Done", cut.Markup);
    }

    /// <summary>
    /// Changing folders while the empty filter stays ready: "a filter is in
    /// effect" never falls — the empty selection is in effect for the new
    /// folder without Apply — yet the result was computed from the old folder,
    /// so it goes. Run stays open over the new folder.
    /// </summary>
    [Fact]
    public async Task FolderChanging_WhileTheEmptyFilterStaysReady_DropsTheResult()
    {
        var cut = await RenderWithCompletedRunAsync(new FilterConfig());

        cut.Render(p => p
            .Add(c => c.Source, FilterSourceToken.FromPath(FolderB))
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.FolderPath, FolderB));

        Assert.DoesNotContain("Done", cut.Markup);
        Assert.False(RunButton(cut).IsDisabled);
    }

    /// <summary>
    /// A run that completes after its inputs were superseded. While it runs,
    /// its progress and Stop stay with it; when it lands, its result is not
    /// shown as the new inputs' — the folder on screen is not the one it read.
    /// </summary>
    [Fact]
    public async Task RunCompletingAfterItsInputsWereSuperseded_DoesNotShowItsResult()
    {
        var cut = RenderReadyPanel(new FilterConfig());
        _server.HoldStatus();

        var run = RunButtonElement(cut).ClickAsync(new MouseEventArgs());
        await _server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        // The folder changes under the running job, the empty filter staying
        // ready for the new one.
        cut.Render(p => p
            .Add(c => c.Source, FilterSourceToken.FromPath(FolderB))
            .Add(c => c.FilterInEffect, new FilterConfig())
            .Add(c => c.FolderPath, FolderB));

        // The running job keeps its own progress and its Stop.
        Assert.Single(cut.FindAll("#startingNotice"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Stop");

        _server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.DoesNotContain("Done", cut.Markup);
        Assert.Empty(cut.FindAll("#startingNotice"));
        Assert.False(RunButton(cut).IsDisabled);
    }

    /// <summary>
    /// The control for the case above: the same held run, released with its
    /// inputs unchanged, lands its result. The silence above is the
    /// supersession's doing, not the hold's.
    /// </summary>
    [Fact]
    public async Task RunCompletingWithItsInputsUnchanged_ShowsItsResult()
    {
        var cut = RenderReadyPanel(new FilterConfig());
        _server.HoldStatus();

        var run = RunButtonElement(cut).ClickAsync(new MouseEventArgs());
        await _server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);
        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig()));

        _server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("Done", cut.Markup);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A panel over folder A with <paramref name="filter"/> in effect and an
    /// output path typed — Run is open.
    /// </summary>
    private IRenderedComponent<LocalModePanel> RenderReadyPanel(FilterConfig filter)
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.Source, FilterSourceToken.FromPath(FolderA))
            .Add(c => c.FilterInEffect, filter)
            .Add(c => c.FolderPath, FolderA)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));
        cut.Find("#outputPath").Input(new ChangeEventArgs { Value = @"D:\xg\out.csv" });
        Assert.False(RunButton(cut).IsDisabled);
        return cut;
    }

    /// <summary>A ready panel whose run has completed, its result on screen.</summary>
    private async Task<IRenderedComponent<LocalModePanel>> RenderWithCompletedRunAsync(FilterConfig filter)
    {
        var cut = RenderReadyPanel(filter);
        await RunButtonElement(cut).ClickAsync(new MouseEventArgs()).WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.Contains("Done", cut.Markup);
        return cut;
    }

    private static AngleSharp.Dom.IElement RunButtonElement(IRenderedComponent<LocalModePanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Run");

    private static IHtmlButtonElement RunButton(IRenderedComponent<LocalModePanel> cut) =>
        (IHtmlButtonElement)RunButtonElement(cut);
}
