using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Components.Pages;
using ExtractFromXgToCsv.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the wire integration between the lib-owned <see cref="FilterPanel"/>
/// and the consumer-side <see cref="Home"/>. The original regression shipped
/// because two of FilterPanel's earlier events (<c>OnFiltersChanged</c>) was
/// silently dropped after the post-arc rename — the consumer's binding pointed
/// at a parameter that no longer existed. These tests fail closed if the
/// remaining <c>OnFilterConfigChanged</c> wire ever gets unwired the same way.
/// </summary>
public class HomeWiringTests : BunitContext
{
    public HomeWiringTests()
    {
        // Every JS interop call (localStorage gets/sets) returns default — the
        // tests don't depend on persisted state.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<XgProcessingService>();
    }

    private void RegisterHttpClient(string appMode) =>
        Services.AddSingleton(new HttpClient(new StubAppModeHandler(appMode))
        {
            BaseAddress = new Uri("http://localhost/"),
        });

    [Fact]
    public void OnFilterConfigChanged_FlipsFilterAppliedAndPropagatesConfigToLocalPanel()
    {
        RegisterHttpClient("Local");

        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<LocalModePanel>().Any(),
            TimeSpan.FromSeconds(5));

        var localPanel = cut.FindComponent<LocalModePanel>();
        Assert.False(localPanel.Instance.FilterApplied);

        var newConfig = new FilterConfig { Players = new List<string> { "Alice" } };
        var filterPanel = cut.FindComponent<FilterPanel>();
        cut.InvokeAsync(() =>
            filterPanel.Instance.OnFilterConfigChanged.InvokeAsync(newConfig));

        Assert.True(localPanel.Instance.FilterApplied);
        Assert.False(localPanel.Instance.FilterDirty);
        Assert.Same(newConfig, localPanel.Instance.FilterConfig);
    }

    [Fact]
    public void OnFilterConfigChanged_PropagatesConfigToWebPanelInWebMode()
    {
        RegisterHttpClient("Web");

        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<WebModePanel>().Any(),
            TimeSpan.FromSeconds(5));

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.False(webPanel.Instance.FilterApplied);

        var newConfig = new FilterConfig { Players = new List<string> { "Bob" } };
        var filterPanel = cut.FindComponent<FilterPanel>();
        cut.InvokeAsync(() =>
            filterPanel.Instance.OnFilterConfigChanged.InvokeAsync(newConfig));

        Assert.True(webPanel.Instance.FilterApplied);
        Assert.False(webPanel.Instance.FilterDirty);
        Assert.Same(newConfig, webPanel.Instance.FilterConfig);
    }

    [Fact]
    public void XgpRadio_EnabledInWebMode()
    {
        RegisterHttpClient("Web");
        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<WebModePanel>().Any(),
            TimeSpan.FromSeconds(5));
        Assert.False(cut.Find("#fmtXgp").HasAttribute("disabled"));
    }

    [Fact]
    public void XgpRadio_DisabledInLocalMode()
    {
        // Transient scaffolding until the server-side Xgp pathway lands —
        // ProcessController would otherwise fall through to CSV for an
        // Xgp POST.
        RegisterHttpClient("Local");
        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<LocalModePanel>().Any(),
            TimeSpan.FromSeconds(5));
        Assert.True(cut.Find("#fmtXgp").HasAttribute("disabled"));
    }

    [Fact]
    public void XgpOptionsBlock_RendersOnlyWhileXgpSelected_AndPropagatesToWebPanel()
    {
        RegisterHttpClient("Web");

        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<WebModePanel>().Any(),
            TimeSpan.FromSeconds(5));

        Assert.Empty(cut.FindAll("#xgpOptions"));

        cut.Find("#fmtXgp").Change(true);
        Assert.Single(cut.FindAll("#xgpOptions"));

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.NotNull(webPanel.Instance.XgpOptions);
        Assert.True(webPanel.Instance.XgpOptions.TryValidate(out _));

        cut.Find("#fmtCsv").Change(true);
        Assert.Empty(cut.FindAll("#xgpOptions"));
    }

    [Fact]
    public void OnXgpExported_AdvancesAndPersistsTheCounter()
    {
        RegisterHttpClient("Web");

        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<WebModePanel>().Any(),
            TimeSpan.FromSeconds(5));
        cut.Find("#fmtXgp").Change(true);

        var webPanel = cut.FindComponent<WebModePanel>();
        Assert.Equal(1, webPanel.Instance.XgpOptions.StartNumber);

        // Panel reports an export of 5 decisions → counter advances to 6
        // and the last used number is persisted.
        cut.InvokeAsync(() => webPanel.Instance.OnXgpExported.InvokeAsync(5));

        Assert.Equal(6, webPanel.Instance.XgpOptions.StartNumber);
        Assert.Contains(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem"
            && (string?)i.Arguments[0] == "xg_xgpLastNumber"
            && (string?)i.Arguments[1] == "5");
    }

    [Fact]
    public void OnFilterDirty_FlipsFilterDirtyWithoutClearingApplied()
    {
        RegisterHttpClient("Local");

        var cut = Render<Home>();
        cut.WaitForState(
            () => cut.FindComponents<LocalModePanel>().Any(),
            TimeSpan.FromSeconds(5));

        var localPanel = cut.FindComponent<LocalModePanel>();
        var filterPanel = cut.FindComponent<FilterPanel>();

        // First Apply → FilterApplied=true, FilterDirty=false
        cut.InvokeAsync(() =>
            filterPanel.Instance.OnFilterConfigChanged.InvokeAsync(new FilterConfig()));
        Assert.True(localPanel.Instance.FilterApplied);
        Assert.False(localPanel.Instance.FilterDirty);

        // Then a keystroke marks dirty — Applied stays true, Dirty flips
        cut.InvokeAsync(() => filterPanel.Instance.OnFilterDirty.InvokeAsync());
        Assert.True(localPanel.Instance.FilterApplied);
        Assert.True(localPanel.Instance.FilterDirty);
    }
}
