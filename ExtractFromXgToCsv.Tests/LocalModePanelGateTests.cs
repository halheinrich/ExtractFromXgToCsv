using AngleSharp.Html.Dom;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the Run-button dirty-gating contract. The bug pattern that
/// produced the original regression was a permanently-disabled Run
/// button because <c>FilterApplied</c> never flipped to true; this
/// test class fails closed if the gating logic is altered such that
/// either of those parameters stops affecting the button's
/// <c>disabled</c> attribute.
/// </summary>
public class LocalModePanelGateTests : BunitContext
{
    public LocalModePanelGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
    }

    [Theory]
    [InlineData(false, false, true)]   // !FilterApplied → disabled
    [InlineData(true,  true,  true)]   // FilterDirty → disabled
    [InlineData(true,  false, false)]  // both clear, paths set → enabled
    public void RunButton_DisabledGatesOnFilterAppliedAndDirty(
        bool filterApplied, bool filterDirty, bool expectDisabled)
    {
        // The folder path is a Home-owned parameter since the source hoist;
        // only the output path is still panel state (normally hydrated from
        // localStorage in OnAfterRenderAsync), so only it needs the
        // reflection seed. Both are set so the FilterApplied/FilterDirty
        // gates are the deciding factor.
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, filterApplied)
            .Add(c => c.FilterDirty, filterDirty)
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
            .Add(c => c.FilterApplied, true)
            .Add(c => c.FilterDirty, false));

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
}
