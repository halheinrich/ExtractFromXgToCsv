using AngleSharp.Html.Dom;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the Run button's filter gate, and what a run's result describes.
/// <para>
/// <b>The gate.</b> The bug pattern that produced the original regression was
/// a permanently-disabled Run button because the filter fact never became
/// true; the theory fails closed if the filter in effect stops deciding the
/// button's <c>disabled</c> attribute. One fact, two rows: a filter is in
/// effect or it is not, and an edit that moves the selection off what is in
/// effect leaves none.
/// </para>
/// <para>
/// <b>The result.</b> A result describes the inputs it was computed from — the
/// source and the filter <em>values</em> — not merely whether some filter is
/// in effect, so a move that changes neither — a format change, the filter
/// read again as an equal config in a new instance — keeps it. Watching "a
/// filter is in effect" fall is not enough now that the
/// empty selection stays in effect across a source replacement
/// (halheinrich/backgammon#266): a folder change never makes it fall, while the
/// folder the result was computed from has gone. And a run that completes
/// after its inputs were superseded must not land its result as the new
/// inputs' (halheinrich/backgammon#374, Arc 2). Each case runs a real run
/// through <see cref="CapturingProcessHandler"/>, because the run is where the
/// coordinator records what it was computed from, and moves the inputs by the
/// gestures that move them: a folder committed, a filter edited or applied on
/// the surface.
/// </para>
/// </summary>
public class LocalModePanelGateTests : BunitContext
{
    private const string FolderB = @"D:\xg\b";

    private CapturingProcessHandler _server = new();

