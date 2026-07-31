using BgDataTypes_Lib;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the filter half of the Local-mode wire: the applied
/// <see cref="FilterConfig"/> must reach <c>/api/process/start</c> intact, since
/// the server materializes it there (<c>ProcessController.Start</c> calls
/// <see cref="FilterConfig.Build"/>) and nothing downstream can recover a member
/// lost in transit. Sibling of <see cref="LocalModePanelXgpAnonymizeTests"/>,
/// which covers the same POST's format/anonymize half; only the HTTP boundary is
/// faked, so the panel's real request-building runs.
///
/// <para>
/// The depth facet is the sharp edge. Since XgFilter_Lib cbca4b3 it is three
/// symmetric per-mode pairs — <see cref="FilterConfig.IncludeEvaluations"/> /
/// <see cref="FilterConfig.EvaluationLevels"/> and the Rollout and BookRollout
/// counterparts — where a level list is <em>inert</em> without its mode toggle.
/// A toggle that failed to cross would silently widen the run to every mode; a
/// level list that failed to cross would silently widen its clause to every
/// level. Neither shows up as an error, so both halves of each pair are pinned
/// explicitly, with an asymmetric selection so a clause bound to the wrong
/// mode's levels cannot pass.
/// </para>
/// </summary>
public class LocalModePanelFilterWireTests : BunitContext
{
    public LocalModePanelFilterWireTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// A config exercising every depth clause shape at once: a qualified
    /// evaluation clause, an any-level rollout clause (the common selection —
    /// checker rollouts carry no Roller-family inner levels), and a qualified
    /// book-rollout clause. The three level lists are pairwise distinct so a
    /// clause wired to the wrong mode's list is observable. Non-depth facets
    /// ride along to pin that the rest of the config crosses too.
    /// </summary>
    private static FilterConfig AppliedConfig() => new()
    {
        Players = ["Alice"],
        ErrorMin = 0.05,
        IncludeEvaluations = true,
        EvaluationLevels = [AnalysisLevel.XgRollerPlusPlus],
        IncludeRollouts = true,
        RolloutLevels = [],
        IncludeBookRollouts = true,
        BookRolloutLevels = [AnalysisLevel.Ply4],
    };

    [Fact]
    public async Task RunLocal_PostsAppliedConfig_DepthPairsCrossTheWireVerbatim()
    {
        var captured = await PostAsync(AppliedConfig());

        Assert.True(captured.IncludeEvaluations);
        Assert.Equal([AnalysisLevel.XgRollerPlusPlus], captured.EvaluationLevels);

        Assert.True(captured.IncludeRollouts);
        Assert.Empty(captured.RolloutLevels);

        Assert.True(captured.IncludeBookRollouts);
        Assert.Equal([AnalysisLevel.Ply4], captured.BookRolloutLevels);

        // The non-depth facets ride the same body.
        Assert.Equal(["Alice"], captured.Players);
        Assert.Equal(0.05, captured.ErrorMin);
    }

    /// <summary>
    /// The end-to-end claim, stated through the lib's own SSOT rather than
    /// field by field: the config the server binds activates exactly the facets
    /// the user applied, so <see cref="FilterConfig.Build"/> server-side yields
    /// the same filter set the panel described.
    /// </summary>
    [Fact]
    public async Task RunLocal_PostedConfig_ActivatesTheSameFacetsServerSide()
    {
        var applied = AppliedConfig();
        var captured = await PostAsync(applied);

        Assert.Equal(applied.GetActiveFacets(), captured.GetActiveFacets());
        Assert.Contains(FilterFacet.AnalysisDepth, captured.GetActiveFacets());
    }

    /// <summary>
    /// The inverse guard: level lists whose toggles are all off are inert, and
    /// crossing the wire must not invent a toggle for them. If it did, the run
    /// would filter by a depth facet the user never activated — the retired
    /// "levels alone activate the facet" semantics, resurrected at the boundary.
    /// </summary>
    [Fact]
    public async Task RunLocal_UntoggledLevelLists_StayInertAcrossTheWire()
    {
        var captured = await PostAsync(new FilterConfig
        {
            EvaluationLevels = [AnalysisLevel.Ply3],
            RolloutLevels = [AnalysisLevel.Ply2],
            BookRolloutLevels = [AnalysisLevel.Ply4],
        });

        Assert.False(captured.IncludeEvaluations);
        Assert.False(captured.IncludeRollouts);
        Assert.False(captured.IncludeBookRollouts);
        Assert.DoesNotContain(FilterFacet.AnalysisDepth, captured.GetActiveFacets());
    }

    /// <summary>
    /// Renders the panel with <paramref name="config"/> applied, clicks Run, and
    /// returns the <see cref="FilterConfig"/> the server would have bound from
    /// the POST body.
    /// </summary>
    private async Task<FilterConfig> PostAsync(FilterConfig config)
    {
        var handler = new CapturingProcessHandler();
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });

        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, config)
            .Add(c => c.FilterApplied, true)
            .Add(c => c.FilterDirty, false)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false));

        // Folder/output paths are normally loaded from localStorage; set them
        // directly so the Run button's path gate is satisfied.
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_folderPath", "D:\\xg");
        bUnitTestHelpers.SetPrivateField(cut.Instance, "_outputPath", "D:\\xg\\decisions.csv");
        cut.Render();

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.NotNull(handler.Captured);
        return handler.Captured!.Filters;
    }
}
