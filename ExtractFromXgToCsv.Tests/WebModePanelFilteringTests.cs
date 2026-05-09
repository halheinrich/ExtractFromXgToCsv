using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the materialization-cache invariants in
/// <see cref="WebModePanel"/>. The brief-scoped intent is to prove
/// the FilterConfig-change → rebuild path fires correctly; live row
/// filtering correctness is owned by the lib's own filter tests and
/// by <c>OutputConsistencyTests</c>.
///
/// Reflection accesses the cached fields directly. Refactoring those
/// fields will require updating these tests; that's the cost of pinning
/// the invariant without exposing internals as a permanent test seam.
/// </summary>
public class WebModePanelFilteringTests : BunitContext
{
    public WebModePanelFilteringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    [Fact]
    public void NotApplied_LeavesBuiltSetUnpopulated()
    {
        var cfg = new FilterConfig();
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, cfg)
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false));

        Assert.Null(bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet"));
        Assert.Null(bUnitTestHelpers.GetPrivateField<FilterConfig?>(
            cut.Instance, "_lastBuiltConfig"));
    }

    [Fact]
    public void Applied_BuildsAndCachesByFilterConfigIdentity()
    {
        var aliceCfg = new FilterConfig { Players = new List<string> { "Alice" } };
        var bobCfg = new FilterConfig { Players = new List<string> { "Bob" } };

        // Initial render: not yet applied — no build expected.
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, aliceCfg)
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false));

        Assert.Null(bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet"));

        // Apply Alice — build fires.
        cut.Render(p => p.Add(c => c.FilterApplied, true));
        var built1 = bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet");
        Assert.NotNull(built1);
        Assert.Same(aliceCfg, bUnitTestHelpers.GetPrivateField<FilterConfig?>(
            cut.Instance, "_lastBuiltConfig"));

        // Re-render with the SAME FilterConfig instance — cache hit; ref unchanged.
        cut.Render(p => p.Add(c => c.FilterConfig, aliceCfg));
        Assert.Same(built1, bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet"));

        // Re-render with a NEW FilterConfig instance — rebuild fires; new ref.
        cut.Render(p => p.Add(c => c.FilterConfig, bobCfg));
        var built2 = bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet");
        Assert.NotSame(built1, built2);
        Assert.Same(bobCfg, bUnitTestHelpers.GetPrivateField<FilterConfig?>(
            cut.Instance, "_lastBuiltConfig"));
    }

    [Fact]
    public void Dirty_DoesNotRebuild()
    {
        var cfg = new FilterConfig();
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, cfg)
            .Add(c => c.FilterApplied, true)
            .Add(c => c.FilterDirty, false));

        var built1 = bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet");
        Assert.NotNull(built1);

        // Mark dirty — the rebuild gate is `FilterApplied && !FilterDirty`,
        // so a dirty filter should leave the cached set untouched.
        cut.Render(p => p.Add(c => c.FilterDirty, true));
        var built2 = bUnitTestHelpers.GetPrivateField<DecisionFilterSet?>(
            cut.Instance, "_builtSet");
        Assert.Same(built1, built2);
    }
}
