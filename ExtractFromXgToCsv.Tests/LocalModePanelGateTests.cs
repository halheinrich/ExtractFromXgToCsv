using AngleSharp.Html.Dom;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the Run button's filter gate. The bug pattern that produced the
/// original regression was a permanently-disabled Run button because
/// <c>FilterApplied</c> never flipped to true; this class fails closed if the
/// gating logic is altered such that the parameter stops affecting the
/// button's <c>disabled</c> attribute.
/// <para>
/// One parameter, two rows. Until the §6.1 collapse
/// (halheinrich/backgammon#101) this theory had a third — applied-but-dirty —
/// which is no longer a representable state: <c>FilterApplied</c> means
/// applied <i>and</i> settled, because the composite clears the shared holder
/// the moment the filter panel's buffers stop equalling a commit. A row
/// asserting it would have had to invent an input the host cannot produce.
/// </para>
/// </summary>
public class LocalModePanelGateTests : BunitContext
{
    public LocalModePanelGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
    }

    [Theory]
    [InlineData(false, true)]   // no filter in effect → disabled
    [InlineData(true,  false)]  // filter in effect, paths set → enabled
    public void RunButton_DisabledGatesOnFilterApplied(
        bool filterApplied, bool expectDisabled)
    {
        // The folder path is a Home-owned parameter since the source hoist;
        // only the output path is still panel state (normally hydrated from
        // localStorage in OnAfterRenderAsync), so only it needs the
        // reflection seed. Both are set so FilterApplied is the deciding
        // factor.
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, filterApplied)
            .Add(c => c.FolderPath, "D:\\xg"));

        bUnitTestHelpers.SetPrivateField(cut.Instance, "_outputPath", "D:\\xg\\out.csv");
        cut.Render();

        var runButton = (IHtmlButtonElement)cut.FindAll("button.btn-primary").Single();
        Assert.Equal(expectDisabled, runButton.IsDisabled);
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
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true));

        var instance = cut.Instance;
        instance.GetType()
            .GetField("_progress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(instance, new ProcessingProgress
            {
                Complete = true,
                ErrorMessage = "boom",
            });
        cut.Render();

        var errorSpan = cut.Find("span.text-danger");
        Assert.Contains("boom", errorSpan.TextContent);
    }

    // ── The post-run status block's lifetime ────────────────────────────────

    /// <summary>
    /// The post-run status block is dropped when the filter stops being in
    /// effect: "Done — N rows" must not go on describing a filter set that is
    /// no longer active.
    /// <para>
    /// The panel watches the <i>falling edge</i> of <c>FilterApplied</c>, which
    /// is what the §6.1 collapse (halheinrich/backgammon#101) turned the old
    /// rising edge of <c>FilterDirty</c> into — one fact, so one edge. This
    /// test and its <c>FilterStayingInEffect</c> sibling pin the two ordinary
    /// directions and deliberately do <i>not</i> distinguish the edge from a
    /// plain level check; <see cref="EditDuringARun_DoesNotDropTheResultThatRunProduces"/>
    /// is the case that does.
    /// </para>
    /// </summary>
    [Fact]
    public void FilterLeavingEffect_DropsThePostRunStatusBlock()
    {
        var cut = RenderWithCompletedRun();
        Assert.Contains("Done", cut.Markup);

        // The shape an edit (or a source change) arrives in: the holder is
        // cleared, so the host hands down FilterApplied = false.
        cut.Render(p => p.Add(c => c.FilterApplied, false));

        Assert.DoesNotContain("Done", cut.Markup);
    }

    [Fact]
    public void FilterStayingInEffect_KeepsThePostRunStatusBlock()
    {
        var cut = RenderWithCompletedRun();
        Assert.Contains("Done", cut.Markup);

        // A re-render that does not move the filter out of effect — the
        // counterpart half. An unguarded drop would clear the block here too,
        // and the user would watch their result vanish on an unrelated
        // keystroke elsewhere on the page.
        cut.Render(p => p.Add(c => c.OutputFormat, OutputFormat.DiagramJson));

        Assert.Contains("Done", cut.Markup);
    }

    /// <summary>
    /// Editing the filter <i>while a run is in flight</i> must not cost the
    /// user the result that run then produces. This is the case that makes the
    /// falling edge load-bearing rather than a stylistic choice, and the reason
    /// the <c>!_busy</c> guard cannot simply be paired with a level check.
    /// <para>
    /// The sequence: the edit lands while <c>_busy</c>, so the drop is
    /// suppressed and the edge is spent — and by the time the run completes,
    /// the filter has <i>already</i> been out of effect for a while. A level
    /// check (<c>!FilterApplied &amp;&amp; !_busy</c>) would fire on the very
    /// next render and wipe the freshly-arrived "Done — N rows"; the edge does
    /// not, because nothing fell.
    /// </para>
    /// </summary>
    [Fact]
    public void EditDuringARun_DoesNotDropTheResultThatRunProduces()
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true));

        // A run is under way…
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_busy", true);

        // …and the user edits the filter mid-run: the holder clears, so
        // FilterApplied falls. The drop is suppressed by the busy guard, and
        // the edge is consumed here.
        cut.Render(p => p.Add(c => c.FilterApplied, false));

        // The run finishes and lands its result.
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_busy", false);
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", new ProcessingProgress
        {
            Complete = true,
            TotalRows = 3,
        });
        cut.Render(p => p.Add(c => c.OutputFormat, OutputFormat.DiagramJson));

        // It describes the run that actually happened, so it stays.
        Assert.Contains("Done", cut.Markup);
    }

    /// <summary>
    /// A panel with a filter in effect and a completed run on screen, seeded
    /// the way <see cref="Progress_WithErrorMessage_RendersErrorBranch"/> seeds
    /// it — <c>_progress</c> is written by the polling loop, which needs a live
    /// server, so the reflection seed stands in for a run that has finished.
    /// </summary>
    private IRenderedComponent<LocalModePanel> RenderWithCompletedRun()
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true));

        bUnitTestHelpers.SetPrivateField(cut.Instance, "_progress", new ProcessingProgress
        {
            Complete = true,
            TotalRows = 3,
        });
        cut.Render();
        return cut;
    }
}