    public LocalModePanelGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
    }

    private void Serve(CapturingProcessHandler server)
    {
        _server = server;
        Services.AddSingleton(new HttpClient(server) { BaseAddress = new Uri("http://localhost/") });
    }

    [Theory]
    [InlineData(false, true)]   // no filter in effect → disabled
    [InlineData(true,  false)]  // filter in effect, paths set → enabled
    public async Task RunButton_DisabledGatesOnFilterInEffect(bool inEffect, bool expectDisabled)
    {
        // The folder and the output path are both set, so the filter is the
        // deciding factor; an edit not applied leaves nothing in effect.
        Serve(new CapturingProcessHandler());
        var host = await LocalPanelHost.RenderReadyAsync(this);
        if (!inEffect)
            await host.EditErrorMinAsync("0.05");

        Assert.Equal(expectDisabled, RunButton(host).IsDisabled);
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
    public async Task Progress_WithErrorMessage_RendersErrorBranch()
    {
        Serve(new CapturingProcessHandler(new ProcessingProgress
        {
            Complete = true,
            ErrorMessage = "boom",
        }));
        var host = await LocalPanelHost.RenderReadyAsync(this);

        await host.RunToCompletionAsync();

        var errorSpan = host.Panel.Find("span.text-danger");
        Assert.Contains("boom", errorSpan.TextContent);
    }

    // ── What a result describes ─────────────────────────────────────────────

    [Fact]
    public async Task UnrelatedRerender_KeepsTheResult()
    {
        var host = await RenderWithCompletedRunAsync(errorMin: "0.05");

        // A move that changes neither input, and a re-render: the format
        // changed, and the filter in effect read again — a new config instance
        // of the same value. A result compared by reference, or dropped on any
        // move, would vanish here on an unrelated gesture elsewhere on the page.
        await host.SetOutputFormatAsync(OutputFormat.DiagramJson);
        host.Panel.Render();

        Assert.Contains("Done", host.Panel.Markup);
    }

    [Fact]
    public async Task FilterLeavingEffect_DropsTheResult()
    {
        var host = await RenderWithCompletedRunAsync(errorMin: "0.05");

        // The shape an edit arrives in: the selection on screen no longer
        // equals what is in effect, so nothing is.
        await host.EditErrorMinAsync("0.06");

        Assert.DoesNotContain("Done", host.Panel.Markup);
    }

    [Fact]
    public async Task FilterValuesChanging_DropsTheResult()
    {
        var host = await RenderWithCompletedRunAsync(errorMin: "0.05");

        // A different filter in effect for the same folder — an edit applied —
        // is not what the result was computed from.
        await host.ApplyErrorMinAsync("0.10");

        Assert.DoesNotContain("Done", host.Panel.Markup);
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
        var host = await RenderWithCompletedRunAsync(errorMin: null);

        await host.CommitFolderAsync(FolderB);

        Assert.DoesNotContain("Done", host.Panel.Markup);
        Assert.False(RunButton(host).IsDisabled);
    }

    /// <summary>
    /// A run that completes after its inputs were superseded. While it runs,
    /// its progress and Stop stay with it; when it lands, its result is not
    /// shown as the new inputs' — the folder on screen is not the one it read.
    /// </summary>
    [Fact]
    public async Task RunCompletingAfterItsInputsWereSuperseded_DoesNotShowItsResult()
    {
        Serve(new CapturingProcessHandler());
        var host = await LocalPanelHost.RenderReadyAsync(this);
        var cut = host.Panel;
        _server.HoldStatus();

        var run = host.ClickRunAsync();
        await _server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);

        // The folder changes under the running job, the empty filter staying
        // ready for the new one.
        await host.CommitFolderAsync(FolderB);

        // The running job keeps its own progress and its Stop.
        Assert.Single(cut.FindAll("#startingNotice"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Stop");

        _server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.DoesNotContain("Done", cut.Markup);
        Assert.Empty(cut.FindAll("#startingNotice"));
        Assert.False(RunButton(host).IsDisabled);
    }

    /// <summary>
    /// The control for the case above: the same held run, released with its
    /// inputs unchanged, lands its result. The silence above is the
    /// supersession's doing, not the hold's.
    /// </summary>
    [Fact]
    public async Task RunCompletingWithItsInputsUnchanged_ShowsItsResult()
    {
        Serve(new CapturingProcessHandler());
        var host = await LocalPanelHost.RenderReadyAsync(this);
        _server.HoldStatus();

        var run = host.ClickRunAsync();
        await _server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);
        host.Panel.Render();

        _server.ReleaseStatus();
        await run.WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Contains("Done", host.Panel.Markup);
    }

    // ── A click past the gate starts nothing ────────────────────────────────

    /// <summary>
    /// The Run gate is the button's and the command's one definition: a click
    /// that reaches the run past any one of the gate's conditions — the
    /// button dark — sends no start.
    /// </summary>
    [Theory]
    [InlineData("no folder")]
    [InlineData("no output path")]
    [InlineData("no filter")]
    [InlineData("invalid XGP options")]
    public async Task AClickPastEachGateCondition_SendsNoStart(string condition)
    {
        Serve(new CapturingProcessHandler());
        var xgp = condition is "invalid XGP options";
        var host = await LocalPanelHost.RenderAsync(this);
        if (xgp)
            await host.SetOutputFormatAsync(OutputFormat.Xgp);
        if (condition != "no folder")
            await host.CommitFolderAsync(LocalPanelHost.Folder);
        if (condition != "no output path")
            await host.SetOutputPathAsync(@"D:\xg\out.csv");
        if (condition == "no filter")
            await host.EditErrorMinAsync("0.05");
        if (xgp)
            await host.EditPatternAsync("a/b");
        Assert.True(RunButton(host).IsDisabled);

        await host.ClickRunAsync();

        Assert.Equal(0, _server.Starts);
        Assert.DoesNotContain("Starting…", host.Panel.Markup);
    }

    /// <summary>
    /// A click past the gate while a run is in hand sends no second start.
    /// Both clicks are awaited only once the held run is released, so a
    /// second run, were one started, ends too and is counted rather than
    /// holding the test.
    /// </summary>
    [Fact]
    public async Task AClickWhileARunIsInHand_SendsNoSecondStart()
    {
        Serve(new CapturingProcessHandler());
        var host = await LocalPanelHost.RenderReadyAsync(this);
        _server.HoldStatus();
        var run = host.ClickRunAsync();
        await _server.StatusReached.WaitAsync(CapturingProcessHandler.RunTimeout);
        Assert.True(RunButton(host).IsDisabled);

        var second = host.ClickRunAsync();
        _server.ReleaseStatus();
        await Task.WhenAll(run, second).WaitAsync(CapturingProcessHandler.RunTimeout);

        Assert.Equal(1, _server.Starts);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A ready panel whose run has completed, its result on screen: the
    /// filter with error minimum <paramref name="errorMin"/> applied, or the
    /// empty filter when null.
    /// </summary>
    private async Task<LocalPanelHost> RenderWithCompletedRunAsync(string? errorMin)
    {
        Serve(new CapturingProcessHandler());
        var host = await LocalPanelHost.RenderReadyAsync(this);
        if (errorMin is not null)
            await host.ApplyErrorMinAsync(errorMin);
        await host.RunToCompletionAsync();
        Assert.Contains("Done", host.Panel.Markup);
        return host;
    }

    private static IHtmlButtonElement RunButton(LocalPanelHost host) => (IHtmlButtonElement)host.RunButton;
}
