using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Wire tests pinning that <see cref="WebModePanel"/> routes its live
/// filtering through <see cref="FilteredRowCache"/> under the
/// applied-and-settled gate (<c>FilterApplied &amp;&amp; !FilterDirty</c>):
/// no materialization before Apply; materialization on Apply; a same-reference
/// re-render is a cache hit; a new reference rebuilds; a dirty filter leaves
/// the cache untouched. The projection rules themselves are pinned by
/// <c>FilteredRowCacheTests</c> — here only the panel→cache routing is under
/// test, observed through the panel's internal <c>RowCache</c> seam (no
/// reflection). The migration failure mode this guards: a panel that compiles
/// but filters beside the cache instead of through it.
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
        var cut = Render<WebModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, false)
            .Add(c => c.FilterDirty, false));

        Assert.Null(cut.Instance.RowCache.BuiltSet);
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

        Assert.Null(cut.Instance.RowCache.BuiltSet);

        // Apply Alice — build fires.
        cut.Render(p => p.Add(c => c.FilterApplied, true));
        var built1 = cut.Instance.RowCache.BuiltSet;
        Assert.NotNull(built1);

        // Re-render with the SAME FilterConfig instance — cache hit; ref unchanged.
        cut.Render(p => p.Add(c => c.FilterConfig, aliceCfg));
        Assert.Same(built1, cut.Instance.RowCache.BuiltSet);

        // Re-render with a NEW FilterConfig instance — rebuild fires; new ref.
        cut.Render(p => p.Add(c => c.FilterConfig, bobCfg));
        Assert.NotSame(built1, cut.Instance.RowCache.BuiltSet);
        Assert.NotNull(cut.Instance.RowCache.BuiltSet);
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

        var built1 = cut.Instance.RowCache.BuiltSet;
        Assert.NotNull(built1);

        // Mark dirty and hand in a NEW config instance in the same render —
        // the panel's applied-and-settled gate must keep the cache on the
        // set built from the last applied config, not build the pending one.
        cut.Render(p => p
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterDirty, true));
        Assert.Same(built1, cut.Instance.RowCache.BuiltSet);
    }
}
