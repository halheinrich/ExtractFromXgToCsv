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
/// where it would mislead.
/// </summary>
public class LocalModePanelZeroMatchTests : BunitContext
{
    public LocalModePanelZeroMatchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
    }

    /// <summary>
    /// Complete + no error + <c>TotalRows == 0</c> through an active filter
    /// set renders the zero-match notice alongside the Done line.
    /// </summary>
    [Fact]
    public void CompleteZeroRows_ActiveFilter_RendersNoticeAlongsideDone()
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig { Players = new List<string> { "Alice" } })
            .Add(c => c.FilterApplied, true));

        SetProgress(cut.Instance, new ProcessingProgress
        {
            Complete = true,
            TotalRows = 0,
            ErrorMessage = null,
        });
        cut.Render();

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
    public void CompleteZeroRows_EmptyFilter_RendersDoneLineOnly()
    {
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, true));

        SetProgress(cut.Instance, new ProcessingProgress
        {
            Complete = true,
            TotalRows = 0,
            ErrorMessage = null,
        });
        cut.Render();

        Assert.Empty(cut.FindAll("div.zero-match-notice"));

        var done = cut.Find("span.text-success");
        Assert.Contains("Done", done.TextContent);
    }

    private static void SetProgress(LocalModePanel instance, ProcessingProgress progress) =>
        instance.GetType()
            .GetField("_progress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(instance, progress);
}
