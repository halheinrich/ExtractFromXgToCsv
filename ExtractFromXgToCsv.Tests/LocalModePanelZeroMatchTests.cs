using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;
using Bunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the Local-mode filtered-to-zero notice, the sibling of the
/// <see cref="WebModePanel"/> zero-match notice shipped at 6850b5a. Local
/// mode has no browser-side rows to count, so it can't reuse Web's emergent
/// <c>_filteredRows.Count == 0</c> signal: <c>TotalRows == 0</c> alone is
/// ambiguous (empty folder vs. filtered-to-zero). The distinguisher is the
/// lib seam <see cref="DecisionFilterSet.IsEmpty"/> — an empty set admits
/// every row, so zero rows through a non-empty set is a genuine filter miss.
///
/// The notice hangs off the completed-success branch only: it must appear
/// alongside the "✓ Done" line, never on error or cancelled terminations
/// where it would mislead. And the set it asks is the <em>run's</em> filter,
/// never the one on screen now: the box describes the run. Each case runs a
/// real run against a server that writes zero rows, because the run is where
/// the panel records which filter it ran.
/// </summary>
public class LocalModePanelZeroMatchTests : BunitContext
{
    public LocalModePanelZeroMatchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddClientServices();
        Services.AddSingleton(new HttpClient(
            new CapturingProcessHandler("{\"complete\":true,\"totalRows\":0}"))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
    }

    /// <summary>
    /// Complete + no error + <c>TotalRows == 0</c> through an active filter
    /// set renders the zero-match notice alongside the Done line.
    /// </summary>
    [Fact]
    public async Task CompleteZeroRows_ActiveFilter_RendersNoticeAlongsideDone()
    {
        var cut = await RunAsync(new FilterConfig { Players = ["Alice"] });

        // Notice renders...
        var notice = cut.Find("div.zero-match-notice");
        Assert.Contains("No decisions matched these filters", notice.TextContent);

        // ...alongside the success Done line, not in place of it.
        var done = cut.Find("span.text-success");
        Assert.Contains("Done", done.TextContent);
    }

    /// <summary>
    /// Complete + no error + <c>TotalRows == 0</c> through an empty
    /// (inactive) filter set renders the plain Done line only — a folder that
    /// simply held zero decisions is not a filter miss.
    /// </summary>
    [Fact]
    public async Task CompleteZeroRows_EmptyFilter_RendersDoneLineOnly()
    {
        var cut = await RunAsync(new FilterConfig());

        Assert.Empty(cut.FindAll("div.zero-match-notice"));

        var done = cut.Find("span.text-success");
        Assert.Contains("Done", done.TextContent);
    }

    /// <summary>
    /// A run through the empty filter wrote zero rows — an empty folder, not a
    /// filter miss. An active filter then comes into effect for the same
    /// folder. The previous run's zero must not be read against the filter now
    /// on screen: that would blame a filter the run never applied.
    /// </summary>
    [Fact]
    public async Task ZeroRowsThroughTheEmptyFilter_ThenAnActiveFilter_BlamesNoFilter()
    {
        var cut = await RunAsync(new FilterConfig());

        cut.Render(p => p.Add(c => c.FilterInEffect, new FilterConfig { Players = ["Alice"] }));

        Assert.Empty(cut.FindAll("div.zero-match-notice"));
    }

    // A panel over one folder with the filter in effect and an output path,
    // whose Run has completed against the zero-row server.
    private async Task<IRenderedComponent<LocalModePanel>> RunAsync(FilterConfig filter)
    {
        var cut = this.RenderReadyLocalPanel(filter);
        await cut.RunToCompletionAsync();
        return cut;
    }
}
