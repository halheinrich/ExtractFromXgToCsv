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
        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.Csv)
            .Add(c => c.FilterConfig, new FilterConfig())
            .Add(c => c.FilterApplied, filterApplied)
            .Add(c => c.FilterDirty, filterDirty));

        // Folder/output paths are loaded from localStorage in
        // OnAfterRenderAsync; in tests we set them directly so the
        // FilterApplied/FilterDirty gates are the deciding factor.
        var instance = cut.Instance;
        instance.GetType()
            .GetField("_folderPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(instance, "D:\\xg");
        instance.GetType()
            .GetField("_outputPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(instance, "D:\\xg\\out.csv");
        cut.Render();

        var runButton = (IHtmlButtonElement)cut.FindAll("button.btn-primary").Single();
        Assert.Equal(expectDisabled, runButton.IsDisabled);
    }
}
